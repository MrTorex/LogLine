using System;
using System.Collections.Generic;
using LogLine.Core;
using LogLine.Layouts;
using UnityEngine;

namespace LogLine.Config
{
    /// <summary>
    /// Central configuration asset for the LogLine architecture.
    /// </summary>
    [CreateAssetMenu(fileName = "LogLineSettings", menuName = "LogLine/Settings Asset")]
    public class LogLineSettings : ScriptableObject
    {
        #region Global Settings

        [Header("Global Settings")]
        [Tooltip("Default fallback minimum log level across all unconfigured loggers.")]
        public LogLevel globalMinimumLevel = LogLevel.Trace;

        [Tooltip("Automatically capture and redirect native Unity Debug.Log calls into the LogLine pipeline.")]
        public bool interceptUnityLogs = true;

        #endregion

        #region Console Sink Settings

        [Header("Unity Console Sink")]
        public bool enableConsoleLogging = true;
        public LogLevel consoleMinimumLevel = LogLevel.Trace;
        public bool consoleUseColorTags = true;

        [Tooltip("Pattern template for Unity Console. Example: [%d{HH:mm:ss.fff}] [%p] [%c]: %m%ex")]
        public string consolePattern = PatternLayout.DefaultConsolePattern;

        #endregion

        #region File Sink Settings

        [Header("Async File Sink")]
        public bool enableFileLogging = true;
        public LogLevel fileMinimumLevel = LogLevel.Debug;
        public string fileName = "game.log";
        public long maxFileSizeBytes = 10 * 1024 * 1024; // 10 MB
        public int maxArchiveFiles = 3;

        [Tooltip("Pattern template for log files. Example: %d{yyyy-MM-dd HH:mm:ss.fff} [%p] [%c] %m%ex%n")]
        public string filePattern = PatternLayout.DefaultFilePattern;

        #endregion

        #region Category Overrides

        [Header("Category Rules")]
        [Tooltip("Rules applied by category prefix matching (e.g. 'Network', 'Combat.AI').")]
        public List<CategoryRule> categoryRules = new();

        #endregion

        #region Resolution API

        /// <summary>
        /// Resolves the effective log level for a given category name based on registered prefix rules.
        /// Executed only upon logger creation or re-configuration.
        /// </summary>
        /// <param name="category">The category name to resolve.</param>
        /// <returns>The resolved <see cref="LogLevel"/>.</returns>
        public LogLevel ResolveLevelForCategory(string category)
        {
            if (categoryRules != null && !string.IsNullOrEmpty(category))
            {
                for (int i = 0; i < categoryRules.Count; i++)
                {
                    CategoryRule rule = categoryRules[i];
                    if (!string.IsNullOrEmpty(rule.CategoryPrefix) &&
                        category.StartsWith(rule.CategoryPrefix, StringComparison.OrdinalIgnoreCase))
                    {
                        return rule.MinimumLevel;
                    }
                }
            }

            return globalMinimumLevel;
        }

        #endregion

        #region Editor Validation

#if UNITY_EDITOR
        private void OnValidate()
        {
            if (Application.isPlaying)
            {
                LogLine.ApplySettings(this);
            }
        }
#endif

        #endregion
    }
}
