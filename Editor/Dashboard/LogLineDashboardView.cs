using System;
using System.Collections.Generic;
using System.IO;
using LogLine.Config;
using LogLine.Core;
using LogLine.Editor.Compilation;
using LogLine.Editor.Dashboard.Tabs;
using LogLine.Sinks;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace LogLine.Editor.Dashboard
{
    /// <summary>
    /// Shared UI Toolkit root view hosted by both <see cref="LogLineDashboardWindow"/>
    /// and <see cref="LogLineSettingsProvider"/>.
    /// </summary>
    internal sealed class LogLineDashboardView : VisualElement
    {
        #region Constants

        private const string UxmlPath = "Packages/com.mrtorex.logline/Editor/UI/LogLineDashboard.uxml";
        private const string UssPath = "Packages/com.mrtorex.logline/Editor/UI/LogLineDashboard.uss";
        private const string ActiveTabSessionKey = "LogLine.Editor.Dashboard.ActiveTabId";
        private const long DiagnosticsPollIntervalMs = 500;

        #endregion

        #region Private Fields

        private readonly List<IDashboardTab> _tabs = new();
        private readonly Dictionary<string, Button> _navButtons = new();

        private readonly Label _modeBadge;
        private readonly Label _compiledBadge;
        private readonly Button _reinitButton;

        private readonly VisualElement _assetLocationBanner;
        private readonly Label _assetLocationText;
        private readonly Button _moveToResourcesButton;
        private readonly VisualElement _compileMismatchBanner;
        private readonly Label _compileMismatchText;
        private readonly VisualElement _reinitRequiredBanner;

        private readonly VisualElement _emptyState;
        private readonly VisualElement _mainBody;
        private readonly VisualElement _navContainer;
        private readonly VisualElement _tabContentHost;
        private readonly Label _assetPathLabel;

        private LogLineSettingsLocator.SettingsLookupResult _lookup;
        private SerializedObject _serializedSettings;
        private IDashboardTab _activeTab;
        private IVisualElementScheduledItem _pollTask;
        private bool _isAttached;

        #endregion

        #region Construction

        /// <summary>
        /// Initializes a new instance of <see cref="LogLineDashboardView"/>.
        /// </summary>
        /// <param name="initialTabId">Optional tab identifier to activate initially.</param>
        public LogLineDashboardView(string initialTabId = null)
        {
            style.flexGrow = 1;

            var uss = AssetDatabase.LoadAssetAtPath<StyleSheet>(UssPath);
            if (uss != null)
            {
                styleSheets.Add(uss);
            }

            var tree = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(UxmlPath);
            if (tree == null)
            {
                Add(new HelpBox($"Missing dashboard UXML asset: {UxmlPath}", HelpBoxMessageType.Error));
                return;
            }

            tree.CloneTree(this);

            _modeBadge = this.Q<Label>("mode-badge");
            _compiledBadge = this.Q<Label>("compiled-badge");
            _reinitButton = this.Q<Button>("reinit-button");

            _assetLocationBanner = this.Q<VisualElement>("asset-location-banner");
            _assetLocationText = this.Q<Label>("asset-location-text");
            _moveToResourcesButton = this.Q<Button>("move-to-resources-button");
            _compileMismatchBanner = this.Q<VisualElement>("compile-mismatch-banner");
            _compileMismatchText = this.Q<Label>("compile-mismatch-text");
            _reinitRequiredBanner = this.Q<VisualElement>("reinit-required-banner");

            _emptyState = this.Q<VisualElement>("empty-state");
            _mainBody = this.Q<VisualElement>("main-body");
            _navContainer = this.Q<VisualElement>("nav-container");
            _tabContentHost = this.Q<VisualElement>("tab-content-host");
            _assetPathLabel = this.Q<Label>("asset-path-label");

            this.Q<Button>("create-settings-button")?.RegisterCallback<ClickEvent>(_ => CreateAndBindSettings());
            this.Q<Button>("ping-asset-button")?.RegisterCallback<ClickEvent>(_ => PingActiveSettings());
            this.Q<Button>("open-logs-button")?.RegisterCallback<ClickEvent>(_ => OpenLogsFolder());
            _reinitButton?.RegisterCallback<ClickEvent>(_ => ReinitializeRuntimePipeline());
            _moveToResourcesButton?.RegisterCallback<ClickEvent>(_ => MoveAssetToResources());

            RegisterTabs();

            string desiredTab = !string.IsNullOrEmpty(initialTabId)
                ? initialTabId
                : SessionState.GetString(ActiveTabSessionKey, "global");
            SelectTab(desiredTab);

            ReloadSettingsAsset();

            RegisterCallback<AttachToPanelEvent>(OnAttachToPanel);
            RegisterCallback<DetachFromPanelEvent>(OnDetachFromPanel);
        }

        #endregion

        #region Public API

        /// <summary>
        /// Activates the tab matching the specified identifier.
        /// </summary>
        /// <param name="tabId">The target tab identifier (e.g. "global", "sinks", "categories", "compilation").</param>
        public void SelectTab(string tabId)
        {
            IDashboardTab targetTab = null;
            for (int i = 0; i < _tabs.Count; i++)
            {
                if (string.Equals(_tabs[i].TabId, tabId, StringComparison.OrdinalIgnoreCase))
                {
                    targetTab = _tabs[i];
                    break;
                }
            }

            targetTab ??= _tabs.Count > 0 ? _tabs[0] : null;
            if (targetTab == null)
            {
                return;
            }

            _activeTab = targetTab;
            SessionState.SetString(ActiveTabSessionKey, targetTab.TabId);

            _tabContentHost.Clear();
            _tabContentHost.Add(targetTab.Root);

            foreach (KeyValuePair<string, Button> pair in _navButtons)
            {
                bool isSelected = string.Equals(pair.Key, targetTab.TabId, StringComparison.Ordinal);
                pair.Value.EnableInClassList("ll-nav-button--active", isSelected);
            }

            targetTab.RefreshRuntimeState();
        }

        /// <summary>
        /// Rescans the project for <see cref="LogLineSettings"/> and rebinds all dashboard tabs only when
        /// the underlying settings asset reference actually changes.
        /// </summary>
        public void ReloadSettingsAsset()
        {
            _lookup = LogLineSettingsLocator.Locate();

            if (_lookup.Asset == null)
            {
                UnbindSettings();
                _emptyState?.RemoveFromClassList("ll-hidden");
                _mainBody?.AddToClassList("ll-hidden");
                RefreshHeaderAndBanners();
                return;
            }

            _emptyState?.AddToClassList("ll-hidden");
            _mainBody?.RemoveFromClassList("ll-hidden");
            _assetPathLabel.text = _lookup.AssetPath;

            // Reuse existing SerializedObject when the same asset remains active.
            // Prevents NotSupportedException from duplicate TrackSerializedObjectValue calls and preserves input focus.
            if (_serializedSettings != null &&
                _serializedSettings.targetObject != null &&
                ReferenceEquals(_serializedSettings.targetObject, _lookup.Asset))
            {
                _serializedSettings.UpdateIfRequiredOrScript();
                RefreshHeaderAndBanners();
                _activeTab?.RefreshRuntimeState();
                return;
            }

            UnbindSettings();

            _serializedSettings = new SerializedObject(_lookup.Asset);
            for (int i = 0; i < _tabs.Count; i++)
            {
                _tabs[i].Bind(_serializedSettings);
            }

            this.TrackSerializedObjectValue(_serializedSettings, OnSerializedSettingsChanged);
            RefreshHeaderAndBanners();
        }

        /// <summary>
        /// Unbinds all UI Toolkit bindings and deterministically releases the underlying <see cref="SerializedObject"/>.
        /// </summary>
        public void Dispose()
        {
            UnbindSettings();

            if (_isAttached)
            {
                _isAttached = false;
                EditorApplication.playModeStateChanged -= OnPlayModeStateChanged;
                _pollTask?.Pause();
                _pollTask = null;
            }
        }

        private void UnbindSettings()
        {
            this.Unbind();

            for (int i = 0; i < _tabs.Count; i++)
            {
                _tabs[i].Bind(null);
            }

            _serializedSettings?.Dispose();
            _serializedSettings = null;
        }

        #endregion

        #region Panel Lifecycle

        private void OnAttachToPanel(AttachToPanelEvent evt)
        {
            if (_isAttached)
            {
                return;
            }

            _isAttached = true;
            EditorApplication.playModeStateChanged += OnPlayModeStateChanged;

            _pollTask = schedule.Execute(OnScheduledRefresh).Every(DiagnosticsPollIntervalMs);
            RefreshAllState();
        }

        private void OnDetachFromPanel(DetachFromPanelEvent evt)
        {
            if (!_isAttached)
            {
                return;
            }

            _isAttached = false;
            EditorApplication.playModeStateChanged -= OnPlayModeStateChanged;

            _pollTask?.Pause();
            _pollTask = null;
        }

        private void OnPlayModeStateChanged(PlayModeStateChange state)
        {
            if (state == PlayModeStateChange.EnteredPlayMode ||
                state == PlayModeStateChange.EnteredEditMode)
            {
                RefreshAllState();
            }
        }

        private void OnScheduledRefresh()
        {
            if (_lookup.Asset == null && _serializedSettings != null)
            {
                ReloadSettingsAsset();
                return;
            }

            if (Application.isPlaying || EditorApplication.isCompiling)
            {
                RefreshHeaderAndBanners();
                _activeTab?.RefreshRuntimeState();
            }
        }

        private void OnSerializedSettingsChanged(SerializedObject obj)
        {
            RefreshHeaderAndBanners();
            _activeTab?.RefreshRuntimeState();
        }

        #endregion

        #region Tab Setup

        private void RegisterTabs()
        {
            _tabs.Clear();
            _navButtons.Clear();
            _navContainer?.Clear();

            AddTab(new GlobalTabView());
            AddTab(new SinksTabView());
            AddTab(new CategoriesTabView());
            AddTab(new CompilationTabView());
        }

        private void AddTab(IDashboardTab tab)
        {
            _tabs.Add(tab);

            var button = new Button(() => SelectTab(tab.TabId))
            {
                text = tab.DisplayName
            };
            button.AddToClassList("ll-nav-button");

            _navButtons[tab.TabId] = button;
            _navContainer?.Add(button);
        }

        #endregion

        #region Header, Banners & Actions

        private void RefreshAllState()
        {
            if (_serializedSettings != null && _lookup.Asset == null)
            {
                ReloadSettingsAsset();
                return;
            }

            RefreshHeaderAndBanners();
            _activeTab?.RefreshRuntimeState();
        }

        private void RefreshHeaderAndBanners()
        {
            bool isPlaying = Application.isPlaying;

            if (_modeBadge != null)
            {
                _modeBadge.text = isPlaying ? "PLAY MODE" : "EDIT MODE";
                _modeBadge.EnableInClassList("ll-badge--play", isPlaying);
                _modeBadge.EnableInClassList("ll-badge--neutral", !isPlaying);
            }

            if (_compiledBadge != null)
            {
                LogLevel compiled = LogLineDefines.CompiledMinimumLevel;
                _compiledBadge.text = CompilationDefineUtility.GetSymbolForLevel(compiled);
                _compiledBadge.EnableInClassList("ll-badge--warn", compiled == LogLevel.None);
                _compiledBadge.EnableInClassList("ll-badge--active", compiled != LogLevel.None);
            }

            _reinitButton?.EnableInClassList("ll-hidden", !isPlaying || _lookup.Asset == null);

            UpdateAssetLocationBanner();
            UpdateCompileMismatchBanner();
            UpdateReinitRequiredBanner(isPlaying);
        }

        private void UpdateAssetLocationBanner()
        {
            if (_assetLocationBanner == null || _assetLocationText == null)
            {
                return;
            }

            if (_lookup.Asset == null)
            {
                _assetLocationBanner.AddToClassList("ll-hidden");
                return;
            }

            if (!_lookup.IsLoadedByResources)
            {
                _assetLocationText.text =
                    $"Settings asset '{_lookup.AssetPath}' is outside 'Resources/LogLineSettings.asset' and will not auto-load in builds.";
                _moveToResourcesButton?.RemoveFromClassList("ll-hidden");
                _assetLocationBanner.RemoveFromClassList("ll-hidden");
                return;
            }

            if (_lookup.TotalAssetsFound > 1)
            {
                _assetLocationText.text =
                    $"Found {_lookup.TotalAssetsFound} LogLineSettings assets in project. Using '{_lookup.AssetPath}'. Remove unused duplicates to avoid confusion.";
                _moveToResourcesButton?.AddToClassList("ll-hidden");
                _assetLocationBanner.RemoveFromClassList("ll-hidden");
                return;
            }

            _assetLocationBanner.AddToClassList("ll-hidden");
        }

        private void UpdateCompileMismatchBanner()
        {
            if (_compileMismatchBanner == null || _compileMismatchText == null)
            {
                return;
            }

            if (_lookup.Asset == null)
            {
                _compileMismatchBanner.AddToClassList("ll-hidden");
                return;
            }

            LogLevel globalMin = _lookup.Asset.globalMinimumLevel;
            LogLevel compiledMin = LogLineDefines.CompiledMinimumLevel;

            if (globalMin < compiledMin)
            {
                string symbol = CompilationDefineUtility.GetSymbolForLevel(compiledMin);
                _compileMismatchText.text =
                    $"Global Minimum Level is {globalMin}, but {symbol} strips all levels below {compiledMin} at compile time.";
                _compileMismatchBanner.RemoveFromClassList("ll-hidden");
            }
            else
            {
                _compileMismatchBanner.AddToClassList("ll-hidden");
            }
        }

        private void UpdateReinitRequiredBanner(bool isPlaying)
        {
            if (_reinitRequiredBanner == null)
            {
                return;
            }

            if (!isPlaying || _lookup.Asset == null)
            {
                _reinitRequiredBanner.AddToClassList("ll-hidden");
                return;
            }

            bool needsReinit = IsRuntimePipelineOutOfSync(_lookup.Asset);
            _reinitRequiredBanner.EnableInClassList("ll-hidden", !needsReinit);
        }

        private static bool IsRuntimePipelineOutOfSync(LogLineSettings settings)
        {
            ILogSink[] sinks = LogLineCore.GetSinksSnapshot();
            bool hasConsoleSink = false;
            bool hasFileSink = false;
            AsyncFileSink activeFileSink = null;

            for (int i = 0; i < sinks.Length; i++)
            {
                if (sinks[i] is UnityConsoleSink)
                {
                    hasConsoleSink = true;
                }
                else if (sinks[i] is AsyncFileSink fileSink)
                {
                    hasFileSink = true;
                    activeFileSink = fileSink;
                }
            }

            if (settings.enableConsoleLogging && !hasConsoleSink)
            {
                return true;
            }

            if (settings.enableFileLogging && !hasFileSink)
            {
                return true;
            }

            if (activeFileSink != null)
            {
                string expectedTemplate = string.IsNullOrWhiteSpace(settings.filePath)
                    ? FilePathResolver.DefaultFilePathTemplate
                    : settings.filePath;

                if (!string.Equals(activeFileSink.FilePathTemplate, expectedTemplate, StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }

        private void CreateAndBindSettings()
        {
            LogLineSettings created = LogLineSettingsLocator.CreateDefaultAsset();
            Selection.activeObject = created;
            ReloadSettingsAsset();
        }

        private void MoveAssetToResources()
        {
            if (LogLineSettingsLocator.MoveToDefaultResourcesPath(_lookup.AssetPath, out string error))
            {
                ReloadSettingsAsset();
            }
            else
            {
                EditorUtility.DisplayDialog("LogLine", $"Could not move asset:\n{error}", "OK");
            }
        }

        private void PingActiveSettings()
        {
            if (_lookup.Asset != null)
            {
                EditorGUIUtility.PingObject(_lookup.Asset);
                Selection.activeObject = _lookup.Asset;
            }
        }

        private void ReinitializeRuntimePipeline()
        {
            if (!Application.isPlaying || _lookup.Asset == null)
            {
                return;
            }

            LogLine.Initialize(_lookup.Asset);
            RefreshAllState();
        }

        private void OpenLogsFolder()
        {
            string targetFile = _lookup.Asset != null
                ? FilePathResolver.Resolve(_lookup.Asset.filePath)
                : FilePathResolver.Resolve(FilePathResolver.DefaultFilePathTemplate);

            string directory = Path.GetDirectoryName(targetFile);
            if (string.IsNullOrEmpty(directory))
            {
                directory = Application.persistentDataPath;
            }

            if (!Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }

            EditorUtility.RevealInFinder(directory);
        }

        #endregion
    }
}
