using System;
using System.Threading;
using LogLine.Buffering;
using LogLine.Core;
using NUnit.Framework;

namespace LogLine.Tests.Editor
{
    [TestFixture]
    public sealed class LogRingBufferTests
    {
        #region Single-Threaded Semantics

        [Test]
        public void EnqueueDequeue_PreservesFifoOrder()
        {
            var buffer = new LogRingBuffer(16);

            for (int i = 0; i < 10; i++)
            {
                var ev = new LogEvent(LogLevel.Info, "Fifo", $"msg_{i}");
                Assert.IsTrue(buffer.TryEnqueue(ev));
            }

            for (int i = 0; i < 10; i++)
            {
                Assert.IsTrue(buffer.TryDequeue(out LogEvent dequeued));
                Assert.AreEqual($"msg_{i}", dequeued.Message);
                Assert.AreEqual("Fifo", dequeued.LoggerName);
                Assert.AreEqual(LogLevel.Info, dequeued.Level);
            }

            Assert.IsFalse(buffer.TryDequeue(out _));
            Assert.AreEqual(0, buffer.DroppedCount);
        }

        [Test]
        public void WrapAround_MultipleCycles_NeverDropsOrCorrupts()
        {
            var buffer = new LogRingBuffer(8);

            for (int cycle = 0; cycle < 50; cycle++)
            {
                for (int i = 0; i < 5; i++)
                {
                    var ev = new LogEvent(LogLevel.Debug, "Wrap", $"c{cycle}_i{i}");
                    Assert.IsTrue(buffer.TryEnqueue(ev));
                }

                for (int i = 0; i < 5; i++)
                {
                    Assert.IsTrue(buffer.TryDequeue(out LogEvent dequeued));
                    Assert.AreEqual($"c{cycle}_i{i}", dequeued.Message);
                }
            }

            Assert.IsFalse(buffer.TryDequeue(out _));
            Assert.AreEqual(0, buffer.DroppedCount);
        }

        [Test]
        public void Saturation_CountsDropsAccurately_AndRecoversAfterDrain()
        {
            const int Capacity = 8;
            const int Attempts = 25;
            var buffer = new LogRingBuffer(Capacity);

            int accepted = 0;
            for (int i = 0; i < Attempts; i++)
            {
                if (buffer.TryEnqueue(new LogEvent(LogLevel.Warn, "Sat", "payload")))
                {
                    accepted++;
                }
            }

            Assert.Greater(accepted, 0);
            Assert.LessOrEqual(accepted, Capacity);
            Assert.AreEqual(Attempts - accepted, buffer.DroppedCount);

            int drained = 0;
            while (buffer.TryDequeue(out _))
            {
                drained++;
            }

            Assert.AreEqual(accepted, drained);

            // Buffer must accept new items cleanly after draining
            Assert.IsTrue(buffer.TryEnqueue(new LogEvent(LogLevel.Error, "Sat", "after_drain")));
            Assert.IsTrue(buffer.TryDequeue(out LogEvent recovered));
            Assert.AreEqual("after_drain", recovered.Message);
        }

        #endregion

        #region Multi-Threaded MPSC Stress

        [Test]
        public void MultiThreaded_10Producers_10000EventsEach_NoLossNoCorruptionNoDeadlock()
        {
            const int ProducerCount = 10;
            const int EventsPerProducer = 10_000;
            const int TotalEvents = ProducerCount * EventsPerProducer;

            var buffer = new LogRingBuffer(1024);
            var startGate = new ManualResetEventSlim(false);
            var producersDoneEvent = new ManualResetEventSlim(false);
            string[] workerNames = new string[ProducerCount];
            for (int i = 0; i < ProducerCount; i++)
            {
                workerNames[i] = $"Worker_{i}";
            }

            int producersDone = 0;
            int dequeuedCount = 0;
            int corruptedCount = 0;
            Exception consumerException = null;

            var consumer = new Thread(() =>
            {
                try
                {
                    var spinner = new SpinWait();
                    while (true)
                    {
                        if (buffer.TryDequeue(out LogEvent ev))
                        {
                            dequeuedCount++;
                            if (ev.Level != LogLevel.Info ||
                                ev.Message != "stress_payload" ||
                                ev.LoggerName == null ||
                                !ev.LoggerName.StartsWith("Worker_", StringComparison.Ordinal))
                            {
                                corruptedCount++;
                            }

                            spinner.Reset();
                        }
                        else if (producersDoneEvent.IsSet)
                        {
                            // Final check after producers finished
                            if (!buffer.TryDequeue(out ev))
                            {
                                break;
                            }

                            dequeuedCount++;
                        }
                        else
                        {
                            spinner.SpinOnce();
                        }
                    }
                }
                catch (Exception ex)
                {
                    consumerException = ex;
                }
            })
            {
                IsBackground = true,
                Name = "LogRingBuffer_TestConsumer"
            };

            consumer.Start();

            var producers = new Thread[ProducerCount];
            var producerExceptions = new Exception[ProducerCount];

            for (int t = 0; t < ProducerCount; t++)
            {
                int workerIndex = t;
                producers[t] = new Thread(() =>
                {
                    try
                    {
                        startGate.Wait();
                        string name = workerNames[workerIndex];
                        for (int i = 0; i < EventsPerProducer; i++)
                        {
                            buffer.TryEnqueue(new LogEvent(LogLevel.Info, name, "stress_payload"));
                        }
                    }
                    catch (Exception ex)
                    {
                        producerExceptions[workerIndex] = ex;
                    }
                })
                {
                    IsBackground = true,
                    Name = $"LogRingBuffer_TestProducer_{t}"
                };

                producers[t].Start();
            }

            startGate.Set();

            for (int t = 0; t < ProducerCount; t++)
            {
                Assert.IsTrue(producers[t].Join(TimeSpan.FromSeconds(15)), $"Producer thread {t} deadlocked.");
                Assert.IsNull(producerExceptions[t], $"Producer thread {t} threw: {producerExceptions[t]}");
            }

            Volatile.Write(ref producersDone, 1);
            producersDoneEvent.Set();
            Assert.IsTrue(consumer.Join(TimeSpan.FromSeconds(15)), "Consumer thread deadlocked.");
            Assert.IsNull(consumerException, $"Consumer thread threw: {consumerException}");

            Assert.AreEqual(0, corruptedCount, "Detected torn or corrupted LogEvent slot during concurrent MPSC access.");
            Assert.AreEqual(TotalEvents, dequeuedCount + buffer.DroppedCount,
                $"Event accounting mismatch: dequeued={dequeuedCount}, dropped={buffer.DroppedCount}, expected={TotalEvents}");
        }

        #endregion
    }
}
