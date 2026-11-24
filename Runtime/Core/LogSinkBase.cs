using System.Threading;
using UnityEngine;

namespace LogLine.Core
{
    /// <summary>
    /// Thread-safe base class for <see cref="ILogSink"/> implementations.
    /// Centralizes atomic enabled/level/category filtering and single-execution disposal.
    /// </summary>
    public abstract class LogSinkBase : ILogSink
    {
        #region Private Fields

        private volatile bool _isEnabled = true;
        private volatile int _minimumLevel = (int)LogLevel.Trace;
        private volatile CategoryFilter _categoryFilter;
        private int _disposed;

        #endregion

        #region Properties

        /// <inheritdoc />
        public abstract string Name { get; }

        /// <inheritdoc />
        public bool IsEnabled
        {
            get => _isEnabled;
            set => _isEnabled = value;
        }

        /// <inheritdoc />
        public LogLevel MinimumLevel
        {
            get => (LogLevel)_minimumLevel;
            set => _minimumLevel = (int)value;
        }

        /// <inheritdoc />
        public CategoryFilter CategoryFilter
        {
            get => _categoryFilter;
            set => _categoryFilter = value;
        }

        /// <summary>
        /// Gets a value indicating whether this sink has been disposed.
        /// </summary>
        protected bool IsDisposed => Volatile.Read(ref _disposed) != 0;

        #endregion

        #region ILogSink Implementation

        /// <inheritdoc />
        [HideInCallstack]
        public void Emit(in LogEvent logEvent)
        {
            if (Volatile.Read(ref _disposed) != 0 || !_isEnabled || (int)logEvent.Level < _minimumLevel)
            {
                return;
            }

            CategoryFilter filter = _categoryFilter;
            if (filter != null && !filter.IsMatch(logEvent.LoggerName))
            {
                return;
            }

            Write(in logEvent);
        }

        /// <inheritdoc />
        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0)
            {
                return;
            }

            OnDispose();
        }

        #endregion

        #region Protected Hooks

        /// <summary>
        /// Writes a pre-filtered log event to the sink's underlying destination.
        /// Guaranteed not to be called if the sink is disposed, disabled, below <see cref="MinimumLevel"/>,
        /// or rejected by <see cref="CategoryFilter"/>.
        /// </summary>
        /// <param name="logEvent">The read-only log event reference.</param>
        [HideInCallstack]
        protected abstract void Write(in LogEvent logEvent);

        /// <summary>
        /// Releases sink-specific resources. Guaranteed to execute at most once.
        /// </summary>
        protected virtual void OnDispose()
        {
        }

        #endregion
    }
}
