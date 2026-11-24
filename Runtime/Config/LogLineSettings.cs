using System;
using System.Collections.Generic;
using LogLine.Core;
using LogLine.Layouts;
using LogLine.Sinks;
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

        public CategoryFilterConfig consoleCategoryFilter = new()
        {
            Mode = CategoryFilterMode.None,
            Prefixes = new List<string>()
        };

        #endregion

        #region File Sink Settings

        [Header("Async File Sink")]
        public bool enableFileLogging = true;
        public LogLevel fileMinimumLevel = LogLevel.Debug;

        [Tooltip("File path template supporting macros ({persistentDataPath}, {dataPath}, {projectPath}, {tempPath}, {productName}, {version}, {platform}, {pid}) and date tokens ({date}, {datetime}, {yyyy-MM-dd}, {date:yyyy-MM-dd_HH-mm-ss}). Relative paths resolve under {persistentDataPath}/Logs/.")]
        public string filePath = FilePathResolver.DefaultFilePathTemplate;

        public long maxFileSizeBytes = 10 * 1024 * 1024; // 10 MB
        public int maxArchiveFiles = 3;

        [Tooltip("Pattern template for log files. Example: %d{yyyy-MM-dd HH:mm:ss.fff} [%p] [%c] %m%ex%n")]
        public string filePattern = PatternLayout.DefaultFilePattern;

        public CategoryFilterConfig fileCategoryFilter = new()
        {
            Mode = CategoryFilterMode.None,
            Prefixes = new List<string>()
        };

        #endregion

        #region Category Overrides

        [Header("Category Rules")]
        [Tooltip("Rules applied by longest matching category prefix (e.g. 'Network', 'Network.Packets').")]
        public List<CategoryRule> categoryRules = new();

        #endregion

        #region Resolution API

        /// <summary>
        /// Resolves the effective log level for a given category name using longest-prefix matching.
        /// Executed only upon logger creation or re-configuration.
        /// </summary>
        /// <param name="category">The category name to resolve.</param>
        /// <returns>The resolved <see cref="LogLevel"/>.</returns>
        public LogLevel ResolveLevelForCategory(string category)
        {
            if (categoryRules != null && !string.IsNullOrEmpty(category))
            {
                int bestMatchLength = -1;
                LogLevel resolvedLevel = globalMinimumLevel;

                for (int i = 0; i < categoryRules.Count; i++)
                {
                    CategoryRule rule = categoryRules[i];
                    if (string.IsNullOrEmpty(rule.CategoryPrefix))
                    {
                        continue;
                    }

                    int prefixLength = rule.CategoryPrefix.Length;
                    if (prefixLength > bestMatchLength &&
                        category.StartsWith(rule.CategoryPrefix, StringComparison.OrdinalIgnoreCase))
                    {
                        bestMatchLength = prefixLength;
                        resolvedLevel = rule.MinimumLevel;
                    }
                }

                if (bestMatchLength >= 0)
                {
                    return resolvedLevel;
                }
            }

            return globalMinimumLevel;
        }

        #endregion

        #region Editor Validation

#if UNITY_EDITOR
        private void OnValidate()
        {
            if (Application.isPlaying && LogLine.Settings == this)
            {
                LogLine.ApplySettings(this);
            }
        }
#endif

        #endregion
    }
}
