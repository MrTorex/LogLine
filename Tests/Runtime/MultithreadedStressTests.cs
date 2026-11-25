using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using LogLine.Config;
using LogLine.Core;
using LogLine.Layouts;
using LogLine.Sinks;
using NUnit.Framework;
using UnityEngine;
using Log = LogLine.LogLine;
using Logger = LogLine.Core.Logger;

namespace LogLine.Tests.Runtime
{
    [TestFixture]
    public sealed class MultithreadedStressTests
    {
        #region Private Fields

        private string _tempDir;

        #endregion

        #region Lifecycle

        [SetUp]
        public void SetUp()
        {
            _tempDir = Path.Combine(Path.GetTempPath(), "LogLine_Stress_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_tempDir);
            LogLineCore.ResetStaticState();
        }

        [TearDown]
        public void TearDown()
        {
            LogLineCore.ClearSinks();
            LogLineCore.ResetStaticState();

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

        #region Stress Tests

        [Test]
        public void TenThreads_10000EventsEach_ThroughAsyncFileSink_ExactAccountingAndZeroDeadlocks()
        {
            Assume.That(
                LogLineDefines.CompiledMinimumLevel <= LogLevel.Info,
                "Test requires LOGLINE_MIN_INFO or lower to be compiled in.");

            const int ThreadCount = 10;
            const int EventsPerThread = 10_000;
            const int TotalExpected = ThreadCount * EventsPerThread;
            const string Payload = "stress_payload_marker";

            string logPath = Path.Combine(_tempDir, "stress_10x10k.log");
            var fileSink = new AsyncFileSink(logPath, new PatternLayout("%m%n", useColorTags: false), ringBufferCapacity: 4096)
            {
                MinimumLevel = LogLevel.Trace,
                MaxFileSizeBytes = 50L * 1024L * 1024L // Keep all entries in a single file for exact line counting
            };

            LogLineCore.GlobalMinimumLevel = LogLevel.Trace;
            LogLineCore.AddSink(fileSink);

            var startGate = new ManualResetEventSlim(false);
            var threads = new Thread[ThreadCount];
            var exceptions = new Exception[ThreadCount];

            for (int t = 0; t < ThreadCount; t++)
            {
                int index = t;
                Logger workerLogger = Log.GetLogger($"Stress.Worker{index}");
                threads[t] = new Thread(() =>
                {
                    try
                    {
                        startGate.Wait();
                        for (int i = 0; i < EventsPerThread; i++)
                        {
                            workerLogger.Info(Payload);
                        }
                    }
                    catch (Exception ex)
                    {
                        exceptions[index] = ex;
                    }
                })
                {
                    IsBackground = true,
                    Name = $"LogLine_StressWorker_{index}"
                };

                threads[t].Start();
            }

            startGate.Set();

            for (int t = 0; t < ThreadCount; t++)
            {
                Assert.IsTrue(threads[t].Join(TimeSpan.FromSeconds(20)), $"Worker thread {t} deadlocked.");
                Assert.IsNull(exceptions[t], $"Worker thread {t} threw: {exceptions[t]}");
            }

            // Capture dropped count and dispose sink to flush remaining ring buffer entries cleanly
            LogLineCore.RemoveSink(fileSink);
            int dropped = fileSink.DroppedCount;

            int writtenPayloadLines = 0;
            using (var reader = new StreamReader(logPath))
            {
                while (reader.ReadLine() is { } line)
                {
                    if (string.Equals(line, Payload, StringComparison.Ordinal))
                    {
                        writtenPayloadLines++;
                    }
                }
            }

            TestContext.Out.WriteLine(
                $"[Stress 10x10k] Written={writtenPayloadLines:N0}, Dropped={dropped:N0}, Total={writtenPayloadLines + dropped:N0}");

            Assert.Greater(writtenPayloadLines, 0, "Expected at least some events to be persisted to disk.");
            Assert.AreEqual(TotalExpected, writtenPayloadLines + dropped,
                $"Accounting mismatch: written ({writtenPayloadLines}) + dropped ({dropped}) != {TotalExpected}");
        }

        [Test]
        public void ConcurrentHotReconfiguration_DuringMultiThreadedLogging_ZeroExceptions()
        {
            Assume.That(
                LogLineDefines.CompiledMinimumLevel <= LogLevel.Info,
                "Test requires LOGLINE_MIN_INFO or lower to be compiled in.");

            const int WriterThreads = 8;
            const int EventsPerWriter = 5_000;

            LogLineSettings settings = ScriptableObject.CreateInstance<LogLineSettings>();
            settings.globalMinimumLevel = LogLevel.Trace;
            settings.enableConsoleLogging = false;
            settings.enableFileLogging = false;
            settings.interceptUnityLogs = false;

            try
            {
                var primarySink = new CountingSink { MinimumLevel = LogLevel.Trace };
                LogLineCore.GlobalMinimumLevel = LogLevel.Trace;
                LogLineCore.AddSink(primarySink);

                var startGate = new ManualResetEventSlim(false);
                var writersDone = new ManualResetEventSlim(false);
                var threads = new Thread[WriterThreads];
                var exceptions = new Exception[WriterThreads];
                Exception configException = null;

                for (int t = 0; t < WriterThreads; t++)
                {
                    int index = t;
                    Logger logger = Log.GetLogger($"HotSwap.Worker{index}");
                    threads[t] = new Thread(() =>
                    {
                        try
                        {
                            startGate.Wait();
                            for (int i = 0; i < EventsPerWriter; i++)
                            {
                                logger.Info("hot_swap_payload");
                            }
                        }
                        catch (Exception ex)
                        {
                            exceptions[index] = ex;
                        }
                    })
                    {
                        IsBackground = true
                    };
                    threads[t].Start();
                }

                var reconfigThread = new Thread(() =>
                {
                    try
                    {
                        startGate.Wait();
                        int iteration = 0;
                        while (!writersDone.IsSet)
                        {
                            var transientSink = new CountingSink { MinimumLevel = LogLevel.Warn };
                            LogLineCore.AddSink(transientSink);

                            settings.globalMinimumLevel = (iteration & 1) == 0 ? LogLevel.Trace : LogLevel.Warn;
                            settings.categoryRules = new List<CategoryRule>
                            {
                                new("HotSwap.Worker0", LogLevel.Trace)
                            };
                            Log.ApplySettings(settings);

                            LogLineCore.RemoveSink(transientSink);
                            iteration++;
                        }
                    }
                    catch (Exception ex)
                    {
                        configException = ex;
                    }
                })
                {
                    IsBackground = true
                };
                reconfigThread.Start();

                startGate.Set();

                for (int t = 0; t < WriterThreads; t++)
                {
                    Assert.IsTrue(threads[t].Join(TimeSpan.FromSeconds(15)), $"Writer {t} deadlocked.");
                    Assert.IsNull(exceptions[t], $"Writer {t} threw: {exceptions[t]}");
                }

                writersDone.Set();
                Assert.IsTrue(reconfigThread.Join(TimeSpan.FromSeconds(15)), "Reconfig thread deadlocked.");
                Assert.IsNull(configException, $"Reconfig thread threw: {configException}");
                Assert.Greater(primarySink.Count, 0);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(settings);
            }
        }

        [Test]
        public void BrokenSink_ThrowsOnWriteAndDispose_DoesNotAffectSiblingSinks()
        {
            Assume.That(
                LogLineDefines.CompiledMinimumLevel <= LogLevel.Info,
                "Test requires LOGLINE_MIN_INFO or lower to be compiled in.");

            const int EventCount = 250;
            var brokenSink = new BrokenSink { MinimumLevel = LogLevel.Trace };
            var healthySink = new CountingSink { MinimumLevel = LogLevel.Trace };

            LogLineCore.GlobalMinimumLevel = LogLevel.Trace;
            LogLineCore.AddSink(brokenSink);
            LogLineCore.AddSink(healthySink);

            Logger logger = Log.GetLogger("FaultIsolation");
            Assert.DoesNotThrow(() =>
            {
                for (int i = 0; i < EventCount; i++)
                {
                    logger.Info("still_delivered");
                }
            });

            Assert.AreEqual(EventCount, healthySink.Count,
                "Healthy sink must still receive 100% of events when a sibling sink throws.");

            Assert.DoesNotThrow(LogLineCore.ClearSinks);
        }

        #endregion
    }
}
