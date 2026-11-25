using LogLine.Config;
using LogLine.Core;
using LogLine.Sinks;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace LogLine.Editor.Dashboard.Tabs
{
    /// <summary>
    /// Dashboard tab managing global fallback threshold, native Unity log interception,
    /// auto-initialization behavior, and Play Mode runtime diagnostics.
    /// </summary>
    internal sealed class GlobalTabView : IDashboardTab
    {
        #region Constants

        private const string UxmlPath = "Packages/com.mrtorex.logline/Editor/UI/GlobalTab.uxml";

        private static readonly LogLevel[] _severityLevels =
        {
            LogLevel.Trace,
            LogLevel.Debug,
            LogLevel.Info,
            LogLevel.Warn,
            LogLevel.Error,
            LogLevel.Fatal
        };

        #endregion

        #region Private Fields

        private readonly VisualElement _levelStrip;
        private readonly Label _diagnosticsSubtitle;
        private readonly VisualElement _diagnosticsContainer;

        private SerializedObject _serializedSettings;

        #endregion

        #region Properties

        /// <inheritdoc />
        public string TabId => "global";

        /// <inheritdoc />
        public string DisplayName => "Global";

        /// <inheritdoc />
        public VisualElement Root { get; }

        #endregion

        #region Construction

        /// <summary>
        /// Initializes a new instance of <see cref="GlobalTabView"/>.
        /// </summary>
        public GlobalTabView()
        {
            var tree = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(UxmlPath);
            if (tree != null)
            {
                Root = tree.Instantiate();
            }
            else
            {
                Root = new VisualElement();
                Root.Add(new HelpBox($"Missing UXML asset: {UxmlPath}", HelpBoxMessageType.Error));
                return;
            }

            _levelStrip = Root.Q<VisualElement>("global-level-strip");
            _diagnosticsSubtitle = Root.Q<Label>("diagnostics-subtitle");
            _diagnosticsContainer = Root.Q<VisualElement>("diagnostics-container");
        }

        #endregion

        #region IDashboardTab Implementation

        /// <inheritdoc />
        public void Bind(SerializedObject serializedSettings)
        {
            _serializedSettings = serializedSettings;
            Root.Unbind();

            if (serializedSettings != null)
            {
                Root.Bind(serializedSettings);
            }

            RefreshRuntimeState();
        }

        /// <inheritdoc />
        public void RefreshRuntimeState()
        {
            RefreshSeverityStrip();
            RefreshDiagnostics();
        }

        #endregion

        #region Visual Updates

        private void RefreshSeverityStrip()
        {
            if (_levelStrip == null)
            {
                return;
            }

            _levelStrip.Clear();

            LogLevel runtimeGlobal = LogLevel.Trace;
            if (_serializedSettings?.targetObject is LogLineSettings settings)
            {
                runtimeGlobal = settings.globalMinimumLevel;
            }

            LogLevel compiledMin = LogLineDefines.CompiledMinimumLevel;

            for (int i = 0; i < _severityLevels.Length; i++)
            {
                LogLevel level = _severityLevels[i];
                var chip = new Label(level.ToString().ToUpperInvariant());
                chip.AddToClassList("ll-level-chip");

                if (level < compiledMin)
                {
                    chip.AddToClassList("ll-level-chip--stripped");
                    chip.tooltip = $"{level} is stripped at compile time by {LogLineDefines.CompiledMinimumLevel} define.";
                }
                else if (level < runtimeGlobal)
                {
                    chip.AddToClassList("ll-level-chip--runtime-filtered");
                    chip.tooltip = $"{level} is compiled in, but filtered out at runtime by globalMinimumLevel ({runtimeGlobal}).";
                }
                else
                {
                    chip.AddToClassList("ll-level-chip--active");
                    chip.tooltip = $"{level} is active for unconfigured categories.";
                }

                _levelStrip.Add(chip);
            }
        }

        private void RefreshDiagnostics()
        {
            if (_diagnosticsContainer == null || _diagnosticsSubtitle == null)
            {
                return;
            }

            _diagnosticsContainer.Clear();

            if (!Application.isPlaying)
            {
                _diagnosticsSubtitle.text = "Enter Play Mode to inspect active sinks, interceptor state, and ring buffer health.";
                return;
            }

            _diagnosticsSubtitle.text = "Live state of LogLineCore on the active Play Mode session:";

            ILogSink[] sinks = LogLineCore.GetSinksSnapshot();
            int loggerCount = LogLineCore.GetActiveLoggersSnapshot().Length;

            AddDiagnosticRow("Active Settings Asset", LogLine.Settings != null ? LogLine.Settings.name : "None (Fallback Defaults)");
            AddDiagnosticRow("Unity Log Interceptor", LogLine.IsInterceptorActive ? "Active" : "Inactive");
            AddDiagnosticRow("Cached Category Loggers", loggerCount.ToString());
            AddDiagnosticRow("Registered Sinks", sinks.Length.ToString());

            for (int i = 0; i < sinks.Length; i++)
            {
                ILogSink sink = sinks[i];
                string status = sink.IsEnabled ? $"Enabled (Min: {sink.MinimumLevel})" : "Disabled";

                if (sink is AsyncFileSink fileSink)
                {
                    status += $" | Dropped: {fileSink.DroppedCount} | File: {fileSink.ResolvedFilePath}";
                }

                AddDiagnosticRow($"  Sink [{i}]: {sink.Name}", status);
            }
        }

        private void AddDiagnosticRow(string key, string value)
        {
            var row = new VisualElement();
            row.AddToClassList("ll-diag-row");

            var keyLabel = new Label(key);
            keyLabel.AddToClassList("ll-diag-key");

            var valLabel = new Label(value);
            valLabel.style.whiteSpace = WhiteSpace.Normal;
            valLabel.style.flexShrink = 1;

            row.Add(keyLabel);
            row.Add(valLabel);
            _diagnosticsContainer.Add(row);
        }

        #endregion
    }
}
