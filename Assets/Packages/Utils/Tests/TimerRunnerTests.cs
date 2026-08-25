using NUnit.Framework;

namespace UnityEssentials.Utilities.Tests
{
    /// <summary>
    /// Covers the runner's bookkeeping by pumping TickAll directly: registration, mutation from
    /// inside a tick pass, deferred compaction, re-entrancy and the domain-reload reset action.
    /// </summary>
    [TestFixture]
    public class TimerRunnerTests
    {
        private const double Tolerance = 1e-4d;

        [SetUp]
        public void SetUp()
        {
            TimerRunner.Reset();
        }

        [TearDown]
        public void TearDown()
        {
            TimerRunner.Reset();
        }

        [Test]
        public void Add_RegistersTheTimer_WithoutStartingALoopOutsidePlayMode()
        {
            var timer = new Timer();

            TimerRunner.Add(timer);

            CollectionAssert.AreEqual(new[] { timer }, TimerRunner.ActiveTimers);
            Assert.IsFalse(TimerRunner.IsLoopRunning);
        }

        [Test]
        public void Add_SameTimerTwice_RegistersItOnce()
        {
            var timer = new Timer();

            TimerRunner.Add(timer);
            TimerRunner.Add(timer);

            Assert.AreEqual(1, TimerRunner.ActiveTimers.Count);
        }

        [Test]
        public void Add_Null_IsIgnored()
        {
            TimerRunner.Add(null);

            Assert.AreEqual(0, TimerRunner.ActiveTimers.Count);
        }

        [Test]
        public void Remove_UnregistersTheTimer()
        {
            var kept = new Timer();
            var removed = new Timer();
            TimerRunner.Add(kept);
            TimerRunner.Add(removed);

            TimerRunner.Remove(removed);

            CollectionAssert.AreEqual(new[] { kept }, TimerRunner.ActiveTimers);
        }

        [Test]
        public void Remove_UnknownTimer_IsHarmless()
        {
            var timer = new Timer();

            Assert.DoesNotThrow(() => TimerRunner.Remove(timer));
            Assert.AreEqual(0, TimerRunner.ActiveTimers.Count);
        }

        [Test]
        public void TickAll_PicksScaledOrUnscaledDeltaPerTimer()
        {
            var scaled = new Timer();
            var unscaled = new Timer(true);
            scaled.Start();
            unscaled.Start();
            TimerRunner.Add(scaled);
            TimerRunner.Add(unscaled);

            TimerRunner.TickAll(0.1f, 0.5f);

            Assert.AreEqual(0.1d, scaled.ElapsedSeconds, Tolerance);
            Assert.AreEqual(0.5d, unscaled.ElapsedSeconds, Tolerance);
        }

        [Test]
        public void TickAll_StoppedTimerInTheList_DoesNotAdvance()
        {
            var timer = new Timer();
            TimerRunner.Add(timer);

            TimerRunner.TickAll(1f, 1f);

            Assert.AreEqual(0d, timer.ElapsedSeconds, Tolerance);
        }

        [Test]
        public void TickAll_CompletedCountdown_UnregistersItself()
        {
            var timer = new Timer(1f);
            timer.Start();
            TimerRunner.Add(timer);

            TimerRunner.TickAll(1f, 1f);

            Assert.IsTrue(timer.IsCompleted);
            Assert.AreEqual(0, TimerRunner.ActiveTimers.Count);
        }

        [Test]
        public void TickAll_LoopingCountdown_StaysRegistered()
        {
            var timer = new Timer(1f, true);
            timer.Start();
            TimerRunner.Add(timer);

            TimerRunner.TickAll(1f, 1f);

            CollectionAssert.AreEqual(new[] { timer }, TimerRunner.ActiveTimers);
        }

        [Test]
        public void TickAll_HandlerStoppingAnotherTimer_IsSafe()
        {
            var trigger = new Timer(1f);
            var victim = new Timer();
            trigger.Completed += () => victim.Stop();
            trigger.Start();
            victim.Start();
            TimerRunner.Add(trigger);
            TimerRunner.Add(victim);

            Assert.DoesNotThrow(() => TimerRunner.TickAll(1f, 1f));

            // The victim was blanked before the pass reached it, so it neither ticked nor left a
            // hole behind: both slots are compacted away once the pass ends.
            Assert.IsFalse(victim.IsRunning);
            Assert.AreEqual(0d, victim.ElapsedSeconds, Tolerance);
            Assert.AreEqual(0, TimerRunner.ActiveTimers.Count);
        }

        [Test]
        public void TickAll_HandlerRemovingAnAlreadyTickedTimer_CompactsCorrectly()
        {
            var early = new Timer();
            var trigger = new Timer(1f);
            var survivor = new Timer();
            trigger.Completed += () => early.Stop();
            early.Start();
            trigger.Start();
            survivor.Start();
            TimerRunner.Add(early);
            TimerRunner.Add(trigger);
            TimerRunner.Add(survivor);

            TimerRunner.TickAll(1f, 1f);

            CollectionAssert.AreEqual(new[] { survivor }, TimerRunner.ActiveTimers);
            Assert.AreEqual(1d, early.ElapsedSeconds, Tolerance);
            Assert.AreEqual(1d, survivor.ElapsedSeconds, Tolerance);
        }

        [Test]
        public void TickAll_TimerAddedByAHandler_FirstTicksOnTheNextPass()
        {
            var trigger = new Timer(1f);
            var late = new Timer();
            trigger.Completed += () =>
            {
                late.Start();
                TimerRunner.Add(late);
            };
            trigger.Start();
            TimerRunner.Add(trigger);

            TimerRunner.TickAll(1f, 1f);

            CollectionAssert.AreEqual(new[] { late }, TimerRunner.ActiveTimers);
            Assert.AreEqual(0d, late.ElapsedSeconds, Tolerance);

            TimerRunner.TickAll(1f, 1f);

            Assert.AreEqual(1d, late.ElapsedSeconds, Tolerance);
        }

        [Test]
        public void TickAll_ReentrantCallFromAHandler_IsIgnored()
        {
            var trigger = new Timer(1f, true);
            var other = new Timer();
            trigger.Completed += () => TimerRunner.TickAll(1f, 1f);
            trigger.Start();
            other.Start();
            TimerRunner.Add(trigger);
            TimerRunner.Add(other);

            TimerRunner.TickAll(1f, 1f);

            Assert.AreEqual(1d, other.ElapsedSeconds, Tolerance);
        }

        [Test]
        public void Reset_ClearsActiveTimers_AndBumpsGeneration()
        {
            TimerRunner.Add(new Timer());
            var generation = TimerRunner.Generation;

            TimerRunner.Reset();

            Assert.AreEqual(0, TimerRunner.ActiveTimers.Count);
            Assert.AreEqual(generation + 1, TimerRunner.Generation);
            Assert.IsFalse(TimerRunner.IsLoopRunning);
        }

        [Test]
        public void StaticResetRegistry_ResetStatics_ClearsActiveTimers()
        {
            TimerRunner.Add(new Timer());
            var generation = TimerRunner.Generation;

            StaticResetRegistry.ResetStatics();

            Assert.AreEqual(0, TimerRunner.ActiveTimers.Count);
            Assert.AreEqual(generation + 1, TimerRunner.Generation);
        }
    }
}
