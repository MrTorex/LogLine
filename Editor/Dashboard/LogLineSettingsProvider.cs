using System.Collections.Generic;
using UnityEditor;
using UnityEngine.UIElements;

namespace LogLine.Editor.Dashboard
{
    /// <summary>
    /// Integrates the LogLine dashboard directly into <c>Edit / Project Settings / LogLine</c>.
    /// </summary>
    internal static class LogLineSettingsProvider
    {
        #region Constants

        private const string SettingsPath = "Project/LogLine";

        private static readonly string[] SearchKeywords =
        {
            "LogLine",
            "Logging",
            "Log",
            "Sink",
            "Console",
            "File",
            "JSON",
            "Pattern",
            "Category",
            "Compilation",
            "Defines",
            "LOGLINE_MIN"
        };

        #endregion

        #region Registration

        /// <summary>
        /// Registers the LogLine settings provider in Unity's Project Settings window.
        /// </summary>
        /// <returns>The configured <see cref="SettingsProvider"/> instance.</returns>
        [SettingsProvider]
        public static SettingsProvider CreateProvider()
        {
            LogLineDashboardView activeView = null;

            return new SettingsProvider(SettingsPath, SettingsScope.Project)
            {
                label = "LogLine",
                keywords = new HashSet<string>(SearchKeywords),
                activateHandler = (_, rootElement) =>
                {
                    activeView?.Dispose();
                    rootElement.Clear();
                    activeView = new LogLineDashboardView();
                    rootElement.Add(activeView);
                },
                deactivateHandler = () =>
                {
                    activeView?.Dispose();
                    activeView = null;
                }
            };
        }

        #endregion
    }
}
