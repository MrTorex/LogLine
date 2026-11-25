using UnityEditor;
using UnityEngine;

namespace LogLine.Editor.Dashboard
{
    /// <summary>
    /// Dockable Unity Editor window hosting the LogLine configuration dashboard.
    /// Accessible via <c>Tools / LogLine / Dashboard</c>.
    /// </summary>
    public sealed class LogLineDashboardWindow : EditorWindow
    {
        #region Constants

        private const string MenuPath = "Tools/LogLine/Dashboard";
        private const string WindowTitle = "LogLine Dashboard";

        #endregion

        #region Private Fields

        private LogLineDashboardView _dashboardView;

        #endregion

        #region Menu & Open API

        /// <summary>
        /// Opens or focuses the LogLine Dashboard window.
        /// </summary>
        [MenuItem(MenuPath, priority = 100)]
        public static void ShowWindow() => Open(null);

        /// <summary>
        /// Opens or focuses the LogLine Dashboard window and selects an optional tab.
        /// </summary>
        /// <param name="tabId">Optional tab identifier (e.g. "global", "sinks", "categories", "compilation").</param>
        public static void Open(string tabId)
        {
            LogLineDashboardWindow window = GetWindow<LogLineDashboardWindow>(WindowTitle);
            window.minSize = new Vector2(640f, 420f);
            window.Show();

            if (!string.IsNullOrEmpty(tabId) && window._dashboardView != null)
            {
                window._dashboardView.SelectTab(tabId);
            }
        }

        #endregion

        #region Unity Lifecycle

        private void CreateGUI()
        {
            rootVisualElement.Clear();
            _dashboardView = new LogLineDashboardView();
            rootVisualElement.Add(_dashboardView);
        }

        private void OnFocus() => _dashboardView?.ReloadSettingsAsset();

        private void OnDestroy()
        {
            _dashboardView?.Dispose();
            _dashboardView = null;
        }

        #endregion
    }
}
