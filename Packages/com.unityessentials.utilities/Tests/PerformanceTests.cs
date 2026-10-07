using System;
using NUnit.Framework;
using UnityEngine.TestTools.Constraints;
using Is = UnityEngine.TestTools.Constraints.Is;

namespace UnityEssentials.Utilities.Tests
{
    /// <summary>
    /// Pins the warm paths that must stay allocation free: EventBus publish, the timer tick and its
    /// elapsed accessors, the runner pass and singleton access.
    /// </summary>
    [TestFixture]
    public class PerformanceTests
    {
        // Logging is exempt by design and deliberately untested here: every log call composes rich
        // text by string concatenation, so it allocates by construction. It pays for itself by being
        // stripped from release builds instead.

        private const float DeltaTime = 1f / 60f;

        private const float LapSeconds = 0.5f;

        // Sinks keep the results alive so the calls cannot be optimised away, and keep the measured
        // lambdas void-returning (the form Unity documents for Is.Not.AllocatingGCMemory(); a
        // value-returning lambda binds to a different NUnit overload that the Not-prefix does not
        // forward to the constraint). They are typed rather than object because assigning a struct
        // result to an object sink would box it and measure the box instead of the accessor.
        private static object _referenceSink;
        private static double _doubleSink;
        private static float _floatSink;
        private static TimeSpan _timeSpanSink;
        private static bool _boolSink;
        private static int _handlerCalls;

        private static readonly DamageEvent _damage = new DamageEvent { Amount = 7, Source = "perf" };

        private Timer _stopwatch;
        private Timer _countdown;
        private Timer _loopingCountdown;

        [SetUp]
        public void SetUp()
        {
            TimerRunner.Reset();
            EventBus.Clear<DamageEvent>();
            SingletonCacheReset.Clear<WellBehavedSingleton>();

            _referenceSink = null;
            _doubleSink = 0d;
            _floatSink = 0f;
            _timeSpanSink = TimeSpan.Zero;
            _boolSink = false;
            _handlerCalls = 0;

            _stopwatch = new Timer();
            _countdown = new Timer(600f);
            _loopingCountdown = new Timer(LapSeconds, true);
        }

        [TearDown]
        public void TearDown()
        {
            EventBus.Clear<DamageEvent>();
            TimerRunner.Reset();
            SingletonCacheReset.Clear<WellBehavedSingleton>();
        }

        private static void OnDamage(DamageEvent evt)
        {
            _handlerCalls += evt.Amount;
        }

        private static void OnDamageAlso(DamageEvent evt)
        {
            _handlerCalls += evt.Amount;
        }

        private static void OnLapCompleted()
        {
            _handlerCalls++;
        }

        [Test]
        public void WarmPublish_ToASubscribedHandler_DoesNotAllocate()
        {
            EventBus.Subscribe<DamageEvent>(OnDamage);

            // Warm-up: the channel's type initializer, its registry registration and the JIT of the
            // whole publish path all happen on this first call.
            EventBus.Publish(_damage);
            Assert.AreEqual(_damage.Amount, _handlerCalls);

            Assert.That(() => { EventBus.Publish(_damage); }, Is.Not.AllocatingGCMemory());
        }

        [Test]
        public void WarmPublish_ToSeveralHandlers_DoesNotAllocate()
        {
            EventBus.Subscribe<DamageEvent>(OnDamage);
            EventBus.Subscribe<DamageEvent>(OnDamageAlso);

            EventBus.Publish(_damage);
            Assert.AreEqual(_damage.Amount * 2, _handlerCalls);

            Assert.That(() => { EventBus.Publish(_damage); }, Is.Not.AllocatingGCMemory());
        }

        [Test]
        public void WarmPublish_WithNoSubscribers_DoesNotAllocate()
        {
            EventBus.Publish(_damage);

            Assert.That(() => { EventBus.Publish(_damage); }, Is.Not.AllocatingGCMemory());
        }

        [Test]
        public void WarmTick_OnAStopwatch_DoesNotAllocate()
        {
            _stopwatch.Start();
            _stopwatch.Tick(DeltaTime);

            Assert.That(() => { _stopwatch.Tick(DeltaTime); }, Is.Not.AllocatingGCMemory());
        }

        [Test]
        public void WarmTick_OnACountdown_DoesNotAllocate()
        {
            _countdown.Start();
            _countdown.Tick(DeltaTime);

            Assert.That(() => { _countdown.Tick(DeltaTime); }, Is.Not.AllocatingGCMemory());
        }

        [Test]
        public void WarmTick_CompletingALoopLap_DoesNotAllocate()
        {
            _loopingCountdown.Completed += OnLapCompleted;
            _loopingCountdown.Start();

            // A lap per tick: LapSeconds is exact in both float and double, so the measured tick
            // raises Completed exactly like the warm-up one did.
            _loopingCountdown.Tick(LapSeconds);
            Assert.AreEqual(1, _handlerCalls);

            Assert.That(() => { _loopingCountdown.Tick(LapSeconds); }, Is.Not.AllocatingGCMemory());
        }

        [Test]
        public void WarmTickAll_OnTheRunner_DoesNotAllocate()
        {
            _stopwatch.Start();
            _countdown.Start();
            TimerRunner.Add(_stopwatch);
            TimerRunner.Add(_countdown);

            // The warm-up pass also settles the list's internal capacity growth.
            TimerRunner.TickAll(DeltaTime, DeltaTime);
            Assert.AreEqual(2, TimerRunner.ActiveTimers.Count);

            Assert.That(() => { TimerRunner.TickAll(DeltaTime, DeltaTime); }, Is.Not.AllocatingGCMemory());
        }

        [Test]
        public void WarmElapsedAccessors_DoNotAllocate()
        {
            _stopwatch.Start();
            _stopwatch.Tick(1.5f);

            _doubleSink = _stopwatch.ElapsedSeconds;
            Assert.AreEqual(1.5d, _doubleSink, 1e-4d);

            _doubleSink = _stopwatch.ElapsedMilliseconds;
            _doubleSink = _stopwatch.ElapsedMinutes;
            _doubleSink = _stopwatch.ElapsedHours;

            Assert.That(() => { _doubleSink = _stopwatch.ElapsedSeconds; }, Is.Not.AllocatingGCMemory());
            Assert.That(() => { _doubleSink = _stopwatch.ElapsedMilliseconds; }, Is.Not.AllocatingGCMemory());
            Assert.That(() => { _doubleSink = _stopwatch.ElapsedMinutes; }, Is.Not.AllocatingGCMemory());
            Assert.That(() => { _doubleSink = _stopwatch.ElapsedHours; }, Is.Not.AllocatingGCMemory());
        }

        [Test]
        public void WarmElapsedTimeSpan_DoesNotAllocate()
        {
            _stopwatch.Start();
            _stopwatch.Tick(1.5f);

            _timeSpanSink = _stopwatch.Elapsed;
            Assert.AreEqual(TimeSpan.FromSeconds(1.5d), _timeSpanSink);

            Assert.That(() => { _timeSpanSink = _stopwatch.Elapsed; }, Is.Not.AllocatingGCMemory());
        }

        [Test]
        public void WarmProgressAndRemaining_DoNotAllocate()
        {
            _countdown.Start();
            _countdown.Tick(1.5f);

            _floatSink = _countdown.Remaining;
            Assert.AreEqual(598.5f, _floatSink, 1e-3f);

            _floatSink = _countdown.Progress;

            Assert.That(() => { _floatSink = _countdown.Progress; }, Is.Not.AllocatingGCMemory());
            Assert.That(() => { _floatSink = _countdown.Remaining; }, Is.Not.AllocatingGCMemory());
        }

        [Test]
        public void WarmSingletonInstance_DoesNotAllocate()
        {
            // Warm-up: the type initializer, the lazy construction and the JIT of the property.
            _referenceSink = WellBehavedSingleton.Instance;
            Assert.IsNotNull(_referenceSink);

            Assert.That(() => { _referenceSink = WellBehavedSingleton.Instance; }, Is.Not.AllocatingGCMemory());
        }

        [Test]
        public void WarmSingletonHasInstance_DoesNotAllocate()
        {
            _referenceSink = WellBehavedSingleton.Instance;
            _boolSink = WellBehavedSingleton.HasInstance;
            Assert.IsTrue(_boolSink);

            Assert.That(() => { _boolSink = WellBehavedSingleton.HasInstance; }, Is.Not.AllocatingGCMemory());
        }
    }
}
