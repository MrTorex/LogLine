using System;
using UnityEngine;

namespace LogLine.Core
{
    /// <summary>
    /// Contract for a category-bound logger instance.
    /// Provides zero-allocation, high-performance dispatchers with generic overloads.
    /// </summary>
    public interface ILogger
    {
        #region Properties

        /// <summary>
        /// Gets the unique category identifier of this logger.
        /// </summary>
        string Name { get; }

        /// <summary>
        /// Gets the current active minimum log level threshold.
        /// </summary>
        LogLevel MinimumLevel { get; }

        #endregion

        #region Level Checks

        /// <summary>
        /// Checks whether messages of the specified severity level are currently processed.
        /// </summary>
        /// <param name="level">The severity level to verify.</param>
        /// <returns><see langword="true"/> if enabled; otherwise, <see langword="false"/>.</returns>
        bool IsEnabled(LogLevel level);

        #endregion

        #region Trace Methods

        /// <summary>Logs a trace message.</summary>
        [HideInCallstack]
        void Trace(string message, UnityEngine.Object context = null);

        /// <summary>Logs a formatted trace message without boxing.</summary>
        [HideInCallstack]
        void Trace<T1>(string format, in T1 arg1, UnityEngine.Object context = null);

        /// <summary>Logs a formatted trace message with two arguments without boxing.</summary>
        [HideInCallstack]
        void Trace<T1, T2>(string format, in T1 arg1, in T2 arg2, UnityEngine.Object context = null);

        /// <summary>Logs a formatted trace message with three arguments without boxing.</summary>
        [HideInCallstack]
        void Trace<T1, T2, T3>(string format, in T1 arg1, in T2 arg2, in T3 arg3, UnityEngine.Object context = null);

        #endregion

        #region Debug Methods

        /// <summary>Logs a debug message.</summary>
        [HideInCallstack]
        void Debug(string message, UnityEngine.Object context = null);

        /// <summary>Logs a formatted debug message without boxing.</summary>
        [HideInCallstack]
        void Debug<T1>(string format, in T1 arg1, UnityEngine.Object context = null);

        /// <summary>Logs a formatted debug message with two arguments without boxing.</summary>
        [HideInCallstack]
        void Debug<T1, T2>(string format, in T1 arg1, in T2 arg2, UnityEngine.Object context = null);

        /// <summary>Logs a formatted debug message with three arguments without boxing.</summary>
        [HideInCallstack]
        void Debug<T1, T2, T3>(string format, in T1 arg1, in T2 arg2, in T3 arg3, UnityEngine.Object context = null);

        #endregion

        #region Info Methods

        /// <summary>Logs an informational message.</summary>
        [HideInCallstack]
        void Info(string message, UnityEngine.Object context = null);

        /// <summary>Logs a formatted informational message without boxing.</summary>
        [HideInCallstack]
        void Info<T1>(string format, in T1 arg1, UnityEngine.Object context = null);

        /// <summary>Logs a formatted informational message with two arguments without boxing.</summary>
        [HideInCallstack]
        void Info<T1, T2>(string format, in T1 arg1, in T2 arg2, UnityEngine.Object context = null);

        /// <summary>Logs a formatted informational message with three arguments without boxing.</summary>
        [HideInCallstack]
        void Info<T1, T2, T3>(string format, in T1 arg1, in T2 arg2, in T3 arg3, UnityEngine.Object context = null);

        #endregion

        #region Warn Methods

        /// <summary>Logs a warning message.</summary>
        [HideInCallstack]
        void Warn(string message, UnityEngine.Object context = null);

        /// <summary>Logs a formatted warning message without boxing.</summary>
        [HideInCallstack]
        void Warn<T1>(string format, in T1 arg1, UnityEngine.Object context = null);

        /// <summary>Logs a formatted warning message with two arguments without boxing.</summary>
        [HideInCallstack]
        void Warn<T1, T2>(string format, in T1 arg1, in T2 arg2, UnityEngine.Object context = null);

        /// <summary>Logs a formatted warning message with three arguments without boxing.</summary>
        [HideInCallstack]
        void Warn<T1, T2, T3>(string format, in T1 arg1, in T2 arg2, in T3 arg3, UnityEngine.Object context = null);

        #endregion

        #region Error Methods

        /// <summary>Logs an error message.</summary>
        [HideInCallstack]
        void Error(string message, UnityEngine.Object context = null);

        /// <summary>Logs an error with an associated exception and optional message.</summary>
        [HideInCallstack]
        void Error(Exception exception, string message = null, UnityEngine.Object context = null);

        /// <summary>Logs a formatted error message without boxing.</summary>
        [HideInCallstack]
        void Error<T1>(string format, in T1 arg1, UnityEngine.Object context = null);

        /// <summary>Logs a formatted error message with two arguments without boxing.</summary>
        [HideInCallstack]
        void Error<T1, T2>(string format, in T1 arg1, in T2 arg2, UnityEngine.Object context = null);

        /// <summary>Logs a formatted error message with three arguments without boxing.</summary>
        [HideInCallstack]
        void Error<T1, T2, T3>(string format, in T1 arg1, in T2 arg2, in T3 arg3, UnityEngine.Object context = null);

        /// <summary>Logs an exception along with a formatted message without boxing.</summary>
        [HideInCallstack]
        void Error<T1>(Exception exception, string format, in T1 arg1, UnityEngine.Object context = null);

        /// <summary>Logs an exception along with a formatted message with two arguments without boxing.</summary>
        [HideInCallstack]
        void Error<T1, T2>(Exception exception, string format, in T1 arg1, in T2 arg2, UnityEngine.Object context = null);

        /// <summary>Logs an exception along with a formatted message with three arguments without boxing.</summary>
        [HideInCallstack]
        void Error<T1, T2, T3>(Exception exception, string format, in T1 arg1, in T2 arg2, in T3 arg3, UnityEngine.Object context = null);

        #endregion

        #region Fatal Methods

        /// <summary>Logs a fatal application error message.</summary>
        [HideInCallstack]
        void Fatal(string message, UnityEngine.Object context = null);

        /// <summary>Logs a fatal error with an associated exception and optional message.</summary>
        [HideInCallstack]
        void Fatal(Exception exception, string message = null, UnityEngine.Object context = null);

        /// <summary>Logs a formatted fatal message without boxing.</summary>
        [HideInCallstack]
        void Fatal<T1>(string format, in T1 arg1, UnityEngine.Object context = null);

        /// <summary>Logs a formatted fatal message with two arguments without boxing.</summary>
        [HideInCallstack]
        void Fatal<T1, T2>(string format, in T1 arg1, in T2 arg2, UnityEngine.Object context = null);

        /// <summary>Logs a formatted fatal message with three arguments without boxing.</summary>
        [HideInCallstack]
        void Fatal<T1, T2, T3>(string format, in T1 arg1, in T2 arg2, in T3 arg3, UnityEngine.Object context = null);

        /// <summary>Logs a fatal exception along with a formatted message without boxing.</summary>
        [HideInCallstack]
        void Fatal<T1>(Exception exception, string format, in T1 arg1, UnityEngine.Object context = null);

        /// <summary>Logs a fatal exception along with a formatted message with two arguments without boxing.</summary>
        [HideInCallstack]
        void Fatal<T1, T2>(Exception exception, string format, in T1 arg1, in T2 arg2, UnityEngine.Object context = null);

        /// <summary>Logs a fatal exception along with a formatted message with three arguments without boxing.</summary>
        [HideInCallstack]
        void Fatal<T1, T2, T3>(Exception exception, string format, in T1 arg1, in T2 arg2, in T3 arg3, UnityEngine.Object context = null);

        #endregion
    }
}
