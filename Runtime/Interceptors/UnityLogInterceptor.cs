using System;
using LogLine.Core;
using UnityEngine;
using Logger = LogLine.Core.Logger;

namespace LogLine.Interceptors
{
    /// <summary>
    /// Intercepts native Unity <see cref="Debug.Log(object)"/> invocations and redirects them into LogLine.
    /// </summary>
    internal sealed class UnityLogInterceptor : ILogHandler, IDisposable
    {
        #region Private Fields

        private static volatile ILogHandler _activeOriginalHandler;

        private readonly ILogHandler _originalHandler;
        private readonly Logger _unityLogger;
        private bool _isDisposed;

        [ThreadStatic]
        private static bool _isProcessingNativeCall;

        #endregion

        #region Properties

        /// <summary>
        /// Gets the original native Unity <see cref="ILogHandler"/> captured before interception,
        /// allowing internal sinks to bypass the interceptor directly.
        /// </summary>
        internal static ILogHandler OriginalHandler => _activeOriginalHandler;

        #endregion

        #region Construction

        /// <summary>
        /// Initializes a new instance of the <see cref="UnityLogInterceptor"/> class and attaches to Unity's log handler.
        /// </summary>
        public UnityLogInterceptor()
        {
            _unityLogger = LogLineCore.GetLogger("Unity");
            _originalHandler = Debug.unityLogger.logHandler;
            _activeOriginalHandler = _originalHandler;
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

            LogLevel level = ConvertLogType(logType);
            if (!_unityLogger.IsEnabled(level))
            {
                return;
            }

            _isProcessingNativeCall = true;
            try
            {
                string message = args == null || args.Length == 0 ? format : string.Format(format, args);

                LogLineCore.Dispatch(level, _unityLogger.Name, message, null, context);
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

            if (exception == null || !_unityLogger.IsEnabled(LogLevel.Fatal))
            {
                return;
            }

            _isProcessingNativeCall = true;
            try
            {
                LogLineCore.Dispatch(LogLevel.Fatal, _unityLogger.Name, exception.Message, exception, context);
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

            if (ReferenceEquals(_activeOriginalHandler, _originalHandler))
            {
                _activeOriginalHandler = null;
            }

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
