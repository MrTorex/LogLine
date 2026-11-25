using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using LogLine.Config;
using LogLine.Core;
using LogLine.Layouts;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Log = LogLine.LogLine;
using Logger = LogLine.Core.Logger;

namespace LogLine.Tests.Runtime
{
    [TestFixture]
    public sealed class UnityIntegrationTests
    {
        #region Private Fields

        private LogLineSettings _settings;
        private string _tempDir;

        #endregion

        #region Lifecycle

        [SetUp]
        public void SetUp()
        {
            _tempDir = Path.Combine(Path.GetTempPath(), "LogLine_PlayMode_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_tempDir);

            _settings = ScriptableObject.CreateInstance<LogLineSettings>();
            _settings.globalMinimumLevel = LogLevel.Trace;
            _settings.enableConsoleLogging = false;
            _settings.enableFileLogging = false;
            _settings.interceptUnityLogs = false;
            _settings.autoInitialize = true;

            LogLineCore.ResetStaticState();
        }

        [TearDown]
        public void TearDown()
        {
            LogLineCore.ClearSinks();
            LogLineCore.ResetStaticState();

            if (_settings != null)
            {
                UnityEngine.Object.DestroyImmediate(_settings);
                _settings = null;
            }

            if (!string.IsNullOrEmpty(_tempDir) && Directory.Exists(_tempDir))
            {
                try
                {
                    Directory.Delete(_tempDir, recursive: true);
                }
                catch (IOException)
                {
                    // Ignore transient OS handle release lag
                }
            }
        }

        #endregion

        #region Unity Lifecycle & Interception Tests

        [UnityTest]
        public IEnumerator UnityLogInterceptor_CapturesDebugLogWarningAndErrorUnderUnityCategory()
        {
            _settings.interceptUnityLogs = true;
            Log.Initialize(_settings);

            var memorySink = new MemorySink { MinimumLevel = LogLevel.Trace };
            Log.AddSink(memorySink);

            Assert.IsTrue(Log.IsInterceptorActive);

            Debug.Log("logline_intercept_info");
            Debug.LogWarning("logline_intercept_warn");
            Debug.LogError("logline_intercept_error");

            yield return null;

            LogEvent[] events = memorySink.ToArray();
            Assert.IsTrue(ContainsEvent(events, "Unity", LogLevel.Info, "logline_intercept_info"),
                "Native Debug.Log was not intercepted into the Unity category.");
            Assert.IsTrue(ContainsEvent(events, "Unity", LogLevel.Warn, "logline_intercept_warn"),
                "Native Debug.LogWarning was not intercepted into the Unity category.");
            Assert.IsTrue(ContainsEvent(events, "Unity", LogLevel.Error, "logline_intercept_error"),
                "Native Debug.LogError was not intercepted into the Unity category.");

            // Disable interceptor dynamically and verify no further native logs are captured
            _settings.interceptUnityLogs = false;
            Log.ApplySettings(_settings);
            Assert.IsFalse(Log.IsInterceptorActive);

            memorySink.Clear();
            Debug.Log("logline_after_disable");
            yield return null;

            Assert.IsFalse(ContainsEvent(memorySink.ToArray(), "Unity", LogLevel.Info, "logline_after_disable"),
                "Interceptor kept capturing logs after being disabled via ApplySettings.");
        }

        [UnityTest]
        public IEnumerator ApplySettings_HotUpdatesCategoryRulesAndJsonFileLayoutWithoutRecreatingSink()
        {
            Assume.That(
                LogLineDefines.CompiledMinimumLevel <= LogLevel.Info,
                "Test requires LOGLINE_MIN_INFO or lower to be compiled in.");

            string logPath = Path.Combine(_tempDir, "hot_update.jsonl");
            _settings.enableFileLogging = true;
            _settings.filePath = logPath;
            _settings.fileLayoutType = LogLayoutType.Json;
            _settings.fileMinimumLevel = LogLevel.Trace;
            _settings.fileJsonLayout = JsonLayoutConfig.Default;

            Log.Initialize(_settings);

            Logger aiLogger = Log.GetLogger("Combat.AI");
            Assert.AreEqual(LogLevel.Trace, aiLogger.MinimumLevel);

            // Hot-update category rule and JSON layout field toggle
            _settings.categoryRules = new List<CategoryRule>
            {
                new("Combat", LogLevel.Error)
            };
            _settings.fileJsonLayout.includeThreadId = false;
            Log.ApplySettings(_settings);

            Assert.AreEqual(LogLevel.Error, aiLogger.MinimumLevel,
                "Cached logger minimum level was not invalidated by ApplySettings.");

            aiLogger.Info("filtered_out_after_rule");
            aiLogger.Error("accepted_after_rule");

            yield return null;

            // Dispose file sink via ClearSinks to guarantee final flush before reading
            LogLineCore.ClearSinks();

            string content = File.ReadAllText(logPath);
            StringAssert.Contains("accepted_after_rule", content);
            StringAssert.DoesNotContain("filtered_out_after_rule", content);
            StringAssert.DoesNotContain("\"threadId\":", content,
                "JsonLayoutConfig.includeThreadId=false was not hot-applied to active AsyncFileSink.");
        }

        [UnityTest]
        public IEnumerator ResetStaticState_SimulatesNoDomainReloadReinitializationCleanly()
        {
            Assume.That(
                LogLineDefines.CompiledMinimumLevel <= LogLevel.Info,
                "Test requires LOGLINE_MIN_INFO or lower to be compiled in.");

            Log.Initialize(_settings);
            var firstSink = new CountingSink { MinimumLevel = LogLevel.Trace };
            Log.AddSink(firstSink);

            Logger cachedStaticLikeLogger = Log.GetLogger("DomainReload.Test");
            cachedStaticLikeLogger.Info("before_reset");
            Assert.AreEqual(1, firstSink.Count);

            // Simulate Enter Play Mode without Domain Reload
            LogLineCore.ResetStaticState();
            Assert.AreEqual(0, LogLineCore.GetSinksSnapshot().Length,
                "All sinks from the previous Play Mode session must be cleared.");

            _settings.globalMinimumLevel = LogLevel.Warn;
            Log.Initialize(_settings);
            var secondSink = new CountingSink { MinimumLevel = LogLevel.Trace };
            Log.AddSink(secondSink);

            Logger freshLogger = Log.GetLogger("DomainReload.Test");
            Assert.AreSame(cachedStaticLikeLogger, freshLogger,
                "Existing static Logger references must stay tracked across No-Domain-Reload restarts.");
            Assert.AreEqual(LogLevel.Warn, cachedStaticLikeLogger.MinimumLevel,
                "Tracked static Logger must receive updated threshold after reinitialization.");

            cachedStaticLikeLogger.Info("filtered_after_reset");
            cachedStaticLikeLogger.Warn("delivered_after_reset");

            yield return null;

            Assert.AreEqual(1, firstSink.Count, "Disposed sink from previous session must not receive new events.");
            Assert.AreEqual(1, secondSink.Count, "Reinitialized pipeline must deliver events to new sinks.");
        }
        #endregion

        #region Helpers

        private static bool ContainsEvent(LogEvent[] events, string expectedLogger, LogLevel expectedLevel, string expectedText)
        {
            for (int i = 0; i < events.Length; i++)
            {
                if (events[i].Level == expectedLevel &&
                    string.Equals(events[i].LoggerName, expectedLogger, StringComparison.Ordinal) &&
                    events[i].Message != null &&
                    events[i].Message.Contains(expectedText))
                {
                    return true;
                }
            }

            return false;
        }

        #endregion
    }
}
