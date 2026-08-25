using System;
using NUnit.Framework;

namespace UnityEssentials.Utilities.Tests
{
    /// <summary>
    /// Covers stopwatch-mode timers through the internal tick seam: start, pause/resume, reset,
    /// restart, the elapsed accessors and the countdown-only members that must stay inert.
    /// </summary>
    [TestFixture]
    public class TimerStopwatchTests
    {
        private const double Tolerance = 1e-4d;

        [Test]
        public void Constructor_CreatesStoppedStopwatch()
        {
            var timer = new Timer();

            Assert.AreEqual(TimerMode.Stopwatch, timer.Mode);
            Assert.IsFalse(timer.IsRunning);
            Assert.IsFalse(timer.IsCompleted);
            Assert.AreEqual(0d, timer.ElapsedSeconds, Tolerance);
        }

        [Test]
        public void Constructor_UseUnscaledTime_IsExposed()
        {
            Assert.IsFalse(new Timer().UseUnscaledTime);
            Assert.IsTrue(new Timer(true).UseUnscaledTime);
        }

        [Test]
        public void Tick_BeforeStart_DoesNotAccumulate()
        {
            var timer = new Timer();

            timer.Tick(1f);

            Assert.AreEqual(0d, timer.ElapsedSeconds, Tolerance);
        }

        [Test]
        public void Start_SetsIsRunning_AndTickAccumulates()
        {
            var timer = new Timer();

            timer.Start();
            timer.Tick(0.5f);
            timer.Tick(0.25f);

            Assert.IsTrue(timer.IsRunning);
            Assert.AreEqual(0.75d, timer.ElapsedSeconds, Tolerance);
        }

        [Test]
        public void Start_WhileRunning_IsIgnored()
        {
            var timer = new Timer();

            timer.Start();
            timer.Tick(1f);
            timer.Start();

            Assert.IsTrue(timer.IsRunning);
            Assert.AreEqual(1d, timer.ElapsedSeconds, Tolerance);
        }

        [Test]
        public void Tick_NonPositiveDelta_DoesNotRewind()
        {
            var timer = new Timer();

            timer.Start();
            timer.Tick(1f);
            timer.Tick(0f);
            timer.Tick(-5f);

            Assert.AreEqual(1d, timer.ElapsedSeconds, Tolerance);
        }

        [Test]
        public void Stop_PausesAccumulation_AndKeepsElapsed()
        {
            var timer = new Timer();

            timer.Start();
            timer.Tick(1f);
            timer.Stop();
            timer.Tick(4f);

            Assert.IsFalse(timer.IsRunning);
            Assert.AreEqual(1d, timer.ElapsedSeconds, Tolerance);
        }

        [Test]
        public void Start_AfterStop_ResumesFromPausedElapsed()
        {
            var timer = new Timer();

            timer.Start();
            timer.Tick(1f);
            timer.Stop();
            timer.Tick(4f);
            timer.Start();
            timer.Tick(0.5f);

            Assert.IsTrue(timer.IsRunning);
            Assert.AreEqual(1.5d, timer.ElapsedSeconds, Tolerance);
        }

        [Test]
        public void Reset_ZeroesElapsed_AndStops()
        {
            var timer = new Timer();

            timer.Start();
            timer.Tick(2f);
            timer.Reset();

            Assert.IsFalse(timer.IsRunning);
            Assert.AreEqual(0d, timer.ElapsedSeconds, Tolerance);

            timer.Tick(1f);

            Assert.AreEqual(0d, timer.ElapsedSeconds, Tolerance);
        }

        [Test]
        public void Restart_ZeroesElapsed_AndKeepsRunning()
        {
            var timer = new Timer();

            timer.Start();
            timer.Tick(2f);
            timer.Restart();

            Assert.IsTrue(timer.IsRunning);
            Assert.AreEqual(0d, timer.ElapsedSeconds, Tolerance);

            timer.Tick(0.5f);

            Assert.AreEqual(0.5d, timer.ElapsedSeconds, Tolerance);
        }

        [Test]
        public void Restart_FromStopped_Starts()
        {
            var timer = new Timer();

            timer.Restart();
            timer.Tick(1f);

            Assert.IsTrue(timer.IsRunning);
            Assert.AreEqual(1d, timer.ElapsedSeconds, Tolerance);
        }

        [Test]
        public void Stop_WhenNotRunning_IsHarmless()
        {
            var timer = new Timer();

            Assert.DoesNotThrow(() => timer.Stop());
            Assert.IsFalse(timer.IsRunning);
        }

        [Test]
        public void ElapsedAccessors_AgreeWithElapsedSeconds()
        {
            var timer = new Timer();

            timer.Start();
            timer.Tick(90f);

            Assert.AreEqual(90d, timer.ElapsedSeconds, Tolerance);
            Assert.AreEqual(90000d, timer.ElapsedMilliseconds, 1e-1d);
            Assert.AreEqual(1.5d, timer.ElapsedMinutes, Tolerance);
            Assert.AreEqual(0.025d, timer.ElapsedHours, Tolerance);
            Assert.AreEqual(TimeSpan.FromSeconds(90d), timer.Elapsed);
        }

        [Test]
        public void CountdownMembers_AreInertForAStopwatch()
        {
            var timer = new Timer();
            var completedCount = 0;
            timer.Completed += () => completedCount++;

            timer.Start();
            timer.Tick(3600f);

            Assert.AreEqual(0f, timer.Progress);
            Assert.AreEqual(0f, timer.Remaining);
            Assert.AreEqual(0f, timer.Duration);
            Assert.IsFalse(timer.IsCompleted);
            Assert.IsTrue(timer.IsRunning);
            Assert.AreEqual(0, completedCount);
        }
    }
}
