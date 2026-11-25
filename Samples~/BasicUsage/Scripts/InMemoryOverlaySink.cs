using System;
using System.Collections.Generic;
using LogLine.Core;
using LogLine.Layouts;

namespace LogLine.Samples.BasicUsage
{
    /// <summary>
    /// Example custom sink built on <see cref="LogSinkBase"/> that formats events using <see cref="ILogLayout"/>
    /// and retains a bounded rolling history of recent lines for an in-game debug overlay.
    /// </summary>
    public sealed class InMemoryOverlaySink : LogSinkBase
    {
        #region Constants

        private const int FormatBufferSize = 1024;

        #endregion

        #region Private Fields

        private readonly object _gate = new();
        private readonly Queue<string> _lines;
        private readonly int _maxLines;
        private readonly ILogLayout _layout;

        #endregion

        #region Properties

        /// <inheritdoc />
        public override string Name => "SampleOverlay";

        #endregion

        #region Construction

        /// <summary>
        /// Initializes a new instance of the <see cref="InMemoryOverlaySink"/> class.
        /// </summary>
        /// <param name="maxLines">Maximum number of formatted log entries retained in memory.</param>
        /// <param name="layout">Optional layout used to format captured events.</param>
        public InMemoryOverlaySink(int maxLines = 40, ILogLayout layout = null)
        {
            _maxLines = maxLines > 0 ? maxLines : 40;
            _lines = new Queue<string>(_maxLines);
            _layout = layout ?? new PatternLayout("[%d{HH:mm:ss.fff}] [%p] [%c] %m%ex", useColorTags: false);
        }

        #endregion

        #region Public API

        /// <summary>
        /// Copies the currently buffered log lines into a snapshot array for rendering.
        /// </summary>
        /// <returns>An array of formatted log lines ordered oldest to newest.</returns>
        public string[] GetLinesSnapshot()
        {
            lock (_gate)
            {
                return _lines.ToArray();
            }
        }

        /// <summary>
        /// Clears all buffered lines from the overlay.
        /// </summary>
        public void Clear()
        {
            lock (_gate)
            {
                _lines.Clear();
            }
        }

        #endregion

        #region LogSinkBase Implementation

        /// <inheritdoc />
        protected override void Write(in LogEvent logEvent)
        {
            Span<char> buffer = stackalloc char[FormatBufferSize];
            string formatted = _layout.TryFormat(in logEvent, buffer, out int written)
                ? buffer[..written].ToString()
                : $"[{logEvent.Level}] [{logEvent.LoggerName}] {logEvent.Message}";

            lock (_gate)
            {
                if (_lines.Count >= _maxLines)
                {
                    _lines.Dequeue();
                }

                _lines.Enqueue(formatted);
            }
        }

        /// <inheritdoc />
        protected override void OnDispose() => Clear();

        #endregion
    }
}
