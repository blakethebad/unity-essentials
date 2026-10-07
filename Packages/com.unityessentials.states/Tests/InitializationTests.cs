using NUnit.Framework;

namespace UnityEssentials.States.Tests
{
    /// <summary>
    /// Covers the configuration-to-runtime handover: what <c>Initialize</c> requires, which state it
    /// enters, the special shape of that first entry, and the guards that keep the runtime methods
    /// unusable until it has run.
    /// </summary>
    [TestFixture]
    public class InitializationTests
    {
        [SetUp]
        public void SetUp() => CallLog.Clear();

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

            CollectionAssert.AreEqual(new[] { "Enter:A:from:A" }, CallLog.Entries);
        }

        [Test]
        public void Initialize_WithoutSetInitialState_EntersFirstRegisteredState()
        {
            // B registers first while A is the enum's default, so falling back to default(TState)
            // instead of the first registration would fail here.
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
            CollectionAssert.AreEqual(new[] { "Enter:C:from:C" }, CallLog.Entries);
        }

        [Test]
        public void Initialize_GetInitialStateOverride_EntersRedirectedState()
        {
            // SetInitialState names B, so C can only have been chosen by the override.
            var machine = new RedirectingStateManager(TestState.C);
            machine.AddState(TestState.A, new RecordingState())
                .AddState(TestState.B, new RecordingState())
                .AddState(TestState.C, new RecordingState())
                .SetInitialState(TestState.B);

            machine.Initialize();

            Assert.AreEqual(TestState.C, machine.CurrentStateType);
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

        [Test]
        public void Initialize_EnterStateReceivesOwnStateType()
        {
            var machine = new PlainStateManager();
            machine.AddState(TestState.A, new RecordingState())
                .AddState(TestState.B, new RecordingState())
                .SetInitialState(TestState.B);

            machine.Initialize();

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
            CollectionAssert.AreEqual(new[] { "Enter:A:from:A" }, CallLog.Entries);
        }

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

            // The flag is raised before the initial entry runs, which is what lets a state read the
            // machine it is being entered into.
            Assert.IsTrue(initializedDuringEntry);
            Assert.IsTrue(machine.IsInitialized);
        }

        [Test]
        public void IsInitialized_BeforeInitialize_IsFalse()
        {
            var empty = new PlainStateManager();
            Assert.IsFalse(empty.IsInitialized);

            var configured = new PlainStateManager((TestState.A, TestState.B));
            configured.AddState(TestState.A, new RecordingState())
                .AddState(TestState.B, new RecordingState())
                .SetInitialState(TestState.A);

            Assert.IsFalse(configured.IsInitialized);
        }

        [Test]
        public void CurrentStateType_BeforeInitialize_ReturnsEnumDefault()
        {
            // B registers first, so what comes back below is "the enum's default", not "the initial
            // state, reported early".
            var machine = new PlainStateManager();
            machine.AddState(TestState.B, new RecordingState())
                .AddState(TestState.A, new RecordingState());

            Assert.AreEqual(default(TestState), machine.CurrentStateType);
            Assert.IsFalse(machine.IsInitialized);
        }

        [Test]
        public void CurrentState_BeforeInitialize_ReturnsNull()
        {
            var machine = new PlainStateManager();
            machine.AddState(TestState.A, new RecordingState());

            Assert.IsNull(machine.CurrentState);
        }

        [Test]
        public void ChangeState_BeforeInitialize_ThrowsStateConfigurationException()
        {
            // The pair is one this machine's hook would declare, so the missing Initialize() is what
            // rejects the call — with a configuration exception, not an InvalidTransitionException.
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
            CollectionAssert.IsEmpty(CallLog.Entries);
        }

        [Test]
        public void Initialize_ChangeStateFromInitialEnterState_DefersUntilInitialEntryCompletes()
        {
            var machine = new PlainStateManager((TestState.A, TestState.B));
            machine.AddState(TestState.A, new ChangeOnEnterState(TestState.B))
                .AddState(TestState.B, new RecordingState());

            machine.StateEntered += (from, to) => CallLog.Record($"Entered:{from}:to:{to}");

            machine.Initialize();

            // The initial entry completed first — StateEntered names A on both sides — and only then
            // did the deferred request run, as an ordinary transition.
            Assert.IsTrue(machine.IsInitialized);
            Assert.AreEqual(TestState.B, machine.CurrentStateType);
            Assert.AreEqual(TestState.A, machine.PreviousStateType);
            CollectionAssert.AreEqual(
                new[] { "Entered:A:to:A", "Enter:B:from:A", "Entered:A:to:B" },
                CallLog.Entries);
        }
    }
}
