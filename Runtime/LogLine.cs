using System;
using System.IO;
using LogLine.Config;
using LogLine.Core;
using LogLine.Interceptors;
using LogLine.Layouts;
using LogLine.Sinks;
using UnityEngine;
using ILogger = LogLine.Core.ILogger;

namespace LogLine
{
    /// <summary>
    /// Central public facade and entry point for the LogLine logging framework.
    /// </summary>
    public static class LogLine
    {
        #region Private Fields

        private static readonly object _syncLock = new();
        private static UnityLogInterceptor _interceptor;

        #endregion

        #region Properties

        /// <summary>
        /// Gets the currently active configuration settings asset.
        /// </summary>
        public static LogLineSettings Settings { get; private set; }

        /// <summary>
        /// Gets the default 'Global' category logger instance.
        /// </summary>
        public static ILogger Global { get; private set; } = LogLineCore.GetLogger("Global");

        #endregion

        #region Lifecycle & Auto-Init

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void AutoInitialize()
        {
            LogLineSettings settings = Resources.Load<LogLineSettings>("LogLineSettings");
            Initialize(settings);
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void OnSubsystemRegistration()
        {
            lock (_syncLock)
            {
                Application.quitting -= OnApplicationQuitting;
                _interceptor?.Dispose();
                _interceptor = null;
                Settings = null;
                LogLineCore.ResetStaticState();
                Global = LogLineCore.GetLogger("Global");
            }
        }

        private static void OnApplicationQuitting()
        {
            lock (_syncLock)
            {
                _interceptor?.Dispose();
                _interceptor = null;
                LogLineCore.ClearSinks();
            }
        }

        #endregion

        #region Initialization API

        /// <summary>
        /// Initializes LogLine with the specified settings asset, disposing any previously registered built-in sinks.
        /// </summary>
        /// <param name="settings">Configuration settings. If <see langword="null"/>, safe defaults are applied.</param>
        public static void Initialize(LogLineSettings settings)
        {
            lock (_syncLock)
            {
                Application.quitting -= OnApplicationQuitting;
                Application.quitting += OnApplicationQuitting;

                _interceptor?.Dispose();
                _interceptor = null;

                LogLineCore.ClearSinks();
                Settings = settings;

                if (settings != null)
                {
                    LogLineCore.GlobalMinimumLevel = settings.globalMinimumLevel;
                    LogLineCore.SetCategoryLevelResolver(settings.ResolveLevelForCategory);

                    if (settings.enableConsoleLogging)
                    {
                        string consolePattern = string.IsNullOrEmpty(settings.consolePattern)
                            ? PatternLayout.DefaultConsolePattern
                            : settings.consolePattern;

                        var consoleLayout = new PatternLayout(consolePattern, settings.consoleUseColorTags);

                        LogLineCore.AddSink(new UnityConsoleSink(consoleLayout)
                        {
                            MinimumLevel = settings.consoleMinimumLevel,
                            CategoryFilter = settings.consoleCategoryFilter.Build()
                        });
                    }

                    if (settings.enableFileLogging)
                    {
                        string logDirectory = Path.Combine(Application.persistentDataPath, "Logs");
                        string fullPath = Path.Combine(logDirectory, settings.fileName);

                        string filePattern = string.IsNullOrEmpty(settings.filePattern)
                            ? PatternLayout.DefaultFilePattern
                            : settings.filePattern;

                        var fileLayout = new PatternLayout(filePattern, useColorTags: false);

                        LogLineCore.AddSink(new AsyncFileSink(fullPath, fileLayout)
                        {
                            MinimumLevel = settings.fileMinimumLevel,
                            MaxFileSizeBytes = settings.maxFileSizeBytes,
                            MaxArchiveFiles = settings.maxArchiveFiles,
                            CategoryFilter = settings.fileCategoryFilter.Build()
                        });
                    }

                    if (settings.interceptUnityLogs)
                    {
                        _interceptor = new UnityLogInterceptor();
                    }

                    LogLineCore.RefreshLoggerLevels(settings.ResolveLevelForCategory);
                }
                else
                {
                    LogLineCore.GlobalMinimumLevel = LogLevel.Trace;
                    LogLineCore.SetCategoryLevelResolver(null);
                    LogLineCore.AddSink(new UnityConsoleSink());
                    LogLineCore.RefreshLoggerLevels(null);
                }
            }
        }

        /// <summary>
        /// Re-applies configuration rules to all active cached loggers and built-in sinks.
        /// </summary>
        /// <param name="settings">The configuration settings asset to apply.</param>
        public static void ApplySettings(LogLineSettings settings)
        {
            if (settings == null) return;

            LogLineCore.GlobalMinimumLevel = settings.globalMinimumLevel;
            LogLineCore.SetCategoryLevelResolver(settings.ResolveLevelForCategory);
            LogLineCore.RefreshLoggerLevels(settings.ResolveLevelForCategory);

            CategoryFilter consoleFilter = settings.consoleCategoryFilter.Build();
            CategoryFilter fileFilter = settings.fileCategoryFilter.Build();

            ILogSink[] sinks = LogLineCore.GetSinksSnapshot();
            for (int i = 0; i < sinks.Length; i++)
            {
                switch (sinks[i])
                {
                    case UnityConsoleSink consoleSink:
                        consoleSink.IsEnabled = settings.enableConsoleLogging;
                        consoleSink.MinimumLevel = settings.consoleMinimumLevel;
                        consoleSink.CategoryFilter = consoleFilter;
                        break;
                    case AsyncFileSink fileSink:
                        fileSink.IsEnabled = settings.enableFileLogging;
                        fileSink.MinimumLevel = settings.fileMinimumLevel;
                        fileSink.CategoryFilter = fileFilter;
                        break;
                }
            }
        }

        #endregion

        #region Sink Management API

        /// <summary>
        /// Registers a new output sink (e.g. Console, File, or custom network sink).
        /// </summary>
        /// <param name="sink">The sink instance to register.</param>
        public static void AddSink(ILogSink sink) => LogLineCore.AddSink(sink);

        /// <summary>
        /// Detaches and disposes an existing sink.
        /// </summary>
        /// <param name="sink">The sink instance to remove.</param>
        public static void RemoveSink(ILogSink sink) => LogLineCore.RemoveSink(sink);

        #endregion

        #region Logger Factory

        /// <summary>
        /// Resolves or instantiates a category-bound logger instance.
        /// </summary>
        /// <param name="category">The category name.</param>
        /// <returns>A thread-safe <see cref="ILogger"/> instance.</returns>
        public static ILogger GetLogger(string category) => LogLineCore.GetLogger(category);

        /// <summary>
        /// Resolves or instantiates a logger bound to the specified type's cached short name.
        /// </summary>
        /// <typeparam name="T">Type to extract category name from.</typeparam>
        /// <returns>A thread-safe <see cref="ILogger"/> instance.</returns>
        public static ILogger GetLogger<T>() => LogLineCore.GetLogger<T>();

        #endregion

        #region Global Facade - Trace

        /// <summary>Logs a trace message to the Global logger.</summary>
        /// <param name="message">The message text.</param>
        /// <param name="context">Optional Unity context object.</param>
        [HideInCallstack]
        public static void Trace(string message, UnityEngine.Object context = null) => Global.Trace(message, context);

        /// <summary>Logs a formatted trace message with one argument without boxing.</summary>
        /// <typeparam name="T1">The type of the first argument.</typeparam>
        /// <param name="format">The composite format template.</param>
        /// <param name="arg1">The first argument.</param>
        /// <param name="context">Optional Unity context object.</param>
        [HideInCallstack]
        public static void Trace<T1>(string format, in T1 arg1, UnityEngine.Object context = null) => Global.Trace(format, arg1, context);

        /// <summary>Logs a formatted trace message with two arguments without boxing.</summary>
        /// <typeparam name="T1">The type of the first argument.</typeparam>
        /// <typeparam name="T2">The type of the second argument.</typeparam>
        /// <param name="format">The composite format template.</param>
        /// <param name="arg1">The first argument.</param>
        /// <param name="arg2">The second argument.</param>
        /// <param name="context">Optional Unity context object.</param>
        [HideInCallstack]
        public static void Trace<T1, T2>(string format, in T1 arg1, in T2 arg2, UnityEngine.Object context = null) => Global.Trace(format, arg1, arg2, context);

        /// <summary>Logs a formatted trace message with three arguments without boxing.</summary>
        /// <typeparam name="T1">The type of the first argument.</typeparam>
        /// <typeparam name="T2">The type of the second argument.</typeparam>
        /// <typeparam name="T3">The type of the third argument.</typeparam>
        /// <param name="format">The composite format template.</param>
        /// <param name="arg1">The first argument.</param>
        /// <param name="arg2">The second argument.</param>
        /// <param name="arg3">The third argument.</param>
        /// <param name="context">Optional Unity context object.</param>
        [HideInCallstack]
        public static void Trace<T1, T2, T3>(string format, in T1 arg1, in T2 arg2, in T3 arg3, UnityEngine.Object context = null) => Global.Trace(format, arg1, arg2, arg3, context);

        #endregion

        #region Global Facade - Debug

        /// <summary>Logs a debug message to the Global logger.</summary>
        /// <param name="message">The message text.</param>
        /// <param name="context">Optional Unity context object.</param>
        [HideInCallstack]
        public static void Debug(string message, UnityEngine.Object context = null) => Global.Debug(message, context);

        /// <summary>Logs a formatted debug message with one argument without boxing.</summary>
        /// <typeparam name="T1">The type of the first argument.</typeparam>
        /// <param name="format">The composite format template.</param>
        /// <param name="arg1">The first argument.</param>
        /// <param name="context">Optional Unity context object.</param>
        [HideInCallstack]
        public static void Debug<T1>(string format, in T1 arg1, UnityEngine.Object context = null) => Global.Debug(format, arg1, context);

        /// <summary>Logs a formatted debug message with two arguments without boxing.</summary>
        /// <typeparam name="T1">The type of the first argument.</typeparam>
        /// <typeparam name="T2">The type of the second argument.</typeparam>
        /// <param name="format">The composite format template.</param>
        /// <param name="arg1">The first argument.</param>
        /// <param name="arg2">The second argument.</param>
        /// <param name="context">Optional Unity context object.</param>
        [HideInCallstack]
        public static void Debug<T1, T2>(string format, in T1 arg1, in T2 arg2, UnityEngine.Object context = null) => Global.Debug(format, arg1, arg2, context);

        /// <summary>Logs a formatted debug message with three arguments without boxing.</summary>
        /// <typeparam name="T1">The type of the first argument.</typeparam>
        /// <typeparam name="T2">The type of the second argument.</typeparam>
        /// <typeparam name="T3">The type of the third argument.</typeparam>
        /// <param name="format">The composite format template.</param>
        /// <param name="arg1">The first argument.</param>
        /// <param name="arg2">The second argument.</param>
        /// <param name="arg3">The third argument.</param>
        /// <param name="context">Optional Unity context object.</param>
        [HideInCallstack]
        public static void Debug<T1, T2, T3>(string format, in T1 arg1, in T2 arg2, in T3 arg3, UnityEngine.Object context = null) => Global.Debug(format, arg1, arg2, arg3, context);

        #endregion

        #region Global Facade - Info

        /// <summary>Logs an informational message to the Global logger.</summary>
        /// <param name="message">The message text.</param>
        /// <param name="context">Optional Unity context object.</param>
        [HideInCallstack]
        public static void Info(string message, UnityEngine.Object context = null) => Global.Info(message, context);

        /// <summary>Logs a formatted informational message with one argument without boxing.</summary>
        /// <typeparam name="T1">The type of the first argument.</typeparam>
        /// <param name="format">The composite format template.</param>
        /// <param name="arg1">The first argument.</param>
        /// <param name="context">Optional Unity context object.</param>
        [HideInCallstack]
        public static void Info<T1>(string format, in T1 arg1, UnityEngine.Object context = null) => Global.Info(format, arg1, context);

        /// <summary>Logs a formatted informational message with two arguments without boxing.</summary>
        /// <typeparam name="T1">The type of the first argument.</typeparam>
        /// <typeparam name="T2">The type of the second argument.</typeparam>
        /// <param name="format">The composite format template.</param>
        /// <param name="arg1">The first argument.</param>
        /// <param name="arg2">The second argument.</param>
        /// <param name="context">Optional Unity context object.</param>
        [HideInCallstack]
        public static void Info<T1, T2>(string format, in T1 arg1, in T2 arg2, UnityEngine.Object context = null) => Global.Info(format, arg1, arg2, context);

        /// <summary>Logs a formatted informational message with three arguments without boxing.</summary>
        /// <typeparam name="T1">The type of the first argument.</typeparam>
        /// <typeparam name="T2">The type of the second argument.</typeparam>
        /// <typeparam name="T3">The type of the third argument.</typeparam>
        /// <param name="format">The composite format template.</param>
        /// <param name="arg1">The first argument.</param>
        /// <param name="arg2">The second argument.</param>
        /// <param name="arg3">The third argument.</param>
        /// <param name="context">Optional Unity context object.</param>
        [HideInCallstack]
        public static void Info<T1, T2, T3>(string format, in T1 arg1, in T2 arg2, in T3 arg3, UnityEngine.Object context = null) => Global.Info(format, arg1, arg2, arg3, context);

        #endregion

        #region Global Facade - Warn

        /// <summary>Logs a warning message to the Global logger.</summary>
        /// <param name="message">The message text.</param>
        /// <param name="context">Optional Unity context object.</param>
        [HideInCallstack]
        public static void Warn(string message, UnityEngine.Object context = null) => Global.Warn(message, context);

        /// <summary>Logs a formatted warning message with one argument without boxing.</summary>
        /// <typeparam name="T1">The type of the first argument.</typeparam>
        /// <param name="format">The composite format template.</param>
        /// <param name="arg1">The first argument.</param>
        /// <param name="context">Optional Unity context object.</param>
        [HideInCallstack]
        public static void Warn<T1>(string format, in T1 arg1, UnityEngine.Object context = null) => Global.Warn(format, arg1, context);

        /// <summary>Logs a formatted warning message with two arguments without boxing.</summary>
        /// <typeparam name="T1">The type of the first argument.</typeparam>
        /// <typeparam name="T2">The type of the second argument.</typeparam>
        /// <param name="format">The composite format template.</param>
        /// <param name="arg1">The first argument.</param>
        /// <param name="arg2">The second argument.</param>
        /// <param name="context">Optional Unity context object.</param>
        [HideInCallstack]
        public static void Warn<T1, T2>(string format, in T1 arg1, in T2 arg2, UnityEngine.Object context = null) => Global.Warn(format, arg1, arg2, context);

        /// <summary>Logs a formatted warning message with three arguments without boxing.</summary>
        /// <typeparam name="T1">The type of the first argument.</typeparam>
        /// <typeparam name="T2">The type of the second argument.</typeparam>
        /// <typeparam name="T3">The type of the third argument.</typeparam>
        /// <param name="format">The composite format template.</param>
        /// <param name="arg1">The first argument.</param>
        /// <param name="arg2">The second argument.</param>
        /// <param name="arg3">The third argument.</param>
        /// <param name="context">Optional Unity context object.</param>
        [HideInCallstack]
        public static void Warn<T1, T2, T3>(string format, in T1 arg1, in T2 arg2, in T3 arg3, UnityEngine.Object context = null) => Global.Warn(format, arg1, arg2, arg3, context);

        #endregion

        #region Global Facade - Error

        /// <summary>Logs an error message to the Global logger.</summary>
        /// <param name="message">The message text.</param>
        /// <param name="context">Optional Unity context object.</param>
        [HideInCallstack]
        public static void Error(string message, UnityEngine.Object context = null) => Global.Error(message, context);

        /// <summary>Logs an error with an associated exception and optional message.</summary>
        /// <param name="exception">The exception to log.</param>
        /// <param name="message">Optional descriptive message.</param>
        /// <param name="context">Optional Unity context object.</param>
        [HideInCallstack]
        public static void Error(Exception exception, string message = null, UnityEngine.Object context = null) => Global.Error(exception, message, context);

        /// <summary>Logs a formatted error message with one argument without boxing.</summary>
        /// <typeparam name="T1">The type of the first argument.</typeparam>
        /// <param name="format">The composite format template.</param>
        /// <param name="arg1">The first argument.</param>
        /// <param name="context">Optional Unity context object.</param>
        [HideInCallstack]
        public static void Error<T1>(string format, in T1 arg1, UnityEngine.Object context = null) => Global.Error(format, arg1, context);

        /// <summary>Logs a formatted error message with two arguments without boxing.</summary>
        /// <typeparam name="T1">The type of the first argument.</typeparam>
        /// <typeparam name="T2">The type of the second argument.</typeparam>
        /// <param name="format">The composite format template.</param>
        /// <param name="arg1">The first argument.</param>
        /// <param name="arg2">The second argument.</param>
        /// <param name="context">Optional Unity context object.</param>
        [HideInCallstack]
        public static void Error<T1, T2>(string format, in T1 arg1, in T2 arg2, UnityEngine.Object context = null) => Global.Error(format, arg1, arg2, context);

        /// <summary>Logs a formatted error message with three arguments without boxing.</summary>
        /// <typeparam name="T1">The type of the first argument.</typeparam>
        /// <typeparam name="T2">The type of the second argument.</typeparam>
        /// <typeparam name="T3">The type of the third argument.</typeparam>
        /// <param name="format">The composite format template.</param>
        /// <param name="arg1">The first argument.</param>
        /// <param name="arg2">The second argument.</param>
        /// <param name="arg3">The third argument.</param>
        /// <param name="context">Optional Unity context object.</param>
        [HideInCallstack]
        public static void Error<T1, T2, T3>(string format, in T1 arg1, in T2 arg2, in T3 arg3, UnityEngine.Object context = null) => Global.Error(format, arg1, arg2, arg3, context);

        /// <summary>Logs an exception with a formatted message and one argument without boxing.</summary>
        /// <typeparam name="T1">The type of the first argument.</typeparam>
        /// <param name="exception">The exception to log.</param>
        /// <param name="format">The composite format template.</param>
        /// <param name="arg1">The first argument.</param>
        /// <param name="context">Optional Unity context object.</param>
        [HideInCallstack]
        public static void Error<T1>(Exception exception, string format, in T1 arg1, UnityEngine.Object context = null) => Global.Error(exception, format, arg1, context);

        /// <summary>Logs an exception with a formatted message and two arguments without boxing.</summary>
        /// <typeparam name="T1">The type of the first argument.</typeparam>
        /// <typeparam name="T2">The type of the second argument.</typeparam>
        /// <param name="exception">The exception to log.</param>
        /// <param name="format">The composite format template.</param>
        /// <param name="arg1">The first argument.</param>
        /// <param name="arg2">The second argument.</param>
        /// <param name="context">Optional Unity context object.</param>
        [HideInCallstack]
        public static void Error<T1, T2>(Exception exception, string format, in T1 arg1, in T2 arg2, UnityEngine.Object context = null) => Global.Error(exception, format, arg1, arg2, context);

        /// <summary>Logs an exception with a formatted message and three arguments without boxing.</summary>
        /// <typeparam name="T1">The type of the first argument.</typeparam>
        /// <typeparam name="T2">The type of the second argument.</typeparam>
        /// <typeparam name="T3">The type of the third argument.</typeparam>
        /// <param name="exception">The exception to log.</param>
        /// <param name="format">The composite format template.</param>
        /// <param name="arg1">The first argument.</param>
        /// <param name="arg2">The second argument.</param>
        /// <param name="arg3">The third argument.</param>
        /// <param name="context">Optional Unity context object.</param>
        [HideInCallstack]
        public static void Error<T1, T2, T3>(Exception exception, string format, in T1 arg1, in T2 arg2, in T3 arg3, UnityEngine.Object context = null) => Global.Error(exception, format, arg1, arg2, arg3, context);

        #endregion

        #region Global Facade - Fatal

        /// <summary>Logs a fatal error message to the Global logger.</summary>
        /// <param name="message">The message text.</param>
        /// <param name="context">Optional Unity context object.</param>
        [HideInCallstack]
        public static void Fatal(string message, UnityEngine.Object context = null) => Global.Fatal(message, context);

        /// <summary>Logs a fatal error with an associated exception and optional message.</summary>
        /// <param name="exception">The fatal exception to log.</param>
        /// <param name="message">Optional descriptive message.</param>
        /// <param name="context">Optional Unity context object.</param>
        [HideInCallstack]
        public static void Fatal(Exception exception, string message = null, UnityEngine.Object context = null) => Global.Fatal(exception, message, context);

        /// <summary>Logs a formatted fatal message with one argument without boxing.</summary>
        /// <typeparam name="T1">The type of the first argument.</typeparam>
        /// <param name="format">The composite format template.</param>
        /// <param name="arg1">The first argument.</param>
        /// <param name="context">Optional Unity context object.</param>
        [HideInCallstack]
        public static void Fatal<T1>(string format, in T1 arg1, UnityEngine.Object context = null) => Global.Fatal(format, arg1, context);

        /// <summary>Logs a formatted fatal message with two arguments without boxing.</summary>
        /// <typeparam name="T1">The type of the first argument.</typeparam>
        /// <typeparam name="T2">The type of the second argument.</typeparam>
        /// <param name="format">The composite format template.</param>
        /// <param name="arg1">The first argument.</param>
        /// <param name="arg2">The second argument.</param>
        /// <param name="context">Optional Unity context object.</param>
        [HideInCallstack]
        public static void Fatal<T1, T2>(string format, in T1 arg1, in T2 arg2, UnityEngine.Object context = null) => Global.Fatal(format, arg1, arg2, context);

        /// <summary>Logs a formatted fatal message with three arguments without boxing.</summary>
        /// <typeparam name="T1">The type of the first argument.</typeparam>
        /// <typeparam name="T2">The type of the second argument.</typeparam>
        /// <typeparam name="T3">The type of the third argument.</typeparam>
        /// <param name="format">The composite format template.</param>
        /// <param name="arg1">The first argument.</param>
        /// <param name="arg2">The second argument.</param>
        /// <param name="arg3">The third argument.</param>
        /// <param name="context">Optional Unity context object.</param>
        [HideInCallstack]
        public static void Fatal<T1, T2, T3>(string format, in T1 arg1, in T2 arg2, in T3 arg3, UnityEngine.Object context = null) => Global.Fatal(format, arg1, arg2, arg3, context);

        /// <summary>Logs a fatal exception with a formatted message and one argument without boxing.</summary>
        /// <typeparam name="T1">The type of the first argument.</typeparam>
        /// <param name="exception">The fatal exception to log.</param>
        /// <param name="format">The composite format template.</param>
        /// <param name="arg1">The first argument.</param>
        /// <param name="context">Optional Unity context object.</param>
        [HideInCallstack]
        public static void Fatal<T1>(Exception exception, string format, in T1 arg1, UnityEngine.Object context = null) => Global.Fatal(exception, format, arg1, context);

        /// <summary>Logs a fatal exception with a formatted message and two arguments without boxing.</summary>
        /// <typeparam name="T1">The type of the first argument.</typeparam>
        /// <typeparam name="T2">The type of the second argument.</typeparam>
        /// <param name="exception">The fatal exception to log.</param>
        /// <param name="format">The composite format template.</param>
        /// <param name="arg1">The first argument.</param>
        /// <param name="arg2">The second argument.</param>
        /// <param name="context">Optional Unity context object.</param>
        [HideInCallstack]
        public static void Fatal<T1, T2>(Exception exception, string format, in T1 arg1, in T2 arg2, UnityEngine.Object context = null) => Global.Fatal(exception, format, arg1, arg2, context);

        /// <summary>Logs a fatal exception with a formatted message and three arguments without boxing.</summary>
        /// <typeparam name="T1">The type of the first argument.</typeparam>
        /// <typeparam name="T2">The type of the second argument.</typeparam>
        /// <typeparam name="T3">The type of the third argument.</typeparam>
        /// <param name="exception">The fatal exception to log.</param>
        /// <param name="format">The composite format template.</param>
        /// <param name="arg1">The first argument.</param>
        /// <param name="arg2">The second argument.</param>
        /// <param name="arg3">The third argument.</param>
        /// <param name="context">Optional Unity context object.</param>
        [HideInCallstack]
        public static void Fatal<T1, T2, T3>(Exception exception, string format, in T1 arg1, in T2 arg2, in T3 arg3, UnityEngine.Object context = null) => Global.Fatal(exception, format, arg1, arg2, arg3, context);

        #endregion
    }
}
