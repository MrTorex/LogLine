using System.Collections.Generic;
using LogLine.Config;
using LogLine.Core;
using LogLine.Editor.Compilation;
using UnityEditor;
using UnityEditor.Build;
using UnityEngine;

namespace LogLine.Editor.Inspectors
{
    /// <summary>
    /// Custom Inspector for <see cref="LogLineSettings"/> adding compile-time scripting define controls.
    /// </summary>
    [CustomEditor(typeof(LogLineSettings))]
    internal sealed class LogLineSettingsEditor : UnityEditor.Editor
    {
        #region Private Fields

        private LogLevel _selectedThreshold;
        private NamedBuildTarget _activeTarget;
        private readonly Dictionary<NamedBuildTarget, bool> _targetSelection = new();

        #endregion

        #region Lifecycle

        private void OnEnable() => RefreshTargetState();

        #endregion

        #region Inspector GUI

        /// <inheritdoc />
        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();

            EditorGUILayout.Space(12f);
            DrawCompilationSection();
        }

        private void DrawCompilationSection()
        {
            EditorGUILayout.LabelField("Compilation Stripping (Scripting Defines)", EditorStyles.boldLabel);

            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                EditorGUILayout.LabelField(
                    "Active Compiled Level",
                    $"{LogLineDefines.CompiledMinimumLevel} ({CompilationDefineUtility.GetSymbolForLevel(LogLineDefines.CompiledMinimumLevel)})",
                    EditorStyles.miniLabel);

                EditorGUILayout.Space(4f);

                _selectedThreshold = (LogLevel)EditorGUILayout.EnumPopup("Compile Threshold", _selectedThreshold);

                if (_selectedThreshold == LogLevel.None)
                {
                    EditorGUILayout.HelpBox(
                        "LOGLINE_MIN_NONE strips all LogLine call sites (including Error and Fatal) at compile time.",
                        MessageType.Warning);
                }

                EditorGUILayout.Space(6f);
                EditorGUILayout.LabelField("Target Platforms", EditorStyles.miniBoldLabel);

                foreach (NamedBuildTarget buildTarget in CompilationDefineUtility.SupportedTargets)
                {
                    bool isSelected = _targetSelection.TryGetValue(buildTarget, out bool current) && current;
                    string activeSuffix = buildTarget == _activeTarget ? " (Active)" : string.Empty;
                    string currentSymbol = CompilationDefineUtility.TryGetThreshold(buildTarget, out LogLevel targetLevel)
                        ? targetLevel.ToString()
                        : "Unset";

                    bool nextSelected = EditorGUILayout.ToggleLeft(
                        $"{buildTarget.TargetName}{activeSuffix}  —  [{currentSymbol}]",
                        isSelected);

                    _targetSelection[buildTarget] = nextSelected;
                }

                EditorGUILayout.Space(6f);

                using (new EditorGUI.DisabledScope(EditorApplication.isCompiling || !HasAnyTargetSelected()))
                {
                    if (GUILayout.Button("Apply Scripting Defines"))
                    {
                        ApplySelectedDefines();
                    }
                }
            }
        }

        #endregion

        #region Helpers

        private void RefreshTargetState()
        {
            _activeTarget = CompilationDefineUtility.GetActiveNamedBuildTarget();
            _selectedThreshold = CompilationDefineUtility.TryGetThreshold(_activeTarget, out LogLevel current)
                ? current
                : LogLevel.Trace;

            _targetSelection.Clear();
            foreach (NamedBuildTarget target in CompilationDefineUtility.SupportedTargets)
            {
                _targetSelection[target] = target == _activeTarget;
            }
        }

        private bool HasAnyTargetSelected()
        {
            foreach (KeyValuePair<NamedBuildTarget, bool> pair in _targetSelection)
            {
                if (pair.Value)
                {
                    return true;
                }
            }

            return false;
        }

        private void ApplySelectedDefines()
        {
            var targets = new List<NamedBuildTarget>();
            foreach (KeyValuePair<NamedBuildTarget, bool> pair in _targetSelection)
            {
                if (pair.Value)
                {
                    targets.Add(pair.Key);
                }
            }

            int modified = CompilationDefineUtility.SetThreshold(targets, _selectedThreshold);
            if (modified == 0)
            {
                UnityEngine.Debug.Log($"[LogLine] Selected targets already use {CompilationDefineUtility.GetSymbolForLevel(_selectedThreshold)}.");
            }
        }

        #endregion
    }
}
