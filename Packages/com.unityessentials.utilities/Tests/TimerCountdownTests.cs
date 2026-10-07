using System;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace UnityEssentials.Utilities.Tests
{
    /// <summary>
    /// Covers countdown-mode timers through the internal tick seam: duration validation, progress
    /// and remaining, completion firing exactly once, loop laps, and handler-exception isolation.
    /// </summary>
    [TestFixture]
    public class TimerCountdownTests
    {
        private const double Tolerance = 1e-4d;
        private const string HandlerFailureMessage = "timer handler failure";

        [Test]
        public void Constructor_NonPositiveDuration_ThrowsArgumentOutOfRangeException()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => new Timer(0f));
            Assert.Throws<ArgumentOutOfRangeException>(() => new Timer(-1f));
        }

        [Test]
        public void Constructor_NonFiniteDuration_ThrowsArgumentOutOfRangeException()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => new Timer(float.NaN));
            Assert.Throws<ArgumentOutOfRangeException>(() => new Timer(float.PositiveInfinity));
            Assert.Throws<ArgumentOutOfRangeException>(() => new Timer(float.NegativeInfinity));
        }

        [Test]
        public void Constructor_CreatesStoppedCountdown()
        {
            var timer = new Timer(2f, true, true);

            Assert.AreEqual(TimerMode.Countdown, timer.Mode);
            Assert.AreEqual(2f, timer.Duration);
            Assert.IsTrue(timer.UseUnscaledTime);
            Assert.IsFalse(timer.IsRunning);
            Assert.IsFalse(timer.IsCompleted);
            Assert.AreEqual(0f, timer.Progress);
            Assert.AreEqual(2f, timer.Remaining);
        }

        [Test]
        public void Tick_ProgressAndRemaining_TrackElapsed()
        {
            var timer = new Timer(2f);

            timer.Start();
            timer.Tick(0.5f);

            Assert.AreEqual(0.25f, timer.Progress, 1e-4f);
            Assert.AreEqual(1.5f, timer.Remaining, 1e-4f);

            timer.Tick(1f);

            Assert.AreEqual(0.75f, timer.Progress, 1e-4f);
            Assert.AreEqual(0.5f, timer.Remaining, 1e-4f);
        }

        [Test]
        public void Tick_ReachingDuration_CompletesAndFiresOnce()
        {
            var timer = new Timer(2f);
            var completedCount = 0;
            timer.Completed += () => completedCount++;

            timer.Start();
            timer.Tick(2f);

            Assert.AreEqual(1, completedCount);
            Assert.IsTrue(timer.IsCompleted);
            Assert.IsFalse(timer.IsRunning);
            Assert.AreEqual(1f, timer.Progress);
            Assert.AreEqual(0f, timer.Remaining);
        }

        [Test]
        public void Tick_OvershootingDuration_StopsExactlyAtDuration()
        {
            var timer = new Timer(2f);

            timer.Start();
            timer.Tick(5f);

            Assert.AreEqual(2d, timer.ElapsedSeconds, Tolerance);
            Assert.AreEqual(TimeSpan.FromSeconds(2d), timer.Elapsed);
            Assert.AreEqual(1f, timer.Progress);
        }

        [Test]
        public void Tick_AfterCompletion_DoesNotFireAgain()
        {
            var timer = new Timer(1f);
            var completedCount = 0;
            timer.Completed += () => completedCount++;

            timer.Start();
            timer.Tick(1f);
            timer.Tick(1f);
            timer.Tick(1f);

            Assert.AreEqual(1, completedCount);
            Assert.AreEqual(1d, timer.ElapsedSeconds, Tolerance);
        }

        [Test]
        public void Start_AfterCompletion_IsIgnored()
        {
            var timer = new Timer(1f);

            timer.Start();
            timer.Tick(1f);
            timer.Start();
            timer.Tick(1f);

            Assert.IsFalse(timer.IsRunning);
            Assert.IsTrue(timer.IsCompleted);
            Assert.AreEqual(1d, timer.ElapsedSeconds, Tolerance);
        }

        [Test]
        public void Restart_AfterCompletion_RunsTheCountdownAgain()
        {
            var timer = new Timer(1f);
            var completedCount = 0;
            timer.Completed += () => completedCount++;

            timer.Start();
            timer.Tick(1f);
            timer.Restart();

            Assert.IsTrue(timer.IsRunning);
            Assert.IsFalse(timer.IsCompleted);
            Assert.AreEqual(0d, timer.ElapsedSeconds, Tolerance);

            timer.Tick(1f);

            Assert.AreEqual(2, completedCount);
            Assert.IsTrue(timer.IsCompleted);
        }

        [Test]
        public void Reset_ClearsCompletedState()
        {
            var timer = new Timer(1f);

            timer.Start();
            timer.Tick(1f);
            timer.Reset();

            Assert.IsFalse(timer.IsCompleted);
            Assert.IsFalse(timer.IsRunning);
            Assert.AreEqual(0f, timer.Progress);
            Assert.AreEqual(1f, timer.Remaining);
        }

        [Test]
        public void Stop_ThenStart_ResumesTheCountdown()
        {
            var timer = new Timer(2f);
            var completedCount = 0;
            timer.Completed += () => completedCount++;

            timer.Start();
            timer.Tick(1f);
            timer.Stop();
            timer.Tick(5f);

            Assert.AreEqual(0, completedCount);
            Assert.AreEqual(1d, timer.ElapsedSeconds, Tolerance);

            timer.Start();
            timer.Tick(1f);

            Assert.AreEqual(1, completedCount);
        }

        [Test]
        public void Loop_FiresEveryLap_AndKeepsRunning()
        {
            var timer = new Timer(1f, true);
            var completedCount = 0;
            timer.Completed += () => completedCount++;

            timer.Start();
            timer.Tick(1f);
            timer.Tick(1f);

            Assert.AreEqual(2, completedCount);
            Assert.IsTrue(timer.IsRunning);
            Assert.IsFalse(timer.IsCompleted);
        }

        [Test]
        public void Loop_RollsOverflowIntoTheNextCycle()
        {
            var timer = new Timer(1f, true);
            var completedCount = 0;
            timer.Completed += () => completedCount++;

            timer.Start();
            timer.Tick(1.25f);

            Assert.AreEqual(1, completedCount);
            Assert.AreEqual(0.25d, timer.ElapsedSeconds, Tolerance);
            Assert.AreEqual(0.25f, timer.Progress, 1e-4f);
            Assert.AreEqual(0.75f, timer.Remaining, 1e-4f);
        }

        [Test]
        public void Loop_SingleTickSpanningManyLaps_FiresPerLap()
        {
            var timer = new Timer(1f, true);
            var completedCount = 0;
            timer.Completed += () => completedCount++;

            timer.Start();
            timer.Tick(3.5f);

            Assert.AreEqual(3, completedCount);
            Assert.AreEqual(0.5d, timer.ElapsedSeconds, Tolerance);
            Assert.IsTrue(timer.IsRunning);
        }

        [Test]
        public void Loop_HandlerStoppingTheTimer_HaltsRemainingLaps()
        {
            var timer = new Timer(1f, true);
            var completedCount = 0;
            timer.Completed += () =>
            {
                completedCount++;
                timer.Stop();
            };

            timer.Start();
            timer.Tick(3.5f);

            Assert.AreEqual(1, completedCount);
            Assert.IsFalse(timer.IsRunning);
            Assert.AreEqual(2.5d, timer.ElapsedSeconds, Tolerance);
        }

        [Test]
        public void Loop_HandlerRestartingTheTimer_StartsAFreshCycle()
        {
            var timer = new Timer(1f, true);
            var completedCount = 0;
            timer.Completed += () =>
            {
                completedCount++;
                timer.Restart();
            };

            timer.Start();
            timer.Tick(2.5f);

            Assert.AreEqual(1, completedCount);
            Assert.IsTrue(timer.IsRunning);
            Assert.AreEqual(0d, timer.ElapsedSeconds, Tolerance);
        }

        [Test]
        public void CompletedHandlerThrowing_IsLogged_AndLeavesStateIntact()
        {
            LogAssert.Expect(LogType.Exception, new Regex(HandlerFailureMessage));

            var timer = new Timer(1f);
            timer.Completed += () => throw new InvalidOperationException(HandlerFailureMessage);

            timer.Start();
            Assert.DoesNotThrow(() => timer.Tick(1.5f));

            Assert.IsTrue(timer.IsCompleted);
            Assert.IsFalse(timer.IsRunning);
            Assert.AreEqual(1d, timer.ElapsedSeconds, Tolerance);
            Assert.AreEqual(1f, timer.Progress);
            Assert.AreEqual(0f, timer.Remaining);
        }

        [Test]
        public void CompletedHandlerThrowing_DoesNotBreakLooping()
        {
            LogAssert.Expect(LogType.Exception, new Regex(HandlerFailureMessage));
            LogAssert.Expect(LogType.Exception, new Regex(HandlerFailureMessage));

            var timer = new Timer(1f, true);
            var completedCount = 0;
            timer.Completed += () =>
            {
                completedCount++;
                throw new InvalidOperationException(HandlerFailureMessage);
            };

            timer.Start();
            Assert.DoesNotThrow(() => timer.Tick(2.25f));

            Assert.AreEqual(2, completedCount);
            Assert.IsTrue(timer.IsRunning);
            Assert.AreEqual(0.25d, timer.ElapsedSeconds, Tolerance);
        }

        [Test]
        public void CompletedHandlerThrowing_EndsTheInvocationList()
        {
            LogAssert.Expect(LogType.Exception, new Regex(HandlerFailureMessage));

            var timer = new Timer(1f);
            var secondHandlerRan = false;
            timer.Completed += () => throw new InvalidOperationException(HandlerFailureMessage);
            timer.Completed += () => secondHandlerRan = true;

            timer.Start();
            timer.Tick(1f);

            // Completed is a plain multicast delegate, so the throw ends the invocation list: the
            // guard protects the timer, not the subscribers queued behind the faulty one.
            Assert.IsFalse(secondHandlerRan);
            Assert.IsTrue(timer.IsCompleted);
        }
    }
}
