using System.Collections.Generic;
using LogLine.Core;
using LogLine.Editor.Compilation;
using UnityEditor;
using UnityEditor.Build;
using UnityEngine.UIElements;
using EnumField = UnityEngine.UIElements.EnumField;

namespace LogLine.Editor.Dashboard.Tabs
{
    /// <summary>
    /// Dashboard tab displaying active compile-time stripping state and managing
    /// <c>LOGLINE_MIN_*</c> scripting define symbols across Unity build targets.
    /// </summary>
    internal sealed class CompilationTabView : IDashboardTab
    {
        #region Constants

        private const string UxmlPath = "Packages/com.mrtorex.logline/Editor/UI/CompilationTab.uxml";

        private static readonly LogLevel[] SeverityLevels =
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

        private readonly Label _compiledSummaryLabel;
        private readonly VisualElement _compiledLevelStrip;
        private readonly EnumField _thresholdField;
        private readonly VisualElement _noneWarningBox;
        private readonly VisualElement _platformsContainer;
        private readonly Button _applyButton;
        private readonly Label _applyStatusLabel;

        private readonly Dictionary<NamedBuildTarget, bool> _targetSelection = new();
        private readonly Dictionary<NamedBuildTarget, Toggle> _targetToggles = new();
        private readonly Dictionary<NamedBuildTarget, Label> _targetBadges = new();

        private NamedBuildTarget _activeTarget;
        private LogLevel _selectedThreshold;

        #endregion

        #region Properties

        /// <inheritdoc />
        public string TabId => "compilation";

        /// <inheritdoc />
        public string DisplayName => "Compilation";

        /// <inheritdoc />
        public VisualElement Root { get; }

        #endregion

        #region Construction

        /// <summary>
        /// Initializes a new instance of <see cref="CompilationTabView"/>.
        /// </summary>
        public CompilationTabView()
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

            _compiledSummaryLabel = Root.Q<Label>("compiled-summary-label");
            _compiledLevelStrip = Root.Q<VisualElement>("compiled-level-strip");
            _noneWarningBox = Root.Q<VisualElement>("none-warning-box");
            _platformsContainer = Root.Q<VisualElement>("platforms-container");
            _applyButton = Root.Q<Button>("apply-defines-button");
            _applyStatusLabel = Root.Q<Label>("apply-status-label");

            _activeTarget = CompilationDefineUtility.GetActiveNamedBuildTarget();
            _selectedThreshold = CompilationDefineUtility.TryGetThreshold(_activeTarget, out LogLevel current)
                ? current
                : LogLevel.Trace;

            var thresholdHost = Root.Q<VisualElement>("threshold-field-host");
            _thresholdField = new EnumField("Compile Threshold", _selectedThreshold);
            _thresholdField.RegisterValueChangedCallback(evt =>
            {
                _selectedThreshold = (LogLevel)evt.newValue;
                UpdateNoneWarning();
                _applyStatusLabel.text = string.Empty;
            });
            thresholdHost?.Add(_thresholdField);

            BuildPlatformRows();

            _applyButton?.RegisterCallback<ClickEvent>(_ => ApplySelectedDefines());
            Root.Q<Button>("select-all-targets-button")?.RegisterCallback<ClickEvent>(_ => SetAllTargets(true));
            Root.Q<Button>("select-active-target-button")?.RegisterCallback<ClickEvent>(_ => SelectActiveTargetOnly());

            RefreshRuntimeState();
        }

        #endregion

        #region IDashboardTab Implementation

        /// <inheritdoc />
        public void Bind(SerializedObject serializedSettings)
        {
            // Compilation symbols live in PlayerSettings rather than LogLineSettings.
            RefreshRuntimeState();
        }

        /// <inheritdoc />
        public void RefreshRuntimeState()
        {
            _activeTarget = CompilationDefineUtility.GetActiveNamedBuildTarget();
            RefreshCompiledSummary();
            RefreshPlatformBadges();
            UpdateNoneWarning();
            UpdateApplyButtonEnabled();
        }

        #endregion

        #region UI Builders & Updates

        private void BuildPlatformRows()
        {
            if (_platformsContainer == null)
            {
                return;
            }

            _platformsContainer.Clear();
            _targetSelection.Clear();
            _targetToggles.Clear();
            _targetBadges.Clear();

            NamedBuildTarget[] targets = CompilationDefineUtility.SupportedTargets;
            for (int i = 0; i < targets.Length; i++)
            {
                NamedBuildTarget target = targets[i];
                bool isActive = target == _activeTarget;
                _targetSelection[target] = isActive;

                var row = new VisualElement();
                row.AddToClassList("ll-platform-row");

                var toggle = new Toggle
                {
                    value = isActive,
                    text = isActive ? $"{target.TargetName} (Active)" : target.TargetName
                };
                toggle.RegisterValueChangedCallback(evt =>
                {
                    _targetSelection[target] = evt.newValue;
                    UpdateApplyButtonEnabled();
                });

                var badge = new Label();
                badge.AddToClassList("ll-badge");
                badge.AddToClassList("ll-badge--neutral");

                row.Add(toggle);
                row.Add(badge);
                _platformsContainer.Add(row);

                _targetToggles[target] = toggle;
                _targetBadges[target] = badge;
            }
        }

        private void RefreshCompiledSummary()
        {
            if (_compiledSummaryLabel == null || _compiledLevelStrip == null)
            {
                return;
            }

            LogLevel compiled = LogLineDefines.CompiledMinimumLevel;
            string symbol = CompilationDefineUtility.GetSymbolForLevel(compiled);
            _compiledSummaryLabel.text = $"Current AppDomain compiled with {symbol} (minimum severity: {compiled}).";

            _compiledLevelStrip.Clear();
            for (int i = 0; i < SeverityLevels.Length; i++)
            {
                LogLevel level = SeverityLevels[i];
                bool isCompiled = level >= compiled;

                var chip = new Label(level.ToString().ToUpperInvariant());
                chip.AddToClassList("ll-level-chip");
                chip.AddToClassList(isCompiled ? "ll-level-chip--active" : "ll-level-chip--stripped");
                chip.tooltip = isCompiled
                    ? $"{level} call sites are included in compiled IL."
                    : $"{level} call sites are completely stripped at compile time.";

                _compiledLevelStrip.Add(chip);
            }
        }

        private void RefreshPlatformBadges()
        {
            NamedBuildTarget[] targets = CompilationDefineUtility.SupportedTargets;
            for (int i = 0; i < targets.Length; i++)
            {
                NamedBuildTarget target = targets[i];
                if (_targetToggles.TryGetValue(target, out Toggle toggle))
                {
                    toggle.text = target == _activeTarget ? $"{target.TargetName} (Active)" : target.TargetName;
                }

                if (_targetBadges.TryGetValue(target, out Label badge))
                {
                    if (CompilationDefineUtility.TryGetThreshold(target, out LogLevel level))
                    {
                        badge.text = CompilationDefineUtility.GetSymbolForLevel(level);
                        badge.RemoveFromClassList("ll-badge--warn");
                        badge.AddToClassList("ll-badge--neutral");
                    }
                    else
                    {
                        badge.text = "UNSET (ALL STRIPPED)";
                        badge.RemoveFromClassList("ll-badge--neutral");
                        badge.AddToClassList("ll-badge--warn");
                    }
                }
            }
        }

        private void UpdateNoneWarning()
        {
            if (_noneWarningBox == null)
            {
                return;
            }

            _noneWarningBox.EnableInClassList("ll-hidden", _selectedThreshold != LogLevel.None);
        }

        private void UpdateApplyButtonEnabled()
        {
            if (_applyButton == null)
            {
                return;
            }

            bool hasSelection = false;
            foreach (KeyValuePair<NamedBuildTarget, bool> pair in _targetSelection)
            {
                if (pair.Value)
                {
                    hasSelection = true;
                    break;
                }
            }

            _applyButton.SetEnabled(!EditorApplication.isCompiling && hasSelection);
        }

        private void SetAllTargets(bool selected)
        {
            foreach (KeyValuePair<NamedBuildTarget, Toggle> pair in _targetToggles)
            {
                _targetSelection[pair.Key] = selected;
                pair.Value.SetValueWithoutNotify(selected);
            }

            UpdateApplyButtonEnabled();
        }

        private void SelectActiveTargetOnly()
        {
            foreach (KeyValuePair<NamedBuildTarget, Toggle> pair in _targetToggles)
            {
                bool isActive = pair.Key == _activeTarget;
                _targetSelection[pair.Key] = isActive;
                pair.Value.SetValueWithoutNotify(isActive);
            }

            UpdateApplyButtonEnabled();
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
            string symbol = CompilationDefineUtility.GetSymbolForLevel(_selectedThreshold);

            _applyStatusLabel.text = modified > 0
                ? $"Applied {symbol} to {modified} target(s). Recompiling scripts..."
                : $"Selected target(s) already use {symbol}.";

            RefreshPlatformBadges();
            UpdateApplyButtonEnabled();
        }

        #endregion
    }
}
