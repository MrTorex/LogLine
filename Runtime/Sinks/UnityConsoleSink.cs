using System;
using System.Buffers;
using LogLine.Core;
using UnityEngine;

namespace LogLine.Sinks
{
    /// <summary>
    /// Thread-safe sink that outputs formatted log events directly to Unity's native console.
    /// Supports Rich-Text coloring, stackalloc buffering, and callstack stripping for Editor UX.
    /// </summary>
    public sealed class UnityConsoleSink : ILogSink
    {
        #region Constants

        private const int StackBufferSize = 1024;

        // Rich-Text hex colors for log levels in Unity Editor console
        private const string ColorTrace = "#7F8C8D"; // Gray
        private const string ColorDebug = "#3498DB"; // Sky Blue
        private const string ColorInfo  = "#2ECC71"; // Emerald Green
        private const string ColorWarn  = "#F39C12"; // Orange
        private const string ColorError = "#E74C3C"; // Crimson Red
        private const string ColorFatal = "#9B59B6"; // Amethyst Purple

        #endregion

        #region Properties

        /// <inheritdoc />
        public string Name => "UnityConsole";

        /// <inheritdoc />
        public bool IsEnabled { get; set; } = true;

        /// <inheritdoc />
        public LogLevel MinimumLevel { get; set; } = LogLevel.Trace;

        /// <summary>
        /// Gets or sets a value indicating whether Rich-Text color tags should be rendered.
        /// Defaults to <see langword="true"/> in Editor and Development builds.
        /// </summary>
        public bool UseColorTags { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether timestamp headers should be prepended.
        /// </summary>
        public bool IncludeTimestamp { get; set; } = true;

        /// <summary>
        /// Gets or sets a value indicating whether the logger category should be displayed.
        /// </summary>
        public bool IncludeCategory { get; set; } = true;

        /// <summary>
        /// Gets or sets a value indicating whether UTC time is used instead of local system time.
        /// </summary>
        public bool UseUtcTime { get; set; } = false;

        #endregion

        #region Construction

        /// <summary>
        /// Initializes a new instance of the <see cref="UnityConsoleSink"/> class.
        /// </summary>
        public UnityConsoleSink()
        {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            UseColorTags = true;
#else
            UseColorTags = false;
#endif
        }

        #endregion

        #region ILogSink Implementation

        /// <inheritdoc />
        [HideInCallstack]
        public void Emit(in LogEvent logEvent)
        {
            if (!IsEnabled || logEvent.Level < MinimumLevel)
            {
                return;
            }

            Span<char> stackBuffer = stackalloc char[StackBufferSize];
            var writer = new ConsoleMessageWriter(stackBuffer);

            try
            {
                FormatPayload(ref writer, in logEvent);
                string formattedString = writer.ToString();

                DispatchToNativeConsole(logEvent.Level, formattedString, logEvent.Context);
            }
            finally
            {
                writer.Dispose();
            }
        }

        /// <inheritdoc />
        public void Dispose()
        {
            // Native Unity Console does not require active unmanaged resource cleanup.
        }

        #endregion

        #region Formatting Core

        [HideInCallstack]
        private void FormatPayload(ref ConsoleMessageWriter writer, in LogEvent logEvent)
        {
            // 1. Timestamp Header: [14:23:05.123]
            if (IncludeTimestamp)
            {
                DateTime time = UseUtcTime ? logEvent.TimestampUtc : logEvent.TimestampUtc.ToLocalTime();
                writer.Append('[');
                writer.AppendTwoDigits(time.Hour);
                writer.Append(':');
                writer.AppendTwoDigits(time.Minute);
                writer.Append(':');
                writer.AppendTwoDigits(time.Second);
                writer.Append('.');
                writer.AppendThreeDigits(time.Millisecond);
                writer.Append("] ");
            }

            // 2. Category Header: [Network]
            if (IncludeCategory)
            {
                writer.Append('[');
                writer.Append(logEvent.LoggerName);
                writer.Append("] ");
            }

            // 3. Level Tag with optional Rich-Text coloring
            AppendLevelTag(ref writer, logEvent.Level);
            writer.Append(": ");

            // 4. Main message body
            writer.Append(logEvent.Message);

            // 5. Exception stack trace payload
            if (logEvent.HasException)
            {
                writer.Append("\n--> Exception: ");
                writer.Append(logEvent.Exception.ToString());
            }
        }

        [HideInCallstack]
        private void AppendLevelTag(ref ConsoleMessageWriter writer, LogLevel level)
        {
            if (UseColorTags)
            {
                string colorHex = level switch
                {
                    LogLevel.Trace => ColorTrace,
                    LogLevel.Debug => ColorDebug,
                    LogLevel.Info  => ColorInfo,
                    LogLevel.Warn  => ColorWarn,
                    LogLevel.Error => ColorError,
                    LogLevel.Fatal => ColorFatal,
                    _ => ColorInfo
                };

                writer.Append("<color=");
                writer.Append(colorHex);
                writer.Append("><b>[");
                writer.Append(GetLevelString(level));
                writer.Append("]</b></color>");
            }
            else
            {
                writer.Append('[');
                writer.Append(GetLevelString(level));
                writer.Append(']');
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

        #region Native Dispatch

        [HideInCallstack]
        private static void DispatchToNativeConsole(LogLevel level, string message, UnityEngine.Object context)
        {
            switch (level)
            {
                case LogLevel.Warn:
                    Debug.LogWarning(message, context);
                    break;
                case LogLevel.Error:
                case LogLevel.Fatal:
                    Debug.LogError(message, context);
                    break;
                case LogLevel.Trace:
                case LogLevel.Debug:
                case LogLevel.Info:
                default:
                    Debug.Log(message, context);
                    break;
            }
        }

        #endregion

        #region Internal Fast Buffer Writer

        /// <summary>
        /// Stack-allocated buffer builder with fast arithmetic digit conversions.
        /// </summary>
        private ref struct ConsoleMessageWriter
        {
            private char[] _rentedArray;
            private Span<char> _buffer;
            private int _position;

            public ConsoleMessageWriter(Span<char> initialBuffer)
            {
                _rentedArray = null;
                _buffer = initialBuffer;
                _position = 0;
            }

            public void Append(char c)
            {
                if (_position >= _buffer.Length) Grow(1);
                _buffer[_position++] = c;
            }

            public void Append(string text)
            {
                if (string.IsNullOrEmpty(text)) return;
                Append(text.AsSpan());
            }

            public void Append(ReadOnlySpan<char> span)
            {
                if (span.IsEmpty) return;
                if (_position + span.Length > _buffer.Length) Grow(span.Length);

                span.CopyTo(_buffer[_position..]);
                _position += span.Length;
            }

            public void AppendTwoDigits(int value)
            {
                if (_position + 2 > _buffer.Length) Grow(2);

                int val = (uint)value < 100 ? value : 99;
                _buffer[_position++] = (char)('0' + val / 10);
                _buffer[_position++] = (char)('0' + val % 10);
            }

            public void AppendThreeDigits(int value)
            {
                if (_position + 3 > _buffer.Length) Grow(3);

                int val = (uint)value < 1000 ? value : 999;
                _buffer[_position++] = (char)('0' + val / 100);
                _buffer[_position++] = (char)('0' + val / 10 % 10);
                _buffer[_position++] = (char)('0' + val % 10);
            }

            public override string ToString() => _buffer[.._position].ToString();

            public void Dispose()
            {
                if (_rentedArray != null)
                {
                    ArrayPool<char>.Shared.Return(_rentedArray);
                    _rentedArray = null;
                }
            }

            private void Grow(int requiredAdditionalCapacity)
            {
                int newCapacity = Math.Max(_buffer.Length * 2, _position + requiredAdditionalCapacity);
                char[] newRented = ArrayPool<char>.Shared.Rent(newCapacity);

                _buffer[.._position].CopyTo(newRented);

                if (_rentedArray != null)
                {
                    ArrayPool<char>.Shared.Return(_rentedArray);
                }

                _rentedArray = newRented;
                _buffer = newRented;
            }
        }

        #endregion
    }
}
