using System;

namespace LogLine.Core
{
    /// <summary>
    /// Represents an immutable logging event payload captured at the point of origin.
    /// </summary>
    public readonly struct LogEvent
    {
        #region Properties

        /// <summary>
        /// Gets the severity level of this event.
        /// </summary>
        public LogLevel Level { get; }

        /// <summary>
        /// Gets the category or logger name that generated this event.
        /// </summary>
        public string LoggerName { get; }

        /// <summary>
        /// Gets the formatted text message.
        /// </summary>
        public string Message { get; }

        /// <summary>
        /// Gets the associated exception, if any; otherwise <see langword="null"/>.
        /// </summary>
        public Exception Exception { get; }

        /// <summary>
        /// Gets the UTC timestamp when the event was instantiated.
        /// </summary>
        public DateTime TimestampUtc { get; }

        /// <summary>
        /// Gets the managed thread ID where the event originated.
        /// </summary>
        public int ThreadId { get; }

        /// <summary>
        /// Gets the optional Unity context object for console pinging in the Editor.
        /// </summary>
        public UnityEngine.Object Context { get; }

        /// <summary>
        /// Gets a value indicating whether an exception is attached to this event.
        /// </summary>
        public bool HasException => Exception != null;

        /// <summary>
        /// Gets a value indicating whether a Unity context object is attached to this event.
        /// </summary>
        public bool HasContext => Context != null;

        #endregion

        #region Construction

        /// <summary>
        /// Initializes a new instance of the <see cref="LogEvent"/> struct.
        /// </summary>
        /// <param name="level">The severity level.</param>
        /// <param name="loggerName">The category or logger name.</param>
        /// <param name="message">The resolved message text.</param>
        /// <param name="exception">An optional exception associated with the log.</param>
        /// <param name="context">An optional Unity Object context.</param>
        public LogEvent(
            LogLevel level,
            string loggerName,
            string message,
            Exception exception = null,
            UnityEngine.Object context = null)
        {
            Level = level;
            LoggerName = loggerName ?? "Global";
            Message = message ?? string.Empty;
            Exception = exception;
            TimestampUtc = DateTime.UtcNow;
            ThreadId = System.Threading.Thread.CurrentThread.ManagedThreadId;
            Context = context;
        }

        #endregion
    }
}
