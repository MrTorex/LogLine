using System;
using System.Buffers;

namespace LogLine.Buffering
{
    /// <summary>
    /// High-performance zero-allocation string formatter operating on stack buffers and Span&lt;char&gt;.
    /// Avoids object boxing for common primitive value types in C# 9.
    /// </summary>
    public static class FastFormatter
    {
        #region Constants

        private const int StackBufferSize = 512;

        #endregion

        #region Formatting Overloads

        /// <summary>
        /// Formats a pattern with one generic argument without heap boxing.
        /// </summary>
        public static string Format<T1>(string format, in T1 arg1)
        {
            if (string.IsNullOrEmpty(format)) return string.Empty;

            Span<char> initialBuffer = stackalloc char[StackBufferSize];
            var writer = new BufferWriter(initialBuffer);

            try
            {
                FormatInternal(ref writer, format, in arg1);
                return writer.ToString();
            }
            finally
            {
                writer.Dispose();
            }
        }

        /// <summary>
        /// Formats a pattern with two generic arguments without heap boxing.
        /// </summary>
        public static string Format<T1, T2>(string format, in T1 arg1, in T2 arg2)
        {
            if (string.IsNullOrEmpty(format)) return string.Empty;

            Span<char> initialBuffer = stackalloc char[StackBufferSize];
            var writer = new BufferWriter(initialBuffer);

            try
            {
                FormatInternal(ref writer, format, in arg1, in arg2);
                return writer.ToString();
            }
            finally
            {
                writer.Dispose();
            }
        }

        /// <summary>
        /// Formats a pattern with three generic arguments without heap boxing.
        /// </summary>
        public static string Format<T1, T2, T3>(string format, in T1 arg1, in T2 arg2, in T3 arg3)
        {
            if (string.IsNullOrEmpty(format)) return string.Empty;

            Span<char> initialBuffer = stackalloc char[StackBufferSize];
            var writer = new BufferWriter(initialBuffer);

            try
            {
                FormatInternal(ref writer, format, in arg1, in arg2, in arg3);
                return writer.ToString();
            }
            finally
            {
                writer.Dispose();
            }
        }

        #endregion

        #region Internal Formatting Engine

        private static void FormatInternal<T1>(ref BufferWriter writer, string format, in T1 arg1)
        {
            ReadOnlySpan<char> span = format.AsSpan();
            int i = 0;

            while (i < span.Length)
            {
                int openBrace = span[i..].IndexOf('{');
                if (openBrace < 0)
                {
                    writer.Append(span[i..]);
                    break;
                }

                writer.Append(span.Slice(i, openBrace));
                i += openBrace;

                if (i + 2 < span.Length && span[i + 1] == '0' && span[i + 2] == '}')
                {
                    AppendValue(ref writer, in arg1);
                    i += 3;
                }
                else
                {
                    writer.Append('{');
                    i++;
                }
            }
        }

        private static void FormatInternal<T1, T2>(ref BufferWriter writer, string format, in T1 arg1, in T2 arg2)
        {
            ReadOnlySpan<char> span = format.AsSpan();
            int i = 0;

            while (i < span.Length)
            {
                int openBrace = span[i..].IndexOf('{');
                if (openBrace < 0)
                {
                    writer.Append(span[i..]);
                    break;
                }

                writer.Append(span.Slice(i, openBrace));
                i += openBrace;

                if (i + 2 < span.Length && span[i + 2] == '}')
                {
                    char indexChar = span[i + 1];
                    switch (indexChar)
                    {
                        case '0':
                            AppendValue(ref writer, in arg1);
                            i += 3;
                            continue;
                        case '1':
                            AppendValue(ref writer, in arg2);
                            i += 3;
                            continue;
                    }
                }

                writer.Append('{');
                i++;
            }
        }

        private static void FormatInternal<T1, T2, T3>(ref BufferWriter writer, string format, in T1 arg1, in T2 arg2, in T3 arg3)
        {
            ReadOnlySpan<char> span = format.AsSpan();
            int i = 0;

            while (i < span.Length)
            {
                int openBrace = span[i..].IndexOf('{');
                if (openBrace < 0)
                {
                    writer.Append(span[i..]);
                    break;
                }

                writer.Append(span.Slice(i, openBrace));
                i += openBrace;

                if (i + 2 < span.Length && span[i + 2] == '}')
                {
                    char indexChar = span[i + 1];
                    switch (indexChar)
                    {
                        case '0':
                            AppendValue(ref writer, in arg1);
                            i += 3;
                            continue;
                        case '1':
                            AppendValue(ref writer, in arg2);
                            i += 3;
                            continue;
                        case '2':
                            AppendValue(ref writer, in arg3);
                            i += 3;
                            continue;
                    }
                }

                writer.Append('{');
                i++;
            }
        }

        private static void AppendValue<T>(ref BufferWriter writer, in T value)
        {
            switch (value)
            {
                // Type tests against primitive types do not allocate boxing overhead in modern Roslyn/IL2CPP.
                case int intVal:
                    writer.Append(intVal);
                    return;
                case float floatVal:
                    writer.Append(floatVal);
                    return;
                case bool boolVal:
                    writer.Append(boolVal ? "True" : "False");
                    return;
                case long longVal:
                    writer.Append(longVal);
                    return;
                case string strVal:
                    writer.Append(strVal);
                    return;
                default:
                    // Fallback for custom reference or struct types (calls constrained ToString)
                    writer.Append(value?.ToString() ?? "null");
                    break;
            }
        }

        #endregion

        #region Stack Buffer Helper Struct

        /// <summary>
        /// Lightweight ref struct for zero-alloc character buffering.
        /// </summary>
        private ref struct BufferWriter
        {
            private char[] _rentedArray;
            private Span<char> _buffer;
            private int _position;

            public BufferWriter(Span<char> initialBuffer)
            {
                _rentedArray = null;
                _buffer = initialBuffer;
                _position = 0;
            }

            public void Append(ReadOnlySpan<char> value)
            {
                if (value.IsEmpty) return;

                if (_position + value.Length > _buffer.Length)
                {
                    Grow(_position + value.Length);
                }

                value.CopyTo(_buffer[_position..]);
                _position += value.Length;
            }

            public void Append(char c)
            {
                if (_position >= _buffer.Length)
                {
                    Grow(_position + 1);
                }

                _buffer[_position++] = c;
            }

            public void Append(int value)
            {
                while (true)
                {
                    Span<char> slice = _buffer[_position..];
                    if (value.TryFormat(slice, out int charsWritten))
                    {
                        _position += charsWritten;
                    }
                    else
                    {
                        Grow(_position + 16);
                        continue;
                    }

                    break;
                }
            }

            public void Append(float value)
            {
                while (true)
                {
                    Span<char> slice = _buffer[_position..];
                    if (value.TryFormat(slice, out int charsWritten))
                    {
                        _position += charsWritten;
                    }
                    else
                    {
                        Grow(_position + 32);
                        continue;
                    }

                    break;
                }
            }

            public void Append(long value)
            {
                while (true)
                {
                    Span<char> slice = _buffer[_position..];
                    if (value.TryFormat(slice, out int charsWritten))
                    {
                        _position += charsWritten;
                    }
                    else
                    {
                        Grow(_position + 24);
                        continue;
                    }

                    break;
                }
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

            private void Grow(int minCapacity)
            {
                int newCapacity = Math.Max(_buffer.Length * 2, minCapacity);
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
