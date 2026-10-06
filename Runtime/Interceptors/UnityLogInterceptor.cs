using System;
using LogLine.Core;
using UnityEngine;

namespace LogLine.Interceptors
{
    /// <summary>
    /// Intercepts native Unity <see cref="Debug.Log(object)"/> invocations and redirects them into LogLine.
    /// </summary>
    internal sealed class UnityLogInterceptor : ILogHandler, IDisposable
    {
        #region Private Fields

        private readonly ILogHandler _originalHandler;
        private bool _isDisposed;

        [ThreadStatic]
        private static bool _isProcessingNativeCall;

        #endregion

        #region Construction

        /// <summary>
        /// Initializes a new instance of the <see cref="UnityLogInterceptor"/> class and attaches to Unity's log handler.
        /// </summary>
        public UnityLogInterceptor()
        {
            _originalHandler = Debug.unityLogger.logHandler;
            Debug.unityLogger.logHandler = this;
        }

        #endregion

        #region ILogHandler Implementation

        /// <inheritdoc />
        [HideInCallstack]
        public void LogFormat(LogType logType, UnityEngine.Object context, string format, params object[] args)
        {
            // Bypass redirection if this call originated from within the LogLine pipeline
            if (_isProcessingNativeCall || LogLineCore.IsDispatching)
            {
                _originalHandler.LogFormat(logType, context, format, args);
                return;
            }

            _isProcessingNativeCall = true;
            try
            {
                LogLevel level = ConvertLogType(logType);
                string message = args == null || args.Length == 0 ? format : string.Format(format, args);

                LogLineCore.Dispatch(level, "Unity", message, null, context);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[UnityLogInterceptor] Intercept failure: {ex}");
            }
            finally
            {
                _isProcessingNativeCall = false;
            }
        }

        /// <inheritdoc />
        [HideInCallstack]
        public void LogException(Exception exception, UnityEngine.Object context)
        {
            if (_isProcessingNativeCall || LogLineCore.IsDispatching)
            {
                _originalHandler.LogException(exception, context);
                return;
            }

            _isProcessingNativeCall = true;
            try
            {
                if (exception != null)
                {
                    LogLineCore.Dispatch(LogLevel.Fatal, "Unity", exception.Message, exception, context);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[UnityLogInterceptor] Exception intercept failure: {ex}");
            }
            finally
            {
                _isProcessingNativeCall = false;
            }
        }

        #endregion

        #region IDisposable Implementation

        /// <inheritdoc />
        public void Dispose()
        {
            if (_isDisposed) return;
            _isDisposed = true;

            if (Debug.unityLogger.logHandler == this)
            {
                Debug.unityLogger.logHandler = _originalHandler;
            }
        }

        #endregion

        #region Helpers

        private static LogLevel ConvertLogType(LogType logType) => logType switch
        {
            LogType.Error or LogType.Assert => LogLevel.Error,
            LogType.Warning => LogLevel.Warn,
            LogType.Log => LogLevel.Info,
            LogType.Exception => LogLevel.Fatal,
            _ => LogLevel.Info
        };

        #endregion
    }
}
