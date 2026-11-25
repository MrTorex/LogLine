using System;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using UnityEngine;

namespace LogLine.Core
{
    /// <summary>
    /// Thread-safe category logger implementation providing fast-path filtering,
    /// zero-allocation message dispatching, and compile-time call-site stripping.
    /// </summary>
    /// <remarks>
    /// Hold a concrete <see cref="Logger"/> reference (e.g. via <c>var</c>) to enable C# <see cref="ConditionalAttribute"/>
    /// call-site stripping. Calls made through the <see cref="ILogger"/> interface bypass compile-time call-site stripping,
    /// though their method bodies remain stripped below <see cref="LogLineDefines.CompiledMinimumLevel"/>.
    /// </remarks>
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
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool IsEnabled(LogLevel level)
        {
            if (level == LogLevel.None)
            {
                return false;
            }

#if LOGLINE_MIN_TRACE
            return (int)level >= _cachedMinimumLevel;
#elif LOGLINE_MIN_DEBUG || LOGLINE_MIN_INFO || LOGLINE_MIN_WARN || LOGLINE_MIN_ERROR || LOGLINE_MIN_FATAL
            return level >= LogLineDefines.CompiledMinimumLevel && (int)level >= _cachedMinimumLevel;
#else
            return false;
#endif
        }

        #endregion

        #region Trace API

        /// <inheritdoc cref="ILogger.Trace(string, UnityEngine.Object)" />
        [Conditional(LogLineDefines.MinTrace)]
        [HideInCallstack]
        public void Trace(string message, UnityEngine.Object context = null)
        {
            if (!IsEnabled(LogLevel.Trace)) return;
            LogLineCore.Dispatch(LogLevel.Trace, Name, message, null, context);
        }

        /// <inheritdoc cref="ILogger.Trace{T1}(string, in T1, UnityEngine.Object)" />
        [Conditional(LogLineDefines.MinTrace)]
        [HideInCallstack]
        public void Trace<T1>(string format, in T1 arg1, UnityEngine.Object context = null)
        {
            if (!IsEnabled(LogLevel.Trace)) return;
            LogLineCore.Dispatch(LogLevel.Trace, Name, format, in arg1, context);
        }

        /// <inheritdoc cref="ILogger.Trace{T1, T2}(string, in T1, in T2, UnityEngine.Object)" />
        [Conditional(LogLineDefines.MinTrace)]
        [HideInCallstack]
        public void Trace<T1, T2>(string format, in T1 arg1, in T2 arg2, UnityEngine.Object context = null)
        {
            if (!IsEnabled(LogLevel.Trace)) return;
            LogLineCore.Dispatch(LogLevel.Trace, Name, format, in arg1, in arg2, context);
        }

        /// <inheritdoc cref="ILogger.Trace{T1, T2, T3}(string, in T1, in T2, in T3, UnityEngine.Object)" />
        [Conditional(LogLineDefines.MinTrace)]
        [HideInCallstack]
        public void Trace<T1, T2, T3>(string format, in T1 arg1, in T2 arg2, in T3 arg3, UnityEngine.Object context = null)
        {
            if (!IsEnabled(LogLevel.Trace)) return;
            LogLineCore.Dispatch(LogLevel.Trace, Name, format, in arg1, in arg2, in arg3, context);
        }

        #endregion

        #region Debug API

        /// <inheritdoc cref="ILogger.Debug(string, UnityEngine.Object)" />
        [Conditional(LogLineDefines.MinTrace), Conditional(LogLineDefines.MinDebug)]
        [HideInCallstack]
        public void Debug(string message, UnityEngine.Object context = null)
        {
            if (!IsEnabled(LogLevel.Debug)) return;
            LogLineCore.Dispatch(LogLevel.Debug, Name, message, null, context);
        }

        /// <inheritdoc cref="ILogger.Debug{T1}(string, in T1, UnityEngine.Object)" />
        [Conditional(LogLineDefines.MinTrace), Conditional(LogLineDefines.MinDebug)]
        [HideInCallstack]
        public void Debug<T1>(string format, in T1 arg1, UnityEngine.Object context = null)
        {
            if (!IsEnabled(LogLevel.Debug)) return;
            LogLineCore.Dispatch(LogLevel.Debug, Name, format, in arg1, context);
        }

        /// <inheritdoc cref="ILogger.Debug{T1, T2}(string, in T1, in T2, UnityEngine.Object)" />
        [Conditional(LogLineDefines.MinTrace), Conditional(LogLineDefines.MinDebug)]
        [HideInCallstack]
        public void Debug<T1, T2>(string format, in T1 arg1, in T2 arg2, UnityEngine.Object context = null)
        {
            if (!IsEnabled(LogLevel.Debug)) return;
            LogLineCore.Dispatch(LogLevel.Debug, Name, format, in arg1, in arg2, context);
        }

        /// <inheritdoc cref="ILogger.Debug{T1, T2, T3}(string, in T1, in T2, in T3, UnityEngine.Object)" />
        [Conditional(LogLineDefines.MinTrace), Conditional(LogLineDefines.MinDebug)]
        [HideInCallstack]
        public void Debug<T1, T2, T3>(string format, in T1 arg1, in T2 arg2, in T3 arg3, UnityEngine.Object context = null)
        {
            if (!IsEnabled(LogLevel.Debug)) return;
            LogLineCore.Dispatch(LogLevel.Debug, Name, format, in arg1, in arg2, in arg3, context);
        }

        #endregion

        #region Info API

        /// <inheritdoc cref="ILogger.Info(string, UnityEngine.Object)" />
        [Conditional(LogLineDefines.MinTrace), Conditional(LogLineDefines.MinDebug), Conditional(LogLineDefines.MinInfo)]
        [HideInCallstack]
        public void Info(string message, UnityEngine.Object context = null)
        {
            if (!IsEnabled(LogLevel.Info)) return;
            LogLineCore.Dispatch(LogLevel.Info, Name, message, null, context);
        }

        /// <inheritdoc cref="ILogger.Info{T1}(string, in T1, UnityEngine.Object)" />
        [Conditional(LogLineDefines.MinTrace), Conditional(LogLineDefines.MinDebug), Conditional(LogLineDefines.MinInfo)]
        [HideInCallstack]
        public void Info<T1>(string format, in T1 arg1, UnityEngine.Object context = null)
        {
            if (!IsEnabled(LogLevel.Info)) return;
            LogLineCore.Dispatch(LogLevel.Info, Name, format, in arg1, context);
        }

        /// <inheritdoc cref="ILogger.Info{T1, T2}(string, in T1, in T2, UnityEngine.Object)" />
        [Conditional(LogLineDefines.MinTrace), Conditional(LogLineDefines.MinDebug), Conditional(LogLineDefines.MinInfo)]
        [HideInCallstack]
        public void Info<T1, T2>(string format, in T1 arg1, in T2 arg2, UnityEngine.Object context = null)
        {
            if (!IsEnabled(LogLevel.Info)) return;
            LogLineCore.Dispatch(LogLevel.Info, Name, format, in arg1, in arg2, context);
        }

        /// <inheritdoc cref="ILogger.Info{T1, T2, T3}(string, in T1, in T2, in T3, UnityEngine.Object)" />
        [Conditional(LogLineDefines.MinTrace), Conditional(LogLineDefines.MinDebug), Conditional(LogLineDefines.MinInfo)]
        [HideInCallstack]
        public void Info<T1, T2, T3>(string format, in T1 arg1, in T2 arg2, in T3 arg3, UnityEngine.Object context = null)
        {
            if (!IsEnabled(LogLevel.Info)) return;
            LogLineCore.Dispatch(LogLevel.Info, Name, format, in arg1, in arg2, in arg3, context);
        }

        #endregion

        #region Warn API

        /// <inheritdoc cref="ILogger.Warn(string, UnityEngine.Object)" />
        [Conditional(LogLineDefines.MinTrace), Conditional(LogLineDefines.MinDebug), Conditional(LogLineDefines.MinInfo), Conditional(LogLineDefines.MinWarn)]
        [HideInCallstack]
        public void Warn(string message, UnityEngine.Object context = null)
        {
            if (!IsEnabled(LogLevel.Warn)) return;
            LogLineCore.Dispatch(LogLevel.Warn, Name, message, null, context);
        }

        /// <inheritdoc cref="ILogger.Warn{T1}(string, in T1, UnityEngine.Object)" />
        [Conditional(LogLineDefines.MinTrace), Conditional(LogLineDefines.MinDebug), Conditional(LogLineDefines.MinInfo), Conditional(LogLineDefines.MinWarn)]
        [HideInCallstack]
        public void Warn<T1>(string format, in T1 arg1, UnityEngine.Object context = null)
        {
            if (!IsEnabled(LogLevel.Warn)) return;
            LogLineCore.Dispatch(LogLevel.Warn, Name, format, in arg1, context);
        }

        /// <inheritdoc cref="ILogger.Warn{T1, T2}(string, in T1, in T2, UnityEngine.Object)" />
        [Conditional(LogLineDefines.MinTrace), Conditional(LogLineDefines.MinDebug), Conditional(LogLineDefines.MinInfo), Conditional(LogLineDefines.MinWarn)]
        [HideInCallstack]
        public void Warn<T1, T2>(string format, in T1 arg1, in T2 arg2, UnityEngine.Object context = null)
        {
            if (!IsEnabled(LogLevel.Warn)) return;
            LogLineCore.Dispatch(LogLevel.Warn, Name, format, in arg1, in arg2, context);
        }

        /// <inheritdoc cref="ILogger.Warn{T1, T2, T3}(string, in T1, in T2, in T3, UnityEngine.Object)" />
        [Conditional(LogLineDefines.MinTrace), Conditional(LogLineDefines.MinDebug), Conditional(LogLineDefines.MinInfo), Conditional(LogLineDefines.MinWarn)]
        [HideInCallstack]
        public void Warn<T1, T2, T3>(string format, in T1 arg1, in T2 arg2, in T3 arg3, UnityEngine.Object context = null)
        {
            if (!IsEnabled(LogLevel.Warn)) return;
            LogLineCore.Dispatch(LogLevel.Warn, Name, format, in arg1, in arg2, in arg3, context);
        }

        #endregion

        #region Error API

        /// <inheritdoc cref="ILogger.Error(string, UnityEngine.Object)" />
        [Conditional(LogLineDefines.MinTrace), Conditional(LogLineDefines.MinDebug), Conditional(LogLineDefines.MinInfo), Conditional(LogLineDefines.MinWarn), Conditional(LogLineDefines.MinError)]
        [HideInCallstack]
        public void Error(string message, UnityEngine.Object context = null)
        {
            if (!IsEnabled(LogLevel.Error)) return;
            LogLineCore.Dispatch(LogLevel.Error, Name, message, null, context);
        }

        /// <inheritdoc cref="ILogger.Error(Exception, string, UnityEngine.Object)" />
        [Conditional(LogLineDefines.MinTrace), Conditional(LogLineDefines.MinDebug), Conditional(LogLineDefines.MinInfo), Conditional(LogLineDefines.MinWarn), Conditional(LogLineDefines.MinError)]
        [HideInCallstack]
        public void Error(Exception exception, string message = null, UnityEngine.Object context = null)
        {
            if (!IsEnabled(LogLevel.Error)) return;
            LogLineCore.Dispatch(LogLevel.Error, Name, message, exception, context);
        }

        /// <inheritdoc cref="ILogger.Error{T1}(string, in T1, UnityEngine.Object)" />
        [Conditional(LogLineDefines.MinTrace), Conditional(LogLineDefines.MinDebug), Conditional(LogLineDefines.MinInfo), Conditional(LogLineDefines.MinWarn), Conditional(LogLineDefines.MinError)]
        [HideInCallstack]
        public void Error<T1>(string format, in T1 arg1, UnityEngine.Object context = null)
        {
            if (!IsEnabled(LogLevel.Error)) return;
            LogLineCore.Dispatch(LogLevel.Error, Name, format, in arg1, context);
        }

        /// <inheritdoc cref="ILogger.Error{T1, T2}(string, in T1, in T2, UnityEngine.Object)" />
        [Conditional(LogLineDefines.MinTrace), Conditional(LogLineDefines.MinDebug), Conditional(LogLineDefines.MinInfo), Conditional(LogLineDefines.MinWarn), Conditional(LogLineDefines.MinError)]
        [HideInCallstack]
        public void Error<T1, T2>(string format, in T1 arg1, in T2 arg2, UnityEngine.Object context = null)
        {
            if (!IsEnabled(LogLevel.Error)) return;
            LogLineCore.Dispatch(LogLevel.Error, Name, format, in arg1, in arg2, context);
        }

        /// <inheritdoc cref="ILogger.Error{T1, T2, T3}(string, in T1, in T2, in T3, UnityEngine.Object)" />
        [Conditional(LogLineDefines.MinTrace), Conditional(LogLineDefines.MinDebug), Conditional(LogLineDefines.MinInfo), Conditional(LogLineDefines.MinWarn), Conditional(LogLineDefines.MinError)]
        [HideInCallstack]
        public void Error<T1, T2, T3>(string format, in T1 arg1, in T2 arg2, in T3 arg3, UnityEngine.Object context = null)
        {
            if (!IsEnabled(LogLevel.Error)) return;
            LogLineCore.Dispatch(LogLevel.Error, Name, format, in arg1, in arg2, in arg3, context);
        }

        /// <inheritdoc cref="ILogger.Error{T1}(Exception, string, in T1, UnityEngine.Object)" />
        [Conditional(LogLineDefines.MinTrace), Conditional(LogLineDefines.MinDebug), Conditional(LogLineDefines.MinInfo), Conditional(LogLineDefines.MinWarn), Conditional(LogLineDefines.MinError)]
        [HideInCallstack]
        public void Error<T1>(Exception exception, string format, in T1 arg1, UnityEngine.Object context = null)
        {
            if (!IsEnabled(LogLevel.Error)) return;
            LogLineCore.Dispatch(LogLevel.Error, Name, exception, format, in arg1, context);
        }

        /// <inheritdoc cref="ILogger.Error{T1, T2}(Exception, string, in T1, in T2, UnityEngine.Object)" />
        [Conditional(LogLineDefines.MinTrace), Conditional(LogLineDefines.MinDebug), Conditional(LogLineDefines.MinInfo), Conditional(LogLineDefines.MinWarn), Conditional(LogLineDefines.MinError)]
        [HideInCallstack]
        public void Error<T1, T2>(Exception exception, string format, in T1 arg1, in T2 arg2, UnityEngine.Object context = null)
        {
            if (!IsEnabled(LogLevel.Error)) return;
            LogLineCore.Dispatch(LogLevel.Error, Name, exception, format, in arg1, in arg2, context);
        }

        /// <inheritdoc cref="ILogger.Error{T1, T2, T3}(Exception, string, in T1, in T2, in T3, UnityEngine.Object)" />
        [Conditional(LogLineDefines.MinTrace), Conditional(LogLineDefines.MinDebug), Conditional(LogLineDefines.MinInfo), Conditional(LogLineDefines.MinWarn), Conditional(LogLineDefines.MinError)]
        [HideInCallstack]
        public void Error<T1, T2, T3>(Exception exception, string format, in T1 arg1, in T2 arg2, in T3 arg3, UnityEngine.Object context = null)
        {
            if (!IsEnabled(LogLevel.Error)) return;
            LogLineCore.Dispatch(LogLevel.Error, Name, exception, format, in arg1, in arg2, in arg3, context);
        }

        #endregion

        #region Fatal API

        /// <inheritdoc cref="ILogger.Fatal(string, UnityEngine.Object)" />
        [Conditional(LogLineDefines.MinTrace), Conditional(LogLineDefines.MinDebug), Conditional(LogLineDefines.MinInfo), Conditional(LogLineDefines.MinWarn), Conditional(LogLineDefines.MinError), Conditional(LogLineDefines.MinFatal)]
        [HideInCallstack]
        public void Fatal(string message, UnityEngine.Object context = null)
        {
            if (!IsEnabled(LogLevel.Fatal)) return;
            LogLineCore.Dispatch(LogLevel.Fatal, Name, message, null, context);
        }

        /// <inheritdoc cref="ILogger.Fatal(Exception, string, UnityEngine.Object)" />
        [Conditional(LogLineDefines.MinTrace), Conditional(LogLineDefines.MinDebug), Conditional(LogLineDefines.MinInfo), Conditional(LogLineDefines.MinWarn), Conditional(LogLineDefines.MinError), Conditional(LogLineDefines.MinFatal)]
        [HideInCallstack]
        public void Fatal(Exception exception, string message = null, UnityEngine.Object context = null)
        {
            if (!IsEnabled(LogLevel.Fatal)) return;
            LogLineCore.Dispatch(LogLevel.Fatal, Name, message, exception, context);
        }

        /// <inheritdoc cref="ILogger.Fatal{T1}(string, in T1, UnityEngine.Object)" />
        [Conditional(LogLineDefines.MinTrace), Conditional(LogLineDefines.MinDebug), Conditional(LogLineDefines.MinInfo), Conditional(LogLineDefines.MinWarn), Conditional(LogLineDefines.MinError), Conditional(LogLineDefines.MinFatal)]
        [HideInCallstack]
        public void Fatal<T1>(string format, in T1 arg1, UnityEngine.Object context = null)
        {
            if (!IsEnabled(LogLevel.Fatal)) return;
            LogLineCore.Dispatch(LogLevel.Fatal, Name, format, in arg1, context);
        }

        /// <inheritdoc cref="ILogger.Fatal{T1, T2}(string, in T1, in T2, UnityEngine.Object)" />
        [Conditional(LogLineDefines.MinTrace), Conditional(LogLineDefines.MinDebug), Conditional(LogLineDefines.MinInfo), Conditional(LogLineDefines.MinWarn), Conditional(LogLineDefines.MinError), Conditional(LogLineDefines.MinFatal)]
        [HideInCallstack]
        public void Fatal<T1, T2>(string format, in T1 arg1, in T2 arg2, UnityEngine.Object context = null)
        {
            if (!IsEnabled(LogLevel.Fatal)) return;
            LogLineCore.Dispatch(LogLevel.Fatal, Name, format, in arg1, in arg2, context);
        }

        /// <inheritdoc cref="ILogger.Fatal{T1, T2, T3}(string, in T1, in T2, in T3, UnityEngine.Object)" />
        [Conditional(LogLineDefines.MinTrace), Conditional(LogLineDefines.MinDebug), Conditional(LogLineDefines.MinInfo), Conditional(LogLineDefines.MinWarn), Conditional(LogLineDefines.MinError), Conditional(LogLineDefines.MinFatal)]
        [HideInCallstack]
        public void Fatal<T1, T2, T3>(string format, in T1 arg1, in T2 arg2, in T3 arg3, UnityEngine.Object context = null)
        {
            if (!IsEnabled(LogLevel.Fatal)) return;
            LogLineCore.Dispatch(LogLevel.Fatal, Name, format, in arg1, in arg2, in arg3, context);
        }

        /// <inheritdoc cref="ILogger.Fatal{T1}(Exception, string, in T1, UnityEngine.Object)" />
        [Conditional(LogLineDefines.MinTrace), Conditional(LogLineDefines.MinDebug), Conditional(LogLineDefines.MinInfo), Conditional(LogLineDefines.MinWarn), Conditional(LogLineDefines.MinError), Conditional(LogLineDefines.MinFatal)]
        [HideInCallstack]
        public void Fatal<T1>(Exception exception, string format, in T1 arg1, UnityEngine.Object context = null)
        {
            if (!IsEnabled(LogLevel.Fatal)) return;
            LogLineCore.Dispatch(LogLevel.Fatal, Name, exception, format, in arg1, context);
        }

        /// <inheritdoc cref="ILogger.Fatal{T1, T2}(Exception, string, in T1, in T2, UnityEngine.Object)" />
        [Conditional(LogLineDefines.MinTrace), Conditional(LogLineDefines.MinDebug), Conditional(LogLineDefines.MinInfo), Conditional(LogLineDefines.MinWarn), Conditional(LogLineDefines.MinError), Conditional(LogLineDefines.MinFatal)]
        [HideInCallstack]
        public void Fatal<T1, T2>(Exception exception, string format, in T1 arg1, in T2 arg2, UnityEngine.Object context = null)
        {
            if (!IsEnabled(LogLevel.Fatal)) return;
            LogLineCore.Dispatch(LogLevel.Fatal, Name, exception, format, in arg1, in arg2, context);
        }

        /// <inheritdoc cref="ILogger.Fatal{T1, T2, T3}(Exception, string, in T1, in T2, in T3, UnityEngine.Object)" />
        [Conditional(LogLineDefines.MinTrace), Conditional(LogLineDefines.MinDebug), Conditional(LogLineDefines.MinInfo), Conditional(LogLineDefines.MinWarn), Conditional(LogLineDefines.MinError), Conditional(LogLineDefines.MinFatal)]
        [HideInCallstack]
        public void Fatal<T1, T2, T3>(Exception exception, string format, in T1 arg1, in T2 arg2, in T3 arg3, UnityEngine.Object context = null)
        {
            if (!IsEnabled(LogLevel.Fatal)) return;
            LogLineCore.Dispatch(LogLevel.Fatal, Name, exception, format, in arg1, in arg2, in arg3, context);
        }

        #endregion

        #region Explicit ILogger Implementation

        [HideInCallstack]
        void ILogger.Trace(string message, UnityEngine.Object context)
        {
#if LOGLINE_MIN_TRACE
            if (!IsEnabled(LogLevel.Trace)) return;
            LogLineCore.Dispatch(LogLevel.Trace, Name, message, null, context);
#endif
        }

        [HideInCallstack]
        void ILogger.Trace<T1>(string format, in T1 arg1, UnityEngine.Object context)
        {
#if LOGLINE_MIN_TRACE
            if (!IsEnabled(LogLevel.Trace)) return;
            LogLineCore.Dispatch(LogLevel.Trace, Name, format, in arg1, context);
#endif
        }

        [HideInCallstack]
        void ILogger.Trace<T1, T2>(string format, in T1 arg1, in T2 arg2, UnityEngine.Object context)
        {
#if LOGLINE_MIN_TRACE
            if (!IsEnabled(LogLevel.Trace)) return;
            LogLineCore.Dispatch(LogLevel.Trace, Name, format, in arg1, in arg2, context);
#endif
        }

        [HideInCallstack]
        void ILogger.Trace<T1, T2, T3>(string format, in T1 arg1, in T2 arg2, in T3 arg3, UnityEngine.Object context)
        {
#if LOGLINE_MIN_TRACE
            if (!IsEnabled(LogLevel.Trace)) return;
            LogLineCore.Dispatch(LogLevel.Trace, Name, format, in arg1, in arg2, in arg3, context);
#endif
        }

        [HideInCallstack]
        void ILogger.Debug(string message, UnityEngine.Object context)
        {
#if LOGLINE_MIN_TRACE || LOGLINE_MIN_DEBUG
            if (!IsEnabled(LogLevel.Debug)) return;
            LogLineCore.Dispatch(LogLevel.Debug, Name, message, null, context);
#endif
        }

        [HideInCallstack]
        void ILogger.Debug<T1>(string format, in T1 arg1, UnityEngine.Object context)
        {
#if LOGLINE_MIN_TRACE || LOGLINE_MIN_DEBUG
            if (!IsEnabled(LogLevel.Debug)) return;
            LogLineCore.Dispatch(LogLevel.Debug, Name, format, in arg1, context);
#endif
        }

        [HideInCallstack]
        void ILogger.Debug<T1, T2>(string format, in T1 arg1, in T2 arg2, UnityEngine.Object context)
        {
#if LOGLINE_MIN_TRACE || LOGLINE_MIN_DEBUG
            if (!IsEnabled(LogLevel.Debug)) return;
            LogLineCore.Dispatch(LogLevel.Debug, Name, format, in arg1, in arg2, context);
#endif
        }

        [HideInCallstack]
        void ILogger.Debug<T1, T2, T3>(string format, in T1 arg1, in T2 arg2, in T3 arg3, UnityEngine.Object context)
        {
#if LOGLINE_MIN_TRACE || LOGLINE_MIN_DEBUG
            if (!IsEnabled(LogLevel.Debug)) return;
            LogLineCore.Dispatch(LogLevel.Debug, Name, format, in arg1, in arg2, in arg3, context);
#endif
        }

        [HideInCallstack]
        void ILogger.Info(string message, UnityEngine.Object context)
        {
#if LOGLINE_MIN_TRACE || LOGLINE_MIN_DEBUG || LOGLINE_MIN_INFO
            if (!IsEnabled(LogLevel.Info)) return;
            LogLineCore.Dispatch(LogLevel.Info, Name, message, null, context);
#endif
        }

        [HideInCallstack]
        void ILogger.Info<T1>(string format, in T1 arg1, UnityEngine.Object context)
        {
#if LOGLINE_MIN_TRACE || LOGLINE_MIN_DEBUG || LOGLINE_MIN_INFO
            if (!IsEnabled(LogLevel.Info)) return;
            LogLineCore.Dispatch(LogLevel.Info, Name, format, in arg1, context);
#endif
        }

        [HideInCallstack]
        void ILogger.Info<T1, T2>(string format, in T1 arg1, in T2 arg2, UnityEngine.Object context)
        {
#if LOGLINE_MIN_TRACE || LOGLINE_MIN_DEBUG || LOGLINE_MIN_INFO
            if (!IsEnabled(LogLevel.Info)) return;
            LogLineCore.Dispatch(LogLevel.Info, Name, format, in arg1, in arg2, context);
#endif
        }

        [HideInCallstack]
        void ILogger.Info<T1, T2, T3>(string format, in T1 arg1, in T2 arg2, in T3 arg3, UnityEngine.Object context)
        {
#if LOGLINE_MIN_TRACE || LOGLINE_MIN_DEBUG || LOGLINE_MIN_INFO
            if (!IsEnabled(LogLevel.Info)) return;
            LogLineCore.Dispatch(LogLevel.Info, Name, format, in arg1, in arg2, in arg3, context);
#endif
        }

        [HideInCallstack]
        void ILogger.Warn(string message, UnityEngine.Object context)
        {
#if LOGLINE_MIN_TRACE || LOGLINE_MIN_DEBUG || LOGLINE_MIN_INFO || LOGLINE_MIN_WARN
            if (!IsEnabled(LogLevel.Warn)) return;
            LogLineCore.Dispatch(LogLevel.Warn, Name, message, null, context);
#endif
        }

        [HideInCallstack]
        void ILogger.Warn<T1>(string format, in T1 arg1, UnityEngine.Object context)
        {
#if LOGLINE_MIN_TRACE || LOGLINE_MIN_DEBUG || LOGLINE_MIN_INFO || LOGLINE_MIN_WARN
            if (!IsEnabled(LogLevel.Warn)) return;
            LogLineCore.Dispatch(LogLevel.Warn, Name, format, in arg1, context);
#endif
        }

        [HideInCallstack]
        void ILogger.Warn<T1, T2>(string format, in T1 arg1, in T2 arg2, UnityEngine.Object context)
        {
#if LOGLINE_MIN_TRACE || LOGLINE_MIN_DEBUG || LOGLINE_MIN_INFO || LOGLINE_MIN_WARN
            if (!IsEnabled(LogLevel.Warn)) return;
            LogLineCore.Dispatch(LogLevel.Warn, Name, format, in arg1, in arg2, context);
#endif
        }

        [HideInCallstack]
        void ILogger.Warn<T1, T2, T3>(string format, in T1 arg1, in T2 arg2, in T3 arg3, UnityEngine.Object context)
        {
#if LOGLINE_MIN_TRACE || LOGLINE_MIN_DEBUG || LOGLINE_MIN_INFO || LOGLINE_MIN_WARN
            if (!IsEnabled(LogLevel.Warn)) return;
            LogLineCore.Dispatch(LogLevel.Warn, Name, format, in arg1, in arg2, in arg3, context);
#endif
        }

        [HideInCallstack]
        void ILogger.Error(string message, UnityEngine.Object context)
        {
#if LOGLINE_MIN_TRACE || LOGLINE_MIN_DEBUG || LOGLINE_MIN_INFO || LOGLINE_MIN_WARN || LOGLINE_MIN_ERROR
            if (!IsEnabled(LogLevel.Error)) return;
            LogLineCore.Dispatch(LogLevel.Error, Name, message, null, context);
#endif
        }

        [HideInCallstack]
        void ILogger.Error(Exception exception, string message, UnityEngine.Object context)
        {
#if LOGLINE_MIN_TRACE || LOGLINE_MIN_DEBUG || LOGLINE_MIN_INFO || LOGLINE_MIN_WARN || LOGLINE_MIN_ERROR
            if (!IsEnabled(LogLevel.Error)) return;
            LogLineCore.Dispatch(LogLevel.Error, Name, message, exception, context);
#endif
        }

        [HideInCallstack]
        void ILogger.Error<T1>(string format, in T1 arg1, UnityEngine.Object context)
        {
#if LOGLINE_MIN_TRACE || LOGLINE_MIN_DEBUG || LOGLINE_MIN_INFO || LOGLINE_MIN_WARN || LOGLINE_MIN_ERROR
            if (!IsEnabled(LogLevel.Error)) return;
            LogLineCore.Dispatch(LogLevel.Error, Name, format, in arg1, context);
#endif
        }

        [HideInCallstack]
        void ILogger.Error<T1, T2>(string format, in T1 arg1, in T2 arg2, UnityEngine.Object context)
        {
#if LOGLINE_MIN_TRACE || LOGLINE_MIN_DEBUG || LOGLINE_MIN_INFO || LOGLINE_MIN_WARN || LOGLINE_MIN_ERROR
            if (!IsEnabled(LogLevel.Error)) return;
            LogLineCore.Dispatch(LogLevel.Error, Name, format, in arg1, in arg2, context);
#endif
        }

        [HideInCallstack]
        void ILogger.Error<T1, T2, T3>(string format, in T1 arg1, in T2 arg2, in T3 arg3, UnityEngine.Object context)
        {
#if LOGLINE_MIN_TRACE || LOGLINE_MIN_DEBUG || LOGLINE_MIN_INFO || LOGLINE_MIN_WARN || LOGLINE_MIN_ERROR
            if (!IsEnabled(LogLevel.Error)) return;
            LogLineCore.Dispatch(LogLevel.Error, Name, format, in arg1, in arg2, in arg3, context);
#endif
        }

        [HideInCallstack]
        void ILogger.Error<T1>(Exception exception, string format, in T1 arg1, UnityEngine.Object context)
        {
#if LOGLINE_MIN_TRACE || LOGLINE_MIN_DEBUG || LOGLINE_MIN_INFO || LOGLINE_MIN_WARN || LOGLINE_MIN_ERROR
            if (!IsEnabled(LogLevel.Error)) return;
            LogLineCore.Dispatch(LogLevel.Error, Name, exception, format, in arg1, context);
#endif
        }

        [HideInCallstack]
        void ILogger.Error<T1, T2>(Exception exception, string format, in T1 arg1, in T2 arg2, UnityEngine.Object context)
        {
#if LOGLINE_MIN_TRACE || LOGLINE_MIN_DEBUG || LOGLINE_MIN_INFO || LOGLINE_MIN_WARN || LOGLINE_MIN_ERROR
            if (!IsEnabled(LogLevel.Error)) return;
            LogLineCore.Dispatch(LogLevel.Error, Name, exception, format, in arg1, in arg2, context);
#endif
        }

        [HideInCallstack]
        void ILogger.Error<T1, T2, T3>(Exception exception, string format, in T1 arg1, in T2 arg2, in T3 arg3, UnityEngine.Object context)
        {
#if LOGLINE_MIN_TRACE || LOGLINE_MIN_DEBUG || LOGLINE_MIN_INFO || LOGLINE_MIN_WARN || LOGLINE_MIN_ERROR
            if (!IsEnabled(LogLevel.Error)) return;
            LogLineCore.Dispatch(LogLevel.Error, Name, exception, format, in arg1, in arg2, in arg3, context);
#endif
        }

        [HideInCallstack]
        void ILogger.Fatal(string message, UnityEngine.Object context)
        {
#if LOGLINE_MIN_TRACE || LOGLINE_MIN_DEBUG || LOGLINE_MIN_INFO || LOGLINE_MIN_WARN || LOGLINE_MIN_ERROR || LOGLINE_MIN_FATAL
            if (!IsEnabled(LogLevel.Fatal)) return;
            LogLineCore.Dispatch(LogLevel.Fatal, Name, message, null, context);
#endif
        }

        [HideInCallstack]
        void ILogger.Fatal(Exception exception, string message, UnityEngine.Object context)
        {
#if LOGLINE_MIN_TRACE || LOGLINE_MIN_DEBUG || LOGLINE_MIN_INFO || LOGLINE_MIN_WARN || LOGLINE_MIN_ERROR || LOGLINE_MIN_FATAL
            if (!IsEnabled(LogLevel.Fatal)) return;
            LogLineCore.Dispatch(LogLevel.Fatal, Name, message, exception, context);
#endif
        }

        [HideInCallstack]
        void ILogger.Fatal<T1>(string format, in T1 arg1, UnityEngine.Object context)
        {
#if LOGLINE_MIN_TRACE || LOGLINE_MIN_DEBUG || LOGLINE_MIN_INFO || LOGLINE_MIN_WARN || LOGLINE_MIN_ERROR || LOGLINE_MIN_FATAL
            if (!IsEnabled(LogLevel.Fatal)) return;
            LogLineCore.Dispatch(LogLevel.Fatal, Name, format, in arg1, context);
#endif
        }

        [HideInCallstack]
        void ILogger.Fatal<T1, T2>(string format, in T1 arg1, in T2 arg2, UnityEngine.Object context)
        {
#if LOGLINE_MIN_TRACE || LOGLINE_MIN_DEBUG || LOGLINE_MIN_INFO || LOGLINE_MIN_WARN || LOGLINE_MIN_ERROR || LOGLINE_MIN_FATAL
            if (!IsEnabled(LogLevel.Fatal)) return;
            LogLineCore.Dispatch(LogLevel.Fatal, Name, format, in arg1, in arg2, context);
#endif
        }

        [HideInCallstack]
        void ILogger.Fatal<T1, T2, T3>(string format, in T1 arg1, in T2 arg2, in T3 arg3, UnityEngine.Object context)
        {
#if LOGLINE_MIN_TRACE || LOGLINE_MIN_DEBUG || LOGLINE_MIN_INFO || LOGLINE_MIN_WARN || LOGLINE_MIN_ERROR || LOGLINE_MIN_FATAL
            if (!IsEnabled(LogLevel.Fatal)) return;
            LogLineCore.Dispatch(LogLevel.Fatal, Name, format, in arg1, in arg2, in arg3, context);
#endif
        }

        [HideInCallstack]
        void ILogger.Fatal<T1>(Exception exception, string format, in T1 arg1, UnityEngine.Object context)
        {
#if LOGLINE_MIN_TRACE || LOGLINE_MIN_DEBUG || LOGLINE_MIN_INFO || LOGLINE_MIN_WARN || LOGLINE_MIN_ERROR || LOGLINE_MIN_FATAL
            if (!IsEnabled(LogLevel.Fatal)) return;
            LogLineCore.Dispatch(LogLevel.Fatal, Name, exception, format, in arg1, context);
#endif
        }

        [HideInCallstack]
        void ILogger.Fatal<T1, T2>(Exception exception, string format, in T1 arg1, in T2 arg2, UnityEngine.Object context)
        {
#if LOGLINE_MIN_TRACE || LOGLINE_MIN_DEBUG || LOGLINE_MIN_INFO || LOGLINE_MIN_WARN || LOGLINE_MIN_ERROR || LOGLINE_MIN_FATAL
            if (!IsEnabled(LogLevel.Fatal)) return;
            LogLineCore.Dispatch(LogLevel.Fatal, Name, exception, format, in arg1, in arg2, context);
#endif
        }

        [HideInCallstack]
        void ILogger.Fatal<T1, T2, T3>(Exception exception, string format, in T1 arg1, in T2 arg2, in T3 arg3, UnityEngine.Object context)
        {
#if LOGLINE_MIN_TRACE || LOGLINE_MIN_DEBUG || LOGLINE_MIN_INFO || LOGLINE_MIN_WARN || LOGLINE_MIN_ERROR || LOGLINE_MIN_FATAL
            if (!IsEnabled(LogLevel.Fatal)) return;
            LogLineCore.Dispatch(LogLevel.Fatal, Name, exception, format, in arg1, in arg2, in arg3, context);
#endif
        }

        #endregion
    }
}
