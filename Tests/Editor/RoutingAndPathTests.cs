using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using LogLine.Config;
using LogLine.Core;
using LogLine.Sinks;
using NUnit.Framework;
using UnityEngine;

namespace LogLine.Tests.Editor
{
    [TestFixture]
    public sealed class RoutingAndPathTests
    {
        #region Private Fields

        private LogLineSettings _settings;

        #endregion

        #region Lifecycle

        [SetUp]
        public void SetUp()
        {
            _settings = ScriptableObject.CreateInstance<LogLineSettings>();
            FilePathResolver.CaptureUnityEnvironment();
        }

        [TearDown]
        public void TearDown()
        {
            if (_settings != null)
            {
                UnityEngine.Object.DestroyImmediate(_settings);
                _settings = null;
            }
        }

        #endregion

        #region CategoryFilter Tests

        [Test]
        public void CategoryFilter_AllowList_MatchesPrefixesCaseInsensitively()
        {
            CategoryFilter filter = CategoryFilter.Allow("Combat", "Network.Sync");

            Assert.AreEqual(CategoryFilterMode.AllowList, filter.Mode);
            Assert.IsFalse(filter.IsEmpty);
            Assert.AreEqual(2, filter.Prefixes.Count);

            Assert.IsTrue(filter.IsMatch("Combat"));
            Assert.IsTrue(filter.IsMatch("combat.ai.boss"));
            Assert.IsTrue(filter.IsMatch("NETWORK.SYNC.Snapshots"));
            Assert.IsFalse(filter.IsMatch("Network.Auth"));
            Assert.IsFalse(filter.IsMatch("Audio"));
            Assert.IsFalse(filter.IsMatch(string.Empty));
            Assert.IsFalse(filter.IsMatch(null));
        }

        [Test]
        public void CategoryFilter_DenyList_BlocksMatchingPrefixes()
        {
            CategoryFilter filter = CategoryFilter.Deny("Noise", "Physics.Broadphase");

            Assert.AreEqual(CategoryFilterMode.DenyList, filter.Mode);
            Assert.IsFalse(filter.IsMatch("Noise"));
            Assert.IsFalse(filter.IsMatch("noise.spam"));
            Assert.IsFalse(filter.IsMatch("PHYSICS.BROADPHASE"));
            Assert.IsTrue(filter.IsMatch("Physics.Raycast"));
            Assert.IsTrue(filter.IsMatch("Global"));
        }

        [Test]
        public void CategoryFilterConfig_NoneMode_BuildsNullFilter()
        {
            var config = new CategoryFilterConfig
            {
                mode = CategoryFilterMode.None,
                prefixes = new List<string> { "Combat" }
            };

            Assert.IsNull(config.Build());
        }

        #endregion

        #region CategoryRule Resolution Tests

        [Test]
        public void ResolveLevelForCategory_UsesLongestPrefixMatchCaseInsensitively()
        {
            _settings.globalMinimumLevel = LogLevel.Info;
            _settings.categoryRules = new List<CategoryRule>
            {
                new("Combat", LogLevel.Warn),
                new("Combat.AI", LogLevel.Trace),
                new("Combat.AI.Boss", LogLevel.Error),
                new("", LogLevel.Fatal) // Empty prefix must be ignored
            };

            Assert.AreEqual(LogLevel.Info, _settings.ResolveLevelForCategory("Audio"));
            Assert.AreEqual(LogLevel.Warn, _settings.ResolveLevelForCategory("combat"));
            Assert.AreEqual(LogLevel.Warn, _settings.ResolveLevelForCategory("Combat.Weapons"));
            Assert.AreEqual(LogLevel.Trace, _settings.ResolveLevelForCategory("combat.ai.pathing"));
            Assert.AreEqual(LogLevel.Error, _settings.ResolveLevelForCategory("COMBAT.AI.BOSS.Phase2"));
        }

        #endregion

        #region FilePathResolver Tests

        [Test]
        public void FilePathResolver_ExpandsKnownMacrosAndDateTokens()
        {
            string expectedDate = DateTime.Now.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            string expectedPid = Process.GetCurrentProcess().Id.ToString(CultureInfo.InvariantCulture);

            string resolved = FilePathResolver.Resolve("{persistentDataPath}/Logs/run_{date}_{pid}_{yyyy}.log");

            StringAssert.StartsWith(Application.persistentDataPath.Replace('\\', '/'), resolved.Replace('\\', '/'));
            StringAssert.Contains($"run_{expectedDate}_{expectedPid}_{DateTime.Now.Year}.log", resolved);
        }

        [Test]
        public void FilePathResolver_HandlesEscapedBracesAndUnknownTokensSafely()
        {
            string resolved = FilePathResolver.Resolve("{tempPath}/{{literal}}_{unknownToken}_{unclosed.log");

            string fileName = Path.GetFileName(resolved);
            Assert.AreEqual("{literal}_{unknownToken}_{unclosed.log", fileName);
        }

        [Test]
        public void FilePathResolver_EmptyOrNullTemplate_FallsBackToDefaultPath()
        {
            string resolvedNull = FilePathResolver.Resolve(null);
            string resolvedEmpty = FilePathResolver.Resolve("   ");

            Assert.IsFalse(string.IsNullOrWhiteSpace(resolvedNull));
            Assert.AreEqual(resolvedNull, resolvedEmpty);
            StringAssert.EndsWith(".log", resolvedNull);
        }

        #endregion
    }
}
