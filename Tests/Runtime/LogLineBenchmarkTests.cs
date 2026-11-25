using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using LogLine.Core;
using LogLine.Layouts;
using LogLine.Sinks;
using NUnit.Framework;
using UnityEngine.Profiling;
using UnityEngine.TestTools;
using Log = LogLine.LogLine;
using Logger = LogLine.Core.Logger;
using UnityDebug = UnityEngine.Debug;

namespace LogLine.Tests.Runtime
{
    [TestFixture]
    public sealed class LogLineBenchmarkTests
    {
        #region Constants

        private const int WarmupIterations = 25;
        private const int BenchmarkIterations = 1_000;
        private const int MaxAllowedHarnessBaseline = 2;

        #endregion

        #region Nested Types

        private readonly struct BenchmarkResult
        {
            public string Scenario { get; }
            public double ElapsedMs { get; }
            public int GcAllocCount { get; }

            public double NsPerOp => ElapsedMs * 1_000_000.0 / BenchmarkIterations;
            public double AllocsPerOp => GcAllocCount / (double)BenchmarkIterations;

            public BenchmarkResult(string scenario, double elapsedMs, int gcAllocCount)
            {
                Scenario = scenario;
                ElapsedMs = elapsedMs;
                GcAllocCount = gcAllocCount;
            }
        }

        #endregion

        #region Private Fields

        private string _tempDir;

        #endregion

        #region Lifecycle

        [SetUp]
        public void SetUp()
        {
            _tempDir = Path.Combine(Path.GetTempPath(), "LogLine_Bench_" + Guid.NewGuid().ToString("N"));
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

        #region Comparative Benchmark

        [UnityTest]
        [Category("Benchmark")]
        public IEnumerator Benchmark_LogLine_Vs_UnityDebugLog_ThroughputAndAllocations()
        {
            Assume.That(
                LogLineDefines.CompiledMinimumLevel <= LogLevel.Info,
                "Benchmark requires LOGLINE_MIN_INFO or lower to be compiled in.");

            var sw = new Stopwatch();
            var results = new List<BenchmarkResult>(6);
            Recorder gcRecorder = Recorder.Get("GC.Alloc");
            gcRecorder.FilterToCurrentThread();
            gcRecorder.enabled = false;

            // 0. Measure warmed no-op baseline to isolate any constant Editor/Debugger recorder noise
            yield return MeasureScenario(gcRecorder, sw, "No-Op Baseline", _ => { }, 0, results);
            int baselineAllocs = results[0].GcAllocCount;
            results.Clear();

            Assert.LessOrEqual(baselineAllocs, MaxAllowedHarnessBaseline,
                $"Unexpectedly high no-op profiler baseline ({baselineAllocs}).");

            // 1. UnityEngine.Debug.Log (formatted baseline)
            yield return MeasureScenario(
                gcRecorder,
                sw,
                "Unity Debug.Log (string.Format)",
                iterations =>
                {
                    for (int i = 0; i < iterations; i++)
                    {
                        UnityDebug.Log(string.Format(CultureInfo.InvariantCulture, "Entity {0} HP={1} Alive={2}", i, 99.5f, true));
                    }
                },
                baselineAllocs,
                results);

            // 2. LogLine disabled level (generic 3 value-type args)
            LogLineCore.GlobalMinimumLevel = LogLevel.Error;
            Logger disabledLogger = Log.GetLogger("Bench.Disabled");
            yield return MeasureScenario(
                gcRecorder,
                sw,
                "LogLine Disabled (Generic 3 args)",
                iterations =>
                {
                    for (int i = 0; i < iterations; i++)
                    {
                        disabledLogger.Info("Entity {0} HP={1} Alive={2}", i, 99.5f, true);
                    }
                },
                baselineAllocs,
                results);

            // 3. LogLine enabled literal -> AsyncFileSink (PatternLayout)
            LogLineCore.GlobalMinimumLevel = LogLevel.Trace;
            string patternPath = Path.Combine(_tempDir, "bench_pattern.log");
            var patternSink = new AsyncFileSink(patternPath, new PatternLayout(PatternLayout.DefaultFilePattern, useColorTags: false), ringBufferCapacity: 4096)
            {
                MinimumLevel = LogLevel.Trace
            };
            LogLineCore.AddSink(patternSink);
            Logger patternLogger = Log.GetLogger("Bench.Pattern");

            yield return MeasureScenario(
                gcRecorder,
                sw,
                "LogLine Enabled Literal (Async Pattern)",
                iterations =>
                {
                    for (int i = 0; i < iterations; i++)
                    {
                        patternLogger.Info("Steady state tick processed");
                    }
                },
                baselineAllocs,
                results);

            // 4. LogLine enabled generic 3 args -> AsyncFileSink (PatternLayout)
            yield return MeasureScenario(
                gcRecorder,
                sw,
                "LogLine Enabled Generic 3 args (Async)",
                iterations =>
                {
                    for (int i = 0; i < iterations; i++)
                    {
                        patternLogger.Info("Entity {0} HP={1} Alive={2}", i, 99.5f, true);
                    }
                },
                baselineAllocs,
                results);

            LogLineCore.RemoveSink(patternSink);

            // 5. LogLine enabled literal -> AsyncFileSink (JsonLayout)
            string jsonPath = Path.Combine(_tempDir, "bench_json.jsonl");
            var jsonSink = new AsyncFileSink(jsonPath, new JsonLayout(appendNewline: true), ringBufferCapacity: 4096)
            {
                MinimumLevel = LogLevel.Trace
            };
            LogLineCore.AddSink(jsonSink);
            Logger jsonLogger = Log.GetLogger("Bench.Json");

            yield return MeasureScenario(
                gcRecorder,
                sw,
                "LogLine Enabled Literal (Async JSON)",
                iterations =>
                {
                    for (int i = 0; i < iterations; i++)
                    {
                        jsonLogger.Info("Steady state tick processed");
                    }
                },
                baselineAllocs,
                results);

            LogLineCore.RemoveSink(jsonSink);

            BenchmarkResult unityDebugFormatted = results[0];
            BenchmarkResult logLineDisabled = results[1];
            BenchmarkResult logLineLiteralPattern = results[2];
            BenchmarkResult logLineGenericPattern = results[3];
            BenchmarkResult logLineLiteralJson = results[4];

            PrintMarkdownTable(
                baselineAllocs,
                unityDebugFormatted,
                logLineDisabled,
                logLineLiteralPattern,
                logLineLiteralJson,
                logLineGenericPattern);

            // Core guarantees
            Assert.Greater(unityDebugFormatted.GcAllocCount, 0, "Unity Debug.Log baseline should produce GC allocations.");
            Assert.AreEqual(0, logLineDisabled.GcAllocCount, "Disabled LogLine call must produce 0 net GC allocations.");
            Assert.AreEqual(0, logLineLiteralPattern.GcAllocCount, "Enabled literal LogLine call (Pattern) must produce 0 net GC allocations on caller thread.");
            Assert.AreEqual(0, logLineLiteralJson.GcAllocCount, "Enabled literal LogLine call (JSON) must produce 0 net GC allocations on caller thread.");
            Assert.Less(logLineDisabled.ElapsedMs, unityDebugFormatted.ElapsedMs,
                "Disabled LogLine path must be faster than UnityEngine.Debug.Log.");
            Assert.Less(logLineGenericPattern.GcAllocCount, unityDebugFormatted.GcAllocCount,
                "Enabled generic LogLine path must produce fewer GC allocations than UnityEngine.Debug.Log.");
        }

        #endregion

        #region Helpers

        private static IEnumerator MeasureScenario(
            Recorder gcRecorder,
            Stopwatch sw,
            string scenario,
            Action<int> work,
            int baselineAllocs,
            List<BenchmarkResult> results)
        {
            work(WarmupIterations);

            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();

            // Wait for a fresh frame so Recorder.sampleBlockCount starts cleanly
            yield return null;

            gcRecorder.enabled = true;
            sw.Restart();
            work(BenchmarkIterations);
            sw.Stop();
            gcRecorder.enabled = false;

            int netAllocs = Math.Max(0, gcRecorder.sampleBlockCount - baselineAllocs);
            results.Add(new BenchmarkResult(scenario, sw.Elapsed.TotalMilliseconds, netAllocs));
        }

        private static void PrintMarkdownTable(int baselineAllocs, params BenchmarkResult[] results)
        {
            var sb = new StringBuilder(512);
            sb.AppendLine($"### LogLine Benchmark ({BenchmarkIterations:N0} ops per scenario, harness baseline={baselineAllocs})");
            sb.AppendLine("| Scenario | Total (ms) | ns / op | GC Allocs | Allocs / op |");
            sb.AppendLine("| :--- | ---: | ---: | ---: | ---: |");

            for (int i = 0; i < results.Length; i++)
            {
                BenchmarkResult r = results[i];
                sb.AppendLine($"| {r.Scenario} | {r.ElapsedMs:F3} | {r.NsPerOp:F0} | {r.GcAllocCount:N0} | {r.AllocsPerOp:F2} |");
            }

            TestContext.Out.WriteLine(sb.ToString());
        }

        #endregion
    }
}
