using System;
using NUnit.Framework;

namespace UnityEssentials.States.Tests
{
    /// <summary>
    /// Covers the configuration surface of <see cref="BaseStateManager{TState}"/>: registration,
    /// transition declarations, the initial-state choice, and the seal <c>Initialize</c> puts on all
    /// three.
    /// </summary>
    [TestFixture]
    public class ConfigurationTests
    {
        [SetUp]
        public void SetUp()
        {
            CallLog.Clear();
        }

        // ---- Registration ---------------------------------------------------

        [Test]
        public void AddState_NullState_ThrowsArgumentNullException()
        {
            var machine = new PlainStateManager();

            Assert.Throws<ArgumentNullException>(() => machine.AddState(null));
        }

        [Test]
        public void AddState_DuplicateStateType_ThrowsStateConfigurationException()
        {
            var machine = new PlainStateManager();
            machine.AddState(new RecordingState(TestState.A));

            // A different instance under the same key: the collision is on StateType, not identity.
            Assert.Throws<StateConfigurationException>(
                () => machine.AddState(new RecordingState(TestState.A)));
        }

        [Test]
        public void AddState_StateAlreadyAttachedToOtherManager_ThrowsStateConfigurationException()
        {
            var shared = new RecordingState(TestState.A);
            var first = new PlainStateManager();
            first.AddState(shared);
            var second = new PlainStateManager();

            Assert.Throws<StateConfigurationException>(() => second.AddState(shared));

            // Ownership stayed with the first machine, and the rejected registration did not
            // half-register the state on the second: it has nothing to initialize.
            Assert.Throws<StateConfigurationException>(() => second.Initialize());
            first.Initialize();
            Assert.AreEqual(TestState.A, first.CurrentStateType);
        }

        [Test]
        public void AddState_ReturnsSameManagerInstance()
        {
            var machine = new PlainStateManager();

            var afterFirst = machine.AddState(new RecordingState(TestState.A));
            var afterSecond = afterFirst.AddState(new RecordingState(TestState.B));

            Assert.AreSame(machine, afterFirst);
            Assert.AreSame(machine, afterSecond);
        }

        // ---- Transition declarations -----------------------------------------

        [Test]
        public void Allow_UnregisteredFromState_ThrowsStateConfigurationException()
        {
            // Only the source is unknown, so a pass cannot be explained by the destination.
            var machine = new HookStateManager(
                m => m.AddState(new RecordingState(TestState.B)),
                delegate(in Transitions<TestState> transitions)
                {
                    transitions.Allow(TestState.D, TestState.B);
                });

            var thrown = Assert.Throws<StateConfigurationException>(() => machine.Initialize());

            StringAssert.Contains("Allow() refers to 'D', which is not registered.", thrown.Message);
            Assert.IsFalse(machine.IsInitialized);
            Assert.IsFalse(machine.CanChangeState(TestState.D, TestState.B));
        }

        [Test]
        public void Allow_DuplicatePair_IsIdempotent()
        {
            var machine = new HookStateManager(
                m => m.AddState(new RecordingState(TestState.A)).AddState(new RecordingState(TestState.B)),
                delegate(in Transitions<TestState> transitions)
                {
                    transitions.Allow(TestState.A, TestState.B)
                        .Allow(TestState.A, TestState.B);
                });

            Assert.DoesNotThrow(() => machine.Initialize());

            Assert.IsTrue(machine.CanChangeState(TestState.A, TestState.B));
            // Re-declaring must not widen the table either: the reverse pair and the self-transition
            // stay closed, exactly as after the first declaration.
            Assert.IsFalse(machine.CanChangeState(TestState.B, TestState.A));
            Assert.IsFalse(machine.CanChangeState(TestState.A, TestState.A));
        }

        [Test]
        public void Allow_NullTargets_ThrowsArgumentNullException()
        {
            // The source is registered so the null array is the only thing left to object to.
            var machine = new HookStateManager(
                m => m.AddState(new RecordingState(TestState.A)),
                delegate(in Transitions<TestState> transitions)
                {
                    transitions.Allow(TestState.A, (TestState[])null);
                });

            Assert.Throws<ArgumentNullException>(() => machine.Initialize());
            Assert.IsFalse(machine.IsInitialized);
        }

        [Test]
        public void Allow_EmptyTargets_ThrowsStateConfigurationException()
        {
            // An empty fan-out reads as though it granted something, so it is an error, not a no-op —
            // in both the params form and the explicit empty-array form. A failed hook abandons the
            // whole initialization, so each form needs a machine of its own.
            var paramsForm = new HookStateManager(
                m => m.AddState(new RecordingState(TestState.A)),
                delegate(in Transitions<TestState> transitions)
                {
                    transitions.Allow(TestState.A);
                });

            var thrown = Assert.Throws<StateConfigurationException>(() => paramsForm.Initialize());
            StringAssert.Contains("was called without any destination states", thrown.Message);

            var arrayForm = new HookStateManager(
                m => m.AddState(new RecordingState(TestState.A)),
                delegate(in Transitions<TestState> transitions)
                {
                    transitions.Allow(TestState.A, new TestState[0]);
                });

            Assert.Throws<StateConfigurationException>(() => arrayForm.Initialize());
        }

        [Test]
        public void CanChangeState_BeforeInitialize_ReportsAnEmptyTable()
        {
            var machine = new HookStateManager(
                m => m.AddState(new RecordingState(TestState.A)).AddState(new RecordingState(TestState.B)),
                delegate(in Transitions<TestState> transitions)
                {
                    transitions.Allow(TestState.A, TestState.B);
                });

            Assert.IsFalse(machine.IsInitialized);

            // Declarations only exist once Initialize has run InsertTransitions, so a configured but
            // uninitialized machine reports a table with nothing in it.
            Assert.IsFalse(machine.CanChangeState(TestState.A, TestState.B));

            // Unknown states answer false instead of throwing, which is what makes the query safe to
            // call from anywhere — including before the machine is initialized.
            Assert.IsFalse(machine.CanChangeState(TestState.D, TestState.A));
            Assert.IsFalse(machine.CanChangeState(TestState.A, TestState.D));

            machine.Initialize();

            Assert.IsTrue(machine.CanChangeState(TestState.A, TestState.B));
            Assert.IsFalse(machine.CanChangeState(TestState.B, TestState.A));
        }

        // ---- Initial state ---------------------------------------------------

        [Test]
        public void SetInitialState_UnregisteredState_ThrowsStateConfigurationException()
        {
            var machine = new PlainStateManager();
            machine.AddState(new RecordingState(TestState.A));

            Assert.Throws<StateConfigurationException>(() => machine.SetInitialState(TestState.D));

            // The rejected call must not have replaced the first-registered default either.
            machine.Initialize();
            Assert.AreEqual(TestState.A, machine.CurrentStateType);
        }

        // ---- Sealed after Initialize -----------------------------------------

        [Test]
        public void AddState_AfterInitialize_ThrowsStateConfigurationException()
        {
            var machine = CreateInitializedMachine();
            var late = new RecordingState(TestState.B);

            Assert.Throws<StateConfigurationException>(() => machine.AddState(late));

            // The seal is checked before ownership is taken, so the rejected instance is still free
            // to be registered somewhere else.
            Assert.DoesNotThrow(() => new PlainStateManager().AddState(late));
        }

        [Test]
        public void Allow_OnCapturedTransitions_AfterInitialize_ThrowsStateConfigurationException()
        {
            var machine = CreateMachineCapturingTransitions();
            var transitions = _capturedTransitions;

            // Both ends name the registered state, so being initialized is the only objection left —
            // for the pair form and the fan-out form alike.
            var thrown = Assert.Throws<StateConfigurationException>(
                () => transitions.Allow(TestState.A, TestState.A));
            StringAssert.Contains(
                "Transitions cannot be declared after Initialize(). Declare them inside InsertTransitions().",
                thrown.Message);

            Assert.Throws<StateConfigurationException>(
                () => transitions.Allow(TestState.A, new[] { TestState.A }));

            Assert.IsFalse(machine.CanChangeState(TestState.A, TestState.A));
        }

        [Test]
        public void AllowAny_OnCapturedTransitions_AfterInitialize_ThrowsStateConfigurationException()
        {
            var machine = CreateMachineCapturingTransitions();
            var transitions = _capturedTransitions;

            var thrown = Assert.Throws<StateConfigurationException>(() => transitions.AllowAny());
            StringAssert.Contains("cannot be declared after Initialize()", thrown.Message);

            // Opening the table cannot be undone, so proving the rejected call left it shut matters
            // more here than for the pair-at-a-time declarations.
            Assert.IsFalse(machine.CanChangeState(TestState.A, TestState.A));
            Assert.IsFalse(machine.CanChangeState(TestState.A, TestState.D));
        }

        [Test]
        public void SetInitialState_AfterInitialize_ThrowsStateConfigurationException()
        {
            var machine = CreateInitializedMachine();

            Assert.Throws<StateConfigurationException>(() => machine.SetInitialState(TestState.A));

            // The running machine was left alone: nothing beyond the initial entry ever ran.
            CollectionAssert.AreEqual(new[] { "Enter:A:from:A" }, CallLog.Entries);
            Assert.AreEqual(TestState.A, machine.CurrentStateType);
        }

        // ---- Helpers ----------------------------------------------------------

        // Written by the hook of the machine CreateMachineCapturingTransitions builds. The handle is
        // a copyable struct, so keeping one past Initialize() is the only way a test can reach the
        // declaration paths on a sealed machine.
        private Transitions<TestState> _capturedTransitions;

        // One state is all it takes to reach the sealed phase, and it keeps the after-Initialize
        // tests from depending on transition wiring they are not there to test.
        private static IStateManager<TestState> CreateInitializedMachine()
        {
            var machine = new PlainStateManager();
            machine.AddState(new RecordingState(TestState.A));
            machine.Initialize();

            return machine;
        }

        private IStateManager<TestState> CreateMachineCapturingTransitions()
        {
            var machine = new HookStateManager(
                m => m.AddState(new RecordingState(TestState.A)),
                delegate(in Transitions<TestState> transitions)
                {
                    _capturedTransitions = transitions;
                });
            machine.Initialize();

            return machine;
        }
    }
}
