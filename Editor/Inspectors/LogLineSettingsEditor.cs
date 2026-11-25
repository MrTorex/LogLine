using LogLine.Config;
using LogLine.Editor.Dashboard;
using LogLine.Sinks;
using UnityEditor;
using UnityEngine;

namespace LogLine.Editor.Inspectors
{
    /// <summary>
    /// Custom Inspector for <see cref="LogLineSettings"/> providing quick navigation to the UI Toolkit Dashboard
    /// and a live preview of the resolved file sink path.
    /// </summary>
    [CustomEditor(typeof(LogLineSettings))]
    internal sealed class LogLineSettingsEditor : UnityEditor.Editor
    {
        #region Inspector GUI

        /// <inheritdoc />
        public override void OnInspectorGUI()
        {
            DrawDashboardHeader();
            EditorGUILayout.Space(8f);

            DrawDefaultInspector();

            DrawResolvedFilePathPreview();
        }

        private static void DrawDashboardHeader()
        {
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                EditorGUILayout.LabelField("LogLine Configuration", EditorStyles.boldLabel);
                EditorGUILayout.LabelField(
                    "Use the LogLine Dashboard for live layout previews, category validation, and compilation defines.",
                    EditorStyles.wordWrappedMiniLabel);

                EditorGUILayout.Space(4f);

                using (new EditorGUILayout.HorizontalScope())
                {
                    if (GUILayout.Button("Open Dashboard"))
                    {
                        LogLineDashboardWindow.ShowWindow();
                    }

                    if (GUILayout.Button("Open Project Settings"))
                    {
                        SettingsService.OpenProjectSettings("Project/LogLine");
                    }
                }
            }
        }

        private void DrawResolvedFilePathPreview()
        {
            var settings = (LogLineSettings)target;
            if (!settings.enableFileLogging)
            {
                return;
            }

            EditorGUILayout.Space(6f);
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                EditorGUILayout.LabelField("Resolved File Path Preview", EditorStyles.miniBoldLabel);
                string preview = FilePathResolver.Resolve(settings.filePath);
                EditorGUILayout.SelectableLabel(preview, EditorStyles.wordWrappedMiniLabel, GUILayout.Height(30f));
            }
        }

        #endregion
    }
}
