using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;

namespace LogLine.Sinks
{
    /// <summary>
    /// Resolves dynamic file path templates containing Unity path macros, runtime metadata tokens,
    /// and date/time formatting patterns in a single non-recursive pass.
    /// </summary>
    public static class FilePathResolver
    {
        #region Constants

        /// <summary>
        /// Default log file path template equivalent to <c>{persistentDataPath}/Logs/game.log</c>.
        /// </summary>
        public const string DefaultFilePathTemplate = "{persistentDataPath}/Logs/game.log";

        private const string DefaultDateFormat = "yyyy-MM-dd";
        private const string DefaultTimeFormat = "HH-mm-ss";
        private const string DefaultDateTimeFormat = "yyyy-MM-dd_HH-mm-ss";

        #endregion

        #region Nested Types

        /// <summary>
        /// Immutable snapshot of Unity environment values captured on the main thread
        /// so path resolution remains safe when invoked from background threads.
        /// </summary>
        internal sealed class PathEnvironment
        {
            public string PersistentDataPath { get; }
            public string DataPath { get; }
            public string TemporaryCachePath { get; }
            public string StreamingAssetsPath { get; }
            public string ProjectPath { get; }
            public string ConsoleLogDirectory { get; }
            public string ProductName { get; }
            public string CompanyName { get; }
            public string Version { get; }
            public string Platform { get; }
            public string ProcessId { get; }

            public PathEnvironment(
                string persistentDataPath,
                string dataPath,
                string temporaryCachePath,
                string streamingAssetsPath,
                string projectPath,
                string consoleLogDirectory,
                string productName,
                string companyName,
                string version,
                string platform,
                string processId)
            {
                PersistentDataPath = persistentDataPath ?? string.Empty;
                DataPath = dataPath ?? string.Empty;
                TemporaryCachePath = temporaryCachePath ?? string.Empty;
                StreamingAssetsPath = streamingAssetsPath ?? string.Empty;
                ProjectPath = projectPath ?? string.Empty;
                ConsoleLogDirectory = consoleLogDirectory ?? string.Empty;
                ProductName = productName ?? string.Empty;
                CompanyName = companyName ?? string.Empty;
                Version = version ?? string.Empty;
                Platform = platform ?? string.Empty;
                ProcessId = processId ?? "0";
            }
        }

        #endregion

        #region Private Fields

        private static volatile PathEnvironment _cachedEnvironment;

        #endregion

        #region Environment Capture

        /// <summary>
        /// Captures Unity <see cref="Application"/> path and metadata values on the main thread.
        /// Must be invoked on the Unity main thread (called automatically during LogLine startup).
        /// </summary>
        public static void CaptureUnityEnvironment()
        {
            string persistentDataPath = Application.persistentDataPath ?? string.Empty;
            string dataPath = Application.dataPath ?? string.Empty;
            string temporaryCachePath = Application.temporaryCachePath ?? string.Empty;
            string streamingAssetsPath = Application.streamingAssetsPath ?? string.Empty;

            string projectPath = string.Empty;
            if (!string.IsNullOrEmpty(dataPath))
            {
                projectPath = Path.GetDirectoryName(dataPath) ?? dataPath;
            }

            string consoleLogDirectory = persistentDataPath;
            string consoleLogPath = Application.consoleLogPath;
            if (!string.IsNullOrEmpty(consoleLogPath))
            {
                string dir = Path.GetDirectoryName(consoleLogPath);
                if (!string.IsNullOrEmpty(dir))
                {
                    consoleLogDirectory = dir;
                }
            }

            string processId = GetSafeProcessId();

            _cachedEnvironment = new PathEnvironment(
                persistentDataPath,
                dataPath,
                temporaryCachePath,
                streamingAssetsPath,
                projectPath,
                consoleLogDirectory,
                Application.productName,
                Application.companyName,
                Application.version,
                Application.platform.ToString(),
                processId);
        }

        private static PathEnvironment GetOrCaptureEnvironment()
        {
            PathEnvironment env = _cachedEnvironment;
            if (env != null)
            {
                return env;
            }

            CaptureUnityEnvironment();
            return _cachedEnvironment;
        }

        private static string GetSafeProcessId()
        {
            try
            {
                using Process current = Process.GetCurrentProcess();
                return current.Id.ToString(CultureInfo.InvariantCulture);
            }
            catch
            {
                return "0";
            }
        }

        #endregion

        #region Public API

        /// <summary>
        /// Resolves all supported path macros and date/time tokens in the provided template.
        /// Relative paths (including legacy file names such as <c>"game.log"</c>) are rooted under
        /// <c>{persistentDataPath}/Logs/</c>.
        /// </summary>
        /// <remarks>
        /// Supported path macros: <c>{persistentDataPath}</c>, <c>{dataPath}</c>, <c>{temporaryCachePath}</c>,
        /// <c>{tempPath}</c>, <c>{streamingAssetsPath}</c>, <c>{projectPath}</c>, <c>{consoleLogPath}</c>.
        /// Note that <c>{dataPath}</c> and <c>{streamingAssetsPath}</c> may be read-only on mobile/console builds.
        /// Supported metadata macros: <c>{productName}</c>, <c>{companyName}</c>, <c>{version}</c>,
        /// <c>{platform}</c>, <c>{pid}</c>.
        /// Supported date/time tokens: <c>{date}</c>, <c>{time}</c>, <c>{datetime}</c>,
        /// <c>{utcDate}</c>, <c>{utcTime}</c>, <c>{utcDatetime}</c>, <c>{date:format}</c>,
        /// <c>{utcDate:format}</c>, or bare date formats such as <c>{yyyy-MM-dd}</c>.
        /// Escape literal braces with <c>{{</c> and <c>}}</c>.
        /// </remarks>
        /// <param name="template">The raw file path template. Falls back to <see cref="DefaultFilePathTemplate"/> if null or whitespace.</param>
        /// <param name="timestampUtc">Optional UTC timestamp override for deterministic resolution; defaults to <see cref="DateTime.UtcNow"/>.</param>
        /// <returns>The normalized, fully resolved file path.</returns>
        public static string Resolve(string template, DateTime? timestampUtc = null)
        {
            PathEnvironment env = GetOrCaptureEnvironment();
            DateTime utcNow = timestampUtc.HasValue
                ? DateTime.SpecifyKind(timestampUtc.Value, DateTimeKind.Utc)
                : DateTime.UtcNow;

            return ResolveInternal(template, env, utcNow);
        }

        #endregion

        #region Internal Resolution Engine

        /// <summary>
        /// Deterministic internal resolution entry point for unit testing without Unity runtime dependencies.
        /// </summary>
        internal static string ResolveInternal(string template, PathEnvironment env, DateTime utcNow)
        {
            string effectiveTemplate = string.IsNullOrWhiteSpace(template)
                ? DefaultFilePathTemplate
                : template.Trim();

            DateTime localNow = utcNow.ToLocalTime();
            string expanded = ExpandTemplate(effectiveTemplate, env, localNow, utcNow);

            if (!Path.IsPathRooted(expanded) && !string.IsNullOrEmpty(env.PersistentDataPath))
            {
                expanded = Path.Combine(env.PersistentDataPath, "Logs", expanded);
            }

            try
            {
                return Path.GetFullPath(expanded);
            }
            catch
            {
                return expanded;
            }
        }

        private static string ExpandTemplate(
            string template,
            PathEnvironment env,
            DateTime localNow,
            DateTime utcNow)
        {
            ReadOnlySpan<char> span = template.AsSpan();
            var sb = new StringBuilder(template.Length + 64);

            int i = 0;
            while (i < span.Length)
            {
                char c = span[i];

                if (c == '{')
                {
                    if (i + 1 < span.Length && span[i + 1] == '{')
                    {
                        sb.Append('{');
                        i += 2;
                        continue;
                    }

                    int nextSpecial = span[(i + 1)..].IndexOfAny('{', '}');
                    if (nextSpecial < 0 || span[i + 1 + nextSpecial] != '}')
                    {
                        sb.Append('{');
                        i++;
                        continue;
                    }

                    ReadOnlySpan<char> token = span.Slice(i + 1, nextSpecial);
                    if (token.Length > 0 && TryAppendToken(sb, token, env, localNow, utcNow))
                    {
                        i += nextSpecial + 2;
                        continue;
                    }

                    // Preserve unrecognized tokens verbatim without corrupting the path
                    sb.Append(template, i, nextSpecial + 2);
                    i += nextSpecial + 2;
                    continue;
                }

                if (c == '}' && i + 1 < span.Length && span[i + 1] == '}')
                {
                    sb.Append('}');
                    i += 2;
                    continue;
                }

                sb.Append(c);
                i++;
            }

            return sb.ToString();
        }

        private static bool TryAppendToken(
            StringBuilder sb,
            ReadOnlySpan<char> token,
            PathEnvironment env,
            DateTime localNow,
            DateTime utcNow)
        {
            // 1. Directory path macros (keep OS path separators intact)
            if (token.Equals("persistentDataPath".AsSpan(), StringComparison.OrdinalIgnoreCase))
            {
                sb.Append(env.PersistentDataPath);
                return true;
            }

            if (token.Equals("dataPath".AsSpan(), StringComparison.OrdinalIgnoreCase))
            {
                sb.Append(env.DataPath);
                return true;
            }

            if (token.Equals("temporaryCachePath".AsSpan(), StringComparison.OrdinalIgnoreCase) ||
                token.Equals("tempPath".AsSpan(), StringComparison.OrdinalIgnoreCase))
            {
                sb.Append(env.TemporaryCachePath);
                return true;
            }

            if (token.Equals("streamingAssetsPath".AsSpan(), StringComparison.OrdinalIgnoreCase))
            {
                sb.Append(env.StreamingAssetsPath);
                return true;
            }

            if (token.Equals("projectPath".AsSpan(), StringComparison.OrdinalIgnoreCase))
            {
                sb.Append(env.ProjectPath);
                return true;
            }

            if (token.Equals("consoleLogPath".AsSpan(), StringComparison.OrdinalIgnoreCase))
            {
                sb.Append(env.ConsoleLogDirectory);
                return true;
            }

            // 2. Application & runtime metadata macros (sanitized for cross-platform filename safety)
            if (token.Equals("productName".AsSpan(), StringComparison.OrdinalIgnoreCase))
            {
                AppendSanitizedSegment(sb, env.ProductName.AsSpan());
                return true;
            }

            if (token.Equals("companyName".AsSpan(), StringComparison.OrdinalIgnoreCase))
            {
                AppendSanitizedSegment(sb, env.CompanyName.AsSpan());
                return true;
            }

            if (token.Equals("version".AsSpan(), StringComparison.OrdinalIgnoreCase))
            {
                AppendSanitizedSegment(sb, env.Version.AsSpan());
                return true;
            }

            if (token.Equals("platform".AsSpan(), StringComparison.OrdinalIgnoreCase))
            {
                AppendSanitizedSegment(sb, env.Platform.AsSpan());
                return true;
            }

            if (token.Equals("pid".AsSpan(), StringComparison.OrdinalIgnoreCase))
            {
                sb.Append(env.ProcessId);
                return true;
            }

            // 3. Built-in date/time aliases
            if (token.Equals("date".AsSpan(), StringComparison.OrdinalIgnoreCase))
            {
                return TryAppendFormattedDate(sb, localNow, DefaultDateFormat.AsSpan());
            }

            if (token.Equals("time".AsSpan(), StringComparison.OrdinalIgnoreCase))
            {
                return TryAppendFormattedDate(sb, localNow, DefaultTimeFormat.AsSpan());
            }

            if (token.Equals("datetime".AsSpan(), StringComparison.OrdinalIgnoreCase))
            {
                return TryAppendFormattedDate(sb, localNow, DefaultDateTimeFormat.AsSpan());
            }

            if (token.Equals("utcDate".AsSpan(), StringComparison.OrdinalIgnoreCase))
            {
                return TryAppendFormattedDate(sb, utcNow, DefaultDateFormat.AsSpan());
            }

            if (token.Equals("utcTime".AsSpan(), StringComparison.OrdinalIgnoreCase))
            {
                return TryAppendFormattedDate(sb, utcNow, DefaultTimeFormat.AsSpan());
            }

            if (token.Equals("utcDatetime".AsSpan(), StringComparison.OrdinalIgnoreCase))
            {
                return TryAppendFormattedDate(sb, utcNow, DefaultDateTimeFormat.AsSpan());
            }

            // 4. Parameterized date tokens: {date:yyyy-MM-dd_HH-mm-ss} or {utcDate:...}
            int colonIndex = token.IndexOf(':');
            if (colonIndex > 0 && colonIndex < token.Length - 1)
            {
                ReadOnlySpan<char> prefix = token[..colonIndex];
                ReadOnlySpan<char> format = token[(colonIndex + 1)..];

                if (prefix.Equals("date".AsSpan(), StringComparison.OrdinalIgnoreCase))
                {
                    return TryAppendFormattedDate(sb, localNow, format);
                }

                if (prefix.Equals("utcDate".AsSpan(), StringComparison.OrdinalIgnoreCase))
                {
                    return TryAppendFormattedDate(sb, utcNow, format);
                }
            }

            // 5. Bare date format tokens: {yyyy-MM-dd}, {HH-mm-ss}, {yyyyMMdd_HHmmss}
            if (IsBareDateFormat(token))
            {
                return TryAppendFormattedDate(sb, localNow, token);
            }

            return false;
        }

        private static bool TryAppendFormattedDate(StringBuilder sb, DateTime timestamp, ReadOnlySpan<char> format)
        {
            Span<char> buffer = stackalloc char[128];
            try
            {
                if (timestamp.TryFormat(buffer, out int charsWritten, format, CultureInfo.InvariantCulture))
                {
                    AppendSanitizedSegment(sb, buffer[..charsWritten]);
                    return true;
                }
            }
            catch (FormatException)
            {
                return false;
            }

            return false;
        }

        private static bool IsBareDateFormat(ReadOnlySpan<char> token)
        {
            if (token.Length < 2)
            {
                return false;
            }

            bool hasDateSpecifier = false;
            for (int i = 0; i < token.Length; i++)
            {
                char c = token[i];
                switch (c)
                {
                    case 'y':
                    case 'M':
                    case 'd':
                    case 'H':
                    case 'h':
                    case 'm':
                    case 's':
                    case 'f':
                    case 'F':
                        hasDateSpecifier = true;
                        break;
                    case '-':
                    case '_':
                    case '.':
                    case ':':
                    case ' ':
                        break;
                    default:
                        return false;
                }
            }

            return hasDateSpecifier;
        }

        private static void AppendSanitizedSegment(StringBuilder sb, ReadOnlySpan<char> value)
        {
            for (int i = 0; i < value.Length; i++)
            {
                char c = value[i];
                if (c == ':')
                {
                    sb.Append('-');
                }
                else if (c < 32 ||
                         c == '<' ||
                         c == '>' ||
                         c == '"' ||
                         c == '/' ||
                         c == '\\' ||
                         c == '|' ||
                         c == '?' ||
                         c == '*')
                {
                    sb.Append('_');
                }
                else
                {
                    sb.Append(c);
                }
            }
        }

        #endregion
    }
}
