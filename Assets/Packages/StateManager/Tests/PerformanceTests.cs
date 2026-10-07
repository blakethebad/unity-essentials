using NUnit.Framework;
using UnityEngine.TestTools.Constraints;
using Is = UnityEngine.TestTools.Constraints.Is;

namespace UnityEssentials.States.Tests
{
    /// <summary>
    /// Pins the zero-allocation claim <see cref="BaseStateManager{TState}"/> makes for its hot path:
    /// a warm <c>ChangeState</c>, <c>Tick</c>, <c>RestartState</c> and <c>CanChangeState</c> must not
    /// touch the GC. Every machine here is built from <c>SilentState</c>, since the recording doubles
    /// interpolate a string per hook.
    /// </summary>
    [TestFixture]
    public class PerformanceTests
    {
        // The measured lambdas are deliberately void-returning: a value-returning lambda binds to a
        // different NUnit overload that the Not prefix does not forward to the constraint, so the
        // assertion would pass without measuring anything. Query answers therefore land in _sink,
        // typed bool rather than object because boxing would manufacture the allocation under test.
        private static bool _sink;

        [SetUp]
        public void SetUp()
        {
            CallLog.Clear();
            _sink = false;
        }

        [Test]
        public void WarmChangeState_DoesNotAllocate()
        {
            var machine = new PlainStateManager(
                (TestState.A, TestState.B),
                (TestState.B, TestState.A));
            machine.AddState(TestState.A, new SilentState())
                .AddState(TestState.B, new SilentState());
            machine.Initialize();

            // A full round trip rather than a single change, so the measured lambda leaves the
            // machine where the warm-up started.
            machine.ChangeState(TestState.B);
            machine.ChangeState(TestState.A);

            Assert.That(() =>
            {
                machine.ChangeState(TestState.B);
                machine.ChangeState(TestState.A);
            }, Is.Not.AllocatingGCMemory());

            Assert.AreEqual(TestState.A, machine.CurrentStateType);
        }

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

        [Test]
        public void WarmCanChangeState_DoesNotAllocate()
        {
            // Three pairs, because they take different routes through the table: a declared pair
            // hits the destination set, a declared self pair proves the lookup is not special-cased,
            // and the unregistered D misses the outer dictionary.
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
