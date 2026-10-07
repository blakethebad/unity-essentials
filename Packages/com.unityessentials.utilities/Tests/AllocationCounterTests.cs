using System;
using NUnit.Framework;

namespace UnityEssentials.Utilities.Tests
{
    /// <summary>
    /// Covers the span bookkeeping of the counter with megabyte-sized probes. GC.GetTotalMemory is a
    /// noisy, process-wide reading, so every measurement is asserted as a generous bound rather than
    /// an exact byte count.
    /// </summary>
    [TestFixture]
    public class AllocationCounterTests
    {
        private const int Megabyte = 1024 * 1024;

        [Test]
        public void Default_IsStoppedAndEmpty()
        {
            var counter = default(AllocationCounter);

            Assert.IsFalse(counter.IsRunning);
            Assert.IsFalse(counter.CollectionOccurred);
            Assert.AreEqual(0L, counter.AllocatedBytes);
        }

        [Test]
        public void StartNew_ReturnsARunningCounter()
        {
            var counter = AllocationCounter.StartNew();

            Assert.IsTrue(counter.IsRunning);
        }

        [Test]
        public void Stop_WhenNotRunning_IsANoOp()
        {
            var counter = default(AllocationCounter);

            counter.Stop();
            counter.Stop();

            Assert.IsFalse(counter.IsRunning);
            Assert.AreEqual(0L, counter.AllocatedBytes);
        }

        [Test]
        public void Span_AroundAMegabyteAllocation_MeasuresAtLeastAMegabyte()
        {
            var counter = AllocationCounter.StartNew();
            var probe = new byte[Megabyte];
            counter.Stop();

            // Kept alive past Stop: a collection of the probe inside the span would shrink the very
            // growth being measured.
            GC.KeepAlive(probe);

            Assert.That(counter.AllocatedBytes, Is.GreaterThanOrEqualTo((long)Megabyte));
        }

        [Test]
        public void AllocatedBytes_IsReadableMidSpan()
        {
            var counter = AllocationCounter.StartNew();
            var probe = new byte[Megabyte];
            var midSpanBytes = counter.AllocatedBytes;
            var runningMidSpan = counter.IsRunning;
            counter.Stop();
            GC.KeepAlive(probe);

            Assert.IsTrue(runningMidSpan);
            Assert.That(midSpanBytes, Is.GreaterThanOrEqualTo((long)Megabyte));
        }

        [Test]
        public void Stop_FreezesTheMeasuredValue()
        {
            var counter = AllocationCounter.StartNew();
            var inside = new byte[Megabyte];
            counter.Stop();
            GC.KeepAlive(inside);

            var stoppedBytes = counter.AllocatedBytes;
            var outside = new byte[Megabyte];
            GC.KeepAlive(outside);

            Assert.AreEqual(stoppedBytes, counter.AllocatedBytes);
        }

        [Test]
        public void Start_WhileRunning_DoesNotRebaselineTheSpan()
        {
            var counter = AllocationCounter.StartNew();
            var first = new byte[Megabyte];
            counter.Start();
            var second = new byte[Megabyte];
            counter.Stop();
            GC.KeepAlive(first);
            GC.KeepAlive(second);

            Assert.That(counter.AllocatedBytes, Is.GreaterThanOrEqualTo(2L * Megabyte));
        }

        [Test]
        public void TwoSpans_Accumulate()
        {
            var counter = AllocationCounter.StartNew();
            var first = new byte[Megabyte];
            counter.Stop();
            GC.KeepAlive(first);

            counter.Start();
            var second = new byte[Megabyte];
            counter.Stop();
            GC.KeepAlive(second);

            Assert.That(counter.AllocatedBytes, Is.GreaterThanOrEqualTo(2L * Megabyte));
        }

        [Test]
        public void Reset_ClearsTheTotalTheFlagAndTheRunningState()
        {
            var counter = AllocationCounter.StartNew();
            var probe = new byte[Megabyte];
            GC.Collect();
            counter.Stop();
            GC.KeepAlive(probe);

            counter.Reset();

            Assert.IsFalse(counter.IsRunning);
            Assert.IsFalse(counter.CollectionOccurred);
            Assert.AreEqual(0L, counter.AllocatedBytes);
        }

        [Test]
        public void Restart_DiscardsTheSpanInProgress()
        {
            var counter = AllocationCounter.StartNew();
            var probe = new byte[8 * Megabyte];
            counter.Restart();
            var runningAfterRestart = counter.IsRunning;
            counter.Stop();
            GC.KeepAlive(probe);

            Assert.IsTrue(runningAfterRestart);
            Assert.That(counter.AllocatedBytes, Is.LessThan(4L * Megabyte));
        }

        [Test]
        public void CollectionInsideSpan_KeepsTheTotalNonNegative()
        {
            var counter = AllocationCounter.StartNew();
            var doomed = new byte[8 * Megabyte];
            doomed[0] = 1;
            doomed = null;
            GC.Collect();
            GC.WaitForPendingFinalizers();
            counter.Stop();

            Assert.That(counter.AllocatedBytes, Is.GreaterThanOrEqualTo(0L));
            Assert.IsTrue(counter.CollectionOccurred);
        }

        [Test]
        public void CollectionOccurred_IsLatchedUntilReset()
        {
            var counter = AllocationCounter.StartNew();
            GC.Collect();
            counter.Stop();

            Assert.IsTrue(counter.CollectionOccurred);

            counter.Start();
            counter.Stop();

            Assert.IsTrue(counter.CollectionOccurred);
        }
    }
}
