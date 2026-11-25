using System;
using LogLine.Config;
using LogLine.Core;
using LogLine.Layouts;
using LogLine.Sinks;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine.UIElements;
using DoubleField = UnityEngine.UIElements.DoubleField;

namespace LogLine.Editor.Dashboard.Tabs
{
    /// <summary>
    /// Dashboard tab configuring built-in Console and Async File sinks, including live layout previews,
    /// clickable pattern/macro helpers, megabyte-based rotation controls, and sink-level category filters.
    /// </summary>
    internal sealed class SinksTabView : IDashboardTab
    {
        #region Constants

        private const string UxmlPath = "Packages/com.mrtorex.logline/Editor/UI/SinksTab.uxml";
        private const double BytesPerMegabyte = 1024.0 * 1024.0;
        private const double MinFileSizeMb = 0.01;
        private const int PreviewBufferSize = 2048;

        private static readonly (string Token, string Tooltip)[] PatternTokens =
        {
            ("%d{HH:mm:ss.fff}", "Local timestamp with custom format"),
            ("%p", "Severity level (TRACE..FATAL)"),
            ("%c", "Logger category name"),
            ("%m", "Formatted log message"),
            ("%t", "Originating managed thread ID"),
            ("%ex", "Exception details (if attached)"),
            ("%n", "Newline delimiter")
        };

        private static readonly (string Macro, string Tooltip)[] PathMacros =
        {
            ("{persistentDataPath}", "Application.persistentDataPath"),
            ("{projectPath}", "Project root in Editor / executable folder in Standalone"),
            ("{tempPath}", "Application.temporaryCachePath"),
            ("{date}", "Local date (yyyy-MM-dd)"),
            ("{datetime}", "Local date and time (yyyy-MM-dd_HH-mm-ss)"),
            ("{productName}", "Sanitized Application.productName"),
            ("{version}", "Sanitized Application.version"),
            ("{platform}", "RuntimePlatform name"),
            ("{pid}", "Current OS process ID")
        };

        #endregion

        #region Private Fields

        private readonly VisualElement _consoleBody;
        private readonly Label _consolePreviewBox;
        private readonly Toggle _consolePreviewExToggle;
        private readonly PropertyField _consoleFilterPrefixesField;

        private readonly VisualElement _fileBody;
        private readonly Label _fileReadOnlyWarning;
        private readonly Label _filePathPreviewBox;
        private readonly DoubleField _maxFileSizeMbField;
        private readonly VisualElement _filePatternSection;
        private readonly VisualElement _fileJsonSection;
        private readonly Label _filePreviewBox;
        private readonly Toggle _filePreviewExToggle;
        private readonly PropertyField _fileFilterPrefixesField;

        private SerializedObject _serializedSettings;

        #endregion

        #region Properties

        /// <inheritdoc />
        public string TabId => "sinks";

        /// <inheritdoc />
        public string DisplayName => "Sinks";

        /// <inheritdoc />
        public VisualElement Root { get; }

        #endregion

        #region Construction

        /// <summary>
        /// Initializes a new instance of <see cref="SinksTabView"/>.
        /// </summary>
        public SinksTabView()
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

            _consoleBody = Root.Q<VisualElement>("console-body");
            _consolePreviewBox = Root.Q<Label>("console-preview-box");
            _consolePreviewExToggle = Root.Q<Toggle>("console-preview-ex-toggle");
            _consoleFilterPrefixesField = Root.Q<PropertyField>("console-filter-prefixes-field");

            _fileBody = Root.Q<VisualElement>("file-body");
            _fileReadOnlyWarning = Root.Q<Label>("file-readonly-warning");
            _filePathPreviewBox = Root.Q<Label>("file-path-preview-box");
            _filePatternSection = Root.Q<VisualElement>("file-pattern-section");
            _fileJsonSection = Root.Q<VisualElement>("file-json-section");
            _filePreviewBox = Root.Q<Label>("file-preview-box");
            _filePreviewExToggle = Root.Q<Toggle>("file-preview-ex-toggle");
            _fileFilterPrefixesField = Root.Q<PropertyField>("file-filter-prefixes-field");

            if (_consolePreviewBox != null)
            {
                _consolePreviewBox.enableRichText = true;
            }

            if (_filePreviewBox != null)
            {
                _filePreviewBox.enableRichText = false;
            }

            if (_filePathPreviewBox != null)
            {
                _filePathPreviewBox.enableRichText = false;
            }

            _consolePreviewExToggle?.RegisterValueChangedCallback(_ => RefreshConsolePreview());
            _filePreviewExToggle?.RegisterValueChangedCallback(_ => RefreshFilePreview());

            var maxSizeHost = Root.Q<VisualElement>("max-size-mb-host");
            _maxFileSizeMbField = new DoubleField("Max File Size (MB)")
            {
                tooltip = "Maximum file size in megabytes before rotation occurs. Stored in bytes as maxFileSizeBytes."
            };
            _maxFileSizeMbField.RegisterValueChangedCallback(OnMaxFileSizeMbChanged);
            maxSizeHost?.Add(_maxFileSizeMbField);

            PopulateTokenStrip(Root.Q<VisualElement>("console-token-strip"), PatternTokens, "consolePattern");
            PopulateTokenStrip(Root.Q<VisualElement>("file-token-strip"), PatternTokens, "filePattern");
            PopulateTokenStrip(Root.Q<VisualElement>("file-macro-strip"), PathMacros, "filePath");
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
            if (_serializedSettings?.targetObject is not LogLineSettings settings)
            {
                return;
            }

            UpdateSectionEnabledState(_consoleBody, settings.enableConsoleLogging);
            UpdateSectionEnabledState(_fileBody, settings.enableFileLogging);

            _consoleFilterPrefixesField?.EnableInClassList(
                "ll-hidden",
                settings.consoleCategoryFilter.Mode == CategoryFilterMode.None);

            _fileFilterPrefixesField?.EnableInClassList(
                "ll-hidden",
                settings.fileCategoryFilter.Mode == CategoryFilterMode.None);

            bool isJsonLayout = settings.fileLayoutType == LogLayoutType.Json;
            _filePatternSection?.EnableInClassList("ll-hidden", isJsonLayout);
            _fileJsonSection?.EnableInClassList("ll-hidden", !isJsonLayout);

            SyncMaxFileSizeField(settings.maxFileSizeBytes);
            RefreshFilePathPreview(settings);
            RefreshConsolePreview();
            RefreshFilePreview();
        }

        #endregion

        #region Previews & Sync

        private void SyncMaxFileSizeField(long maxBytes)
        {
            if (_maxFileSizeMbField == null)
            {
                return;
            }

            // Avoid overwriting text while the user is actively typing a decimal number.
            if (_maxFileSizeMbField.focusController?.focusedElement == _maxFileSizeMbField)
            {
                return;
            }

            double mb = Math.Round(maxBytes / BytesPerMegabyte, 2);
            if (Math.Abs(_maxFileSizeMbField.value - mb) > 0.0001)
            {
                _maxFileSizeMbField.SetValueWithoutNotify(mb);
            }
        }

        private void OnMaxFileSizeMbChanged(ChangeEvent<double> evt)
        {
            if (_serializedSettings == null || _serializedSettings.targetObject == null)
            {
                return;
            }

            double clampedMb = evt.newValue < MinFileSizeMb ? MinFileSizeMb : evt.newValue;
            long bytes = Math.Max(1024L, (long)Math.Round(clampedMb * BytesPerMegabyte));

            _serializedSettings.Update();
            SerializedProperty prop = _serializedSettings.FindProperty("maxFileSizeBytes");
            if (prop != null && prop.longValue != bytes)
            {
                prop.longValue = bytes;
                _serializedSettings.ApplyModifiedProperties();
            }
        }

        private void RefreshFilePathPreview(LogLineSettings settings)
        {
            if (_filePathPreviewBox != null)
            {
                _filePathPreviewBox.text = FilePathResolver.Resolve(settings.filePath);
            }

            if (_fileReadOnlyWarning != null)
            {
                string raw = settings.filePath ?? string.Empty;
                bool usesReadOnlyMacro =
                    raw.IndexOf("{dataPath}", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    raw.IndexOf("{streamingAssetsPath}", StringComparison.OrdinalIgnoreCase) >= 0;

                _fileReadOnlyWarning.EnableInClassList("ll-hidden", !usesReadOnlyMacro);
            }
        }

        private void RefreshConsolePreview()
        {
            if (_consolePreviewBox == null || _serializedSettings?.targetObject is not LogLineSettings settings)
            {
                return;
            }

            string pattern = string.IsNullOrEmpty(settings.consolePattern)
                ? PatternLayout.DefaultConsolePattern
                : settings.consolePattern;

            bool includeException = _consolePreviewExToggle != null && _consolePreviewExToggle.value;
            var sampleEvent = CreateSampleEvent(includeException);

            try
            {
                var layout = new PatternLayout(pattern, settings.consoleUseColorTags);
                string formatted = FormatPreview(layout, in sampleEvent);
                _consolePreviewBox.text = formatted;
                _consolePreviewBox.RemoveFromClassList("ll-preview-box--error");
            }
            catch (Exception ex)
            {
                _consolePreviewBox.text = $"Pattern error: {ex.Message}";
                _consolePreviewBox.AddToClassList("ll-preview-box--error");
            }
        }

        private void RefreshFilePreview()
        {
            if (_filePreviewBox == null || _serializedSettings?.targetObject is not LogLineSettings settings)
            {
                return;
            }

            bool includeException = _filePreviewExToggle != null && _filePreviewExToggle.value;
            var sampleEvent = CreateSampleEvent(includeException);

            try
            {
                ILogLayout layout;
                if (settings.fileLayoutType == LogLayoutType.Json)
                {
                    layout = settings.fileJsonLayout.Build(appendNewline: false);
                }
                else
                {
                    string pattern = string.IsNullOrEmpty(settings.filePattern)
                        ? PatternLayout.DefaultFilePattern
                        : settings.filePattern;

                    layout = new PatternLayout(pattern, useColorTags: false);
                }

                string formatted = FormatPreview(layout, in sampleEvent).TrimEnd('\n', '\r');
                _filePreviewBox.text = formatted;
                _filePreviewBox.RemoveFromClassList("ll-preview-box--error");
            }
            catch (Exception ex)
            {
                _filePreviewBox.text = $"Layout error: {ex.Message}";
                _filePreviewBox.AddToClassList("ll-preview-box--error");
            }
        }

        #endregion

        #region Helpers

        private void PopulateTokenStrip(
            VisualElement container,
            (string Token, string Tooltip)[] items,
            string targetPropertyName)
        {
            if (container == null)
            {
                return;
            }

            container.Clear();

            for (int i = 0; i < items.Length; i++)
            {
                string token = items[i].Token;
                var button = new Button(() => AppendTokenToStringProperty(targetPropertyName, token))
                {
                    text = token,
                    tooltip = $"{items[i].Tooltip} (click to append)"
                };
                button.AddToClassList("ll-token-button");
                container.Add(button);
            }
        }

        private void AppendTokenToStringProperty(string propertyName, string token)
        {
            if (_serializedSettings == null || _serializedSettings.targetObject == null)
            {
                return;
            }

            _serializedSettings.Update();
            SerializedProperty prop = _serializedSettings.FindProperty(propertyName);
            if (prop == null)
            {
                return;
            }

            prop.stringValue = (prop.stringValue ?? string.Empty) + token;
            _serializedSettings.ApplyModifiedProperties();
            RefreshRuntimeState();
        }

        private static void UpdateSectionEnabledState(VisualElement section, bool isEnabled)
        {
            if (section == null)
            {
                return;
            }

            section.SetEnabled(isEnabled);
            section.EnableInClassList("ll-disabled-section", !isEnabled);
        }

        private static LogEvent CreateSampleEvent(bool includeException)
        {
            Exception sampleException = includeException
                ? new InvalidOperationException("Target \"Boss\" state mismatch")
                : null;

            return new LogEvent(
                LogLevel.Warn,
                "Combat.AI",
                "Target \"Boss\" out of range (dist=42.5)",
                sampleException);
        }

        private static string FormatPreview(ILogLayout layout, in LogEvent sampleEvent)
        {
            Span<char> buffer = stackalloc char[PreviewBufferSize];
            if (layout.TryFormat(in sampleEvent, buffer, out int written))
            {
                return buffer[..written].ToString();
            }

            return "(preview output exceeds 2048 characters)";
        }

        #endregion
    }
}
