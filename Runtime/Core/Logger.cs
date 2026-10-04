using System;
using UnityEngine;

namespace LogLine.Core
{
    /// <summary>
    /// Thread-safe category logger implementation providing fast-path filtering
    /// and zero-allocation message dispatching.
    /// </summary>
    public sealed class Logger : ILogger
    {
        #region Private Fields

        // Cached level enables single-instruction CPU check without locking or lookup.
        private volatile int _cachedMinimumLevel;

        #endregion

        #region Properties

        /// <inheritdoc />
        public string Name { get; }

        /// <inheritdoc />
        public LogLevel MinimumLevel => (LogLevel)_cachedMinimumLevel;

        #endregion

        #region Construction

        /// <summary>
        /// Initializes a new instance of the <see cref="Logger"/> class.
        /// </summary>
        /// <param name="name">The name/category of the logger.</param>
        /// <param name="initialLevel">The initial minimum log level filter.</param>
        internal Logger(string name, LogLevel initialLevel = LogLevel.Trace)
        {
            Name = string.IsNullOrEmpty(name) ? "Global" : name;
            _cachedMinimumLevel = (int)initialLevel;
        }

        #endregion

        #region Internal API

        /// <summary>
        /// Updates the cached minimum log level for this logger instance.
        /// Called by the centralized configuration manager.
        /// </summary>
        /// <param name="newLevel">The new minimum level threshold.</param>
        internal void SetMinimumLevel(LogLevel newLevel) => _cachedMinimumLevel = (int)newLevel;

        #endregion

        #region Level Checks

        /// <inheritdoc />
        [HideInCallstack]
        public bool IsEnabled(LogLevel level) => (int)level >= _cachedMinimumLevel;

        #endregion

        #region Trace API

        /// <inheritdoc />
        [HideInCallstack]
        public void Trace(string message, UnityEngine.Object context = null)
        {
            if (!IsEnabled(LogLevel.Trace)) return;
            LogLineCore.Dispatch(LogLevel.Trace, Name, message, null, context);
        }

        /// <inheritdoc />
        [HideInCallstack]
        public void Trace<T1>(string format, in T1 arg1, UnityEngine.Object context = null)
        {
            if (!IsEnabled(LogLevel.Trace)) return;
            LogLineCore.Dispatch(LogLevel.Trace, Name, format, arg1, context);
        }

        /// <inheritdoc />
        [HideInCallstack]
        public void Trace<T1, T2>(string format, in T1 arg1, in T2 arg2, UnityEngine.Object context = null)
        {
            if (!IsEnabled(LogLevel.Trace)) return;
            LogLineCore.Dispatch(LogLevel.Trace, Name, format, arg1, arg2, context);
        }

        /// <inheritdoc />
        [HideInCallstack]
        public void Trace<T1, T2, T3>(string format, in T1 arg1, in T2 arg2, in T3 arg3, UnityEngine.Object context = null)
        {
            if (!IsEnabled(LogLevel.Trace)) return;
            LogLineCore.Dispatch(LogLevel.Trace, Name, format, arg1, arg2, arg3, context);
        }

        #endregion

        #region Debug API

        /// <inheritdoc />
        [HideInCallstack]
        public void Debug(string message, UnityEngine.Object context = null)
        {
            if (!IsEnabled(LogLevel.Debug)) return;
            LogLineCore.Dispatch(LogLevel.Debug, Name, message, null, context);
        }

        /// <inheritdoc />
        [HideInCallstack]
        public void Debug<T1>(string format, in T1 arg1, UnityEngine.Object context = null)
        {
            if (!IsEnabled(LogLevel.Debug)) return;
            LogLineCore.Dispatch(LogLevel.Debug, Name, format, arg1, context);
        }

        /// <inheritdoc />
        [HideInCallstack]
        public void Debug<T1, T2>(string format, in T1 arg1, in T2 arg2, UnityEngine.Object context = null)
        {
            if (!IsEnabled(LogLevel.Debug)) return;
            LogLineCore.Dispatch(LogLevel.Debug, Name, format, arg1, arg2, context);
        }

        /// <inheritdoc />
        [HideInCallstack]
        public void Debug<T1, T2, T3>(string format, in T1 arg1, in T2 arg2, in T3 arg3, UnityEngine.Object context = null)
        {
            if (!IsEnabled(LogLevel.Debug)) return;
            LogLineCore.Dispatch(LogLevel.Debug, Name, format, arg1, arg2, arg3, context);
        }

        #endregion

        #region Info API

        /// <inheritdoc />
        [HideInCallstack]
        public void Info(string message, UnityEngine.Object context = null)
        {
            if (!IsEnabled(LogLevel.Info)) return;
            LogLineCore.Dispatch(LogLevel.Info, Name, message, null, context);
        }

        /// <inheritdoc />
        [HideInCallstack]
        public void Info<T1>(string format, in T1 arg1, UnityEngine.Object context = null)
        {
            if (!IsEnabled(LogLevel.Info)) return;
            LogLineCore.Dispatch(LogLevel.Info, Name, format, arg1, context);
        }

        /// <inheritdoc />
        [HideInCallstack]
        public void Info<T1, T2>(string format, in T1 arg1, in T2 arg2, UnityEngine.Object context = null)
        {
            if (!IsEnabled(LogLevel.Info)) return;
            LogLineCore.Dispatch(LogLevel.Info, Name, format, arg1, arg2, context);
        }

        /// <inheritdoc />
        [HideInCallstack]
        public void Info<T1, T2, T3>(string format, in T1 arg1, in T2 arg2, in T3 arg3, UnityEngine.Object context = null)
        {
            if (!IsEnabled(LogLevel.Info)) return;
            LogLineCore.Dispatch(LogLevel.Info, Name, format, arg1, arg2, arg3, context);
        }

        #endregion

        #region Warn API

        /// <inheritdoc />
        [HideInCallstack]
        public void Warn(string message, UnityEngine.Object context = null)
        {
            if (!IsEnabled(LogLevel.Warn)) return;
            LogLineCore.Dispatch(LogLevel.Warn, Name, message, null, context);
        }

        /// <inheritdoc />
        [HideInCallstack]
        public void Warn<T1>(string format, in T1 arg1, UnityEngine.Object context = null)
        {
            if (!IsEnabled(LogLevel.Warn)) return;
            LogLineCore.Dispatch(LogLevel.Warn, Name, format, arg1, context);
        }

        /// <inheritdoc />
        [HideInCallstack]
        public void Warn<T1, T2>(string format, in T1 arg1, in T2 arg2, UnityEngine.Object context = null)
        {
            if (!IsEnabled(LogLevel.Warn)) return;
            LogLineCore.Dispatch(LogLevel.Warn, Name, format, arg1, arg2, context);
        }

        /// <inheritdoc />
        [HideInCallstack]
        public void Warn<T1, T2, T3>(string format, in T1 arg1, in T2 arg2, in T3 arg3, UnityEngine.Object context = null)
        {
            if (!IsEnabled(LogLevel.Warn)) return;
            LogLineCore.Dispatch(LogLevel.Warn, Name, format, arg1, arg2, arg3, context);
        }

        #endregion

        #region Error API

        /// <inheritdoc />
        [HideInCallstack]
        public void Error(string message, UnityEngine.Object context = null)
        {
            if (!IsEnabled(LogLevel.Error)) return;
            LogLineCore.Dispatch(LogLevel.Error, Name, message, null, context);
        }

        /// <inheritdoc />
        [HideInCallstack]
        public void Error(Exception exception, string message = null, UnityEngine.Object context = null)
        {
            if (!IsEnabled(LogLevel.Error)) return;
            LogLineCore.Dispatch(LogLevel.Error, Name, message, exception, context);
        }

        /// <inheritdoc />
        [HideInCallstack]
        public void Error<T1>(string format, in T1 arg1, UnityEngine.Object context = null)
        {
            if (!IsEnabled(LogLevel.Error)) return;
            LogLineCore.Dispatch(LogLevel.Error, Name, format, arg1, context);
        }

        /// <inheritdoc />
        [HideInCallstack]
        public void Error<T1, T2>(string format, in T1 arg1, in T2 arg2, UnityEngine.Object context = null)
        {
            if (!IsEnabled(LogLevel.Error)) return;
            LogLineCore.Dispatch(LogLevel.Error, Name, format, arg1, arg2, context);
        }

        /// <inheritdoc />
        [HideInCallstack]
        public void Error<T1, T2, T3>(string format, in T1 arg1, in T2 arg2, in T3 arg3, UnityEngine.Object context = null)
        {
            if (!IsEnabled(LogLevel.Error)) return;
            LogLineCore.Dispatch(LogLevel.Error, Name, format, arg1, arg2, arg3, context);
        }

        /// <inheritdoc />
        [HideInCallstack]
        public void Error<T1>(Exception exception, string format, in T1 arg1, UnityEngine.Object context = null)
        {
            if (!IsEnabled(LogLevel.Error)) return;
            LogLineCore.Dispatch(LogLevel.Error, Name, exception, format, arg1, context);
        }

        /// <inheritdoc />
        [HideInCallstack]
        public void Error<T1, T2>(Exception exception, string format, in T1 arg1, in T2 arg2, UnityEngine.Object context = null)
        {
            if (!IsEnabled(LogLevel.Error)) return;
            LogLineCore.Dispatch(LogLevel.Error, Name, exception, format, arg1, arg2, context);
        }

        /// <inheritdoc />
        [HideInCallstack]
        public void Error<T1, T2, T3>(Exception exception, string format, in T1 arg1, in T2 arg2, in T3 arg3, UnityEngine.Object context = null)
        {
            if (!IsEnabled(LogLevel.Error)) return;
            LogLineCore.Dispatch(LogLevel.Error, Name, exception, format, arg1, arg2, arg3, context);
        }

        #endregion

        #region Fatal API

        /// <inheritdoc />
        [HideInCallstack]
        public void Fatal(string message, UnityEngine.Object context = null)
        {
            if (!IsEnabled(LogLevel.Fatal)) return;
            LogLineCore.Dispatch(LogLevel.Fatal, Name, message, null, context);
        }

        /// <inheritdoc />
        [HideInCallstack]
        public void Fatal(Exception exception, string message = null, UnityEngine.Object context = null)
        {
            if (!IsEnabled(LogLevel.Fatal)) return;
            LogLineCore.Dispatch(LogLevel.Fatal, Name, message, exception, context);
        }

        /// <inheritdoc />
        [HideInCallstack]
        public void Fatal<T1>(string format, in T1 arg1, UnityEngine.Object context = null)
        {
            if (!IsEnabled(LogLevel.Fatal)) return;
            LogLineCore.Dispatch(LogLevel.Fatal, Name, format, arg1, context);
        }

        /// <inheritdoc />
        [HideInCallstack]
        public void Fatal<T1, T2>(string format, in T1 arg1, in T2 arg2, UnityEngine.Object context = null)
        {
            if (!IsEnabled(LogLevel.Fatal)) return;
            LogLineCore.Dispatch(LogLevel.Fatal, Name, format, arg1, arg2, context);
        }

        /// <inheritdoc />
        [HideInCallstack]
        public void Fatal<T1, T2, T3>(string format, in T1 arg1, in T2 arg2, in T3 arg3, UnityEngine.Object context = null)
        {
            if (!IsEnabled(LogLevel.Fatal)) return;
            LogLineCore.Dispatch(LogLevel.Fatal, Name, format, arg1, arg2, arg3, context);
        }

        /// <inheritdoc />
        [HideInCallstack]
        public void Fatal<T1>(Exception exception, string format, in T1 arg1, UnityEngine.Object context = null)
        {
            if (!IsEnabled(LogLevel.Fatal)) return;
            LogLineCore.Dispatch(LogLevel.Fatal, Name, exception, format, arg1, context);
        }

        /// <inheritdoc />
        [HideInCallstack]
        public void Fatal<T1, T2>(Exception exception, string format, in T1 arg1, in T2 arg2, UnityEngine.Object context = null)
        {
            if (!IsEnabled(LogLevel.Fatal)) return;
            LogLineCore.Dispatch(LogLevel.Fatal, Name, exception, format, arg1, arg2, context);
        }

        /// <inheritdoc />
        [HideInCallstack]
        public void Fatal<T1, T2, T3>(Exception exception, string format, in T1 arg1, in T2 arg2, in T3 arg3, UnityEngine.Object context = null)
        {
            if (!IsEnabled(LogLevel.Fatal)) return;
            LogLineCore.Dispatch(LogLevel.Fatal, Name, exception, format, arg1, arg2, arg3, context);
        }

        #endregion
    }
}
