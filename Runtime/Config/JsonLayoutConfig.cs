using System;
using LogLine.Layouts;
using UnityEngine;

namespace LogLine.Config
{
    /// <summary>
    /// Serializable Inspector configuration for building and hot-updating a <see cref="JsonLayout"/>.
    /// </summary>
    [Serializable]
    public struct JsonLayoutConfig
    {
        #region Fields

        [Tooltip("Include the UTC ISO-8601 'timestamp' field.")]
        public bool includeTimestamp;

        [Tooltip("Include the severity 'level' field.")]
        public bool includeLevel;

        [Tooltip("Include the category 'logger' field.")]
        public bool includeLogger;

        [Tooltip("Include the originating managed 'threadId' field.")]
        public bool includeThreadId;

        [Tooltip("Include the 'exception' field when an exception is attached.")]
        public bool includeException;

        #endregion

        #region Defaults & Factory

        /// <summary>
        /// Gets the default JSON layout configuration with all optional fields enabled.
        /// </summary>
        public static JsonLayoutConfig Default => new()
        {
            includeTimestamp = true,
            includeLevel = true,
            includeLogger = true,
            includeThreadId = true,
            includeException = true
        };

        /// <summary>
        /// Creates a new <see cref="JsonLayout"/> configured with these field toggles.
        /// </summary>
        /// <param name="appendNewline">Whether to append a trailing newline delimiter for NDJSON output.</param>
        /// <returns>A configured <see cref="JsonLayout"/> instance.</returns>
        public JsonLayout Build(bool appendNewline = true) => new(
            includeTimestamp,
            includeLevel,
            includeLogger,
            includeThreadId,
            includeException,
            appendNewline);

        /// <summary>
        /// Applies these field toggles in-place to an existing <see cref="JsonLayout"/> without heap allocations.
        /// </summary>
        /// <param name="layout">The target layout instance to update.</param>
        public void ApplyTo(JsonLayout layout)
        {
            if (layout == null)
            {
                return;
            }

            layout.IncludeTimestamp = includeTimestamp;
            layout.IncludeLevel = includeLevel;
            layout.IncludeLogger = includeLogger;
            layout.IncludeThreadId = includeThreadId;
            layout.IncludeException = includeException;
        }

        #endregion
    }
}
