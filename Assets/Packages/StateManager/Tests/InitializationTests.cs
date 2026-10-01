using NUnit.Framework;

namespace UnityEssentials.States.Tests
{
    /// <summary>
    /// Covers the configuration-to-runtime handover: what <c>Initialize</c> requires, which state it
    /// enters, the special shape of that first entry, and the guards that keep the runtime methods
    /// unusable until it has run — the observation properties excepted, since they report a "nothing
    /// yet" value instead of throwing.
    /// </summary>
    [TestFixture]
    public class InitializationTests
    {
        // The initial entry is deliberately not a transition: it bypasses the table, passes the
        // initial state to itself as previousState, fires only StateEntered and leaves
        // PreviousStateType null. Each asymmetry is easy to "fix" by accident, so each has its own test.

        [SetUp]
        public void SetUp() => CallLog.Clear();

        // ---- Preconditions ------------------------------------------------

        [Test]
        public void Initialize_WithNoStates_ThrowsStateConfigurationException()
        {
            var machine = new PlainStateManager();

            Assert.Throws<StateConfigurationException>(() => machine.Initialize());
            Assert.IsFalse(machine.IsInitialized);
        }

        [Test]
        public void Initialize_CalledTwice_ThrowsStateConfigurationException()
        {
            var machine = new PlainStateManager();
            machine.AddState(TestState.A, new RecordingState());
            machine.Initialize();

            Assert.Throws<StateConfigurationException>(() => machine.Initialize());

            // The rejected second call must not have re-run the initial entry.
            CollectionAssert.AreEqual(new[] { "Enter:A:from:A" }, CallLog.Entries);
        }

        // ---- Initial state selection ---------------------------------------

        [Test]
        public void Initialize_WithoutSetInitialState_EntersFirstRegisteredState()
        {
            // B registers first while A is the enum's default value, so a machine that fell back to
            // default(TState) instead of the first registration would fail here.
            var machine = new PlainStateManager();
            machine.AddState(TestState.B, new RecordingState())
                .AddState(TestState.A, new RecordingState())
                .AddState(TestState.C, new RecordingState());

            machine.Initialize();

            Assert.AreEqual(TestState.B, machine.CurrentStateType);
            CollectionAssert.AreEqual(new[] { "Enter:B:from:B" }, CallLog.Entries);
        }

        [Test]
        public void Initialize_WithSetInitialState_EntersDeclaredState()
        {
            var machine = new PlainStateManager();
            machine.AddState(TestState.A, new RecordingState())
                .AddState(TestState.B, new RecordingState())
                .AddState(TestState.C, new RecordingState())
                .SetInitialState(TestState.C);

            machine.Initialize();

            Assert.AreEqual(TestState.C, machine.CurrentStateType);
            CollectionAssert.AreEqual(new[] { "Enter:C:from:C" }, CallLog.Entries);
        }

        [Test]
        public void SetInitialState_CalledTwice_LastCallWins()
        {
            var machine = new PlainStateManager();
            machine.AddState(TestState.A, new RecordingState())
                .AddState(TestState.B, new RecordingState())
                .AddState(TestState.C, new RecordingState())
                .SetInitialState(TestState.B)
                .SetInitialState(TestState.C);

            machine.Initialize();

            Assert.AreEqual(TestState.C, machine.CurrentStateType);

            // The superseded choice must be forgotten outright, not entered and then left.
            CollectionAssert.AreEqual(new[] { "Enter:C:from:C" }, CallLog.Entries);
        }

        [Test]
        public void Initialize_GetInitialStateOverride_EntersRedirectedState()
        {
            // SetInitialState names B, so C can only have been chosen by the override — which is
            // the point: a derived manager decides where it starts, and an override that ignores
            // base wins outright over the configured value rather than being merged with it.
            var machine = new RedirectingStateManager(TestState.C);
            machine.AddState(TestState.A, new RecordingState())
                .AddState(TestState.B, new RecordingState())
                .AddState(TestState.C, new RecordingState())
                .SetInitialState(TestState.B);

            machine.Initialize();

            Assert.AreEqual(TestState.C, machine.CurrentStateType);

            // B was never entered on the way, so the redirect replaced the initial state rather
            // than transitioned away from it.
            CollectionAssert.AreEqual(new[] { "Enter:C:from:C" }, CallLog.Entries);
        }

        [Test]
        public void Initialize_WithNoAllowedTransitions_Succeeds()
        {
            var machine = new PlainStateManager();
            machine.AddState(TestState.A, new RecordingState())
                .AddState(TestState.B, new RecordingState());

            Assert.DoesNotThrow(() => machine.Initialize());

            // The table really is empty — the initial entry got in without consulting it.
            Assert.IsFalse(machine.CanChangeState(TestState.A, TestState.B));
            Assert.IsFalse(machine.CanChangeState(TestState.A, TestState.A));
            Assert.IsTrue(machine.IsInitialized);
            Assert.AreEqual(TestState.A, machine.CurrentStateType);
        }

        // ---- Initial entry semantics ---------------------------------------

        [Test]
        public void Initialize_EnterStateReceivesOwnStateType()
        {
            var machine = new PlainStateManager();
            machine.AddState(TestState.A, new RecordingState())
                .AddState(TestState.B, new RecordingState())
                .SetInitialState(TestState.B);

            machine.Initialize();

            // The sentinel: no state was left, so the entered state is handed its own value.
            CollectionAssert.AreEqual(new[] { "Enter:B:from:B" }, CallLog.Entries);
        }

        [Test]
        public void Initialize_FiresStateEnteredWithInitialStateAsBothArguments()
        {
            var machine = new PlainStateManager();
            machine.AddState(TestState.A, new RecordingState())
                .AddState(TestState.B, new RecordingState())
                .SetInitialState(TestState.B);

            var entered = 0;
            machine.StateEntered += (previous, next) =>
            {
                entered++;
                CallLog.Record($"Entered:{previous}:{next}");
            };

            machine.Initialize();

            Assert.AreEqual(1, entered);

            // Ordering matters as much as the arguments: the event trails the hook.
            CollectionAssert.AreEqual(new[] { "Enter:B:from:B", "Entered:B:B" }, CallLog.Entries);
        }

        [Test]
        public void Initialize_DoesNotFireStateExited()
        {
            var machine = new PlainStateManager();
            machine.AddState(TestState.A, new RecordingState())
                .AddState(TestState.B, new RecordingState());

            var exited = 0;
            machine.StateExited += (previous, next) =>
            {
                exited++;
                CallLog.Record($"Exited:{previous}:{next}");
            };

            machine.Initialize();

            Assert.AreEqual(0, exited);

            // Nothing was left, so neither the hook nor the event has anything to report.
            CollectionAssert.AreEqual(new[] { "Enter:A:from:A" }, CallLog.Entries);
        }

        // ---- Post-initialization observation --------------------------------

        [Test]
        public void Initialize_PreviousStateTypeIsNull()
        {
            var machine = new PlainStateManager();
            machine.AddState(TestState.A, new RecordingState());

            machine.Initialize();

            // Null here is the documented way for EnterState to tell a first entry from a real one.
            Assert.IsNull(machine.PreviousStateType);
            Assert.IsFalse(machine.PreviousStateType.HasValue);
        }

        [Test]
        public void Initialize_PreviousStateIsNull()
        {
            var machine = new PlainStateManager();
            var initial = new RecordingState();
            machine.AddState(TestState.A, initial);

            machine.Initialize();

            Assert.IsNull(machine.PreviousState);
            Assert.AreSame(initial, machine.CurrentState);
        }

        [Test]
        public void Initialize_SetsIsInitialized()
        {
            var machine = new PlainStateManager();
            machine.AddState(TestState.A, new RecordingState());

            var initializedDuringEntry = false;
            machine.StateEntered += (previous, next) => initializedDuringEntry = machine.IsInitialized;

            machine.Initialize();

            // The flag is raised before the initial entry runs, not after it returns, which is what
            // lets a state read the machine it is being entered into.
            Assert.IsTrue(initializedDuringEntry);
            Assert.IsTrue(machine.IsInitialized);
        }

        // ---- Before-initialization behaviour --------------------------------

        [Test]
        public void IsInitialized_BeforeInitialize_IsFalse()
        {
            var empty = new PlainStateManager();
            Assert.IsFalse(empty.IsInitialized);

            var configured = new PlainStateManager((TestState.A, TestState.B));
            configured.AddState(TestState.A, new RecordingState())
                .AddState(TestState.B, new RecordingState())
                .SetInitialState(TestState.A);

            // Fully configured is still not initialized: only Initialize() flips the phase.
            Assert.IsFalse(configured.IsInitialized);
        }

        [Test]
        public void CurrentStateType_BeforeInitialize_ReturnsEnumDefault()
        {
            // B registers first, so the state this machine would enter is not the enum's default:
            // what comes back below is therefore "the default value", not "the initial state,
            // reported early".
            var machine = new PlainStateManager();
            machine.AddState(TestState.B, new RecordingState())
                .AddState(TestState.A, new RecordingState());

            Assert.AreEqual(default(TestState), machine.CurrentStateType);

            // The enum's default is a real state, so the value cannot tell the phases apart on its
            // own: IsInitialized is what a caller checks.
            Assert.IsFalse(machine.IsInitialized);
        }

        [Test]
        public void CurrentState_BeforeInitialize_ReturnsNull()
        {
            var machine = new PlainStateManager();
            machine.AddState(TestState.A, new RecordingState());

            // Null is the unambiguous half of the pair: no registered state can be mistaken for it,
            // even when the enum's default is one.
            Assert.IsNull(machine.CurrentState);
        }

        [Test]
        public void ChangeState_BeforeInitialize_ThrowsStateConfigurationException()
        {
            // The target is registered and the pair is one the machine's hook would declare, so the
            // missing Initialize() is what rejects the call — and it says so with a configuration
            // exception rather than the InvalidTransitionException an undeclared pair earns.
            var machine = new PlainStateManager((TestState.A, TestState.B));
            machine.AddState(TestState.A, new RecordingState())
                .AddState(TestState.B, new RecordingState());

            Assert.Throws<StateConfigurationException>(() => machine.ChangeState(TestState.B));
            CollectionAssert.IsEmpty(CallLog.Entries);
        }

        [Test]
        public void RestartState_BeforeInitialize_ThrowsStateConfigurationException()
        {
            var machine = new PlainStateManager();
            machine.AddState(TestState.A, new RecordingState());

            Assert.Throws<StateConfigurationException>(() => machine.RestartState());
            CollectionAssert.IsEmpty(CallLog.Entries);
        }

        [Test]
        public void Tick_BeforeInitialize_ThrowsStateConfigurationException()
        {
            var machine = new PlainStateManager();
            machine.AddState(TestState.A, new RecordingState());

            Assert.Throws<StateConfigurationException>(() => machine.Tick());

            // No state is current yet, so nothing may have been dispatched to.
            CollectionAssert.IsEmpty(CallLog.Entries);
        }

        // ---- Deferral during the initial entry -------------------------------

        [Test]
        public void Initialize_ChangeStateFromInitialEnterState_DefersUntilInitialEntryCompletes()
        {
            // The bootstrap pattern: an initial state whose entry hook does its startup work and
            // immediately flows the machine onward.
            var machine = new PlainStateManager((TestState.A, TestState.B));
            machine.AddState(TestState.A, new ChangeOnEnterState(TestState.B))
                .AddState(TestState.B, new RecordingState());

            machine.StateEntered += (from, to) => CallLog.Record($"Entered:{from}:to:{to}");

            machine.Initialize();

            // The initial entry completed first — StateEntered names A on both sides — and only then
            // did the deferred request run, as an ordinary transition with the full sequence.
            Assert.IsTrue(machine.IsInitialized);
            Assert.AreEqual(TestState.B, machine.CurrentStateType);
            Assert.AreEqual(TestState.A, machine.PreviousStateType);
            CollectionAssert.AreEqual(
                new[] { "Entered:A:to:A", "Enter:B:from:A", "Entered:A:to:B" },
                CallLog.Entries);
        }
    }
}
