using UnityEditor;
using UnityEngine.UIElements;

namespace LogLine.Editor.Dashboard.Tabs
{
    /// <summary>
    /// Contract for modular tabs hosted inside <see cref="LogLineDashboardView"/>.
    /// </summary>
    internal interface IDashboardTab
    {
        #region Properties

        /// <summary>
        /// Gets the stable tab identifier used for session persistence and deep-linking.
        /// </summary>
        string TabId { get; }

        /// <summary>
        /// Gets the human-readable tab title displayed in the sidebar navigation.
        /// </summary>
        string DisplayName { get; }

        /// <summary>
        /// Gets the root UI Toolkit element of this tab.
        /// </summary>
        VisualElement Root { get; }

        #endregion

        #region Lifecycle

        /// <summary>
        /// Binds this tab's controls to the active <see cref="SerializedObject"/> representing <c>LogLineSettings</c>.
        /// </summary>
        /// <param name="serializedSettings">The serialized settings object, or <see langword="null"/> when unbound.</param>
        void Bind(SerializedObject serializedSettings);

        /// <summary>
        /// Refreshes non-serialized visual indicators (compiled thresholds, Play Mode diagnostics, live validation).
        /// </summary>
        void RefreshRuntimeState();

        #endregion
    }
}
