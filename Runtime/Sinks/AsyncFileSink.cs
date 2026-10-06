using System;
using System.Buffers;
using System.IO;
using System.Text;
using System.Threading;
using LogLine.Buffering;
using LogLine.Core;
using LogLine.Layouts;

namespace LogLine.Sinks
{
    /// <summary>
    /// Asynchronous file sink writing batches to persistent disk streams via a lock-free background pipeline.
    /// Serializes log events via <see cref="ILogLayout"/> with zero-allocation buffers.
    /// </summary>
    public sealed class AsyncFileSink : ILogSink
    {
        #region Constants

        private const int DefaultBufferSize = 65536; // 64 KB OS buffer
        private const int FlushIntervalMs = 500;
        private const int FormatStackBufferSize = 2048;

        #endregion

        #region Private Fields

        private readonly LogRingBuffer _ringBuffer;
        private readonly AutoResetEvent _signal = new(false);
        private readonly Thread _workerThread;
        private readonly string _baseFilePath;
        private readonly Encoding _encoding = new UTF8Encoding(false);

        private FileStream _fileStream;
        private StreamWriter _writer;
        private volatile bool _isRunning = true;
        private bool _isDisposed;
        private int _lastLoggedDroppedCount;

        #endregion

        #region Properties

        /// <inheritdoc />
        public string Name => "AsyncFile";

        /// <inheritdoc />
        public bool IsEnabled { get; set; } = true;

        /// <inheritdoc />
        public LogLevel MinimumLevel { get; set; } = LogLevel.Trace;

        /// <summary>
        /// Gets or sets the active layout used to format events written to disk.
        /// </summary>
        public ILogLayout Layout { get; set; }

        /// <summary>
        /// Gets or sets the maximum size in bytes before the log file is rotated. Default is 10 MB.
        /// </summary>
        public long MaxFileSizeBytes { get; set; } = 10 * 1024 * 1024;

        /// <summary>
        /// Gets or sets the maximum number of archived rotated log files to preserve.
        /// </summary>
        public int MaxArchiveFiles { get; set; } = 3;

        #endregion

        #region Construction

        /// <summary>
        /// Initializes a new instance of the <see cref="AsyncFileSink"/> class.
        /// </summary>
        /// <param name="filePath">Target log file path.</param>
        /// <param name="layout">Custom layout. If null, standard file pattern layout is used.</param>
        /// <param name="ringBufferCapacity">Capacity of the internal ring buffer.</param>
        public AsyncFileSink(string filePath, ILogLayout layout = null, int ringBufferCapacity = 4096)
        {
            _baseFilePath = filePath;
            Layout = layout ?? new PatternLayout(PatternLayout.DefaultFilePattern, useColorTags: false);
            _ringBuffer = new LogRingBuffer(ringBufferCapacity);

            EnsureDirectoryExists(_baseFilePath);
            OpenStream();

            _workerThread = new Thread(WorkerLoop)
            {
                Name = "LogLine.AsyncFileWorker",
                IsBackground = true,
                Priority = ThreadPriority.BelowNormal
            };
            _workerThread.Start();
        }

        #endregion

        #region ILogSink Implementation

        /// <inheritdoc />
        public void Emit(in LogEvent logEvent)
        {
            if (_isDisposed || !IsEnabled || logEvent.Level < MinimumLevel)
            {
                return;
            }

            if (_ringBuffer.TryEnqueue(in logEvent))
            {
                _signal.Set();
            }
        }

        /// <inheritdoc />
        public void Dispose()
        {
            if (_isDisposed) return;
            _isDisposed = true;

            _isRunning = false;
            _signal.Set();

            if (_workerThread is { IsAlive: true })
            {
                _workerThread.Join(1500);
            }

            CloseStream();
            _signal.Dispose();
        }

        #endregion

        #region Worker Loop

        private void WorkerLoop()
        {
            Span<char> formatBuffer = stackalloc char[FormatStackBufferSize];

            while (_isRunning)
            {
                _signal.WaitOne(FlushIntervalMs);
                DrainQueue(formatBuffer);
            }

            DrainQueue(formatBuffer);
        }

        private void DrainQueue(Span<char> formatBuffer)
        {
            if (_writer == null) return;

            bool hasEntries = false;
            bool forceFlush = false;

            int currentDropped = _ringBuffer.DroppedCount;
            if (currentDropped > _lastLoggedDroppedCount)
            {
                int delta = currentDropped - _lastLoggedDroppedCount;
                _lastLoggedDroppedCount = currentDropped;
                _writer.WriteLine($"[WARN] LogLine: Dropped {delta} log events due to internal ring buffer overflow!");
                hasEntries = true;
            }

            while (_ringBuffer.TryDequeue(out LogEvent logEvent))
            {
                hasEntries = true;
                CheckFileRotation();
                WriteLogEvent(ref formatBuffer, in logEvent);

                if (logEvent.Level >= LogLevel.Error)
                {
                    forceFlush = true;
                }
            }

            if (hasEntries)
            {
                if (forceFlush)
                {
                    _writer.Flush();
                }
            }
        }

        private void WriteLogEvent(ref Span<char> formatBuffer, in LogEvent logEvent)
        {
            if (Layout == null) return;

            // Fast path: layout fits into stack buffer
            if (Layout.TryFormat(in logEvent, formatBuffer, out int charsWritten))
            {
                _writer.Write(formatBuffer[..charsWritten]);
                return;
            }

            // Slow path: large event, rent from pool
            int poolSize = formatBuffer.Length * 2;
            char[] rented = ArrayPool<char>.Shared.Rent(poolSize);
            try
            {
                while (!Layout.TryFormat(in logEvent, rented, out charsWritten))
                {
                    poolSize *= 2;
                    ArrayPool<char>.Shared.Return(rented);
                    rented = ArrayPool<char>.Shared.Rent(poolSize);
                }

                _writer.Write(rented.AsSpan(0, charsWritten));
            }
            finally
            {
                ArrayPool<char>.Shared.Return(rented);
            }
        }

        #endregion

        #region File Stream & Rotation

        private void OpenStream()
        {
            _fileStream = new FileStream(
                _baseFilePath,
                FileMode.Append,
                FileAccess.Write,
                FileShare.ReadWrite,
                DefaultBufferSize,
                useAsync: false);

            _writer = new StreamWriter(_fileStream, _encoding, DefaultBufferSize);
        }

        private void CloseStream()
        {
            try
            {
                _writer?.Flush();
                _writer?.Dispose();
                _fileStream?.Dispose();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[AsyncFileSink] Stream close fault: {ex}");
            }
            finally
            {
                _writer = null;
                _fileStream = null;
            }
        }

        private void CheckFileRotation()
        {
            if (_fileStream == null || _fileStream.Length < MaxFileSizeBytes)
            {
                return;
            }

            CloseStream();

            try
            {
                for (int i = MaxArchiveFiles - 1; i >= 1; i--)
                {
                    string oldPath = $"{_baseFilePath}.{i}";
                    string newPath = $"{_baseFilePath}.{i + 1}";

                    if (File.Exists(newPath)) File.Delete(newPath);
                    if (File.Exists(oldPath)) File.Move(oldPath, newPath);
                }

                string firstArchive = $"{_baseFilePath}.1";
                if (File.Exists(firstArchive)) File.Delete(firstArchive);
                if (File.Exists(_baseFilePath)) File.Move(_baseFilePath, firstArchive);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[AsyncFileSink] File rotation failed: {ex}");
            }

            OpenStream();
        }

        private static void EnsureDirectoryExists(string filePath)
        {
            string dir = Path.GetDirectoryName(filePath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }
        }

        #endregion
    }
}
