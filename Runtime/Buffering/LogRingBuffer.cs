using System.Threading;
using LogLine.Core;

namespace LogLine.Buffering
{
    /// <summary>
    /// High-throughput, bounded, lock-free Multi-Producer Single-Consumer (MPSC) ring buffer.
    /// Eliminates heap allocations during queueing using pre-allocated slot arrays.
    /// </summary>
    public sealed class LogRingBuffer
    {
        #region Nested Types

        private struct Slot
        {
            public long Sequence;
            public LogEvent Event;
        }

        #endregion

        #region Private Fields

        private readonly Slot[] _slots;
        private readonly int _mask;
        private long _head;
        private long _tail;
        private int _droppedCount;

        #endregion

        #region Properties

        /// <summary>
        /// Gets the total capacity of the ring buffer (always a power of two).
        /// </summary>
        public int Capacity => _slots.Length;

        /// <summary>
        /// Gets the cumulative number of log events dropped due to buffer saturation.
        /// </summary>
        public int DroppedCount => Volatile.Read(ref _droppedCount);

        #endregion

        #region Construction

        /// <summary>
        /// Initializes a new instance of the <see cref="LogRingBuffer"/> class.
        /// </summary>
        /// <param name="capacity">Buffer size. Will be normalized to the nearest power of two.</param>
        public LogRingBuffer(int capacity = 4096)
        {
            // Normalize capacity to the next power of two for fast bitwise masking
            int actualCapacity = 1;
            while (actualCapacity < capacity)
            {
                actualCapacity <<= 1;
            }

            _slots = new Slot[actualCapacity];
            _mask = actualCapacity - 1;

            // Initialize sequence markers
            for (int i = 0; i < _slots.Length; i++)
            {
                _slots[i].Sequence = i;
            }
        }

        #endregion

        #region Public Methods

        /// <summary>
        /// Attempts to enqueue a log event in a non-blocking, lock-free manner.
        /// </summary>
        /// <param name="logEvent">The log event payload to store.</param>
        /// <returns><see langword="true"/> if stored; <see langword="false"/> if buffer is saturated.</returns>
        public bool TryEnqueue(in LogEvent logEvent)
        {
            while (true)
            {
                long currentTail = Volatile.Read(ref _tail);
                long currentHead = Volatile.Read(ref _head);

                // Buffer saturation check
                if (currentTail - currentHead >= _slots.Length)
                {
                    Interlocked.Increment(ref _droppedCount);
                    return false;
                }

                int index = (int)(currentTail & _mask);
                long slotSequence = Volatile.Read(ref _slots[index].Sequence);

                // Check if this slot is ready for the current tail index
                if (slotSequence == currentTail)
                {
                    if (Interlocked.CompareExchange(ref _tail, currentTail + 1, currentTail) == currentTail)
                    {
                        // Safely claim slot and copy payload
                        _slots[index].Event = logEvent;

                        // Increment sequence to make event visible to the consumer
                        Volatile.Write(ref _slots[index].Sequence, currentTail + 1);
                        return true;
                    }
                }
                else if (slotSequence < currentTail)
                {
                    // Slot is lagging behind or wrapped; retry spin
                    Thread.Yield();
                }
            }
        }

        /// <summary>
        /// Attempts to dequeue the next available log event (Single-Consumer only).
        /// </summary>
        /// <param name="logEvent">Extracted log event.</param>
        /// <returns><see langword="true"/> if an event was dequeued; otherwise, <see langword="false"/>.</returns>
        public bool TryDequeue(out LogEvent logEvent)
        {
            long currentHead = _head;
            int index = (int)(currentHead & _mask);
            long slotSequence = Volatile.Read(ref _slots[index].Sequence);

            // Verify the producer has finalized writing to this slot
            if (slotSequence == currentHead + 1)
            {
                logEvent = _slots[index].Event;

                // Clear event reference to prevent managed memory retention
                _slots[index].Event = default;

                // Advance slot sequence for the next producer round
                Volatile.Write(ref _slots[index].Sequence, currentHead + _slots.Length);
                _head = currentHead + 1;
                return true;
            }

            logEvent = default;
            return false;
        }

        #endregion
    }
}
