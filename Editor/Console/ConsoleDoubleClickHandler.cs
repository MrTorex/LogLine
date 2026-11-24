using System;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;
using UnityEditor;
using UnityEditor.Compilation;
using UnityEditorInternal;
using UnityEngine;
using UnityEngine.UIElements;

namespace LogLine.Editor.Console
{
    /// <summary>
    /// Intercepts Unity Console double-click events at the <c>panel.visualTree</c> TrickleDown phase
    /// before the sibling <c>IMGUIContainer</c> dispatches them to <c>ConsoleWindow.OnGUI</c>,
    /// redirecting navigation from internal LogLine runtime frames to the user's call site.
    /// </summary>
    [InitializeOnLoad]
    internal static class ConsoleDoubleClickHandler
    {
        #region Constants

        private const string PackageRuntimePrefix = "Packages/com.mrtorex.logline/Runtime/";
        private const string PackageIdSegment = "/com.mrtorex.logline";
        private const string RuntimeSegment = "/Runtime/";
        private const string RuntimeAssemblyName = "LogLine.Runtime.dll";
        private const string FrameMarker = " (at ";
        private const float ConsoleToolbarHeight = 20f;

        #endregion

        #region Private Fields

        private static readonly Type _consoleWindowType;
        private static readonly FieldInfo _activeTextField;
        private static readonly FieldInfo _splitterField;
        private static readonly FieldInfo _splitterRealSizesField;
        private static readonly ConditionalWeakTable<VisualElement, object> _hookedPanelRoots = new();

        #endregion

        #region Static Construction

        static ConsoleDoubleClickHandler()
        {
            _consoleWindowType = typeof(EditorWindow).Assembly.GetType("UnityEditor.ConsoleWindow");
            if (_consoleWindowType == null)
            {
                return;
            }

            _activeTextField = _consoleWindowType.GetField(
                "m_ActiveText",
                BindingFlags.Instance | BindingFlags.NonPublic);

            _splitterField = _consoleWindowType.GetField(
                "spl",
                BindingFlags.Instance | BindingFlags.NonPublic);

            if (_splitterField != null)
            {
                _splitterRealSizesField = _splitterField.FieldType.GetField(
                    "realSizes",
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            }

            EditorApplication.update -= TryHookFocusedConsolePanel;
            EditorApplication.update += TryHookFocusedConsolePanel;
        }

        #endregion

        #region Panel Hooking

        private static void TryHookFocusedConsolePanel()
        {
            EditorWindow focused = EditorWindow.focusedWindow;
            if (focused == null || focused.GetType() != _consoleWindowType)
            {
                return;
            }

            VisualElement panelRoot = focused.rootVisualElement?.panel?.visualTree;
            if (panelRoot == null || _hookedPanelRoots.TryGetValue(panelRoot, out _))
            {
                return;
            }

            _hookedPanelRoots.Add(panelRoot, null);
            panelRoot.RegisterCallback<MouseDownEvent>(OnPanelMouseDown, TrickleDown.TrickleDown);
        }

        #endregion

        #region Event Interception

        private static void OnPanelMouseDown(MouseDownEvent evt)
        {
            if (evt.button != 0 || evt.clickCount != 2)
            {
                return;
            }

            EditorWindow focused = EditorWindow.focusedWindow;
            if (focused == null || focused.GetType() != _consoleWindowType || _activeTextField == null)
            {
                return;
            }

            VisualElement consoleRoot = focused.rootVisualElement;
            if (consoleRoot == null)
            {
                return;
            }

            Vector2 localPos = consoleRoot.WorldToLocal(evt.mousePosition);
            if (!IsClickInLogListArea(focused, localPos))
            {
                return;
            }

            string activeText = _activeTextField.GetValue(focused) as string;
            if (string.IsNullOrEmpty(activeText))
            {
                return;
            }

            if (!TryFindExternalCaller(activeText, out bool hasLogLineFrame, out string targetPath, out int targetLine))
            {
                return;
            }

            if (!hasLogLineFrame)
            {
                return;
            }

            evt.StopImmediatePropagation();
            evt.PreventDefault();
            OpenTargetLocation(targetPath, targetLine);
        }

        private static bool IsClickInLogListArea(EditorWindow consoleWindow, Vector2 localPosition)
        {
            if (localPosition.x < 0f || localPosition.x > consoleWindow.position.width)
            {
                return false;
            }

            if (localPosition.y < ConsoleToolbarHeight)
            {
                return false;
            }

            if (_splitterField != null && _splitterRealSizesField != null)
            {
                object splitter = _splitterField.GetValue(consoleWindow);
                if (splitter != null &&
                    _splitterRealSizesField.GetValue(splitter) is int[] realSizes &&
                    realSizes.Length > 0 &&
                    realSizes[0] > 0)
                {
                    return localPosition.y <= ConsoleToolbarHeight + realSizes[0];
                }
            }

            return localPosition.y <= consoleWindow.position.height;
        }

        #endregion

        #region Stack Trace Parsing

        private static bool TryFindExternalCaller(
            string stackTrace,
            out bool hasLogLineFrame,
            out string targetPath,
            out int targetLine)
        {
            hasLogLineFrame = false;
            targetPath = null;
            targetLine = 0;

            ReadOnlySpan<char> remaining = stackTrace.AsSpan();

            while (!remaining.IsEmpty)
            {
                int newlineIndex = remaining.IndexOf('\n');
                ReadOnlySpan<char> lineSpan;

                if (newlineIndex >= 0)
                {
                    lineSpan = remaining[..newlineIndex].TrimEnd('\r');
                    remaining = remaining[(newlineIndex + 1)..];
                }
                else
                {
                    lineSpan = remaining.TrimEnd('\r');
                    remaining = ReadOnlySpan<char>.Empty;
                }

                if (!TryExtractFrameLocation(lineSpan, out string normalizedPath, out int parsedLine))
                {
                    continue;
                }

                if (IsLogLineRuntimePath(normalizedPath))
                {
                    hasLogLineFrame = true;
                    continue;
                }

                if (!hasLogLineFrame)
                {
                    return false;
                }

                if (!IsValidTargetFile(normalizedPath))
                {
                    continue;
                }

                targetPath = normalizedPath;
                targetLine = parsedLine;
                return true;
            }

            return false;
        }

        private static bool TryExtractFrameLocation(
            ReadOnlySpan<char> lineSpan,
            out string normalizedPath,
            out int lineNumber)
        {
            normalizedPath = null;
            lineNumber = 0;

            int markerIndex = lineSpan.LastIndexOf(FrameMarker.AsSpan());
            if (markerIndex < 0)
            {
                return false;
            }

            ReadOnlySpan<char> locationSpan = lineSpan[(markerIndex + FrameMarker.Length)..];
            int closeParenIndex = locationSpan.LastIndexOf(')');
            if (closeParenIndex <= 0)
            {
                return false;
            }

            locationSpan = locationSpan[..closeParenIndex];

            int colonIndex = locationSpan.LastIndexOf(':');
            if (colonIndex <= 0 || colonIndex >= locationSpan.Length - 1)
            {
                return false;
            }

            ReadOnlySpan<char> lineDigits = locationSpan[(colonIndex + 1)..];
            if (!int.TryParse(lineDigits, NumberStyles.Integer, CultureInfo.InvariantCulture, out lineNumber) ||
                lineNumber <= 0)
            {
                return false;
            }

            ReadOnlySpan<char> rawPath = locationSpan[..colonIndex];
            if (rawPath.StartsWith("./".AsSpan()) || rawPath.StartsWith(".\\".AsSpan()))
            {
                rawPath = rawPath[2..];
            }

            if (rawPath.IsEmpty)
            {
                return false;
            }

            normalizedPath = rawPath.ToString().Replace('\\', '/');
            return true;
        }

        #endregion

        #region Path Classification & Navigation

        private static bool IsLogLineRuntimePath(string normalizedPath)
        {
            if (string.IsNullOrEmpty(normalizedPath))
            {
                return false;
            }

            if (normalizedPath.StartsWith(PackageRuntimePrefix, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            if (normalizedPath.IndexOf(PackageIdSegment, StringComparison.OrdinalIgnoreCase) >= 0 &&
                normalizedPath.IndexOf(RuntimeSegment, StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return true;
            }

            string assemblyName = CompilationPipeline.GetAssemblyNameFromScriptPath(normalizedPath);
            return string.Equals(assemblyName, RuntimeAssemblyName, StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsValidTargetFile(string normalizedPath)
        {
            if (AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(normalizedPath) != null)
            {
                return true;
            }

            return File.Exists(normalizedPath);
        }

        private static void OpenTargetLocation(string targetPath, int targetLine)
        {
            UnityEngine.Object targetAsset = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(targetPath);
            if (targetAsset != null)
            {
                AssetDatabase.OpenAsset(targetAsset, targetLine);
                return;
            }

            InternalEditorUtility.OpenFileAtLineExternal(targetPath, targetLine);
        }

        #endregion
    }
}
