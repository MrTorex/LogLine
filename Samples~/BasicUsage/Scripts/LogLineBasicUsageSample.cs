using System;
using LogLine.Core;
using LogLine.Layouts;
using LogLine.Sinks;
using UnityEngine;
using Log = LogLine.LogLine;
using Logger = LogLine.Core.Logger;

namespace LogLine.Samples.BasicUsage
{
    /// <summary>
    /// Interactive Play Mode sample demonstrating category loggers, zero-boxing generic formatting,
    /// exception capture, custom <see cref="LogSinkBase"/> sinks, and programmatic JSON file routing.
    /// </summary>
    public sealed class LogLineBasicUsageSample : MonoBehaviour
    {
        #region Private Fields

        private static readonly Logger _sampleLog = Log.GetLogger<LogLineBasicUsageSample>();
        private static readonly Logger _combatLog = Log.GetLogger("Combat.AI");
        private static readonly Logger _networkLog = Log.GetLogger("Network.Sync");
        private static readonly Logger _audioLog = Log.GetLogger("Audio.Mixer");

        private InMemoryOverlaySink _overlaySink;
        private AsyncFileSink _combatJsonSink;
        private Vector2 _scrollPosition;
        private int _tickCounter;

        #endregion

        #region Unity Lifecycle

        private void OnEnable()
        {
            _overlaySink = new InMemoryOverlaySink(maxLines: 50)
            {
                MinimumLevel = LogLevel.Trace
            };
            Log.AddSink(_overlaySink);

            _sampleLog.Info("LogLine BasicUsage sample started (compiled min level: {0}).", LogLineDefines.CompiledMinimumLevel);
        }

        private void OnDisable()
        {
            DetachCombatJsonSink();

            if (_overlaySink != null)
            {
                Log.RemoveSink(_overlaySink);
                _overlaySink = null;
            }
        }

        #endregion

        #region Demo Actions

        private void EmitCombatBurst()
        {
            _tickCounter++;
            float damage = 42.5f + (_tickCounter % 15);
            bool isCritical = (_tickCounter & 1) == 0;

            // Zero-boxing generic formatting for up to 3 primitive arguments
            _combatLog.Trace("Tick {0}: scanning nearby targets.", _tickCounter);
            _combatLog.Info("Target #{0} took {1} damage (crit={2}).", _tickCounter, damage, isCritical);
            _audioLog.Debug("Triggered hit SFX voice #{0}.", _tickCounter % 8);
        }

        private static void EmitWarningAndError()
        {
            _networkLog.Warn("Snapshot jitter exceeded threshold: {0} ms.", 148.2f);

            try
            {
                throw new InvalidOperationException("Simulated malformed network packet header (opcode 0xFF).");
            }
            catch (Exception ex)
            {
                _networkLog.Error(ex, "Failed to decode inbound packet.");
            }
        }

        private static void EmitGlobalAndNativeLogs()
        {
            Log.Global.Info("Global logger message (no custom category required).");
            Debug.Log("Native UnityEngine.Debug.Log message (captured when Intercept Unity Logs is enabled).");
        }

        private void ToggleCombatJsonSink()
        {
            if (_combatJsonSink != null)
            {
                DetachCombatJsonSink();
                _sampleLog.Info("Detached programmatic Combat NDJSON file sink.");
                return;
            }

#if UNITY_WEBGL && !UNITY_EDITOR
            _sampleLog.Warn("AsyncFileSink is not supported on WebGL builds due to threading and filesystem restrictions.");
            return;
#else
            _combatJsonSink = new AsyncFileSink(
                filePath: "{persistentDataPath}/Logs/sample_combat_{date}.jsonl",
                layout: new JsonLayout(appendNewline: true))
            {
                MinimumLevel = LogLevel.Trace,
                CategoryFilter = CategoryFilter.Allow("Combat")
            };

            Log.AddSink(_combatJsonSink);
            _sampleLog.Info("Attached Combat-only NDJSON sink -> {0}", _combatJsonSink.ResolvedFilePath);
#endif
        }

        private void DetachCombatJsonSink()
        {
            if (_combatJsonSink == null)
            {
                return;
            }

            Log.RemoveSink(_combatJsonSink);
            _combatJsonSink = null;
        }

        #endregion

        #region OnGUI Overlay

        private void OnGUI()
        {
            const float Margin = 16f;
            float width = Mathf.Min(Screen.width - Margin * 2f, 860f);
            float height = Mathf.Min(Screen.height - Margin * 2f, 540f);

            GUILayout.BeginArea(new Rect(Margin, Margin, width, height), "LogLine — Basic Usage Sample", GUI.skin.window);

            GUILayout.Label($"Compiled Minimum Level: {LogLineDefines.CompiledMinimumLevel}   |   Open Tools -> LogLine -> Dashboard to tweak filters live.");

            GUILayout.Space(6f);
            GUILayout.BeginHorizontal();

            if (GUILayout.Button("Emit Combat & Audio Logs", GUILayout.Height(30f)))
            {
                EmitCombatBurst();
            }

            if (GUILayout.Button("Emit Warn + Exception", GUILayout.Height(30f)))
            {
                EmitWarningAndError();
            }

            if (GUILayout.Button("Global + Unity Debug.Log", GUILayout.Height(30f)))
            {
                EmitGlobalAndNativeLogs();
            }

            string jsonButtonLabel = _combatJsonSink == null
                ? "Attach Combat JSON Sink"
                : "Detach Combat JSON Sink";
            if (GUILayout.Button(jsonButtonLabel, GUILayout.Height(30f)))
            {
                ToggleCombatJsonSink();
            }

            if (GUILayout.Button("Clear", GUILayout.Width(64f), GUILayout.Height(30f)))
            {
                _overlaySink?.Clear();
            }

            GUILayout.EndHorizontal();

            if (_combatJsonSink != null)
            {
                GUILayout.Label($"Active Combat JSON File: {_combatJsonSink.ResolvedFilePath}");
            }

            GUILayout.Space(6f);
            _scrollPosition = GUILayout.BeginScrollView(_scrollPosition, GUI.skin.box);

            if (_overlaySink != null)
            {
                string[] lines = _overlaySink.GetLinesSnapshot();
                for (int i = 0; i < lines.Length; i++)
                {
                    GUILayout.Label(lines[i]);
                }
            }

            GUILayout.EndScrollView();
            GUILayout.EndArea();
        }

        #endregion
    }
}
