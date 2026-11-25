using System;
using System.Collections.Generic;
using System.IO;
using LogLine.Config;
using UnityEditor;
using UnityEngine;

namespace LogLine.Editor.Dashboard
{
    /// <summary>
    /// Locates, validates, creates, and relocates <see cref="LogLineSettings"/> assets in the Unity project.
    /// </summary>
    internal static class LogLineSettingsLocator
    {
        #region Constants

        private const string DefaultResourcesFolder = "Assets/Resources";
        private const string DefaultAssetPath = "Assets/Resources/LogLineSettings.asset";
        private const string ExpectedAssetFileName = "LogLineSettings.asset";

        #endregion

        #region Nested Types

        /// <summary>
        /// Result of scanning the project for <see cref="LogLineSettings"/> assets.
        /// </summary>
        internal readonly struct SettingsLookupResult
        {
            public LogLineSettings Asset { get; }
            public string AssetPath { get; }
            public bool IsLoadedByResources { get; }
            public int TotalAssetsFound { get; }

            public SettingsLookupResult(
                LogLineSettings asset,
                string assetPath,
                bool isLoadedByResources,
                int totalAssetsFound)
            {
                Asset = asset;
                AssetPath = assetPath;
                IsLoadedByResources = isLoadedByResources;
                TotalAssetsFound = totalAssetsFound;
            }
        }

        #endregion

        #region Lookup & Asset Management

        /// <summary>
        /// Resolves the primary <see cref="LogLineSettings"/> asset, preferring one loadable via
        /// <c>Resources.Load&lt;LogLineSettings&gt;("LogLineSettings")</c>.
        /// </summary>
        /// <returns>A lookup result describing the resolved asset and any misconfiguration.</returns>
        public static SettingsLookupResult Locate()
        {
            string[] guids = AssetDatabase.FindAssets("t:LogLineSettings");
            if (guids == null || guids.Length == 0)
            {
                return new SettingsLookupResult(null, string.Empty, isLoadedByResources: false, totalAssetsFound: 0);
            }

            LogLineSettings fallbackAsset = null;
            string fallbackPath = string.Empty;
            var validPaths = new List<string>(guids.Length);

            for (int i = 0; i < guids.Length; i++)
            {
                string path = AssetDatabase.GUIDToAssetPath(guids[i]);
                if (string.IsNullOrEmpty(path))
                {
                    continue;
                }

                LogLineSettings asset = AssetDatabase.LoadAssetAtPath<LogLineSettings>(path);
                if (asset == null)
                {
                    continue;
                }

                validPaths.Add(path);

                if (IsResourcesAutoLoadPath(path))
                {
                    return new SettingsLookupResult(asset, path, isLoadedByResources: true, totalAssetsFound: guids.Length);
                }

                if (fallbackAsset == null)
                {
                    fallbackAsset = asset;
                    fallbackPath = path;
                }
            }

            return new SettingsLookupResult(fallbackAsset, fallbackPath, isLoadedByResources: false, totalAssetsFound: validPaths.Count);
        }

        /// <summary>
        /// Creates a new <see cref="LogLineSettings"/> asset at <see cref="DefaultAssetPath"/>.
        /// </summary>
        /// <returns>The created asset instance.</returns>
        public static LogLineSettings CreateDefaultAsset()
        {
            EnsureFolderExists(DefaultResourcesFolder);

            LogLineSettings existing = AssetDatabase.LoadAssetAtPath<LogLineSettings>(DefaultAssetPath);
            if (existing != null)
            {
                return existing;
            }

            LogLineSettings settings = ScriptableObject.CreateInstance<LogLineSettings>();
            AssetDatabase.CreateAsset(settings, DefaultAssetPath);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            return settings;
        }

        /// <summary>
        /// Moves an existing <see cref="LogLineSettings"/> asset to <see cref="DefaultAssetPath"/> so Unity's
        /// <c>Resources.Load</c> discovers it automatically in builds.
        /// </summary>
        /// <param name="sourcePath">The current asset path.</param>
        /// <param name="errorMessage">Failure reason if the move failed; otherwise empty.</param>
        /// <returns><see langword="true"/> if the move succeeded; otherwise <see langword="false"/>.</returns>
        public static bool MoveToDefaultResourcesPath(string sourcePath, out string errorMessage)
        {
            errorMessage = string.Empty;
            if (string.IsNullOrEmpty(sourcePath))
            {
                errorMessage = "Source asset path is empty.";
                return false;
            }

            EnsureFolderExists(DefaultResourcesFolder);
            errorMessage = AssetDatabase.MoveAsset(sourcePath, DefaultAssetPath);
            if (!string.IsNullOrEmpty(errorMessage))
            {
                return false;
            }

            AssetDatabase.SaveAssets();
            return true;
        }

        /// <summary>
        /// Checks whether an asset path will be found by <c>Resources.Load&lt;LogLineSettings&gt;("LogLineSettings")</c>.
        /// </summary>
        /// <param name="assetPath">Normalized Unity asset path.</param>
        /// <returns><see langword="true"/> if inside a Resources folder root with the expected filename.</returns>
        public static bool IsResourcesAutoLoadPath(string assetPath)
        {
            if (string.IsNullOrEmpty(assetPath))
            {
                return false;
            }

            string normalized = assetPath.Replace('\\', '/');
            if (!normalized.EndsWith("/" + ExpectedAssetFileName, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            int resourcesMarker = normalized.LastIndexOf("/Resources/", StringComparison.OrdinalIgnoreCase);
            if (resourcesMarker < 0)
            {
                return false;
            }

            // Must sit directly under Resources/, not in a subfolder of Resources/
            string relativeInsideResources = normalized[(resourcesMarker + "/Resources/".Length)..];
            return string.Equals(relativeInsideResources, ExpectedAssetFileName, StringComparison.OrdinalIgnoreCase);
        }

        #endregion

        #region Helpers

        private static void EnsureFolderExists(string folderPath)
        {
            if (AssetDatabase.IsValidFolder(folderPath))
            {
                return;
            }

            string parent = Path.GetDirectoryName(folderPath)?.Replace('\\', '/');
            string folderName = Path.GetFileName(folderPath);

            if (!string.IsNullOrEmpty(parent) && !AssetDatabase.IsValidFolder(parent))
            {
                EnsureFolderExists(parent);
            }

            if (!string.IsNullOrEmpty(parent) && !string.IsNullOrEmpty(folderName))
            {
                AssetDatabase.CreateFolder(parent, folderName);
            }
        }

        #endregion
    }
}
