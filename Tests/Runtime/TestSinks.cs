using System;
using System.Collections.Concurrent;
using System.Threading;
using LogLine.Core;

namespace LogLine.Tests.Runtime
{
    /// <summary>
    /// Zero-allocation sink that atomically counts emitted log events for allocation and stress tests.
    /// </summary>
    internal sealed class CountingSink : LogSinkBase
    {
        #region Private Fields

        private int _count;

        #endregion

        #region Properties

        /// <inheritdoc />
        public override string Name => "CountingTestSink";

        /// <summary>
        /// Gets the total number of events accepted by this sink.
        /// </summary>
        public int Count => Volatile.Read(ref _count);

        #endregion

        #region Public API

        /// <summary>
        /// Resets the accepted event counter to zero.
        /// </summary>
        public void Reset() => Interlocked.Exchange(ref _count, 0);

        #endregion

        #region LogSinkBase Implementation

        /// <inheritdoc />
        protected override void Write(in LogEvent logEvent) => Interlocked.Increment(ref _count);

        #endregion
    }

    /// <summary>
    /// Thread-safe in-memory sink capturing <see cref="LogEvent"/> entries for assertion in integration tests.
    /// </summary>
    internal sealed class MemorySink : LogSinkBase
    {
        #region Private Fields

        private readonly ConcurrentQueue<LogEvent> _events = new();

        #endregion

        #region Properties

        /// <inheritdoc />
        public override string Name => "MemoryTestSink";

        /// <summary>
        /// Gets the number of captured events.
        /// </summary>
        public int Count => _events.Count;

        #endregion

        #region Public API

        /// <summary>
        /// Captures a point-in-time array copy of all recorded events.
        /// </summary>
        /// <returns>An array of recorded <see cref="LogEvent"/> values.</returns>
        public LogEvent[] ToArray() => _events.ToArray();

        /// <summary>
        /// Drains all recorded events.
        /// </summary>
        public void Clear()
        {
            while (_events.TryDequeue(out _))
            {
            }
        }

        #endregion

        #region LogSinkBase Implementation

        /// <inheritdoc />
        protected override void Write(in LogEvent logEvent) => _events.Enqueue(logEvent);

        #endregion
    }

    /// <summary>
    /// Fault-injecting sink that always throws on <see cref="Write"/> and <see cref="OnDispose"/>
    /// to verify pipeline fault isolation.
    /// </summary>
    internal sealed class BrokenSink : LogSinkBase
    {
        #region Properties

        /// <inheritdoc />
        public override string Name => "BrokenTestSink";

        #endregion

        #region LogSinkBase Implementation

        /// <inheritdoc />
        protected override void Write(in LogEvent logEvent) =>
            throw new InvalidOperationException("Simulated sink write failure.");

        /// <inheritdoc />
        protected override void OnDispose() =>
            throw new InvalidOperationException("Simulated sink dispose failure.");

        #endregion
    }
}
