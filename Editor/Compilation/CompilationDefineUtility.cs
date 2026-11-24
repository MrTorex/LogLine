using System;
using System.Collections.Generic;
using LogLine.Core;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace LogLine.Editor.Compilation
{
    /// <summary>
    /// Manages <c>LOGLINE_MIN_*</c> scripting define symbols across Unity <see cref="NamedBuildTarget"/> targets.
    /// Automatically provisions <see cref="LogLineDefines.MinTrace"/> on fresh projects with no threshold symbol defined.
    /// </summary>
    [InitializeOnLoad]
    public static class CompilationDefineUtility
    {
        #region Constants

        private static readonly string[] _allThresholdSymbols =
        {
            LogLineDefines.MinTrace,
            LogLineDefines.MinDebug,
            LogLineDefines.MinInfo,
            LogLineDefines.MinWarn,
            LogLineDefines.MinError,
            LogLineDefines.MinFatal,
            LogLineDefines.MinNone
        };

        /// <summary>
        /// Common build targets exposed in Editor compilation UI.
        /// </summary>
        public static readonly NamedBuildTarget[] SupportedTargets =
        {
            NamedBuildTarget.Standalone,
            NamedBuildTarget.Server,
            NamedBuildTarget.Android,
            NamedBuildTarget.iOS,
            NamedBuildTarget.WebGL
        };

        #endregion

        #region Static Construction

        static CompilationDefineUtility()
        {
            EditorApplication.delayCall += EnsureActiveTargetHasDefine;
        }

        #endregion

        #region Public API

        /// <summary>
        /// Resolves the currently active <see cref="NamedBuildTarget"/>, including Dedicated Server subtarget detection.
        /// </summary>
        /// <returns>The active <see cref="NamedBuildTarget"/>.</returns>
        public static NamedBuildTarget GetActiveNamedBuildTarget()
        {
            BuildTargetGroup activeGroup = EditorUserBuildSettings.selectedBuildTargetGroup;
            if (activeGroup == BuildTargetGroup.Unknown)
            {
                activeGroup = BuildPipeline.GetBuildTargetGroup(EditorUserBuildSettings.activeBuildTarget);
            }

            if (activeGroup == BuildTargetGroup.Standalone &&
                EditorUserBuildSettings.standaloneBuildSubtarget == StandaloneBuildSubtarget.Server)
            {
                return NamedBuildTarget.Server;
            }

            return NamedBuildTarget.FromBuildTargetGroup(activeGroup);
        }

        /// <summary>
        /// Maps a <see cref="LogLevel"/> threshold to its corresponding <c>LOGLINE_MIN_*</c> define symbol.
        /// </summary>
        /// <param name="level">The compilation threshold level.</param>
        /// <returns>The scripting define symbol name.</returns>
        public static string GetSymbolForLevel(LogLevel level) => level switch
        {
            LogLevel.Trace => LogLineDefines.MinTrace,
            LogLevel.Debug => LogLineDefines.MinDebug,
            LogLevel.Info  => LogLineDefines.MinInfo,
            LogLevel.Warn  => LogLineDefines.MinWarn,
            LogLevel.Error => LogLineDefines.MinError,
            LogLevel.Fatal => LogLineDefines.MinFatal,
            LogLevel.None  => LogLineDefines.MinNone,
            _ => LogLineDefines.MinTrace
        };

        /// <summary>
        /// Reads the effective compilation threshold configured for the specified target.
        /// If multiple <c>LOGLINE_MIN_*</c> symbols exist, returns the most permissive (lowest) level to match
        /// <see cref="System.Diagnostics.ConditionalAttribute"/> OR semantics.
        /// </summary>
        /// <param name="target">The target platform to inspect.</param>
        /// <param name="threshold">The resolved threshold if at least one symbol is present; otherwise <see cref="LogLevel.None"/>.</param>
        /// <returns><see langword="true"/> if a <c>LOGLINE_MIN_*</c> define is present; otherwise <see langword="false"/>.</returns>
        public static bool TryGetThreshold(NamedBuildTarget target, out LogLevel threshold)
        {
            threshold = LogLevel.None;
            if (target == NamedBuildTarget.Unknown)
            {
                return false;
            }

            PlayerSettings.GetScriptingDefineSymbols(target, out string[] defines);
            if (defines == null || defines.Length == 0)
            {
                return false;
            }

            int lowestIndex = int.MaxValue;
            for (int i = 0; i < defines.Length; i++)
            {
                int symbolIndex = Array.IndexOf(_allThresholdSymbols, defines[i]);
                if (symbolIndex >= 0 && symbolIndex < lowestIndex)
                {
                    lowestIndex = symbolIndex;
                }
            }

            if (lowestIndex == int.MaxValue)
            {
                return false;
            }

            threshold = (LogLevel)lowestIndex;
            return true;
        }

        /// <summary>
        /// Applies the specified compilation threshold to a single <see cref="NamedBuildTarget"/>,
        /// replacing any previous <c>LOGLINE_MIN_*</c> symbols. Skips writing if already up to date.
        /// </summary>
        /// <param name="target">The target platform to update.</param>
        /// <param name="threshold">The desired compilation threshold.</param>
        /// <returns><see langword="true"/> if scripting defines were modified; otherwise <see langword="false"/>.</returns>
        public static bool SetThreshold(NamedBuildTarget target, LogLevel threshold)
        {
            if (target == NamedBuildTarget.Unknown)
            {
                return false;
            }

            string targetSymbol = GetSymbolForLevel(threshold);
            PlayerSettings.GetScriptingDefineSymbols(target, out string[] existing);

            var updated = new List<string>(existing != null ? existing.Length + 1 : 1);
            bool alreadyExactMatch = false;
            int foundThresholdCount = 0;

            if (existing != null)
            {
                for (int i = 0; i < existing.Length; i++)
                {
                    string sym = existing[i];
                    if (IsThresholdSymbol(sym))
                    {
                        foundThresholdCount++;
                        if (string.Equals(sym, targetSymbol, StringComparison.Ordinal))
                        {
                            alreadyExactMatch = true;
                        }

                        continue;
                    }

                    updated.Add(sym);
                }
            }

            if (alreadyExactMatch && foundThresholdCount == 1)
            {
                return false;
            }

            updated.Add(targetSymbol);
            PlayerSettings.SetScriptingDefineSymbols(target, updated.ToArray());
            return true;
        }

        /// <summary>
        /// Applies the specified compilation threshold to a collection of <see cref="NamedBuildTarget"/> platforms.
        /// </summary>
        /// <param name="targets">The target platforms to update.</param>
        /// <param name="threshold">The desired compilation threshold.</param>
        /// <returns>The number of targets whose scripting defines were actually modified.</returns>
        public static int SetThreshold(IEnumerable<NamedBuildTarget> targets, LogLevel threshold)
        {
            if (targets == null)
            {
                return 0;
            }

            int modifiedCount = 0;
            foreach (NamedBuildTarget target in targets)
            {
                if (SetThreshold(target, threshold))
                {
                    modifiedCount++;
                }
            }

            return modifiedCount;
        }

        #endregion

        #region Auto-Bootstrap & Helpers

        private static void EnsureActiveTargetHasDefine()
        {
            if (EditorApplication.isCompiling || EditorApplication.isUpdating || Application.isBatchMode)
            {
                return;
            }

            NamedBuildTarget activeTarget = GetActiveNamedBuildTarget();
            if (activeTarget == NamedBuildTarget.Unknown)
            {
                return;
            }

            if (!TryGetThreshold(activeTarget, out _))
            {
                SetThreshold(activeTarget, LogLevel.Trace);
            }
        }

        private static bool IsThresholdSymbol(string symbol)
        {
            for (int i = 0; i < _allThresholdSymbols.Length; i++)
            {
                if (string.Equals(symbol, _allThresholdSymbols[i], StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }

        #endregion
    }

    /// <summary>
    /// Validates before player builds that the target platform has an explicit <c>LOGLINE_MIN_*</c> symbol defined.
    /// </summary>
    internal sealed class CompilationBuildPreprocessor : IPreprocessBuildWithReport
    {
        /// <inheritdoc />
        public int callbackOrder => 0;

        /// <inheritdoc />
        public void OnPreprocessBuild(BuildReport report)
        {
            BuildTargetGroup group = report.summary.platformGroup;
            NamedBuildTarget target = group == BuildTargetGroup.Standalone &&
                                      EditorUserBuildSettings.standaloneBuildSubtarget == StandaloneBuildSubtarget.Server
                ? NamedBuildTarget.Server
                : NamedBuildTarget.FromBuildTargetGroup(group);

            if (!CompilationDefineUtility.TryGetThreshold(target, out _))
            {
                UnityEngine.Debug.LogWarning(
                    $"[LogLine] Build target '{target.TargetName}' has no LOGLINE_MIN_* scripting define symbol configured. " +
                    "All LogLine call sites will be stripped in this build. Configure a threshold in LogLineSettings.");
            }
        }
    }
}
