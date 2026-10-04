namespace LogLine.Core
{
    /// <summary>
    /// Specifies the severity level of a log event.
    /// Ordered from lowest severity (<see cref="Trace"/>) to highest severity (<see cref="Fatal"/>).
    /// </summary>
    public enum LogLevel : byte
    {
        /// <summary>
        /// Highly detailed diagnostic messages for in-depth debugging.
        /// Typically disabled in production builds.
        /// </summary>
        Trace = 0,

        /// <summary>
        /// Informational messages intended for developers during debugging and local testing.
        /// </summary>
        Debug = 1,

        /// <summary>
        /// General application operational events (e.g. state changes, milestones).
        /// </summary>
        Info = 2,

        /// <summary>
        /// Non-critical anomalies or unexpected situations that do not halt the application.
        /// </summary>
        Warn = 3,

        /// <summary>
        /// Recoverable errors, handled exceptions, or operation failures.
        /// </summary>
        Error = 4,

        /// <summary>
        /// Critical failures that cause system abort, application crash, or unrecoverable state.
        /// </summary>
        Fatal = 5,

        /// <summary>
        /// Special level used solely for filtering to completely disable logging.
        /// </summary>
        None = 6
    }
}
