using NUnit.Framework;

namespace UnityEssentials.States.Tests
{
    /// <summary>
    /// Pins <c>RestartState</c> as the deliberate non-transition it is documented to be: it re-runs
    /// the current state without consulting the transition table, without raising
    /// <c>StateExited</c>/<c>StateEntered</c>, and without touching current or previous.
    /// </summary>
    [TestFixture]
    public class RestartTests
    {
        // Restart's value lies in what it does not do, so most tests here are negative: they build a
        // machine that would fail loudly if restart were a self-transition, then show it does not.

        [SetUp]
        public void SetUp()
        {
            CallLog.Clear();
        }

        // ---- Default sequence ---------------------------------------------

        /// <summary>
        /// The inherited restart runs exit then enter, in that order and nothing else.
        /// </summary>
        [Test]
        public void RestartState_DefaultImplementation_CallsExitThenEnterOnSameState()
        {
            var machine = SoloMachine(TestState.A, new RecordingState());

            machine.RestartState();

            CollectionAssert.AreEqual(new[] { "Exit:A:to:A", "Enter:A:from:A" }, CallLog.Entries);
        }

        /// <summary>
        /// Both hooks receive the restarting state's own <c>StateType</c>, not the state the machine
        /// happens to have come from.
        /// </summary>
        [Test]
        public void RestartState_PassesOwnStateTypeToExitAndEnter()
        {
            var machine = PairMachine(TestState.A, new RecordingState(), TestState.B, new RecordingState());
            machine.ChangeState(TestState.B);

            // The A -> B transition's own hooks would otherwise head the expectation below.
            CallLog.Clear();

            machine.RestartState();

            // B on both sides: a machine sitting on B after arriving from A still restarts into
            // itself, so neither hook may see A.
            CollectionAssert.AreEqual(new[] { "Exit:B:to:B", "Enter:B:from:B" }, CallLog.Entries);
        }

        // ---- Machine state ------------------------------------------------

        /// <summary>
        /// A restart leaves the machine on the same state object, so the exit half must not be
        /// mistaken for a real departure.
        /// </summary>
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

        /// <summary>
        /// A restart does not count as a transition, so it neither invents a previous state on a
        /// machine that has never moved nor overwrites a real one.
        /// </summary>
        [Test]
        public void RestartState_DoesNotChangePreviousState()
        {
            var a = new RecordingState();
            var machine = PairMachine(TestState.A, a, TestState.B, new RecordingState());

            machine.RestartState();

            // Null is the documented "never transitioned" signal; a self-transition would have set
            // it to A and broken every EnterState that tests it for the machine's first entry.
            Assert.IsNull(machine.PreviousStateType);
            Assert.IsNull(machine.PreviousState);

            machine.ChangeState(TestState.B);
            machine.RestartState();

            // Still the state left behind by the last real transition, not the restarted B.
            Assert.AreEqual(TestState.A, machine.PreviousStateType);
            Assert.AreSame(a, machine.PreviousState);
        }

        // ---- Events and table ---------------------------------------------

        /// <summary>
        /// Neither <c>StateExited</c> nor <c>StateEntered</c> is raised by a restart, so observers
        /// wired to react to movement do not see one.
        /// </summary>
        [Test]
        public void RestartState_FiresNoEvents()
        {
            var machine = SoloMachine(TestState.A, new RecordingState());

            var exitedCount = 0;
            var enteredCount = 0;

            // Subscribed after Initialize on purpose: the initial entry raises StateEntered in its
            // own right, and counting that would say nothing about restart.
            machine.StateExited += (previous, next) => exitedCount++;
            machine.StateEntered += (previous, next) => enteredCount++;

            machine.RestartState();

            Assert.AreEqual(0, exitedCount);
            Assert.AreEqual(0, enteredCount);

            // The restart really did run its hooks, so the zeros above are silence rather than a
            // call that never happened.
            Assert.AreEqual(2, CallLog.Entries.Count);
        }

        /// <summary>
        /// Restart bypasses the transition table entirely: a machine with no declared pairs — not
        /// even the self-pair — restarts successfully.
        /// </summary>
        [Test]
        public void RestartState_RequiresNoTransitionTableEntry()
        {
            var machine = SoloMachine(TestState.A, new RecordingState());

            // Establishes that the self-pair really is undeclared, so the restart below cannot be
            // passing merely because the table happened to permit A -> A.
            Assert.IsFalse(machine.CanChangeState(TestState.A, TestState.A));

            Assert.DoesNotThrow(() => machine.RestartState());

            CollectionAssert.AreEqual(new[] { "Exit:A:to:A", "Enter:A:from:A" }, CallLog.Entries);
        }

        // ---- Overrides ----------------------------------------------------

        /// <summary>
        /// An override of the state's restart replaces the inherited exit/enter pair rather than
        /// running alongside it, which is what makes it a usable cheap-reset extension point.
        /// </summary>
        [Test]
        public void RestartState_OverriddenImplementation_ReplacesExitEnterSequence()
        {
            var machine = SoloMachine(TestState.A, new CustomRestartState());

            machine.RestartState();

            // The machine calls the state's RestartState and nothing more: no Exit:/Enter: entries
            // may appear beside the override's own marker.
            CollectionAssert.AreEqual(new[] { "CustomRestart:A" }, CallLog.Entries);
        }

        // ---- Re-entrancy --------------------------------------------------

        /// <summary>
        /// The re-entrancy guard is held for the whole restart, so a hook invoked by it cannot
        /// transition the machine out from under the restart in progress.
        /// </summary>
        [Test]
        public void RestartState_ChangeStateDuringRestart_ThrowsStateManagerException()
        {
            var machine = PairMachine(TestState.A, new ChangeOnExitState(TestState.B), TestState.B, new RecordingState());

            // A -> B is registered and declared, so the guard is the only thing left that can
            // reject the probe's request.
            Assert.IsTrue(machine.CanChangeState(TestState.A, TestState.B));

            // Assert.Throws matches the type exactly: whatever escapes the restart hook comes back
            // wrapped in the base StateManagerException, where a mis-built machine would have been
            // rejected before the hook ran, by a StateConfigurationException.
            var exception = Assert.Throws<StateManagerException>(() => machine.RestartState());

            // The rejected call is the inner ChangeState, surfacing out of the outer RestartState:
            // the wrapper embeds the message of the exception it caught, so the member name reads
            // back out of it, and the exception itself is kept underneath.
            StringAssert.Contains(nameof(BaseStateManager<TestState>.ChangeState), exception.Message);
            Assert.IsInstanceOf<StateManagerException>(exception.InnerException);

            // The abandoned change never reached the swap, and the guard was released on the way
            // out, so the machine is still usable and still on A.
            Assert.AreEqual(TestState.A, machine.CurrentStateType);
        }

        // ---- Machine builders ---------------------------------------------

        // One state and an empty transition table, then an emptied CallLog so assertions read as the
        // restart's own output. No transition is declared because none is needed: restart never
        // consults the table, so every test built this way would fail if that ever changed.
        private static BaseStateManager<TestState> SoloMachine(TestState stateType, BaseState<TestState> state)
        {
            var machine = new PlainStateManager();
            machine.AddState(stateType, state)
                .SetInitialState(stateType);

            machine.Initialize();

            CallLog.Clear();
            return machine;
        }

        // Starts on `from` and is allowed to move to `to`, for the tests that need a real transition
        // behind or ahead of the restart. The reverse pair is deliberately left undeclared.
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
