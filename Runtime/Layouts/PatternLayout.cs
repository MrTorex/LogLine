using System;
using System.Collections.Generic;
using LogLine.Core;

namespace LogLine.Layouts
{
    /// <summary>
    /// High-performance pattern-based layout that compiles format templates into an optimized converter pipeline.
    /// Formats log events zero-allocation directly into destination character spans.
    /// </summary>
    public sealed class PatternLayout : ILogLayout
    {
        #region Constants

        // Console pattern excludes %n because Unity's Debug.Log automatically creates an item entry.
        public const string DefaultConsolePattern = "[%d{HH:mm:ss.fff}] [%p] [%c]: %m%ex";

        // File pattern includes %n at the end to delimit lines on disk.
        public const string DefaultFilePattern = "%d{yyyy-MM-dd HH:mm:ss.fff} [%p] [%c] %m%ex%n";

        // Color definitions for Unity Editor Console
        private const string ColorTrace = "#7F8C8D";
        private const string ColorDebug = "#3498DB";
        private const string ColorInfo  = "#2ECC71";
        private const string ColorWarn  = "#F39C12";
        private const string ColorError = "#E74C3C";
        private const string ColorFatal = "#9B59B6";

        #endregion

        #region Private Fields

        private readonly PatternConverter[] _converters;

        #endregion

        #region Properties

        /// <summary>
        /// Gets the raw format pattern string used by this layout.
        /// </summary>
        public string Pattern { get; }

        /// <summary>
        /// Gets a value indicating whether Rich-Text color tags are rendered for severity levels.
        /// </summary>
        public bool UseColorTags { get; }

        #endregion

        #region Construction

        /// <summary>
        /// Initializes a new instance of <see cref="PatternLayout"/>.
        /// </summary>
        /// <param name="pattern">Template pattern string (e.g. "%d [%p] %m%n").</param>
        /// <param name="useColorTags">Whether to wrap log level tags in Rich-Text color markers.</param>
        public PatternLayout(string pattern = null, bool useColorTags = false)
        {
            Pattern = string.IsNullOrEmpty(pattern) ? DefaultConsolePattern : pattern;
            UseColorTags = useColorTags;
            _converters = PatternParser.Parse(Pattern, UseColorTags);
        }

        #endregion

        #region ILogLayout Implementation

        /// <inheritdoc />
        public bool TryFormat(in LogEvent logEvent, Span<char> destination, out int charsWritten)
        {
            charsWritten = 0;

            for (int i = 0; i < _converters.Length; i++)
            {
                PatternConverter t = _converters[i];
                Span<char> remaining = destination[charsWritten..];
                if (!t.TryConvert(in logEvent, remaining, out int written)) return false;

                charsWritten += written;
            }

            return true;
        }

        #endregion

        #region Nested Pattern Converters

        private abstract class PatternConverter
        {
            public abstract bool TryConvert(in LogEvent logEvent, Span<char> destination, out int charsWritten);
        }

        private sealed class LiteralConverter : PatternConverter
        {
            private readonly string _literal;

            public LiteralConverter(string literal)
            {
                _literal = literal;
            }

            public override bool TryConvert(in LogEvent logEvent, Span<char> destination, out int charsWritten)
            {
                if (_literal.Length > destination.Length)
                {
                    charsWritten = 0;
                    return false;
                }

                _literal.AsSpan().CopyTo(destination);
                charsWritten = _literal.Length;
                return true;
            }
        }

        private sealed class MessageConverter : PatternConverter
        {
            public override bool TryConvert(in LogEvent logEvent, Span<char> destination, out int charsWritten)
            {
                ReadOnlySpan<char> messageSpan = logEvent.Message.AsSpan();
                if (messageSpan.Length > destination.Length)
                {
                    charsWritten = 0;
                    return false;
                }

                messageSpan.CopyTo(destination);
                charsWritten = messageSpan.Length;
                return true;
            }
        }

        private sealed class LevelConverter : PatternConverter
        {
            private readonly bool _useColorTags;

            public LevelConverter(bool useColorTags)
            {
                _useColorTags = useColorTags;
            }

            public override bool TryConvert(in LogEvent logEvent, Span<char> destination, out int charsWritten)
            {
                if (_useColorTags)
                {
                    string colorHex = logEvent.Level switch
                    {
                        LogLevel.Trace => ColorTrace,
                        LogLevel.Debug => ColorDebug,
                        LogLevel.Info  => ColorInfo,
                        LogLevel.Warn  => ColorWarn,
                        LogLevel.Error => ColorError,
                        LogLevel.Fatal => ColorFatal,
                        _ => ColorInfo
                    };

                    ReadOnlySpan<char> levelName = GetLevelSpan(logEvent.Level);

                    // Format: <color=#RRGGBB>LEVEL</color>
                    int requiredLength = 7 + colorHex.Length + 1 + levelName.Length + 8; // exactly 24 + levelName.Length
                    if (destination.Length < requiredLength)
                    {
                        charsWritten = 0;
                        return false;
                    }

                    int pos = 0;
                    "<color=".AsSpan().CopyTo(destination[pos..]); pos += 7;
                    colorHex.AsSpan().CopyTo(destination[pos..]); pos += colorHex.Length;
                    destination[pos++] = '>';
                    levelName.CopyTo(destination[pos..]); pos += levelName.Length;
                    "</color>".AsSpan().CopyTo(destination[pos..]); pos += 8;

                    charsWritten = pos;
                }
                else
                {
                    ReadOnlySpan<char> levelSpan = GetLevelSpan(logEvent.Level);
                    if (levelSpan.Length > destination.Length)
                    {
                        charsWritten = 0;
                        return false;
                    }

                    levelSpan.CopyTo(destination);
                    charsWritten = levelSpan.Length;
                }

                return true;
            }

            private static ReadOnlySpan<char> GetLevelSpan(LogLevel level) => level switch
            {
                LogLevel.Trace => "TRACE",
                LogLevel.Debug => "DEBUG",
                LogLevel.Info  => "INFO",
                LogLevel.Warn  => "WARN",
                LogLevel.Error => "ERROR",
                LogLevel.Fatal => "FATAL",
                _ => "LOG"
            };
        }

        private sealed class LoggerConverter : PatternConverter
        {
            public override bool TryConvert(in LogEvent logEvent, Span<char> destination, out int charsWritten)
            {
                ReadOnlySpan<char> nameSpan = logEvent.LoggerName.AsSpan();
                if (nameSpan.Length > destination.Length)
                {
                    charsWritten = 0;
                    return false;
                }

                nameSpan.CopyTo(destination);
                charsWritten = nameSpan.Length;
                return true;
            }
        }

        private sealed class ThreadConverter : PatternConverter
        {
            public override bool TryConvert(in LogEvent logEvent, Span<char> destination, out int charsWritten) =>
                logEvent.ThreadId.TryFormat(destination, out charsWritten);
        }

        private sealed class NewlineConverter : PatternConverter
        {
            public override bool TryConvert(in LogEvent logEvent, Span<char> destination, out int charsWritten)
            {
                if (destination.Length < 1)
                {
                    charsWritten = 0;
                    return false;
                }

                destination[0] = '\n';
                charsWritten = 1;
                return true;
            }
        }

        private sealed class ExceptionConverter : PatternConverter
        {
            public override bool TryConvert(in LogEvent logEvent, Span<char> destination, out int charsWritten)
            {
                if (!logEvent.HasException)
                {
                    charsWritten = 0;
                    return true;
                }

                string exString = logEvent.Exception.ToString();
                int required = 15 + exString.Length; // "\n--> Exception: " is 15 chars

                if (destination.Length < required)
                {
                    charsWritten = 0;
                    return false;
                }

                "\n--> Exception: ".AsSpan().CopyTo(destination);
                exString.AsSpan().CopyTo(destination[15..]);

                charsWritten = required;
                return true;
            }
        }

        private sealed class FastTimeConverter : PatternConverter
        {
            public override bool TryConvert(in LogEvent logEvent, Span<char> destination, out int charsWritten)
            {
                if (destination.Length < 12)
                {
                    charsWritten = 0;
                    return false;
                }

                DateTime time = logEvent.TimestampUtc.ToLocalTime();

                int hour = time.Hour;
                destination[0] = (char)('0' + hour / 10);
                destination[1] = (char)('0' + hour % 10);
                destination[2] = ':';

                int minute = time.Minute;
                destination[3] = (char)('0' + minute / 10);
                destination[4] = (char)('0' + minute % 10);
                destination[5] = ':';

                int second = time.Second;
                destination[6] = (char)('0' + second / 10);
                destination[7] = (char)('0' + second % 10);
                destination[8] = '.';

                int ms = time.Millisecond;
                destination[9] = (char)('0' + ms / 100);
                destination[10] = (char)('0' + ms / 10 % 10);
                destination[11] = (char)('0' + ms % 10);

                charsWritten = 12;
                return true;
            }
        }

        private sealed class CustomDateConverter : PatternConverter
        {
            private readonly string _format;

            public CustomDateConverter(string format)
            {
                _format = format;
            }

            public override bool TryConvert(in LogEvent logEvent, Span<char> destination, out int charsWritten)
            {
                DateTime time = logEvent.TimestampUtc.ToLocalTime();
                return time.TryFormat(destination, out charsWritten, _format);
            }
        }

        #endregion

        #region Compiler & Parser

        private static class PatternParser
        {
            public static PatternConverter[] Parse(string pattern, bool useColorTags)
            {
                var list = new List<PatternConverter>();
                ReadOnlySpan<char> span = pattern.AsSpan();
                int i = 0;
                int literalStart = 0;

                while (i < span.Length)
                {
                    if (span[i] == '%')
                    {
                        if (i > literalStart)
                        {
                            list.Add(new LiteralConverter(span.Slice(literalStart, i - literalStart).ToString()));
                        }

                        i++; // Skip '%'
                        if (i >= span.Length) break;

                        char token = span[i];
                        switch (token)
                        {
                            case '%':
                                list.Add(new LiteralConverter("%"));
                                i++;
                                break;
                            case 'm':
                                list.Add(new MessageConverter());
                                i++;
                                break;
                            case 'p':
                                list.Add(new LevelConverter(useColorTags));
                                i++;
                                break;
                            case 'c':
                                list.Add(new LoggerConverter());
                                i++;
                                break;
                            case 't':
                                list.Add(new ThreadConverter());
                                i++;
                                break;
                            case 'n':
                                list.Add(new NewlineConverter());
                                i++;
                                break;
                            case 'd':
                                i++;
                                if (i < span.Length && span[i] == '{')
                                {
                                    int closeBrace = span[i..].IndexOf('}');
                                    if (closeBrace > 0)
                                    {
                                        string dateFormat = span.Slice(i + 1, closeBrace - 1).ToString();
                                        if (dateFormat == "HH:mm:ss.fff")
                                            list.Add(new FastTimeConverter());
                                        else
                                            list.Add(new CustomDateConverter(dateFormat));

                                        i += closeBrace + 1;
                                    }
                                    else
                                    {
                                        list.Add(new CustomDateConverter("yyyy-MM-dd HH:mm:ss.fff"));
                                    }
                                }
                                else
                                {
                                    list.Add(new CustomDateConverter("yyyy-MM-dd HH:mm:ss.fff"));
                                }
                                break;
                            case 'e':
                                if (i + 1 < span.Length && span[i + 1] == 'x')
                                {
                                    list.Add(new ExceptionConverter());
                                    i += 2;
                                }
                                else
                                {
                                    list.Add(new LiteralConverter("%e"));
                                    i++;
                                }
                                break;
                            default:
                                list.Add(new LiteralConverter("%" + token));
                                i++;
                                break;
                        }

                        literalStart = i;
                    }
                    else
                    {
                        i++;
                    }
                }

                if (literalStart < span.Length)
                {
                    list.Add(new LiteralConverter(span[literalStart..].ToString()));
                }

                return list.ToArray();
            }
        }

        #endregion
    }
}
