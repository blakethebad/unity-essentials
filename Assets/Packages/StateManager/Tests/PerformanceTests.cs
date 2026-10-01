using NUnit.Framework;
using UnityEngine.TestTools.Constraints;
using Is = UnityEngine.TestTools.Constraints.Is;

namespace UnityEssentials.States.Tests
{
    /// <summary>
    /// Pins the zero-allocation claim <see cref="BaseStateManager{TState}"/> makes for its hot path:
    /// a warm <c>ChangeState</c>, <c>Tick</c>, <c>RestartState</c> and <c>CanChangeState</c> must not
    /// touch the GC. A machine driven every frame is exactly where a stray allocation turns into a
    /// per-frame hitch, so the promise is only worth making if a test defends it.
    /// </summary>
    [TestFixture]
    public class PerformanceTests
    {
        // ---- Fixture state ------------------------------------------------

        // Every machine here is built from SilentState alone: the recording doubles interpolate a
        // string per hook, which would dwarf anything the machine itself did. Each test also calls the
        // operation it measures once beforehand, so JIT and one-time growth are charged to setup.
        //
        // The measured lambdas are deliberately void-returning: a value-returning lambda binds to a
        // different NUnit overload that the Not prefix does not forward to the constraint, so the
        // assertion would pass without measuring anything. Query answers therefore land in _sink,
        // which is typed bool rather than object because boxing would manufacture the allocation
        // under test.
        private static bool _sink;

        [SetUp]
        public void SetUp()
        {
            CallLog.Clear();
            _sink = false;
        }

        // ---- Transitions ----------------------------------------------------

        /// <summary>
        /// A warm round trip between two states allocates nothing: the transition path is lookup,
        /// table check, field swaps and virtual calls, with no temporaries.
        /// </summary>
        [Test]
        public void WarmChangeState_DoesNotAllocate()
        {
            var machine = new PlainStateManager(
                (TestState.A, TestState.B),
                (TestState.B, TestState.A));
            machine.AddState(TestState.A, new SilentState())
                .AddState(TestState.B, new SilentState());
            machine.Initialize();

            // A full A -> B -> A round trip rather than a single change, so the measured lambda
            // leaves the machine where the warm-up started; a one-way lambda would only be legal
            // the first time it ran.
            machine.ChangeState(TestState.B);
            machine.ChangeState(TestState.A);

            Assert.That(() =>
            {
                machine.ChangeState(TestState.B);
                machine.ChangeState(TestState.A);
            }, Is.Not.AllocatingGCMemory());

            Assert.AreEqual(TestState.A, machine.CurrentStateType);
        }

        /// <summary>
        /// The same round trip with both events subscribed still allocates nothing: raising
        /// <c>StateExited</c> and <c>StateEntered</c> with enum arguments goes through a closed
        /// generic <c>Action</c>, so neither argument is boxed.
        /// </summary>
        [Test]
        public void WarmChangeState_WithSubscribers_DoesNotAllocate()
        {
            var machine = new PlainStateManager(
                (TestState.A, TestState.B),
                (TestState.B, TestState.A));
            machine.AddState(TestState.A, new SilentState())
                .AddState(TestState.B, new SilentState());
            machine.Initialize();

            // Empty bodies on purpose: any work here would be measured as the machine's cost.
            machine.StateExited += (from, to) => { };
            machine.StateEntered += (from, to) => { };

            machine.ChangeState(TestState.B);
            machine.ChangeState(TestState.A);

            Assert.That(() =>
            {
                machine.ChangeState(TestState.B);
                machine.ChangeState(TestState.A);
            }, Is.Not.AllocatingGCMemory());

            Assert.AreEqual(TestState.A, machine.CurrentStateType);
        }

        // ---- Per-frame work -------------------------------------------------

        /// <summary>
        /// A warm <c>Tick</c> allocates nothing — the one that matters most, since it is the call a
        /// host behaviour makes every single frame.
        /// </summary>
        [Test]
        public void WarmTick_DoesNotAllocate()
        {
            var machine = new PlainStateManager(
                (TestState.A, TestState.B),
                (TestState.B, TestState.A));
            machine.AddState(TestState.A, new SilentState())
                .AddState(TestState.B, new SilentState());
            machine.Initialize();

            machine.Tick();

            Assert.That(() => { machine.Tick(); }, Is.Not.AllocatingGCMemory());
        }

        /// <summary>
        /// A warm <c>RestartState</c> allocates nothing, including the inherited exit/enter pair the
        /// state's own restart runs by default.
        /// </summary>
        [Test]
        public void WarmRestartState_DoesNotAllocate()
        {
            // No (A, A) pair is declared: a restart bypasses the table entirely, so declaring the
            // self pair would hide a regression that started routing restarts through it.
            var machine = new PlainStateManager(
                (TestState.A, TestState.B),
                (TestState.B, TestState.A));
            machine.AddState(TestState.A, new SilentState())
                .AddState(TestState.B, new SilentState());
            machine.Initialize();

            machine.RestartState();

            Assert.That(() => { machine.RestartState(); }, Is.Not.AllocatingGCMemory());
        }

        // ---- Queries --------------------------------------------------------

        /// <summary>
        /// <c>CanChangeState</c> allocates nothing for either answer, which is what makes it usable
        /// from UI code that asks once per frame whether to offer a button.
        /// </summary>
        [Test]
        public void WarmCanChangeState_DoesNotAllocate()
        {
            // Three pairs, because they take different routes through the table: a declared pair
            // hits the destination set, a declared self pair proves the lookup is not special-cased,
            // and the unregistered D misses the outer dictionary. The warm-up asserts keep the
            // measurement from degenerating into three identical misses.
            var machine = new PlainStateManager(
                (TestState.A, TestState.B),
                (TestState.B, TestState.A),
                (TestState.A, TestState.A));
            machine.AddState(TestState.A, new SilentState())
                .AddState(TestState.B, new SilentState());
            machine.Initialize();

            _sink = machine.CanChangeState(TestState.A, TestState.B);
            Assert.IsTrue(_sink);

            _sink = machine.CanChangeState(TestState.A, TestState.A);
            Assert.IsTrue(_sink);

            _sink = machine.CanChangeState(TestState.A, TestState.D);
            Assert.IsFalse(_sink);

            Assert.That(() =>
            {
                _sink = machine.CanChangeState(TestState.A, TestState.B);
                _sink = machine.CanChangeState(TestState.A, TestState.A);
                _sink = machine.CanChangeState(TestState.A, TestState.D);
            }, Is.Not.AllocatingGCMemory());
        }
    }
}
