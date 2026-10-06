using System;
using System.Collections.Concurrent;
using LogLine.Buffering;
using UnityEngine;

namespace LogLine.Core
{
    /// <summary>
    /// Internal engine for sink management, logger resolution, and zero-allocation dispatching.
    /// Not accessible directly outside the assembly; use <see cref="LogLine"/> facade.
    /// </summary>
    internal static class LogLineCore
    {
        #region Private Fields

        private static readonly object _syncLock = new();

        // Copy-On-Write array for 100% lock-free reads during logging dispatch.
        private static volatile ILogSink[] _activeSinks = Array.Empty<ILogSink>();

        // Cache of all active logger instances organized by category name.
        private static readonly ConcurrentDictionary<string, Logger> _loggers = new(StringComparer.OrdinalIgnoreCase);

        #endregion

        #region Properties

        /// <summary>
        /// Gets or sets the global fallback minimum level for newly spawned or unconfigured loggers.
        /// </summary>
        public static LogLevel GlobalMinimumLevel { get; set; } = LogLevel.Trace;

        /// <summary>
        /// Indicates whether the core engine is actively dispatching an event on the current thread.
        /// Prevents circular recursion if a sink invokes Unity's Debug.Log during dispatch.
        /// </summary>
        [field: ThreadStatic]
        internal static bool IsDispatching { get; private set; }

        #endregion

        #region Domain Reload Safety

        /// <summary>
        /// Guarantees zero memory leaks and thread safety when Domain Reload is disabled in Unity.
        /// </summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStaticState()
        {
            lock (_syncLock)
            {
                DisposeAllSinksInternal();
                _loggers.Clear();
                GlobalMinimumLevel = LogLevel.Trace;
            }
        }

        #endregion

        #region Logger Factory

        /// <summary>
        /// Resolves or registers a category-bound logger instance.
        /// </summary>
        /// <param name="category">The category identifier.</param>
        /// <returns>A thread-safe <see cref="Logger"/> instance.</returns>
        public static Logger GetLogger(string category)
        {
            string key = string.IsNullOrEmpty(category) ? "Global" : category;
            return _loggers.GetOrAdd(key, static (name) => new Logger(name, GlobalMinimumLevel));
        }

        /// <summary>
        /// Resolves or registers a logger instance using the declaring type name.
        /// </summary>
        /// <typeparam name="T">The type to infer category name from.</typeparam>
        /// <returns>A thread-safe <see cref="Logger"/> instance.</returns>
        public static Logger GetLogger<T>() => GetLogger(TypeCache<T>.Name);

        /// <summary>
        /// Provides cached reflection metadata to eliminate runtime reflection overhead.
        /// </summary>
        /// <typeparam name="T">The cached type.</typeparam>
        private static class TypeCache<T>
        {
            /// <summary>
            /// The cached short name of type <typeparamref name="T"/>.
            /// </summary>
            public static readonly string Name = typeof(T).Name;
        }

        #endregion

        #region Sink Management

        /// <summary>
        /// Registers a new output sink to the active processing pipeline.
        /// Uses atomic reference swapping to maintain lock-free reads for worker threads.
        /// </summary>
        /// <param name="sink">The sink instance to register.</param>
        public static void AddSink(ILogSink sink)
        {
            if (sink == null) return;

            lock (_syncLock)
            {
                if (Array.IndexOf(_activeSinks, sink) >= 0) return;

                var newSinks = new ILogSink[_activeSinks.Length + 1];
                Array.Copy(_activeSinks, newSinks, _activeSinks.Length);
                newSinks[^1] = sink;

                _activeSinks = newSinks;
            }
        }

        /// <summary>
        /// Removes and disposes an existing sink from the processing pipeline.
        /// </summary>
        /// <param name="sink">The sink instance to detach.</param>
        public static void RemoveSink(ILogSink sink)
        {
            if (sink == null) return;

            lock (_syncLock)
            {
                int index = Array.IndexOf(_activeSinks, sink);
                if (index < 0) return;

                var newSinks = new ILogSink[_activeSinks.Length - 1];
                Array.Copy(_activeSinks, 0, newSinks, 0, index);
                Array.Copy(_activeSinks, index + 1, newSinks, index, _activeSinks.Length - index - 1);

                _activeSinks = newSinks;
                sink.Dispose();
            }
        }

        /// <summary>
        /// Disposes all registered sinks and clears the active sinks collection.
        /// </summary>
        private static void DisposeAllSinksInternal()
        {
            ILogSink[] oldSinks = _activeSinks;
            _activeSinks = Array.Empty<ILogSink>();

            for (int i = 0; i < oldSinks.Length; i++)
            {
                ILogSink t = oldSinks[i];
                try
                {
                    t?.Dispose();
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[LogLineCore] Sink dispose fault: {ex}");
                }
            }
        }

        #endregion

        #region Dispatch Engine

        /// <summary>
        /// Dispatches a raw string message directly into the registered sinks.
        /// </summary>
        /// <param name="level">The severity level of the log.</param>
        /// <param name="category">The category name.</param>
        /// <param name="message">The raw log message.</param>
        /// <param name="exception">An optional exception associated with this event.</param>
        /// <param name="context">The contextual Unity object reference.</param>
        [HideInCallstack]
        internal static void Dispatch(
            LogLevel level,
            string category,
            string message,
            Exception exception,
            UnityEngine.Object context)
        {
            if (IsDispatching) return;

            ILogSink[] sinks = _activeSinks;
            if (sinks.Length == 0) return;

            var logEvent = new LogEvent(level, category, message, exception, context);
            EmitToSinks(sinks, in logEvent);
        }

        /// <summary>
        /// Formats and dispatches a message containing 1 argument without boxing.
        /// </summary>
        /// <typeparam name="T1">The type of the first argument.</typeparam>
        /// <param name="level">The severity level of the log.</param>
        /// <param name="category">The category name.</param>
        /// <param name="format">The composite format string.</param>
        /// <param name="arg1">The first argument to format.</param>
        /// <param name="context">The contextual Unity object reference.</param>
        [HideInCallstack]
        internal static void Dispatch<T1>(
            LogLevel level,
            string category,
            string format,
            in T1 arg1,
            UnityEngine.Object context)
        {
            if (IsDispatching) return;

            ILogSink[] sinks = _activeSinks;
            if (sinks.Length == 0) return;

            string formattedMessage = FastFormatter.Format(format, in arg1);
            var logEvent = new LogEvent(level, category, formattedMessage, null, context);
            EmitToSinks(sinks, in logEvent);
        }

        /// <summary>
        /// Formats and dispatches a message containing 2 arguments without boxing.
        /// </summary>
        /// <typeparam name="T1">The type of the first argument.</typeparam>
        /// <typeparam name="T2">The type of the second argument.</typeparam>
        /// <param name="level">The severity level of the log.</param>
        /// <param name="category">The category name.</param>
        /// <param name="format">The composite format string.</param>
        /// <param name="arg1">The first argument to format.</param>
        /// <param name="arg2">The second argument to format.</param>
        /// <param name="context">The contextual Unity object reference.</param>
        [HideInCallstack]
        internal static void Dispatch<T1, T2>(
            LogLevel level,
            string category,
            string format,
            in T1 arg1,
            in T2 arg2,
            UnityEngine.Object context)
        {
            if (IsDispatching) return;

            ILogSink[] sinks = _activeSinks;
            if (sinks.Length == 0) return;

            string formattedMessage = FastFormatter.Format(format, in arg1, in arg2);
            var logEvent = new LogEvent(level, category, formattedMessage, null, context);
            EmitToSinks(sinks, in logEvent);
        }

        /// <summary>
        /// Formats and dispatches a message containing 3 arguments without boxing.
        /// </summary>
        /// <typeparam name="T1">The type of the first argument.</typeparam>
        /// <typeparam name="T2">The type of the second argument.</typeparam>
        /// <typeparam name="T3">The type of the third argument.</typeparam>
        /// <param name="level">The severity level of the log.</param>
        /// <param name="category">The category name.</param>
        /// <param name="format">The composite format string.</param>
        /// <param name="arg1">The first argument to format.</param>
        /// <param name="arg2">The second argument to format.</param>
        /// <param name="arg3">The third argument to format.</param>
        /// <param name="context">The contextual Unity object reference.</param>
        [HideInCallstack]
        internal static void Dispatch<T1, T2, T3>(
            LogLevel level,
            string category,
            string format,
            in T1 arg1,
            in T2 arg2,
            in T3 arg3,
            UnityEngine.Object context)
        {
            if (IsDispatching) return;

            ILogSink[] sinks = _activeSinks;
            if (sinks.Length == 0) return;

            string formattedMessage = FastFormatter.Format(format, in arg1, in arg2, in arg3);
            var logEvent = new LogEvent(level, category, formattedMessage, null, context);
            EmitToSinks(sinks, in logEvent);
        }

        /// <summary>
        /// Formats and dispatches a message containing an exception and 1 argument without boxing.
        /// </summary>
        /// <typeparam name="T1">The type of the first argument.</typeparam>
        /// <param name="level">The severity level of the log.</param>
        /// <param name="category">The category name.</param>
        /// <param name="exception">The exception associated with this event.</param>
        /// <param name="format">The composite format string.</param>
        /// <param name="arg1">The first argument to format.</param>
        /// <param name="context">The contextual Unity object reference.</param>
        [HideInCallstack]
        internal static void Dispatch<T1>(
            LogLevel level,
            string category,
            Exception exception,
            string format,
            in T1 arg1,
            UnityEngine.Object context)
        {
            if (IsDispatching) return;

            ILogSink[] sinks = _activeSinks;
            if (sinks.Length == 0) return;

            string formattedMessage = FastFormatter.Format(format, in arg1);
            var logEvent = new LogEvent(level, category, formattedMessage, exception, context);
            EmitToSinks(sinks, in logEvent);
        }

        /// <summary>
        /// Formats and dispatches a message containing an exception and 2 arguments without boxing.
        /// </summary>
        /// <typeparam name="T1">The type of the first argument.</typeparam>
        /// <typeparam name="T2">The type of the second argument.</typeparam>
        /// <param name="level">The severity level of the log.</param>
        /// <param name="category">The category name.</param>
        /// <param name="exception">The exception associated with this event.</param>
        /// <param name="format">The composite format string.</param>
        /// <param name="arg1">The first argument to format.</param>
        /// <param name="arg2">The second argument to format.</param>
        /// <param name="context">The contextual Unity object reference.</param>
        [HideInCallstack]
        internal static void Dispatch<T1, T2>(
            LogLevel level,
            string category,
            Exception exception,
            string format,
            in T1 arg1,
            in T2 arg2,
            UnityEngine.Object context)
        {
            if (IsDispatching) return;

            ILogSink[] sinks = _activeSinks;
            if (sinks.Length == 0) return;

            string formattedMessage = FastFormatter.Format(format, in arg1, in arg2);
            var logEvent = new LogEvent(level, category, formattedMessage, exception, context);
            EmitToSinks(sinks, in logEvent);
        }

        /// <summary>
        /// Formats and dispatches a message containing an exception and 3 arguments without boxing.
        /// </summary>
        /// <typeparam name="T1">The type of the first argument.</typeparam>
        /// <typeparam name="T2">The type of the second argument.</typeparam>
        /// <typeparam name="T3">The type of the third argument.</typeparam>
        /// <param name="level">The severity level of the log.</param>
        /// <param name="category">The category name.</param>
        /// <param name="exception">The exception associated with this event.</param>
        /// <param name="format">The composite format string.</param>
        /// <param name="arg1">The first argument to format.</param>
        /// <param name="arg2">The second argument to format.</param>
        /// <param name="arg3">The third argument to format.</param>
        /// <param name="context">The contextual Unity object reference.</param>
        [HideInCallstack]
        internal static void Dispatch<T1, T2, T3>(
            LogLevel level,
            string category,
            Exception exception,
            string format,
            in T1 arg1,
            in T2 arg2,
            in T3 arg3,
            UnityEngine.Object context)
        {
            if (IsDispatching) return;

            ILogSink[] sinks = _activeSinks;
            if (sinks.Length == 0) return;

            string formattedMessage = FastFormatter.Format(format, in arg1, in arg2, in arg3);
            var logEvent = new LogEvent(level, category, formattedMessage, exception, context);
            EmitToSinks(sinks, in logEvent);
        }

        /// <summary>
        /// Emits a log event to each active sink that satisfies the filter criteria.
        /// </summary>
        /// <param name="sinks">The current snapshot of active sinks.</param>
        /// <param name="logEvent">The log event payload passed by reference.</param>
        [HideInCallstack]
        private static void EmitToSinks(ILogSink[] sinks, in LogEvent logEvent)
        {
            IsDispatching = true;
            try
            {
                for (int i = 0; i < sinks.Length; i++)
                {
                    ILogSink sink = sinks[i];
                    if (!sink.IsEnabled || logEvent.Level < sink.MinimumLevel)
                    {
                        continue;
                    }

                    try
                    {
                        sink.Emit(in logEvent);
                    }
                    catch (Exception sinkEx)
                    {
                        System.Diagnostics.Debug.WriteLine($"[LogLineCore] Sink '{sink.Name}' threw error: {sinkEx}");
                    }
                }
            }
            finally
            {
                IsDispatching = false;
            }
        }

        #endregion
    }
}
