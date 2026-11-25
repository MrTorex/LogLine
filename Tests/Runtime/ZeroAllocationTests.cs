using System;
using System.Collections;
using System.Globalization;
using System.IO;
using LogLine.Buffering;
using LogLine.Core;
using LogLine.Layouts;
using LogLine.Sinks;
using NUnit.Framework;
using UnityEngine.Profiling;
using UnityEngine.TestTools;
using Log = LogLine.LogLine;
using Logger = LogLine.Core.Logger;

namespace LogLine.Tests.Runtime
{
    [TestFixture]
    public sealed class ZeroAllocationTests
    {
        #region Constants

        private const int WarmupIterations = 32;
        private const int MeasuredIterations = 1_000;
        private const int MaxAllowedHarnessBaseline = 2;

        #endregion

        #region Private Fields

        private string _tempDir;
        private Recorder _gcRecorder;

        #endregion

        #region Lifecycle

        [SetUp]
        public void SetUp()
        {
            _tempDir = Path.Combine(Path.GetTempPath(), "LogLine_Alloc_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_tempDir);
            LogLineCore.ResetStaticState();

            _gcRecorder = Recorder.Get("GC.Alloc");
            _gcRecorder.FilterToCurrentThread();
            _gcRecorder.enabled = false;
        }

        [TearDown]
        public void TearDown()
        {
            if (_gcRecorder != null)
            {
                _gcRecorder.enabled = false;
            }

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

        #region Strict Zero-Allocation Hot Paths

        [UnityTest]
        public IEnumerator SanityCheck_GcAllocRecorderDetectsManagedAllocationsAccurately()
        {
            const int ExpectedAllocations = 100;
            object lastAllocated = null;

            Action<int> allocatingWork = iterations =>
            {
                for (int i = 0; i < iterations; i++)
                {
                    lastAllocated = new object();
                }
            };

            int baseline = 0;
            int measured = 0;
            yield return MeasureWithBaseline(allocatingWork, ExpectedAllocations, null,
                (b, m) =>
                {
                    baseline = b;
                    measured = m;
                });

            GC.KeepAlive(lastAllocated);
            int net = measured - baseline;

            Assert.GreaterOrEqual(net, ExpectedAllocations,
                $"GC.Alloc recorder failed to detect {ExpectedAllocations} allocations (measured={measured}, baseline={baseline}).");
        }

        [UnityTest]
        public IEnumerator DisabledLevel_GenericThreeValueTypeArgs_1000Calls_AllocatesZeroGc()
        {
            var sink = new CountingSink { MinimumLevel = LogLevel.Trace };
            LogLineCore.GlobalMinimumLevel = LogLevel.Error;
            LogLineCore.AddSink(sink);

            Logger logger = Log.GetLogger("Alloc.Disabled");
            Action<int> work = iterations =>
            {
                for (int i = 0; i < iterations; i++)
                {
                    logger.Info("HP: {0}, Pos: {1}, Alive: {2}", i, 42.5f, true);
                }
            };

            yield return AssertZeroNetAllocations(work, MeasuredIterations, sink.Reset, "Disabled generic log call");
            Assert.AreEqual(0, sink.Count);
        }

        [UnityTest]
        public IEnumerator EnabledLiteral_ThroughLogLineCoreAndCountingSink_1000Calls_AllocatesZeroGc()
        {
            Assume.That(
                LogLineDefines.CompiledMinimumLevel <= LogLevel.Info,
                "Test requires LOGLINE_MIN_INFO or lower to be compiled in.");

            var sink = new CountingSink { MinimumLevel = LogLevel.Trace };
            LogLineCore.GlobalMinimumLevel = LogLevel.Trace;
            LogLineCore.AddSink(sink);

            Logger logger = Log.GetLogger("Alloc.Literal");
            Action<int> work = iterations =>
            {
                for (int i = 0; i < iterations; i++)
                {
                    logger.Info("Zero-alloc literal payload");
                }
            };

            yield return AssertZeroNetAllocations(work, MeasuredIterations, sink.Reset, "Enabled literal dispatch");
            Assert.AreEqual(MeasuredIterations, sink.Count);
        }

        [UnityTest]
        public IEnumerator EnabledLiteral_ThroughAsyncFileSink_1000Calls_AllocatesZeroGcOnCallerThread()
        {
            Assume.That(
                LogLineDefines.CompiledMinimumLevel <= LogLevel.Info,
                "Test requires LOGLINE_MIN_INFO or lower to be compiled in.");

            string logPath = Path.Combine(_tempDir, "zero_alloc_async.log");
            using var fileSink = new AsyncFileSink(logPath, new JsonLayout(appendNewline: true), ringBufferCapacity: 4096);
            fileSink.MinimumLevel = LogLevel.Trace;

            LogLineCore.GlobalMinimumLevel = LogLevel.Trace;
            LogLineCore.AddSink(fileSink);

            Logger logger = Log.GetLogger("Alloc.AsyncFile");
            Action<int> work = iterations =>
            {
                for (int i = 0; i < iterations; i++)
                {
                    logger.Info("Zero-alloc async file entry");
                }
            };

            yield return AssertZeroNetAllocations(work, MeasuredIterations, null, "AsyncFileSink enqueue on caller thread");
            LogLineCore.RemoveSink(fileSink);
        }

        [UnityTest]
        public IEnumerator LogRingBuffer_EnqueueDequeue_1000Calls_AllocatesZeroGc()
        {
            var ringBuffer = new LogRingBuffer(128);
            var sampleEvent = new LogEvent(LogLevel.Warn, "Combat.AI", "RingBuffer payload");

            int dequeuedCount = 0;
            Action<int> work = iterations =>
            {
                for (int i = 0; i < iterations; i++)
                {
                    ringBuffer.TryEnqueue(in sampleEvent);
                    if (ringBuffer.TryDequeue(out _))
                    {
                        dequeuedCount++;
                    }
                }
            };

            yield return AssertZeroNetAllocations(work, MeasuredIterations, () => dequeuedCount = 0, "LogRingBuffer");
            Assert.AreEqual(MeasuredIterations, dequeuedCount);
        }

        [UnityTest]
        public IEnumerator CategoryFilter_IsMatch_1000Calls_AllocatesZeroGc()
        {
            CategoryFilter filter = CategoryFilter.Allow("Combat", "Network");

            int matches = 0;
            Action<int> work = iterations =>
            {
                for (int i = 0; i < iterations; i++)
                {
                    if (filter.IsMatch("Combat.AI"))
                    {
                        matches++;
                    }
                }
            };

            yield return AssertZeroNetAllocations(work, MeasuredIterations, () => matches = 0, "CategoryFilter.IsMatch");
            Assert.AreEqual(MeasuredIterations, matches);
        }

        [UnityTest]
        public IEnumerator PatternLayout_TryFormat_1000Calls_AllocatesZeroGc()
        {
            var patternLayout = new PatternLayout(PatternLayout.DefaultFilePattern, useColorTags: false);
            var sampleEvent = new LogEvent(LogLevel.Warn, "Combat.AI", "Target \"Boss\" failed\nretry=1");

            int totalChars = 0;
            Action<int> work = iterations =>
            {
                Span<char> stackBuffer = stackalloc char[512];
                for (int i = 0; i < iterations; i++)
                {
                    if (patternLayout.TryFormat(in sampleEvent, stackBuffer, out int written))
                    {
                        totalChars += written;
                    }
                }
            };

            yield return AssertZeroNetAllocations(work, MeasuredIterations, () => totalChars = 0, "PatternLayout.TryFormat");
            Assert.Greater(totalChars, 0);
        }

        [UnityTest]
        public IEnumerator JsonLayout_TryFormat_1000Calls_AllocatesZeroGc()
        {
            var jsonLayout = new JsonLayout(appendNewline: true);
            var sampleEvent = new LogEvent(LogLevel.Warn, "Combat.AI", "Target \"Boss\" failed\nretry=1");

            int totalChars = 0;
            Action<int> work = iterations =>
            {
                Span<char> stackBuffer = stackalloc char[512];
                for (int i = 0; i < iterations; i++)
                {
                    if (jsonLayout.TryFormat(in sampleEvent, stackBuffer, out int written))
                    {
                        totalChars += written;
                    }
                }
            };

            yield return AssertZeroNetAllocations(work, MeasuredIterations, () => totalChars = 0, "JsonLayout.TryFormat");
            Assert.Greater(totalChars, 0);
        }

        #endregion

        #region Enabled Generic Formatting (Zero-Boxing Verification)

        [UnityTest]
        public IEnumerator EnabledGenericFormatting_1000Calls_ProducesFarFewerAllocationsThanStringFormat()
        {
            Assume.That(
                LogLineDefines.CompiledMinimumLevel <= LogLevel.Info,
                "Test requires LOGLINE_MIN_INFO or lower to be compiled in.");

            var sink = new CountingSink { MinimumLevel = LogLevel.Trace };
            LogLineCore.GlobalMinimumLevel = LogLevel.Trace;
            LogLineCore.AddSink(sink);

            Logger logger = Log.GetLogger("Alloc.Generic");
            const string Format = "Entity {0} HP={1} Alive={2}";

            Action<int> logLineWork = iterations =>
            {
                for (int i = 0; i < iterations; i++)
                {
                    logger.Info(Format, i, 99.5f, true);
                }
            };

            int sinkLength = 0;
            Action<int> baselineWork = iterations =>
            {
                for (int i = 0; i < iterations; i++)
                {
                    sinkLength += string.Format(CultureInfo.InvariantCulture, Format, i, 99.5f, true).Length;
                }
            };

            int logLineBaseline = 0;
            int logLineMeasured = 0;
            yield return MeasureWithBaseline(logLineWork, MeasuredIterations, sink.Reset,
                (b, m) =>
                {
                    logLineBaseline = b;
                    logLineMeasured = m;
                });

            int formatBaseline = 0;
            int formatMeasured = 0;
            yield return MeasureWithBaseline(baselineWork, MeasuredIterations, () => sinkLength = 0,
                (b, m) =>
                {
                    formatBaseline = b;
                    formatMeasured = m;
                });

            int logLineNet = Math.Max(0, logLineMeasured - logLineBaseline);
            int formatNet = Math.Max(0, formatMeasured - formatBaseline);

            TestContext.Out.WriteLine(
                $"[Enabled Generic 1000x] LogLine={logLineNet:N0} allocs ({logLineNet / (double)MeasuredIterations:F2}/op) vs " +
                $"string.Format={formatNet:N0} allocs ({formatNet / (double)MeasuredIterations:F2}/op)");

            Assert.AreEqual(MeasuredIterations, sink.Count);
            Assert.Greater(sinkLength, 0);
            Assert.Greater(formatNet, MeasuredIterations,
                "Baseline string.Format should allocate more than 1 object per call due to value-type boxing.");
            Assert.Less(logLineNet, formatNet,
                "LogLine generic formatting must avoid value-type boxing and produce fewer GC allocations than string.Format.");
        }

        #endregion

        #region Measurement Helpers

        private IEnumerator AssertZeroNetAllocations(
            Action<int> work,
            int iterations,
            Action beforeMeasure,
            string label)
        {
            int baseline = 0;
            int measured = 0;
            yield return MeasureWithBaseline(work, iterations, beforeMeasure,
                (b, m) =>
                {
                    baseline = b;
                    measured = m;
                });

            Assert.LessOrEqual(baseline, MaxAllowedHarnessBaseline,
                $"Unexpectedly high no-op profiler baseline ({baseline}) during '{label}'.");
            Assert.LessOrEqual(measured, baseline,
                $"'{label}' produced {measured - baseline} net GC allocation(s) across {iterations} iterations " +
                $"(raw={measured}, no-op baseline={baseline}).");
        }

        private IEnumerator MeasureWithBaseline(
            Action<int> work,
            int iterations,
            Action beforeMeasure,
            Action<int, int> onCompleted)
        {
            Action<int> noop = _ => { };
            noop(WarmupIterations);
            work(WarmupIterations);

            // Measure warmed-up no-op on a fresh frame to isolate any constant Editor/Debugger recorder noise
            yield return null;
            _gcRecorder.enabled = true;
            noop(iterations);
            _gcRecorder.enabled = false;
            int baseline = _gcRecorder.sampleBlockCount;

            beforeMeasure?.Invoke();

            // Measure actual workload on a fresh frame
            yield return null;
            _gcRecorder.enabled = true;
            work(iterations);
            _gcRecorder.enabled = false;
            int measured = _gcRecorder.sampleBlockCount;

            onCompleted(baseline, measured);
        }

        #endregion
    }
}
