using System;
using System.IO;
using LogLine.Config;
using LogLine.Core;
using LogLine.Interceptors;
using LogLine.Layouts;
using LogLine.Sinks;
using UnityEngine;
using ILogger = LogLine.Core.ILogger;
using Logger = LogLine.Core.Logger;

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
            var settings = Resources.Load<LogLineSettings>("LogLineSettings");
            Initialize(settings);
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void OnSubsystemRegistration()
        {
            lock (_syncLock)
            {
                _interceptor?.Dispose();
                _interceptor = null;
                Settings = null;
                Global = LogLineCore.GetLogger("Global");
            }
        }

        #endregion

        #region Initialization API

        /// <summary>
        /// Initializes LogLine with the specified settings asset.
        /// </summary>
        /// <param name="settings">Configuration settings. If null, safe defaults are applied.</param>
        public static void Initialize(LogLineSettings settings)
        {
            lock (_syncLock)
            {
                _interceptor?.Dispose();
                _interceptor = null;

                Settings = settings;

                if (settings != null)
                {
                    LogLineCore.GlobalMinimumLevel = settings.globalMinimumLevel;

                    if (settings.enableConsoleLogging)
                    {
                        string consolePattern = string.IsNullOrEmpty(settings.consolePattern)
                            ? PatternLayout.DefaultConsolePattern
                            : settings.consolePattern;

                        var consoleLayout = new PatternLayout(consolePattern, settings.consoleUseColorTags);

                        LogLineCore.AddSink(new UnityConsoleSink(consoleLayout)
                        {
                            MinimumLevel = settings.consoleMinimumLevel
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
                            MaxArchiveFiles = settings.maxArchiveFiles
                        });
                    }

                    if (settings.interceptUnityLogs)
                    {
                        _interceptor = new UnityLogInterceptor();
                    }
                }
                else
                {
                    LogLineCore.AddSink(new UnityConsoleSink());
                }

                ApplySettings(settings);
            }
        }

        /// <summary>
        /// Re-applies configuration rules to active loggers.
        /// </summary>
        public static void ApplySettings(LogLineSettings settings)
        {
            if (settings == null) return;

            if (Global is Logger concreteGlobal)
            {
                concreteGlobal.SetMinimumLevel(settings.ResolveLevelForCategory("Global"));
            }
        }

        #endregion

        #region Sink Management API

        /// <summary>
        /// Registers a new output sink (e.g. Console, File, or custom network sink).
        /// </summary>
        public static void AddSink(ILogSink sink) => LogLineCore.AddSink(sink);

        /// <summary>
        /// Detaches and disposes an existing sink.
        /// </summary>
        public static void RemoveSink(ILogSink sink) => LogLineCore.RemoveSink(sink);

        #endregion

        #region Logger Factory

        /// <summary>
        /// Resolves or instantiates a category-bound logger instance.
        /// </summary>
        /// <param name="category">The category name.</param>
        public static ILogger GetLogger(string category)
        {
            var logger = LogLineCore.GetLogger(category);
            if (Settings != null)
            {
                logger.SetMinimumLevel(Settings.ResolveLevelForCategory(category));
            }
            return logger;
        }

        /// <summary>
        /// Resolves or instantiates a logger bound to the specified type's name.
        /// </summary>
        /// <typeparam name="T">Type to extract category name from.</typeparam>
        public static ILogger GetLogger<T>() => GetLogger(typeof(T).Name);

        #endregion

        #region Global Facade - Trace

        [HideInCallstack]
        public static void Trace(string message, UnityEngine.Object context = null) => Global.Trace(message, context);

        [HideInCallstack]
        public static void Trace<T1>(string format, in T1 arg1, UnityEngine.Object context = null) => Global.Trace(format, arg1, context);

        [HideInCallstack]
        public static void Trace<T1, T2>(string format, in T1 arg1, in T2 arg2, UnityEngine.Object context = null) => Global.Trace(format, arg1, arg2, context);

        [HideInCallstack]
        public static void Trace<T1, T2, T3>(string format, in T1 arg1, in T2 arg2, in T3 arg3, UnityEngine.Object context = null) => Global.Trace(format, arg1, arg2, arg3, context);

        #endregion

        #region Global Facade - Debug

        [HideInCallstack]
        public static void Debug(string message, UnityEngine.Object context = null) => Global.Debug(message, context);

        [HideInCallstack]
        public static void Debug<T1>(string format, in T1 arg1, UnityEngine.Object context = null) => Global.Debug(format, arg1, context);

        [HideInCallstack]
        public static void Debug<T1, T2>(string format, in T1 arg1, in T2 arg2, UnityEngine.Object context = null) => Global.Debug(format, arg1, arg2, context);

        [HideInCallstack]
        public static void Debug<T1, T2, T3>(string format, in T1 arg1, in T2 arg2, in T3 arg3, UnityEngine.Object context = null) => Global.Debug(format, arg1, arg2, arg3, context);

        #endregion

        #region Global Facade - Info

        [HideInCallstack]
        public static void Info(string message, UnityEngine.Object context = null) => Global.Info(message, context);

        [HideInCallstack]
        public static void Info<T1>(string format, in T1 arg1, UnityEngine.Object context = null) => Global.Info(format, arg1, context);

        [HideInCallstack]
        public static void Info<T1, T2>(string format, in T1 arg1, in T2 arg2, UnityEngine.Object context = null) => Global.Info(format, arg1, arg2, context);

        [HideInCallstack]
        public static void Info<T1, T2, T3>(string format, in T1 arg1, in T2 arg2, in T3 arg3, UnityEngine.Object context = null) => Global.Info(format, arg1, arg2, arg3, context);

        #endregion

        #region Global Facade - Warn

        [HideInCallstack]
        public static void Warn(string message, UnityEngine.Object context = null) => Global.Warn(message, context);

        [HideInCallstack]
        public static void Warn<T1>(string format, in T1 arg1, UnityEngine.Object context = null) => Global.Warn(format, arg1, context);

        [HideInCallstack]
        public static void Warn<T1, T2>(string format, in T1 arg1, in T2 arg2, UnityEngine.Object context = null) => Global.Warn(format, arg1, arg2, context);

        [HideInCallstack]
        public static void Warn<T1, T2, T3>(string format, in T1 arg1, in T2 arg2, in T3 arg3, UnityEngine.Object context = null) => Global.Warn(format, arg1, arg2, arg3, context);

        #endregion

        #region Global Facade - Error

        [HideInCallstack]
        public static void Error(string message, UnityEngine.Object context = null) => Global.Error(message, context);

        [HideInCallstack]
        public static void Error(Exception exception, string message = null, UnityEngine.Object context = null) => Global.Error(exception, message, context);

        [HideInCallstack]
        public static void Error<T1>(string format, in T1 arg1, UnityEngine.Object context = null) => Global.Error(format, arg1, context);

        [HideInCallstack]
        public static void Error<T1, T2>(string format, in T1 arg1, in T2 arg2, UnityEngine.Object context = null) => Global.Error(format, arg1, arg2, context);

        [HideInCallstack]
        public static void Error<T1, T2, T3>(string format, in T1 arg1, in T2 arg2, in T3 arg3, UnityEngine.Object context = null) => Global.Error(format, arg1, arg2, arg3, context);

        [HideInCallstack]
        public static void Error<T1>(Exception exception, string format, in T1 arg1, UnityEngine.Object context = null) => Global.Error(exception, format, arg1, context);

        [HideInCallstack]
        public static void Error<T1, T2>(Exception exception, string format, in T1 arg1, in T2 arg2, UnityEngine.Object context = null) => Global.Error(exception, format, arg1, arg2, context);

        [HideInCallstack]
        public static void Error<T1, T2, T3>(Exception exception, string format, in T1 arg1, in T2 arg2, in T3 arg3, UnityEngine.Object context = null) => Global.Error(exception, format, arg1, arg2, arg3, context);

        #endregion

        #region Global Facade - Fatal

        [HideInCallstack]
        public static void Fatal(string message, UnityEngine.Object context = null) => Global.Fatal(message, context);

        [HideInCallstack]
        public static void Fatal(Exception exception, string message = null, UnityEngine.Object context = null) => Global.Fatal(exception, message, context);

        [HideInCallstack]
        public static void Fatal<T1>(string format, in T1 arg1, UnityEngine.Object context = null) => Global.Fatal(format, arg1, context);

        [HideInCallstack]
        public static void Fatal<T1, T2>(string format, in T1 arg1, in T2 arg2, UnityEngine.Object context = null) => Global.Fatal(format, arg1, arg2, context);

        [HideInCallstack]
        public static void Fatal<T1, T2, T3>(string format, in T1 arg1, in T2 arg2, in T3 arg3, UnityEngine.Object context = null) => Global.Fatal(format, arg1, arg2, arg3, context);

        [HideInCallstack]
        public static void Fatal<T1>(Exception exception, string format, in T1 arg1, UnityEngine.Object context = null) => Global.Fatal(exception, format, arg1, context);

        [HideInCallstack]
        public static void Fatal<T1, T2>(Exception exception, string format, in T1 arg1, in T2 arg2, UnityEngine.Object context = null) => Global.Fatal(exception, format, arg1, arg2, context);

        [HideInCallstack]
        public static void Fatal<T1, T2, T3>(Exception exception, string format, in T1 arg1, in T2 arg2, in T3 arg3, UnityEngine.Object context = null) => Global.Fatal(exception, format, arg1, arg2, arg3, context);

        #endregion
    }
}
