using System;
using System.Collections.Generic;
using LogLine.Config;
using LogLine.Core;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;
using EnumField = UnityEngine.UIElements.EnumField;

namespace LogLine.Editor.Dashboard.Tabs
{
    /// <summary>
    /// Dashboard tab for editing <see cref="CategoryRule"/> overrides with live validation,
    /// interactive longest-prefix resolution testing, and Play Mode active logger inspection.
    /// </summary>
    internal sealed class CategoriesTabView : IDashboardTab
    {
        #region Constants

        private const string UxmlPath = "Packages/com.mrtorex.logline/Editor/UI/CategoriesTab.uxml";
        private const string TesterSessionKey = "LogLine.Editor.Dashboard.TestCategory";

        #endregion

        #region Nested Types

        private readonly struct LiveLoggerRowSignature : IEquatable<LiveLoggerRowSignature>
        {
            public string Category { get; }
            public LogLevel Level { get; }
            public bool HasExactRule { get; }

            public LiveLoggerRowSignature(string category, LogLevel level, bool hasExactRule)
            {
                Category = category;
                Level = level;
                HasExactRule = hasExactRule;
            }

            public bool Equals(LiveLoggerRowSignature other) =>
                string.Equals(Category, other.Category, StringComparison.Ordinal) &&
                Level == other.Level &&
                HasExactRule == other.HasExactRule;
        }

        #endregion

        #region Private Fields

        private readonly VisualElement _rulesListContainer;
        private readonly Label _rulesEmptyLabel;
        private readonly Label _rulesValidationSummary;
        private readonly TextField _testerInput;
        private readonly VisualElement _testerResultsContainer;
        private readonly Label _liveLoggersSubtitle;
        private readonly VisualElement _liveLoggersContainer;

        private readonly List<Label> _rowStatusBadges = new();
        private readonly List<LiveLoggerRowSignature> _lastLiveLoggerSnapshot = new();

        private SerializedObject _serializedSettings;
        private int _lastBuiltRuleCount = -1;
        private bool _lastWasPlaying;

        #endregion

        #region Properties

        /// <inheritdoc />
        public string TabId => "categories";

        /// <inheritdoc />
        public string DisplayName => "Categories";

        /// <inheritdoc />
        public VisualElement Root { get; }

        #endregion

        #region Construction

        /// <summary>
        /// Initializes a new instance of <see cref="CategoriesTabView"/>.
        /// </summary>
        public CategoriesTabView()
        {
            VisualTreeAsset tree = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(UxmlPath);
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

            _rulesListContainer = Root.Q<VisualElement>("rules-list-container");
            _rulesEmptyLabel = Root.Q<Label>("rules-empty-label");
            _rulesValidationSummary = Root.Q<Label>("rules-validation-summary");
            _testerInput = Root.Q<TextField>("tester-input");
            _testerResultsContainer = Root.Q<VisualElement>("tester-results-container");
            _liveLoggersSubtitle = Root.Q<Label>("live-loggers-subtitle");
            _liveLoggersContainer = Root.Q<VisualElement>("live-loggers-container");

            Root.Q<Button>("add-rule-button")?.RegisterCallback<ClickEvent>(_ => AddRule(string.Empty, LogLevel.Debug));

            if (_testerInput != null)
            {
                string savedTest = SessionState.GetString(TesterSessionKey, "Combat.AI.Boss");
                _testerInput.SetValueWithoutNotify(savedTest);
                _testerInput.RegisterValueChangedCallback(evt =>
                {
                    SessionState.SetString(TesterSessionKey, evt.newValue ?? string.Empty);
                    RefreshRuleTester();
                });
            }
        }

        #endregion

        #region IDashboardTab Implementation

        /// <inheritdoc />
        public void Bind(SerializedObject serializedSettings)
        {
            _serializedSettings = serializedSettings;
            _lastBuiltRuleCount = -1;
            _lastLiveLoggerSnapshot.Clear();

            RebuildRulesList();
            RefreshRuntimeState();
        }

        /// <inheritdoc />
        public void RefreshRuntimeState()
        {
            if (_serializedSettings?.targetObject is not LogLineSettings settings)
            {
                _rulesListContainer?.Clear();
                _rowStatusBadges.Clear();
                _lastBuiltRuleCount = -1;
                return;
            }

            int currentCount = settings.categoryRules?.Count ?? 0;
            if (currentCount != _lastBuiltRuleCount)
            {
                RebuildRulesList();
            }

            ValidateRules(settings);
            RefreshRuleTester();
            RefreshLiveLoggers(settings);
        }

        #endregion

        #region Rules Editor

        private void RebuildRulesList()
        {
            if (_rulesListContainer == null)
            {
                return;
            }

            _rulesListContainer.Clear();
            _rowStatusBadges.Clear();

            if (_serializedSettings == null || _serializedSettings.targetObject == null)
            {
                _lastBuiltRuleCount = -1;
                _rulesEmptyLabel?.RemoveFromClassList("ll-hidden");
                return;
            }

            _serializedSettings.Update();

            SerializedProperty rulesProp = _serializedSettings.FindProperty("categoryRules");
            if (rulesProp == null)
            {
                _lastBuiltRuleCount = 0;
                _rulesEmptyLabel?.RemoveFromClassList("ll-hidden");
                return;
            }

            int count = rulesProp.arraySize;
            _lastBuiltRuleCount = count;
            _rulesEmptyLabel?.EnableInClassList("ll-hidden", count > 0);

            for (int i = 0; i < count; i++)
            {
                int rowIndex = i;
                SerializedProperty elementProp = rulesProp.GetArrayElementAtIndex(i);
                SerializedProperty prefixProp = elementProp.FindPropertyRelative("categoryPrefix");
                SerializedProperty levelProp = elementProp.FindPropertyRelative("minimumLevel");

                var row = new VisualElement();
                row.AddToClassList("ll-rule-row");
                if (i % 2 == 1)
                {
                    row.AddToClassList("ll-rule-row--alt");
                }

                var prefixField = new TextField
                {
                    tooltip = "Category prefix (case-insensitive, longest prefix wins)."
                };
                prefixField.AddToClassList("ll-rule-prefix");
                prefixField.BindProperty(prefixProp);

                var levelField = new EnumField(LogLevel.Trace)
                {
                    tooltip = "Minimum runtime severity level for categories matching this prefix."
                };
                levelField.AddToClassList("ll-rule-level");
                levelField.BindProperty(levelProp);

                var statusBadge = new Label();
                statusBadge.AddToClassList("ll-badge");
                statusBadge.AddToClassList("ll-hidden");
                _rowStatusBadges.Add(statusBadge);

                var removeButton = new Button(() => RemoveRuleAt(rowIndex))
                {
                    text = "×",
                    tooltip = "Remove rule"
                };
                removeButton.AddToClassList("ll-rule-remove");

                row.Add(prefixField);
                row.Add(levelField);
                row.Add(statusBadge);
                row.Add(removeButton);
                _rulesListContainer.Add(row);
            }
        }

        private void AddRule(string defaultPrefix, LogLevel defaultLevel)
        {
            if (_serializedSettings == null || _serializedSettings.targetObject == null)
            {
                return;
            }

            _serializedSettings.Update();
            SerializedProperty rulesProp = _serializedSettings.FindProperty("categoryRules");
            if (rulesProp == null)
            {
                return;
            }

            int insertIndex = rulesProp.arraySize;
            rulesProp.InsertArrayElementAtIndex(insertIndex);

            SerializedProperty newElement = rulesProp.GetArrayElementAtIndex(insertIndex);
            newElement.FindPropertyRelative("categoryPrefix").stringValue = defaultPrefix ?? string.Empty;
            newElement.FindPropertyRelative("minimumLevel").intValue = (int)defaultLevel;

            _serializedSettings.ApplyModifiedProperties();

            RebuildRulesList();
            RefreshRuntimeState();
        }

        private void RemoveRuleAt(int index)
        {
            if (_serializedSettings == null || _serializedSettings.targetObject == null)
            {
                return;
            }

            _serializedSettings.Update();
            SerializedProperty rulesProp = _serializedSettings.FindProperty("categoryRules");
            if (rulesProp == null || index < 0 || index >= rulesProp.arraySize)
            {
                return;
            }

            rulesProp.DeleteArrayElementAtIndex(index);
            _serializedSettings.ApplyModifiedProperties();

            RebuildRulesList();
            RefreshRuntimeState();
        }

        private void ValidateRules(LogLineSettings settings)
        {
            if (_rulesValidationSummary == null)
            {
                return;
            }

            List<CategoryRule> rules = settings.categoryRules;
            if (rules == null || rules.Count == 0)
            {
                _rulesValidationSummary.AddToClassList("ll-hidden");
                return;
            }

            var seenPrefixes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            LogLevel compiledMin = LogLineDefines.CompiledMinimumLevel;

            int emptyCount = 0;
            int duplicateCount = 0;
            int compileClampedCount = 0;

            for (int i = 0; i < rules.Count && i < _rowStatusBadges.Count; i++)
            {
                CategoryRule rule = rules[i];
                Label badge = _rowStatusBadges[i];

                badge.RemoveFromClassList("ll-badge--warn");
                badge.RemoveFromClassList("ll-badge--error");
                badge.RemoveFromClassList("ll-badge--neutral");

                if (string.IsNullOrWhiteSpace(rule.categoryPrefix))
                {
                    emptyCount++;
                    badge.text = "EMPTY";
                    badge.tooltip = "Empty prefix is ignored during resolution.";
                    badge.AddToClassList("ll-badge--error");
                    badge.RemoveFromClassList("ll-hidden");
                }
                else if (!seenPrefixes.Add(rule.categoryPrefix))
                {
                    duplicateCount++;
                    badge.text = "DUP";
                    badge.tooltip = "Duplicate prefix (case-insensitive). Earlier rule takes precedence.";
                    badge.AddToClassList("ll-badge--warn");
                    badge.RemoveFromClassList("ll-hidden");
                }
                else if (rule.minimumLevel < compiledMin)
                {
                    compileClampedCount++;
                    badge.text = $"MIN {compiledMin.ToString().ToUpperInvariant()}";
                    badge.tooltip = $"Compile define strips levels below {compiledMin}; rule level {rule.minimumLevel} is clamped.";
                    badge.AddToClassList("ll-badge--neutral");
                    badge.RemoveFromClassList("ll-hidden");
                }
                else
                {
                    badge.AddToClassList("ll-hidden");
                }
            }

            if (emptyCount == 0 && duplicateCount == 0 && compileClampedCount == 0)
            {
                _rulesValidationSummary.AddToClassList("ll-hidden");
                return;
            }

            var issues = new List<string>(3);
            if (emptyCount > 0) issues.Add($"{emptyCount} empty prefix(es)");
            if (duplicateCount > 0) issues.Add($"{duplicateCount} duplicate prefix(es)");
            if (compileClampedCount > 0) issues.Add($"{compileClampedCount} rule(s) below compile threshold ({compiledMin})");

            _rulesValidationSummary.text = "Validation: " + string.Join(" | ", issues) + ".";
            _rulesValidationSummary.RemoveFromClassList("ll-hidden");
        }

        #endregion

        #region Rule Tester Sandbox

        private void RefreshRuleTester()
        {
            if (_testerResultsContainer == null || _serializedSettings?.targetObject is not LogLineSettings settings)
            {
                return;
            }

            _testerResultsContainer.Clear();

            string query = _testerInput != null ? _testerInput.value : string.Empty;
            if (string.IsNullOrWhiteSpace(query))
            {
                AddTesterRow("Status", "Enter a category name to evaluate.");
                return;
            }

            string matchedPrefix = FindWinningPrefix(settings, query);
            LogLevel resolvedLevel = settings.ResolveLevelForCategory(query);
            LogLevel compiledMin = LogLineDefines.CompiledMinimumLevel;
            LogLevel effectiveLevel = resolvedLevel > compiledMin ? resolvedLevel : compiledMin;

            string matchDesc = matchedPrefix != null
                ? $"Rule \"{matchedPrefix}\" -> {resolvedLevel}"
                : $"Global Fallback -> {settings.globalMinimumLevel}";

            string effectiveDesc = effectiveLevel != resolvedLevel
                ? $"{effectiveLevel} (clamped by compile define {compiledMin})"
                : effectiveLevel.ToString();

            CategoryFilter consoleFilter = settings.consoleCategoryFilter.Build();
            string consoleRoute = !settings.enableConsoleLogging
                ? "Disabled"
                : consoleFilter == null || consoleFilter.IsMatch(query)
                    ? $"Accepted (Sink Min: {settings.consoleMinimumLevel})"
                    : $"Blocked by {consoleFilter.Mode}";

            CategoryFilter fileFilter = settings.fileCategoryFilter.Build();
            string fileRoute = !settings.enableFileLogging
                ? "Disabled"
                : fileFilter == null || fileFilter.IsMatch(query)
                    ? $"Accepted (Sink Min: {settings.fileMinimumLevel})"
                    : $"Blocked by {fileFilter.Mode}";

            AddTesterRow("Matched Rule", matchDesc);
            AddTesterRow("Effective Logger Level", effectiveDesc);
            AddTesterRow("UnityConsole Sink", consoleRoute);
            AddTesterRow("AsyncFile Sink", fileRoute);
        }

        private static string FindWinningPrefix(LogLineSettings settings, string category)
        {
            if (settings.categoryRules == null)
            {
                return null;
            }

            int bestLength = -1;
            string winner = null;

            for (int i = 0; i < settings.categoryRules.Count; i++)
            {
                CategoryRule rule = settings.categoryRules[i];
                if (string.IsNullOrEmpty(rule.categoryPrefix))
                {
                    continue;
                }

                int len = rule.categoryPrefix.Length;
                if (len > bestLength &&
                    category.StartsWith(rule.categoryPrefix, StringComparison.OrdinalIgnoreCase))
                {
                    bestLength = len;
                    winner = rule.categoryPrefix;
                }
            }

            return winner;
        }

        private void AddTesterRow(string key, string value)
        {
            var row = new VisualElement();
            row.AddToClassList("ll-diag-row");

            var keyLabel = new Label(key);
            keyLabel.AddToClassList("ll-diag-key");

            var valLabel = new Label(value) { style = { whiteSpace = WhiteSpace.Normal, flexShrink = 1 } };

            row.Add(keyLabel);
            row.Add(valLabel);
            _testerResultsContainer.Add(row);
        }

        #endregion

        #region Live Play Mode Loggers

        private void RefreshLiveLoggers(LogLineSettings settings)
        {
            if (_liveLoggersContainer == null || _liveLoggersSubtitle == null)
            {
                return;
            }

            bool isPlaying = Application.isPlaying;
            if (!isPlaying)
            {
                if (_lastWasPlaying || _lastLiveLoggerSnapshot.Count > 0)
                {
                    _lastWasPlaying = false;
                    _lastLiveLoggerSnapshot.Clear();
                    _liveLoggersContainer.Clear();
                }

                _liveLoggersSubtitle.text =
                    "Enter Play Mode to inspect active category loggers and create overrides with one click.";
                return;
            }

            _lastWasPlaying = true;
            KeyValuePair<string, LogLevel>[] active = LogLineCore.GetActiveLoggersSnapshot();
            Array.Sort(active, (a, b) => StringComparer.OrdinalIgnoreCase.Compare(a.Key, b.Key));

            _liveLoggersSubtitle.text = $"{active.Length} active logger(s) registered in LogLineCore:";

            var nextSnapshot = new List<LiveLoggerRowSignature>(active.Length);
            for (int i = 0; i < active.Length; i++)
            {
                string name = active[i].Key;
                bool hasExactRule = ContainsExactRule(settings, name);
                nextSnapshot.Add(new LiveLoggerRowSignature(name, active[i].Value, hasExactRule));
            }

            if (SnapshotsEqual(_lastLiveLoggerSnapshot, nextSnapshot))
            {
                return;
            }

            _lastLiveLoggerSnapshot.Clear();
            _lastLiveLoggerSnapshot.AddRange(nextSnapshot);
            _liveLoggersContainer.Clear();

            for (int i = 0; i < nextSnapshot.Count; i++)
            {
                LiveLoggerRowSignature entry = nextSnapshot[i];

                var row = new VisualElement();
                row.AddToClassList("ll-live-logger-row");
                if (i % 2 == 1)
                {
                    row.AddToClassList("ll-rule-row--alt");
                }

                var nameLabel = new Label(entry.Category);
                nameLabel.AddToClassList("ll-diag-key");

                var right = new VisualElement();
                right.AddToClassList("ll-live-logger-right");

                var levelBadge = new Label(entry.Level.ToString().ToUpperInvariant());
                levelBadge.AddToClassList("ll-badge");
                levelBadge.AddToClassList(entry.Level == LogLevel.None ? "ll-badge--warn" : "ll-badge--neutral");
                right.Add(levelBadge);

                if (!entry.HasExactRule)
                {
                    string capturedCategory = entry.Category;
                    LogLevel capturedLevel = entry.Level;
                    var overrideButton = new Button(() => AddRule(capturedCategory, capturedLevel))
                    {
                        text = "+ Rule",
                        tooltip = $"Add a CategoryRule for '{capturedCategory}'"
                    };
                    overrideButton.AddToClassList("ll-small-button");
                    right.Add(overrideButton);
                }

                row.Add(nameLabel);
                row.Add(right);
                _liveLoggersContainer.Add(row);
            }
        }

        private static bool ContainsExactRule(LogLineSettings settings, string category)
        {
            if (settings.categoryRules == null)
            {
                return false;
            }

            for (int i = 0; i < settings.categoryRules.Count; i++)
            {
                if (string.Equals(settings.categoryRules[i].categoryPrefix, category, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool SnapshotsEqual(
            List<LiveLoggerRowSignature> a,
            List<LiveLoggerRowSignature> b)
        {
            if (a.Count != b.Count)
            {
                return false;
            }

            for (int i = 0; i < a.Count; i++)
            {
                if (!a[i].Equals(b[i]))
                {
                    return false;
                }
            }

            return true;
        }

        #endregion
    }
}
