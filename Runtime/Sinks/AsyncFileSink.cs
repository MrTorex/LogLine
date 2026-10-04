using System;
using System.Buffers;
using System.IO;
using System.Text;
using System.Threading;
using LogLine.Buffering;
using LogLine.Core;

namespace LogLine.Sinks
{
    /// <summary>
    /// Asynchronous file sink writing batches to persistent disk streams via a lock-free background pipeline.
    /// Supports automatic file rotation and zero-allocation stack formatting.
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
        /// Gets or sets the maximum size in bytes before the log file is rotated. Default is 10 MB.
        /// </summary>
        public long MaxFileSizeBytes { get; set; } = 10 * 1024 * 1024;

        /// <summary>
        /// Gets or sets the maximum number of archived rotated log files to preserve.
        /// </summary>
        public int MaxArchiveFiles { get; set; } = 3;

        /// <summary>
        /// Gets or sets a value indicating whether UTC time is used instead of local system time.
        /// </summary>
        public bool UseUtcTime { get; set; } = false;

        #endregion

        #region Construction

        /// <summary>
        /// Initializes a new instance of the <see cref="AsyncFileSink"/> class.
        /// </summary>
        /// <param name="filePath">Target log file path.</param>
        /// <param name="ringBufferCapacity">Capacity of the internal ring buffer.</param>
        public AsyncFileSink(string filePath, int ringBufferCapacity = 4096)
        {
            _baseFilePath = filePath;
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
                // Signal worker thread if queue was idle
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
                // Wait briefly for worker to flush all remaining entries
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

            // Final drain on application shutdown
            DrainQueue(formatBuffer);
        }

        private void DrainQueue(Span<char> formatBuffer)
        {
            if (_writer == null) return;

            bool hasEntries = false;
            bool forceFlush = false;

            // Check if items were dropped due to buffer saturation
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

                // Rotate file if current stream exceeds configured size limit
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
                else
                {
                    // Periodic non-forced flush to ensure data lands in OS buffers
                    _writer.Flush();
                }
            }
        }

        private void WriteLogEvent(ref Span<char> formatBuffer, in LogEvent logEvent)
        {
            var writer = new FastFileWriter(formatBuffer);
            try
            {
                DateTime time = UseUtcTime ? logEvent.TimestampUtc : logEvent.TimestampUtc.ToLocalTime();

                // Format: YYYY-MM-DD HH:MM:SS.FFF [LEVEL] [Category] Message
                writer.AppendFourDigits(time.Year);
                writer.Append('-');
                writer.AppendTwoDigits(time.Month);
                writer.Append('-');
                writer.AppendTwoDigits(time.Day);
                writer.Append(' ');
                writer.AppendTwoDigits(time.Hour);
                writer.Append(':');
                writer.AppendTwoDigits(time.Minute);
                writer.Append(':');
                writer.AppendTwoDigits(time.Second);
                writer.Append('.');
                writer.AppendThreeDigits(time.Millisecond);

                writer.Append(" [");
                writer.Append(GetLevelString(logEvent.Level));
                writer.Append("] [");
                writer.Append(logEvent.LoggerName);
                writer.Append("] ");
                writer.Append(logEvent.Message);

                if (logEvent.HasException)
                {
                    writer.Append("\n--> Exception: ");
                    writer.Append(logEvent.Exception.ToString());
                }

                _writer.WriteLine(writer.AsSpan());
            }
            finally
            {
                writer.Dispose();
            }
        }

        private static string GetLevelString(LogLevel level) => level switch
        {
            LogLevel.Trace => "TRACE",
            LogLevel.Debug => "DEBUG",
            LogLevel.Info  => "INFO",
            LogLevel.Warn  => "WARN",
            LogLevel.Error => "ERROR",
            LogLevel.Fatal => "FATAL",
            _ => "LOG"
        };

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
                // Shift existing rotated archive files: file.2 -> file.3, file.1 -> file.2, etc.
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

        #region Fast Formatting Buffer

        private ref struct FastFileWriter
        {
            private char[] _rented;
            private Span<char> _buffer;
            private int _pos;

            public FastFileWriter(Span<char> initialBuffer)
            {
                _rented = null;
                _buffer = initialBuffer;
                _pos = 0;
            }

            public void Append(char c)
            {
                if (_pos >= _buffer.Length) Grow(1);
                _buffer[_pos++] = c;
            }

            public void Append(string str)
            {
                if (string.IsNullOrEmpty(str)) return;
                Append(str.AsSpan());
            }

            public void Append(ReadOnlySpan<char> span)
            {
                if (span.IsEmpty) return;
                if (_pos + span.Length > _buffer.Length) Grow(span.Length);

                span.CopyTo(_buffer[_pos..]);
                _pos += span.Length;
            }

            public void AppendTwoDigits(int val)
            {
                if (_pos + 2 > _buffer.Length) Grow(2);
                int clamped = (uint)val < 100 ? val : 99;
                _buffer[_pos++] = (char)('0' + clamped / 10);
                _buffer[_pos++] = (char)('0' + clamped % 10);
            }

            public void AppendThreeDigits(int val)
            {
                if (_pos + 3 > _buffer.Length) Grow(3);
                int clamped = (uint)val < 1000 ? val : 999;
                _buffer[_pos++] = (char)('0' + clamped / 100);
                _buffer[_pos++] = (char)('0' + clamped / 10 % 10);
                _buffer[_pos++] = (char)('0' + clamped % 10);
            }

            public void AppendFourDigits(int val)
            {
                if (_pos + 4 > _buffer.Length) Grow(4);
                _buffer[_pos++] = (char)('0' + val / 1000);
                _buffer[_pos++] = (char)('0' + val / 100 % 10);
                _buffer[_pos++] = (char)('0' + val / 10 % 10);
                _buffer[_pos++] = (char)('0' + val % 10);
            }

            public ReadOnlySpan<char> AsSpan() => _buffer[.._pos];

            public void Dispose()
            {
                if (_rented != null)
                {
                    ArrayPool<char>.Shared.Return(_rented);
                    _rented = null;
                }
            }

            private void Grow(int minRequired)
            {
                int newCap = Math.Max(_buffer.Length * 2, _pos + minRequired);
                char[] rented = ArrayPool<char>.Shared.Rent(newCap);

                _buffer[.._pos].CopyTo(rented);

                if (_rented != null)
                {
                    ArrayPool<char>.Shared.Return(_rented);
                }

                _rented = rented;
                _buffer = rented;
            }
        }

        #endregion
    }
}
