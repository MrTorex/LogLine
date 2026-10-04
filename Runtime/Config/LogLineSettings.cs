using System;
using System.Collections.Generic;
using LogLine.Core;
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
        public LogLevel GlobalMinimumLevel = LogLevel.Trace;

        [Tooltip("Automatically capture and redirect native Unity Debug.Log calls into the LogLine pipeline.")]
        public bool InterceptUnityLogs = true;

        #endregion

        #region Console Sink Settings

        [Header("Unity Console Sink")]
        public bool EnableConsoleLogging = true;
        public LogLevel ConsoleMinimumLevel = LogLevel.Trace;
        public bool ConsoleUseColorTags = true;

        #endregion

        #region File Sink Settings

        [Header("Async File Sink")]
        public bool EnableFileLogging = true;
        public LogLevel FileMinimumLevel = LogLevel.Debug;
        public string FileName = "game.log";
        public long MaxFileSizeBytes = 10 * 1024 * 1024; // 10 MB
        public int MaxArchiveFiles = 3;

        #endregion

        #region Category Overrides

        [Header("Category Rules")]
        [Tooltip("Rules applied by category prefix matching (e.g. 'Network', 'Combat.AI').")]
        public List<CategoryRule> CategoryRules = new();

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
            if (CategoryRules != null && !string.IsNullOrEmpty(category))
            {
                for (int i = 0; i < CategoryRules.Count; i++)
                {
                    var rule = CategoryRules[i];
                    if (!string.IsNullOrEmpty(rule.CategoryPrefix) &&
                        category.StartsWith(rule.CategoryPrefix, StringComparison.OrdinalIgnoreCase))
                    {
                        return rule.MinimumLevel;
                    }
                }
            }

            return GlobalMinimumLevel;
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
