using System;
using System.Globalization;
using System.Runtime.CompilerServices;
using LogLine.Core;

namespace LogLine.Layouts
{
    /// <summary>
    /// High-performance, zero-allocation layout that serializes <see cref="LogEvent"/> payloads
    /// into flat RFC 8259 JSON objects (NDJSON by default) for structured log ingestors
    /// such as Grafana Loki, ELK, and Datadog.
    /// </summary>
    public sealed class JsonLayout : ILogLayout
    {
        #region Constants

        private const string HexDigits = "0123456789abcdef";
        private const int IsoTimestampLength = 24; // yyyy-MM-ddTHH:mm:ss.fffZ
        private const int MaxInnerExceptionDepth = 8;

        #endregion

        #region Private Fields

        private volatile bool _includeTimestamp;
        private volatile bool _includeLevel;
        private volatile bool _includeLogger;
        private volatile bool _includeThreadId;
        private volatile bool _includeException;
        private volatile bool _appendNewline;

        #endregion

        #region Properties

        /// <summary>
        /// Gets or sets a value indicating whether the UTC ISO-8601 <c>"timestamp"</c> field is included.
        /// </summary>
        public bool IncludeTimestamp
        {
            get => _includeTimestamp;
            set => _includeTimestamp = value;
        }

        /// <summary>
        /// Gets or sets a value indicating whether the severity <c>"level"</c> field is included.
        /// </summary>
        public bool IncludeLevel
        {
            get => _includeLevel;
            set => _includeLevel = value;
        }

        /// <summary>
        /// Gets or sets a value indicating whether the category <c>"logger"</c> field is included.
        /// </summary>
        public bool IncludeLogger
        {
            get => _includeLogger;
            set => _includeLogger = value;
        }

        /// <summary>
        /// Gets or sets a value indicating whether the originating managed <c>"threadId"</c> field is included.
        /// </summary>
        public bool IncludeThreadId
        {
            get => _includeThreadId;
            set => _includeThreadId = value;
        }

        /// <summary>
        /// Gets or sets a value indicating whether the <c>"exception"</c> field is included when an exception is attached.
        /// </summary>
        public bool IncludeException
        {
            get => _includeException;
            set => _includeException = value;
        }

        /// <summary>
        /// Gets or sets a value indicating whether a trailing <c>'\n'</c> delimiter is appended for NDJSON streams.
        /// Set to <see langword="false"/> when targeting sinks that delimit entries automatically (e.g. Unity Console).
        /// </summary>
        public bool AppendNewline
        {
            get => _appendNewline;
            set => _appendNewline = value;
        }

        #endregion

        #region Construction

        /// <summary>
        /// Initializes a new instance of the <see cref="JsonLayout"/> class.
        /// </summary>
        /// <param name="includeTimestamp">Whether to serialize the UTC ISO-8601 <c>"timestamp"</c> field.</param>
        /// <param name="includeLevel">Whether to serialize the <c>"level"</c> field.</param>
        /// <param name="includeLogger">Whether to serialize the <c>"logger"</c> field.</param>
        /// <param name="includeThreadId">Whether to serialize the <c>"threadId"</c> field.</param>
        /// <param name="includeException">Whether to serialize the <c>"exception"</c> field when present.</param>
        /// <param name="appendNewline">Whether to append a trailing newline character for NDJSON output.</param>
        public JsonLayout(
            bool includeTimestamp = true,
            bool includeLevel = true,
            bool includeLogger = true,
            bool includeThreadId = true,
            bool includeException = true,
            bool appendNewline = true)
        {
            _includeTimestamp = includeTimestamp;
            _includeLevel = includeLevel;
            _includeLogger = includeLogger;
            _includeThreadId = includeThreadId;
            _includeException = includeException;
            _appendNewline = appendNewline;
        }

        #endregion

        #region ILogLayout Implementation

        /// <inheritdoc />
        public bool TryFormat(in LogEvent logEvent, Span<char> destination, out int charsWritten)
        {
            var writer = new JsonSpanWriter(destination);

            if (!writer.TryWriteChar('{') || _includeTimestamp &&
                !writer.TryWriteTimestampProperty("timestamp".AsSpan(), logEvent.TimestampUtc) || _includeLevel &&
                !writer.TryWriteRawStringProperty("level".AsSpan(), GetLevelSpan(logEvent.Level)) || _includeLogger &&
                !writer.TryWriteStringProperty("logger".AsSpan(), logEvent.LoggerName.AsSpan()) || !writer.TryWriteStringProperty("message".AsSpan(), logEvent.Message.AsSpan()) || _includeThreadId &&
                !writer.TryWriteIntProperty("threadId".AsSpan(), logEvent.ThreadId) || _includeException &&
                logEvent.HasException &&
                !writer.TryWriteExceptionProperty("exception".AsSpan(), logEvent.Exception) || !writer.TryWriteChar('}') || _appendNewline && !writer.TryWriteChar('\n'))
            {
                charsWritten = 0;
                return false;
            }

            charsWritten = writer.Position;
            return true;
        }

        #endregion

        #region Helpers

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static ReadOnlySpan<char> GetLevelSpan(LogLevel level) => level switch
        {
            LogLevel.Trace => "TRACE".AsSpan(),
            LogLevel.Debug => "DEBUG".AsSpan(),
            LogLevel.Info  => "INFO".AsSpan(),
            LogLevel.Warn  => "WARN".AsSpan(),
            LogLevel.Error => "ERROR".AsSpan(),
            LogLevel.Fatal => "FATAL".AsSpan(),
            _ => "LOG".AsSpan()
        };

        #endregion

        #region Stack JSON Writer

        /// <summary>
        /// Zero-allocation stack-only writer for flat JSON serialization into a caller-provided character span.
        /// </summary>
        private ref struct JsonSpanWriter
        {
            private readonly Span<char> _destination;
            private bool _hasFields;

            public int Position { get; private set; }

            public JsonSpanWriter(Span<char> destination)
            {
                _destination = destination;
                Position = 0;
                _hasFields = false;
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public bool TryWriteChar(char c)
            {
                if (Position >= _destination.Length)
                {
                    return false;
                }

                _destination[Position++] = c;
                return true;
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            private bool TryWriteRaw(ReadOnlySpan<char> text)
            {
                if (text.Length > _destination.Length - Position)
                {
                    return false;
                }

                text.CopyTo(_destination[Position..]);
                Position += text.Length;
                return true;
            }

            public bool TryWriteRawStringProperty(ReadOnlySpan<char> propertyName, ReadOnlySpan<char> safeValue)
            {
                int required = (_hasFields ? 1 : 0) + propertyName.Length + safeValue.Length + 5; // ,"k":"v"
                if (required > _destination.Length - Position)
                {
                    return false;
                }

                if (_hasFields)
                {
                    _destination[Position++] = ',';
                }

                _destination[Position++] = '"';
                propertyName.CopyTo(_destination[Position..]);
                Position += propertyName.Length;
                _destination[Position++] = '"';
                _destination[Position++] = ':';
                _destination[Position++] = '"';
                safeValue.CopyTo(_destination[Position..]);
                Position += safeValue.Length;
                _destination[Position++] = '"';

                _hasFields = true;
                return true;
            }

            public bool TryWriteStringProperty(ReadOnlySpan<char> propertyName, ReadOnlySpan<char> value)
            {
                if (!TryWritePropertyPrefix(propertyName) || !TryWriteChar('"'))
                {
                    return false;
                }

                return TryWriteEscapedContent(value) && TryWriteChar('"');
            }

            public bool TryWriteIntProperty(ReadOnlySpan<char> propertyName, int value)
            {
                if (!TryWritePropertyPrefix(propertyName))
                {
                    return false;
                }

                if (!value.TryFormat(_destination[Position..], out int written, default, CultureInfo.InvariantCulture))
                {
                    return false;
                }

                Position += written;
                return true;
            }

            public bool TryWriteTimestampProperty(ReadOnlySpan<char> propertyName, DateTime utc)
            {
                if (!TryWritePropertyPrefix(propertyName))
                {
                    return false;
                }

                // Quoted ISO-8601 UTC timestamp: "yyyy-MM-ddTHH:mm:ss.fffZ" (26 chars)
                if (_destination.Length - Position < IsoTimestampLength + 2)
                {
                    return false;
                }

                Span<char> dst = _destination[Position..];
                dst[0] = '"';

                int year = utc.Year;
                dst[1] = (char)('0' + year / 1000);
                dst[2] = (char)('0' + year / 100 % 10);
                dst[3] = (char)('0' + year / 10 % 10);
                dst[4] = (char)('0' + year % 10);
                dst[5] = '-';

                int month = utc.Month;
                dst[6] = (char)('0' + month / 10);
                dst[7] = (char)('0' + month % 10);
                dst[8] = '-';

                int day = utc.Day;
                dst[9] = (char)('0' + day / 10);
                dst[10] = (char)('0' + day % 10);
                dst[11] = 'T';

                int hour = utc.Hour;
                dst[12] = (char)('0' + hour / 10);
                dst[13] = (char)('0' + hour % 10);
                dst[14] = ':';

                int minute = utc.Minute;
                dst[15] = (char)('0' + minute / 10);
                dst[16] = (char)('0' + minute % 10);
                dst[17] = ':';

                int second = utc.Second;
                dst[18] = (char)('0' + second / 10);
                dst[19] = (char)('0' + second % 10);
                dst[20] = '.';

                int ms = utc.Millisecond;
                dst[21] = (char)('0' + ms / 100);
                dst[22] = (char)('0' + ms / 10 % 10);
                dst[23] = (char)('0' + ms % 10);
                dst[24] = 'Z';
                dst[25] = '"';

                Position += IsoTimestampLength + 2;
                return true;
            }

            public bool TryWriteExceptionProperty(ReadOnlySpan<char> propertyName, Exception exception)
            {
                if (!TryWritePropertyPrefix(propertyName) || !TryWriteChar('"'))
                {
                    return false;
                }

                Exception current = exception;
                int depth = 0;

                while (current != null && depth < MaxInnerExceptionDepth)
                {
                    if (depth > 0 && !TryWriteRaw(" ---> ".AsSpan()))
                    {
                        return false;
                    }

                    Type exType = current.GetType();
                    string typeName = exType.FullName ?? exType.Name;
                    if (!TryWriteEscapedContent(typeName.AsSpan()))
                    {
                        return false;
                    }

                    string message = current.Message;
                    if (!string.IsNullOrEmpty(message))
                    {
                        if (!TryWriteRaw(": ".AsSpan()) || !TryWriteEscapedContent(message.AsSpan()))
                        {
                            return false;
                        }
                    }

                    string stackTrace = current.StackTrace;
                    if (!string.IsNullOrEmpty(stackTrace))
                    {
                        if (!TryWriteRaw("\\n".AsSpan()) || !TryWriteEscapedContent(stackTrace.AsSpan()))
                        {
                            return false;
                        }
                    }

                    current = current.InnerException;
                    depth++;
                }

                if (current != null && !TryWriteRaw(" ... (inner exceptions truncated)".AsSpan()))
                {
                    return false;
                }

                return TryWriteChar('"');
            }

            private bool TryWritePropertyPrefix(ReadOnlySpan<char> propertyName)
            {
                int required = (_hasFields ? 1 : 0) + propertyName.Length + 3; // ,"key":
                if (required > _destination.Length - Position)
                {
                    return false;
                }

                if (_hasFields)
                {
                    _destination[Position++] = ',';
                }

                _destination[Position++] = '"';
                propertyName.CopyTo(_destination[Position..]);
                Position += propertyName.Length;
                _destination[Position++] = '"';
                _destination[Position++] = ':';

                _hasFields = true;
                return true;
            }

            private bool TryWriteEscapedContent(ReadOnlySpan<char> text)
            {
                int chunkStart = 0;

                for (int i = 0; i < text.Length; i++)
                {
                    char c = text[i];
                    if (c >= 0x20 && c != '"' && c != '\\')
                    {
                        continue;
                    }

                    int chunkLength = i - chunkStart;
                    if (chunkLength > 0 && !TryWriteRaw(text.Slice(chunkStart, chunkLength)))
                    {
                        return false;
                    }

                    if (!TryWriteEscapedChar(c))
                    {
                        return false;
                    }

                    chunkStart = i + 1;
                }

                int remainingLength = text.Length - chunkStart;
                return remainingLength <= 0 || TryWriteRaw(text.Slice(chunkStart, remainingLength));
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            private bool TryWriteEscapedChar(char c)
            {
                return c switch
                {
                    '"' => TryWriteTwoChars('\\', '"'),
                    '\\' => TryWriteTwoChars('\\', '\\'),
                    '\b' => TryWriteTwoChars('\\', 'b'),
                    '\f' => TryWriteTwoChars('\\', 'f'),
                    '\n' => TryWriteTwoChars('\\', 'n'),
                    '\r' => TryWriteTwoChars('\\', 'r'),
                    '\t' => TryWriteTwoChars('\\', 't'),
                    _ => TryWriteUnicodeControlEscape(c)
                };
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            private bool TryWriteTwoChars(char first, char second)
            {
                if (_destination.Length - Position < 2)
                {
                    return false;
                }

                _destination[Position++] = first;
                _destination[Position++] = second;
                return true;
            }

            private bool TryWriteUnicodeControlEscape(char c)
            {
                if (_destination.Length - Position < 6)
                {
                    return false;
                }

                Span<char> dst = _destination[Position..];
                dst[0] = '\\';
                dst[1] = 'u';
                dst[2] = '0';
                dst[3] = '0';
                dst[4] = HexDigits[(c >> 4) & 0xF];
                dst[5] = HexDigits[c & 0xF];
                Position += 6;
                return true;
            }
        }

        #endregion
    }
}
