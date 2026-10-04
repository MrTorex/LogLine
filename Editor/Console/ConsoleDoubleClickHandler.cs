#if UNITY_EDITOR
using System;
using System.IO;
using System.Reflection;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEditor.Callbacks;
using UnityEngine;

namespace LogLine.Editor.Console
{
    /// <summary>
    /// Intercepts asset opening requests to ensure double-clicking on a LogLine entry in the Unity Console
    /// redirects to the calling user code rather than LogLine internal framework files.
    /// </summary>
    public static class ConsoleDoubleClickHandler
    {
        #region Private Fields

        // Matches lines formatted as: "(at Assets/Test.cs:30)" from bottom to top
        private static readonly Regex StackFrameRegex = new(
            @"\(at\s+(.+?\.cs):(\d+)\)",
            RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.RightToLeft);

        #endregion

        #region Asset Open Callback

        [OnOpenAsset(-1)]
        public static bool OnOpenAsset(int instanceId, int line)
        {
            var targetObject = EditorUtility.InstanceIDToObject(instanceId);
            string assetPath = AssetDatabase.GetAssetPath(instanceId);
            string assetName = targetObject != null ? targetObject.name : string.Empty;

            // Check if the opened file belongs to LogLine (by name or by asset path)
            bool isLogLineFile =
                assetName.IndexOf("LogLine", StringComparison.OrdinalIgnoreCase) >= 0 ||
                assetName.IndexOf("Logger", StringComparison.OrdinalIgnoreCase) >= 0 ||
                assetName.IndexOf("UnityConsoleSink", StringComparison.OrdinalIgnoreCase) >= 0 ||
                assetPath.IndexOf("logline", StringComparison.OrdinalIgnoreCase) >= 0;

            if (!isLogLineFile)
            {
                return false;
            }

            string stackTrace = GetActiveConsoleStackTrace();
            if (string.IsNullOrEmpty(stackTrace))
            {
                return false;
            }

            // Search from bottom up to find the user script that triggered the log
            var matches = StackFrameRegex.Matches(stackTrace);
            for (int i = 0; i < matches.Count; i++)
            {
                string rawFilePath = matches[i].Groups[1].Value.Trim();
                if (!int.TryParse(matches[i].Groups[2].Value, out int lineNumber))
                {
                    continue;
                }

                // Skip internal framework files in the callstack
                if (rawFilePath.IndexOf("LogLine", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    rawFilePath.IndexOf("UnityConsoleSink", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    rawFilePath.IndexOf("UnityLogInterceptor", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    continue;
                }

                // Resolve absolute path to file in project
                string projectRoot = Path.GetDirectoryName(Application.dataPath);
                string fullPath = Path.Combine(projectRoot, rawFilePath);

                if (File.Exists(fullPath))
                {
                    UnityEditorInternal.InternalEditorUtility.OpenFileAtLineExternal(fullPath, lineNumber);
                    return true; // Successfully redirected!
                }

                // Fallback for relative Unity asset database paths
                var asset = AssetDatabase.LoadMainAssetAtPath(rawFilePath);
                if (asset != null)
                {
                    AssetDatabase.OpenAsset(asset, lineNumber);
                    return true;
                }
            }

            return false;
        }

        #endregion

        #region Console Stack Trace Retrieval

        private static string GetActiveConsoleStackTrace()
        {
            var assembly = typeof(EditorWindow).Assembly;
            var consoleWindowType = assembly.GetType("UnityEditor.ConsoleWindow");
            if (consoleWindowType == null) return null;

            // Fetch the active or docked ConsoleWindow instance
            var field = consoleWindowType.GetField("ms_ConsoleWindow", BindingFlags.Static | BindingFlags.NonPublic);
            object consoleInstance = field?.GetValue(null);

            if (consoleInstance == null)
            {
                var windows = Resources.FindObjectsOfTypeAll(consoleWindowType);
                if (windows != null && windows.Length > 0)
                {
                    consoleInstance = windows[0];
                }
            }

            if (consoleInstance == null) return null;

            var activeTextField = consoleWindowType.GetField("m_ActiveText", BindingFlags.Instance | BindingFlags.NonPublic);
            return activeTextField?.GetValue(consoleInstance) as string;
        }

        #endregion
    }
}
#endif
