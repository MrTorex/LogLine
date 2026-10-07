using System;
using System.Buffers;
using LogLine.Core;
using LogLine.Layouts;
using UnityEngine;

namespace LogLine.Sinks
{
    /// <summary>
    /// Thread-safe sink that serializes log events via <see cref="ILogLayout"/>
    /// and dispatches them directly to Unity's native console.
    /// </summary>
    public sealed class UnityConsoleSink : ILogSink
    {
        #region Constants

        private const int StackBufferSize = 1024;

        #endregion

        #region Properties

        /// <inheritdoc />
        public string Name => "UnityConsole";

        /// <inheritdoc />
        public bool IsEnabled { get; set; } = true;

        /// <inheritdoc />
        public LogLevel MinimumLevel { get; set; } = LogLevel.Trace;

        /// <summary>
        /// Gets or sets the active layout responsible for formatting log events.
        /// </summary>
        public ILogLayout Layout { get; set; }

        #endregion

        #region Construction

        /// <summary>
        /// Initializes a new instance of <see cref="UnityConsoleSink"/> with an optional layout.
        /// </summary>
        /// <param name="layout">Custom layout. If null, a default colored PatternLayout is applied.</param>
        public UnityConsoleSink(ILogLayout layout = null)
        {
            if (layout != null)
            {
                Layout = layout;
            }
            else
            {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
                Layout = new PatternLayout(PatternLayout.DefaultConsolePattern, useColorTags: true);
#else
                Layout = new PatternLayout(PatternLayout.DefaultConsolePattern, useColorTags: false);
#endif
            }
        }

        #endregion

        #region ILogSink Implementation

        /// <inheritdoc />
        [HideInCallstack]
        public void Emit(in LogEvent logEvent)
        {
            if (!IsEnabled || logEvent.Level < MinimumLevel || Layout == null)
            {
                return;
            }

            // 1. Fast Path: Format directly into stack memory
            Span<char> stackBuffer = stackalloc char[StackBufferSize];
            if (Layout.TryFormat(in logEvent, stackBuffer, out int charsWritten))
            {
                string formattedString = stackBuffer[..charsWritten].ToString();
                DispatchToNativeConsole(logEvent.Level, formattedString, logEvent.Context);
                return;
            }

            // 2. Slow Path: Log message is unusually large (rent from pool)
            int poolSize = StackBufferSize * 4;
            char[] rented = ArrayPool<char>.Shared.Rent(poolSize);
            try
            {
                while (!Layout.TryFormat(in logEvent, rented, out charsWritten))
                {
                    poolSize *= 2;
                    ArrayPool<char>.Shared.Return(rented);
                    rented = ArrayPool<char>.Shared.Rent(poolSize);
                }

                string formattedString = rented.AsSpan(0, charsWritten).ToString();
                DispatchToNativeConsole(logEvent.Level, formattedString, logEvent.Context);
            }
            finally
            {
                ArrayPool<char>.Shared.Return(rented);
            }
        }

        /// <inheritdoc />
        public void Dispose() { }

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
    }
}
