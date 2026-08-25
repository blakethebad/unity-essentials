using NUnit.Framework;

namespace UnityEssentials.States.Tests
{
    /// <summary>
    /// Covers <see cref="BaseState{TManager, TState}"/>'s typed manager: which machine a state sees
    /// as its <c>Manager</c>, what happens when it is added to a machine of the wrong type, and how a
    /// state closed over <see cref="IStateManager{TState}"/> stays portable between machines.
    /// </summary>
    [TestFixture]
    public class TypedManagerTests
    {
        // Pure C# throughout: nothing here creates a UnityEngine object, so the fixture stays
        // runnable outside the editor. The behaviour half lives in BehaviourTests.

        [SetUp]
        public void SetUp()
        {
            CallLog.Clear();
        }

        // ---- Typed manager identity ----------------------------------------

        /// <summary>
        /// A state closed over a concrete manager type sees the exact machine instance it was added
        /// to, not a copy and not the wrapper of one.
        /// </summary>
        [Test]
        public void AddState_TypedState_SeesTheManagerItWasAddedTo()
        {
            var machine = new PlainStateManager();
            var state = new PlainManagerState(TestState.A);

            // Null beforehand is what makes the reference check below a statement about
            // registration rather than about construction.
            Assert.IsNull(state.ObservedManager);

            machine.AddState(state);

            Assert.IsTrue(ReferenceEquals(machine, state.ObservedManager));
            Assert.AreSame(machine, state.ObservedManager);

            // Two machines of the same type: the state must report its own, not merely "a machine".
            var other = new PlainStateManager();
            var otherState = new PlainManagerState(TestState.A);
            other.AddState(otherState);

            Assert.AreSame(other, otherState.ObservedManager);
            Assert.AreNotSame(machine, otherState.ObservedManager);
        }

        /// <summary>
        /// The typed manager is live: a state reached through it can drive the machine it belongs to.
        /// </summary>
        [Test]
        public void TypedState_DrivesItsOwnManager()
        {
            var machine = new PlainStateManager((TestState.A, TestState.B));
            var a = new PlainManagerState(TestState.A);
            machine.AddState(a)
                .AddState(new RecordingState(TestState.B));

            machine.Initialize();

            a.ObservedManager.ChangeState(TestState.B);

            Assert.AreEqual(TestState.B, machine.CurrentStateType);
            CollectionAssert.AreEqual(new[] { "Enter:B:from:A" }, CallLog.Entries);
        }

        // ---- Mismatched manager types --------------------------------------

        /// <summary>
        /// Adding a state to a machine that is not its <c>TManager</c> throws, registers nothing, and
        /// leaves the instance free to be added to a machine of the right type afterwards.
        /// </summary>
        [Test]
        public void AddState_MismatchedManagerType_ThrowsAndLeavesStateUnattached()
        {
            var wrongType = new HookStateManager();
            var state = new PlainManagerState(TestState.A);

            var thrown = Assert.Throws<StateConfigurationException>(() => wrongType.AddState(state));

            StringAssert.Contains(nameof(PlainManagerState), thrown.Message);
            StringAssert.Contains(nameof(PlainStateManager), thrown.Message);
            StringAssert.Contains(nameof(HookStateManager), thrown.Message);

            // OnAttach runs before ownership is taken, so the rejected state kept no manager...
            Assert.IsNull(state.ObservedManager);

            // ...and the machine kept no state: with nothing registered, it cannot initialize.
            Assert.Throws<StateConfigurationException>(() => wrongType.Initialize());
            Assert.IsFalse(wrongType.IsInitialized);
            CollectionAssert.IsEmpty(CallLog.Entries);

            // The same instance, unspoiled by the rejection, on a machine that does match.
            var rightType = new PlainStateManager();
            rightType.AddState(state);

            Assert.AreSame(rightType, state.ObservedManager);

            rightType.Initialize();

            Assert.AreEqual(TestState.A, rightType.CurrentStateType);
            Assert.AreSame(state, rightType.CurrentState);
        }

        /// <summary>
        /// A rejected registration does not consume the enum key either: the same value can still be
        /// registered by a state the machine accepts.
        /// </summary>
        [Test]
        public void AddState_MismatchedManagerType_LeavesTheStateKeyFree()
        {
            var machine = new HookStateManager();

            Assert.Throws<StateConfigurationException>(
                () => machine.AddState(new PlainManagerState(TestState.A)));

            var replacement = new RecordingState(TestState.A);

            Assert.DoesNotThrow(() => machine.AddState(replacement));

            machine.Initialize();

            Assert.AreSame(replacement, machine.CurrentState);
            CollectionAssert.AreEqual(new[] { "Enter:A:from:A" }, CallLog.Entries);
        }

        // ---- Portable states -----------------------------------------------

        /// <summary>
        /// A state closed over <see cref="IStateManager{TState}"/> attaches to any machine type, and
        /// reports each one as its manager.
        /// </summary>
        [Test]
        public void InterfaceTypedState_AttachesToAnyMachineType()
        {
            var plain = new PlainStateManager();
            var plainState = new AnyManagerState(TestState.A);
            plain.AddState(plainState);

            var hooked = new HookStateManager();
            var hookedState = new AnyManagerState(TestState.A);
            hooked.AddState(hookedState);

            Assert.AreSame(plain, plainState.ObservedManager);
            Assert.AreSame(hooked, hookedState.ObservedManager);

            var redirecting = new RedirectingStateManager(TestState.B);
            var redirectedState = new AnyManagerState(TestState.B);

            Assert.DoesNotThrow(() => redirecting.AddState(redirectedState));
            Assert.AreSame(redirecting, redirectedState.ObservedManager);
        }

        /// <summary>
        /// Attaching is still one machine per instance: an interface-typed state is portable across
        /// machine <em>types</em>, not shareable between machines.
        /// </summary>
        [Test]
        public void InterfaceTypedState_StillBelongsToOneMachine()
        {
            var first = new PlainStateManager();
            var shared = new AnyManagerState(TestState.A);
            first.AddState(shared);

            var second = new HookStateManager();

            Assert.Throws<StateConfigurationException>(() => second.AddState(shared));

            // Ownership stayed with the machine that took it first.
            Assert.AreSame(first, shared.ObservedManager);
            Assert.Throws<StateConfigurationException>(() => second.Initialize());
        }

        // ---- Interface-typed callers ---------------------------------------

        /// <summary>
        /// A plain machine is a complete <see cref="IStateManager{TState}"/>: a caller holding only
        /// the interface can configure, initialize, transition and tick it.
        /// </summary>
        [Test]
        public void PlainMachine_AsInterface_DrivesFullLifecycle()
        {
            IStateManager<TestState> manager = new PlainStateManager((TestState.A, TestState.B));

            var chained = manager.AddState(new RecordingState(TestState.A))
                .AddState(new RecordingState(TestState.B))
                .SetInitialState(TestState.A);

            // The configuration calls hand the same machine back, so chains keep targeting it.
            Assert.AreSame(manager, chained);

            manager.Initialize();

            Assert.IsTrue(manager.IsInitialized);
            Assert.IsTrue(manager.CanChangeState(TestState.A, TestState.B));

            manager.ChangeState(TestState.B);
            manager.Tick();
            manager.RestartState();

            Assert.AreEqual(TestState.B, manager.CurrentStateType);
            Assert.AreEqual(TestState.A, manager.PreviousStateType);
            CollectionAssert.AreEqual(
                new[]
                {
                    "Enter:A:from:A",
                    "Exit:A:to:B",
                    "Enter:B:from:A",
                    "Update:B",
                    "Exit:B:to:B",
                    "Enter:B:from:B"
                },
                CallLog.Entries);
        }
    }
}
