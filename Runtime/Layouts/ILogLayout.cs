using System;
using LogLine.Core;

namespace LogLine.Layouts
{
    /// <summary>
    /// Contract for serializing and formatting <see cref="LogEvent"/> instances into character buffers.
    /// </summary>
    public interface ILogLayout
    {
        #region Formatting Core

        /// <summary>
        /// Attempts to format a log event into the destination character span.
        /// </summary>
        /// <param name="logEvent">The source log event.</param>
        /// <param name="destination">Target character span buffer.</param>
        /// <param name="charsWritten">Number of characters successfully written to the destination.</param>
        /// <returns>
        /// <see langword="true"/> if the format operation succeeded;
        /// <see langword="false"/> if the destination span was too small.
        /// </returns>
        bool TryFormat(in LogEvent logEvent, Span<char> destination, out int charsWritten);

        #endregion
    }
}
