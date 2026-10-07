using NUnit.Framework;

namespace UnityEssentials.States.Tests
{
    /// <summary>
    /// Pins <c>RestartState</c> as the deliberate non-transition it is: it re-runs the current state
    /// without consulting the transition table, without raising <c>StateExited</c>/<c>StateEntered</c>,
    /// and without touching current or previous.
    /// </summary>
    [TestFixture]
    public class RestartTests
    {
        [SetUp]
        public void SetUp()
        {
            CallLog.Clear();
        }

        [Test]
        public void RestartState_DefaultImplementation_CallsExitThenEnterOnSameState()
        {
            var machine = SoloMachine(TestState.A, new RecordingState());

            machine.RestartState();

            CollectionAssert.AreEqual(new[] { "Exit:A:to:A", "Enter:A:from:A" }, CallLog.Entries);
        }

        [Test]
        public void RestartState_PassesOwnStateTypeToExitAndEnter()
        {
            var machine = PairMachine(TestState.A, new RecordingState(), TestState.B, new RecordingState());
            machine.ChangeState(TestState.B);

            CallLog.Clear();

            machine.RestartState();

            // B on both sides: a machine sitting on B after arriving from A still restarts into
            // itself, so neither hook may see A.
            CollectionAssert.AreEqual(new[] { "Exit:B:to:B", "Enter:B:from:B" }, CallLog.Entries);
        }

        [Test]
        public void RestartState_DoesNotChangeCurrentState()
        {
            var b = new RecordingState();
            var machine = PairMachine(TestState.A, new RecordingState(), TestState.B, b);
            machine.ChangeState(TestState.B);

            machine.RestartState();

            Assert.AreEqual(TestState.B, machine.CurrentStateType);
            Assert.AreSame(b, machine.CurrentState);
        }

        [Test]
        public void RestartState_DoesNotChangePreviousState()
        {
            var a = new RecordingState();
            var machine = PairMachine(TestState.A, a, TestState.B, new RecordingState());

            machine.RestartState();

            // A self-transition would have set this to A and broken every EnterState that tests it
            // for the machine's first entry.
            Assert.IsNull(machine.PreviousStateType);
            Assert.IsNull(machine.PreviousState);

            machine.ChangeState(TestState.B);
            machine.RestartState();

            // Still the state left behind by the last real transition, not the restarted B.
            Assert.AreEqual(TestState.A, machine.PreviousStateType);
            Assert.AreSame(a, machine.PreviousState);
        }

        [Test]
        public void RestartState_FiresNoEvents()
        {
            var machine = SoloMachine(TestState.A, new RecordingState());

            var exitedCount = 0;
            var enteredCount = 0;

            // Subscribed after Initialize: counting the initial entry's StateEntered would say
            // nothing about restart.
            machine.StateExited += (previous, next) => exitedCount++;
            machine.StateEntered += (previous, next) => enteredCount++;

            machine.RestartState();

            Assert.AreEqual(0, exitedCount);
            Assert.AreEqual(0, enteredCount);

            // The restart really did run its hooks, so the zeros above are silence rather than a
            // call that never happened.
            Assert.AreEqual(2, CallLog.Entries.Count);
        }

        [Test]
        public void RestartState_RequiresNoTransitionTableEntry()
        {
            var machine = SoloMachine(TestState.A, new RecordingState());

            // The self-pair really is undeclared, so the restart below cannot be passing merely
            // because the table happened to permit A -> A.
            Assert.IsFalse(machine.CanChangeState(TestState.A, TestState.A));

            Assert.DoesNotThrow(() => machine.RestartState());

            CollectionAssert.AreEqual(new[] { "Exit:A:to:A", "Enter:A:from:A" }, CallLog.Entries);
        }

        [Test]
        public void RestartState_OverriddenImplementation_ReplacesExitEnterSequence()
        {
            var machine = SoloMachine(TestState.A, new CustomRestartState());

            machine.RestartState();

            // No Exit:/Enter: entries may appear beside the override's own marker.
            CollectionAssert.AreEqual(new[] { "CustomRestart:A" }, CallLog.Entries);
        }

        [Test]
        public void RestartState_ChangeStateDuringRestart_ThrowsStateManagerException()
        {
            var machine = PairMachine(TestState.A, new ChangeOnExitState(TestState.B), TestState.B, new RecordingState());

            // A -> B is declared, so the guard is the only thing left that can reject the request.
            Assert.IsTrue(machine.CanChangeState(TestState.A, TestState.B));

            // Assert.Throws matches the type exactly: a mis-built machine would have been rejected
            // before the hook ran, by a StateConfigurationException.
            var exception = Assert.Throws<StateManagerException>(() => machine.RestartState());

            StringAssert.Contains(nameof(BaseStateManager<TestState>.ChangeState), exception.Message);
            Assert.IsInstanceOf<StateManagerException>(exception.InnerException);

            // The abandoned change never reached the swap, and the guard was released on the way out.
            Assert.AreEqual(TestState.A, machine.CurrentStateType);
        }

        // One state and an empty transition table: restart never consults the table, so every test
        // built this way would fail if that ever changed.
        private static BaseStateManager<TestState> SoloMachine(TestState stateType, BaseState<TestState> state)
        {
            var machine = new PlainStateManager();
            machine.AddState(stateType, state)
                .SetInitialState(stateType);

            machine.Initialize();

            CallLog.Clear();
            return machine;
        }

        // Starts on `from` and may move to `to`; the reverse pair is deliberately left undeclared.
        private static BaseStateManager<TestState> PairMachine(
            TestState fromType,
            BaseState<TestState> from,
            TestState toType,
            BaseState<TestState> to)
        {
            var machine = new PlainStateManager((fromType, toType));
            machine.AddState(fromType, from)
                .AddState(toType, to)
                .SetInitialState(fromType);

            machine.Initialize();

            CallLog.Clear();
            return machine;
        }
    }
}
