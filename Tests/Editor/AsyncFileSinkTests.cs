using System;
using System.IO;
using System.Threading;
using LogLine.Core;
using LogLine.Layouts;
using LogLine.Sinks;
using NUnit.Framework;
using UnityEngine;

namespace LogLine.Tests.Editor
{
    [TestFixture]
    public sealed class AsyncFileSinkTests
    {
        #region JSON DTO

        [Serializable]
        private struct ParsedNdjsonLine
        {
            public string level;
            public string logger;
            public string message;
        }

        #endregion

        #region Private Fields

        private string _tempDir;

        #endregion

        #region Lifecycle

        [SetUp]
        public void SetUp()
        {
            _tempDir = Path.Combine(Path.GetTempPath(), "LogLine_Tests_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_tempDir);
        }

        [TearDown]
        public void TearDown()
        {
            if (!string.IsNullOrEmpty(_tempDir) && Directory.Exists(_tempDir))
            {
                try
                {
                    Directory.Delete(_tempDir, recursive: true);
                }
                catch (IOException)
                {
                    // Ignore transient OS file handle release lag in temp directory
                }
            }
        }

        #endregion

        #region Flush & Filtering Tests

        [Test]
        public void ErrorEvent_FlushesImmediatelyWithoutDispose()
        {
            string logPath = Path.Combine(_tempDir, "immediate_flush.log");
            using var sink = new AsyncFileSink(logPath, new PatternLayout("[%p] %m%n", useColorTags: false));

            sink.Emit(new LogEvent(LogLevel.Error, "Core", "Critical disk check"));

            bool flushed = WaitUntil(() => ReadSharedText(logPath).Contains("Critical disk check"), timeoutMs: 1000);
            Assert.IsTrue(flushed, "Error log was not flushed promptly before sink disposal.");
        }

        [Test]
        public void InfoEvent_FlushesPeriodicallyWithoutDispose()
        {
            string logPath = Path.Combine(_tempDir, "periodic_flush.log");
            using var sink = new AsyncFileSink(logPath, new PatternLayout("[%p] %m%n", useColorTags: false));

            sink.Emit(new LogEvent(LogLevel.Info, "Core", "Periodic heartbeat"));

            // FlushIntervalMs is 500ms; allow generous margin for CI agents
            bool flushed = WaitUntil(() => ReadSharedText(logPath).Contains("Periodic heartbeat"), timeoutMs: 2500);
            Assert.IsTrue(flushed, "Info log was not flushed by periodic timer before sink disposal.");
        }

        [Test]
        public void CategoryFilterAndMinimumLevel_FilterOutUnmatchedEvents()
        {
            string logPath = Path.Combine(_tempDir, "filtered.log");
            using (var sink = new AsyncFileSink(logPath, new PatternLayout("[%p] [%c] %m%n", useColorTags: false)))
            {
                sink.MinimumLevel = LogLevel.Warn;
                sink.CategoryFilter = CategoryFilter.Allow("Combat");

                sink.Emit(new LogEvent(LogLevel.Info, "Combat.AI", "blocked_by_level"));
                sink.Emit(new LogEvent(LogLevel.Error, "Audio", "blocked_by_category"));
                sink.Emit(new LogEvent(LogLevel.Warn, "Combat.AI", "accepted_entry"));
            }

            string content = File.ReadAllText(logPath);
            StringAssert.Contains("accepted_entry", content);
            StringAssert.DoesNotContain("blocked_by_level", content);
            StringAssert.DoesNotContain("blocked_by_category", content);
        }

        #endregion

        #region Rotation & Saturation Tests

        [Test]
        public void Rotation_RespectsMaxFileSizeAndMaxArchiveFiles()
        {
            string logPath = Path.Combine(_tempDir, "rotate.log");
            string payload = new string('A', 200);

            using (var sink = new AsyncFileSink(logPath, new PatternLayout("%m%n", useColorTags: false)))
            {
                sink.MaxFileSizeBytes = 500;
                sink.MaxArchiveFiles = 2;

                for (int i = 0; i < 30; i++)
                {
                    sink.Emit(new LogEvent(LogLevel.Error, "Rot", $"{i:D2}_{payload}"));
                }
            }

            Assert.IsTrue(File.Exists(logPath), "Active log file missing after rotation.");
            Assert.IsTrue(File.Exists(logPath + ".1"), "Archive .1 missing after rotation.");
            Assert.IsTrue(File.Exists(logPath + ".2"), "Archive .2 missing after rotation.");
            Assert.IsFalse(File.Exists(logPath + ".3"), "Archive .3 exceeded MaxArchiveFiles limit.");
        }

        [Test]
        public void Rotation_ZeroMaxArchiveFiles_DoesNotCreateArchiveFiles()
        {
            string logPath = Path.Combine(_tempDir, "rotate_zero.log");
            string payload = new string('B', 200);

            using (var sink = new AsyncFileSink(logPath, new PatternLayout("%m%n", useColorTags: false)))
            {
                sink.MaxFileSizeBytes = 300;
                sink.MaxArchiveFiles = 0;

                for (int i = 0; i < 15; i++)
                {
                    sink.Emit(new LogEvent(LogLevel.Error, "Rot", $"{i:D2}_{payload}"));
                }
            }

            Assert.IsTrue(File.Exists(logPath));
            Assert.IsFalse(File.Exists(logPath + ".1"), "No archive files should exist when MaxArchiveFiles is 0.");
        }

        [Test]
        public void RingBufferSaturation_WithJsonLayout_OutputsValidNdjsonIncludingDroppedWarning()
        {
            string logPath = Path.Combine(_tempDir, "overflow.jsonl");
            int dropped;

            using (var sink = new AsyncFileSink(logPath, new JsonLayout(appendNewline: true), ringBufferCapacity: 4))
            {
                for (int i = 0; i < 10_000; i++)
                {
                    sink.Emit(new LogEvent(LogLevel.Info, "Burst", "overflow_event"));
                }

                dropped = sink.DroppedCount;
            }

            string[] lines = File.ReadAllLines(logPath);
            Assert.Greater(lines.Length, 0);

            bool sawDroppedWarning = false;
            for (int i = 0; i < lines.Length; i++)
            {
                if (string.IsNullOrWhiteSpace(lines[i]))
                {
                    continue;
                }

                ParsedNdjsonLine parsed = default;
                Assert.DoesNotThrow(() => parsed = JsonUtility.FromJson<ParsedNdjsonLine>(lines[i]),
                    $"Line {i + 1} is not valid JSON: {lines[i]}");

                if (parsed is { logger: "LogLine", message: not null } && parsed.message.Contains("Dropped "))
                {
                    sawDroppedWarning = true;
                }
            }

            if (dropped > 0)
            {
                Assert.IsTrue(sawDroppedWarning, "Ring buffer overflow occurred, but no JSON dropped-events warning was emitted.");
            }
        }

        #endregion

        #region Helpers

        private static string ReadSharedText(string path)
        {
            if (!File.Exists(path))
            {
                return string.Empty;
            }

            using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var reader = new StreamReader(fs);
            return reader.ReadToEnd();
        }

        private static bool WaitUntil(Func<bool> predicate, int timeoutMs)
        {
            int elapsed = 0;
            const int StepMs = 25;
            while (elapsed < timeoutMs)
            {
                if (predicate())
                {
                    return true;
                }

                Thread.Sleep(StepMs);
                elapsed += StepMs;
            }

            return predicate();
        }

        #endregion
    }
}
