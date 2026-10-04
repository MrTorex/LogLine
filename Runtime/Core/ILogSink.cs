using System;
using UnityEngine;

namespace LogLine.Core
{
    /// <summary>
    /// Represents an output destination target for log events (e.g. Unity Console, File, Network).
    /// </summary>
    public interface ILogSink : IDisposable
    {
        #region Properties

        /// <summary>
        /// Gets the human-readable identifier of this sink instance.
        /// </summary>
        string Name { get; }

        /// <summary>
        /// Gets or sets a value indicating whether this sink is currently receiving log events.
        /// </summary>
        bool IsEnabled { get; set; }

        /// <summary>
        /// Gets or sets the minimum severity level required for this sink to emit logs.
        /// </summary>
        LogLevel MinimumLevel { get; set; }

        #endregion

        #region Emission

        /// <summary>
        /// Emits a log event payload to the sink's underlying destination.
        /// Implementation must be thread-safe.
        /// </summary>
        /// <param name="logEvent">The read-only log event reference.</param>
        [HideInCallstack]
        void Emit(in LogEvent logEvent);

        #endregion
    }
}
