using System;
using System.Buffers;
using LogLine.Core;
using LogLine.Interceptors;
using LogLine.Layouts;
using UnityEngine;

namespace LogLine.Sinks
{
    /// <summary>
    /// Thread-safe sink that serializes log events via <see cref="ILogLayout"/>
    /// and dispatches them directly to Unity's native console.
    /// </summary>
    public sealed class UnityConsoleSink : LogSinkBase
    {
        #region Constants

        private const int StackBufferSize = 1024;

        #endregion

        #region Private Fields

        private volatile ILogLayout _layout;

        #endregion

        #region Properties

        /// <inheritdoc />
        public override string Name => "UnityConsole";

        /// <summary>
        /// Gets or sets the active layout responsible for formatting log events.
        /// Swapped atomically for thread-safe runtime reconfiguration.
        /// </summary>
        public ILogLayout Layout
        {
            get => _layout;
            set => _layout = value;
        }

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

        #region LogSinkBase Implementation

        /// <inheritdoc />
        [HideInCallstack]
        protected override void Write(in LogEvent logEvent)
        {
            ILogLayout layout = Layout;
            if (layout == null)
            {
                return;
            }

            // 1. Fast Path: Format directly into stack memory
            Span<char> stackBuffer = stackalloc char[StackBufferSize];
            if (layout.TryFormat(in logEvent, stackBuffer, out int charsWritten))
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
                while (!layout.TryFormat(in logEvent, rented, out charsWritten))
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

        #endregion

        #region Native Dispatch

        [HideInCallstack]
        private static void DispatchToNativeConsole(LogLevel level, string message, UnityEngine.Object context)
        {
            LogType logType = level switch
            {
                LogLevel.Warn => LogType.Warning,
                LogLevel.Error or LogLevel.Fatal => LogType.Error,
                _ => LogType.Log
            };

            ILogHandler targetHandler = UnityLogInterceptor.OriginalHandler ?? Debug.unityLogger.logHandler;
            targetHandler.LogFormat(logType, context, "{0}", message);
        }

        #endregion
    }
}
