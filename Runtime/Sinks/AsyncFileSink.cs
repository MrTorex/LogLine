using System;
using System.Buffers;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using LogLine.Buffering;
using LogLine.Core;
using LogLine.Layouts;
using UnityEngine;
using ThreadPriority = System.Threading.ThreadPriority;

namespace LogLine.Sinks
{
    /// <summary>
    /// Asynchronous file sink writing batches to persistent disk streams via a lock-free background pipeline.
    /// Serializes log events via <see cref="ILogLayout"/> with zero-allocation buffers.
    /// </summary>
    public sealed class AsyncFileSink : LogSinkBase
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
        private readonly Encoding _encoding = new UTF8Encoding(false);

        private volatile ILogLayout _layout;
        private long _maxFileSizeBytes = 10 * 1024 * 1024;
        private volatile int _maxArchiveFiles = 3;

        private FileStream _fileStream;
        private StreamWriter _writer;
        private int _lastLoggedDroppedCount;
        private bool _hasUnflushedData;
        private long _lastFlushTimestamp;

        #endregion

        #region Properties

        /// <inheritdoc />
        public override string Name => "AsyncFile";

        /// <summary>
        /// Gets the raw file path template passed to this sink before macro expansion.
        /// </summary>
        public string FilePathTemplate { get; }

        /// <summary>
        /// Gets the fully resolved absolute file path currently written to by this sink.
        /// </summary>
        public string ResolvedFilePath { get; }

        /// <summary>
        /// Gets the cumulative number of log events dropped due to internal ring buffer saturation.
        /// </summary>
        public int DroppedCount => _ringBuffer.DroppedCount;

        /// <summary>
        /// Gets or sets the active layout used to format events written to disk.
        /// Swapped atomically for thread-safe runtime reconfiguration.
        /// </summary>
        public ILogLayout Layout
        {
            get => _layout;
            set => _layout = value;
        }

        /// <summary>
        /// Gets or sets the maximum size in bytes before the log file is rotated. Default is 10 MB.
        /// </summary>
        public long MaxFileSizeBytes
        {
            get => Interlocked.Read(ref _maxFileSizeBytes);
            set => Interlocked.Exchange(ref _maxFileSizeBytes, value > 0 ? value : 1);
        }

        /// <summary>
        /// Gets or sets the maximum number of archived rotated log files to preserve.
        /// </summary>
        public int MaxArchiveFiles
        {
            get => _maxArchiveFiles;
            set => _maxArchiveFiles = value < 0 ? 0 : value;
        }

        #endregion

        #region Construction

        /// <summary>
        /// Initializes a new instance of the <see cref="AsyncFileSink"/> class.
        /// Resolves any path macros and date tokens once during construction.
        /// </summary>
        /// <param name="filePath">
        /// Target log file path or macro template (e.g. <c>"{persistentDataPath}/Logs/game_{yyyy-MM-dd}.log"</c>).
        /// </param>
        /// <param name="layout">Custom layout. If null, standard file pattern layout is used.</param>
        /// <param name="ringBufferCapacity">Capacity of the internal ring buffer.</param>
        public AsyncFileSink(string filePath, ILogLayout layout = null, int ringBufferCapacity = 4096)
        {
            FilePathTemplate = string.IsNullOrWhiteSpace(filePath)
                ? FilePathResolver.DefaultFilePathTemplate
                : filePath;
            ResolvedFilePath = FilePathResolver.Resolve(FilePathTemplate);
            Layout = layout ?? new PatternLayout(PatternLayout.DefaultFilePattern, useColorTags: false);
            _ringBuffer = new LogRingBuffer(ringBufferCapacity);

            EnsureDirectoryExists(ResolvedFilePath);
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

        #region LogSinkBase Implementation

        /// <inheritdoc />
        [HideInCallstack]
        protected override void Write(in LogEvent logEvent)
        {
            if (_ringBuffer.TryEnqueue(in logEvent))
            {
                _signal.Set();
            }
        }

        /// <inheritdoc />
        protected override void OnDispose()
        {
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
            long flushIntervalTicks = (long)(Stopwatch.Frequency * (FlushIntervalMs / 1000.0));
            _lastFlushTimestamp = Stopwatch.GetTimestamp();

            while (!IsDisposed)
            {
                bool signaled = _signal.WaitOne(FlushIntervalMs);
                DrainQueue(formatBuffer, flushIntervalTicks, timedOut: !signaled);
            }

            DrainQueue(formatBuffer, flushIntervalTicks, timedOut: true);
        }

        private void DrainQueue(Span<char> formatBuffer, long flushIntervalTicks, bool timedOut)
        {
            if (_writer == null) return;

            bool forceFlush = false;

            int currentDropped = _ringBuffer.DroppedCount;
            if (currentDropped > _lastLoggedDroppedCount)
            {
                int delta = currentDropped - _lastLoggedDroppedCount;
                _lastLoggedDroppedCount = currentDropped;

                var droppedEvent = new LogEvent(
                    LogLevel.Warn,
                    "LogLine",
                    $"Dropped {delta} log events due to internal ring buffer overflow.");

                CheckFileRotation();
                WriteLogEvent(ref formatBuffer, in droppedEvent);
                _hasUnflushedData = true;
            }

            while (_ringBuffer.TryDequeue(out LogEvent logEvent))
            {
                CheckFileRotation();
                WriteLogEvent(ref formatBuffer, in logEvent);
                _hasUnflushedData = true;

                if (logEvent.Level >= LogLevel.Error)
                {
                    forceFlush = true;
                }
            }

            if (_hasUnflushedData)
            {
                long now = Stopwatch.GetTimestamp();
                if (forceFlush || timedOut || now - _lastFlushTimestamp >= flushIntervalTicks)
                {
                    _writer.Flush();
                    _hasUnflushedData = false;
                    _lastFlushTimestamp = now;
                }
            }
        }

        private void WriteLogEvent(ref Span<char> formatBuffer, in LogEvent logEvent)
        {
            ILogLayout layout = Layout;
            if (layout == null) return;

            // Fast path: layout fits into stack buffer
            if (layout.TryFormat(in logEvent, formatBuffer, out int charsWritten))
            {
                _writer.Write(formatBuffer[..charsWritten]);
                return;
            }

            // Slow path: large event, rent from pool
            int poolSize = formatBuffer.Length * 2;
            char[] rented = ArrayPool<char>.Shared.Rent(poolSize);
            try
            {
                while (!layout.TryFormat(in logEvent, rented, out charsWritten))
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
                ResolvedFilePath,
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
            long maxBytes = MaxFileSizeBytes;
            if (_fileStream == null || _fileStream.Length < maxBytes)
            {
                return;
            }

            CloseStream();

            int maxArchives = MaxArchiveFiles;
            try
            {
                if (maxArchives <= 0)
                {
                    if (File.Exists(ResolvedFilePath))
                    {
                        File.Delete(ResolvedFilePath);
                    }
                }
                else
                {
                    for (int i = maxArchives - 1; i >= 1; i--)
                    {
                        string oldPath = $"{ResolvedFilePath}.{i}";
                        string newPath = $"{ResolvedFilePath}.{i + 1}";

                        if (File.Exists(newPath)) File.Delete(newPath);
                        if (File.Exists(oldPath)) File.Move(oldPath, newPath);
                    }

                    string firstArchive = $"{ResolvedFilePath}.1";
                    if (File.Exists(firstArchive)) File.Delete(firstArchive);
                    if (File.Exists(ResolvedFilePath)) File.Move(ResolvedFilePath, firstArchive);
                }
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
