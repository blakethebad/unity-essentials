using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

namespace UnityEssentials.States.Tests
{
    /// <summary>
    /// Covers <see cref="StateManagerBehaviour{TState}"/>: the Unity lifecycle wiring (configure and
    /// initialize in <c>Start</c>, tick in <c>Update</c>), every member that forwards to the hosted
    /// machine, and the typed-manager surface a state authored against the behaviour sees.
    /// </summary>
    [TestFixture]
    public class BehaviourTests
    {
        // EditMode gives no player loop, so the hosts' Invoke* methods drive Awake/Start/Update by
        // hand — also the only way to stop between Awake and Start. Delegation tests assert against
        // the hosted machine: an adapter keeping its own copy would pass a behaviour-only assertion.

        // ---- Fixture plumbing ---------------------------------------------

        // EditMode tests share one scene for the whole run, so leaked hosts would accumulate across
        // the fixture and outlive it.
        private readonly List<GameObject> _hosts = new List<GameObject>();

        /// <summary>Empties the shared <see cref="CallLog"/> before the host under test is built.</summary>
        [SetUp]
        public void SetUp()
        {
            CallLog.Clear();
        }

        /// <summary>Destroys every host created by the test that just ran.</summary>
        [TearDown]
        public void TearDown()
        {
            foreach (var host in _hosts)
            {
                // A test may have destroyed its own host; Unity's overloaded == reports that as null.
                if (host != null)
                {
                    UnityEngine.Object.DestroyImmediate(host);
                }
            }

            _hosts.Clear();
        }

        // Creates a tracked host with none of its lifecycle run yet — the caller decides how far
        // through Awake/Start/Update to drive it.
        private TestFlowBehaviour CreateHost()
        {
            return CreateHost<TestFlowBehaviour>();
        }

        private THost CreateHost<THost>() where THost : MonoBehaviour
        {
            var host = new GameObject("test");
            _hosts.Add(host);

            return host.AddComponent<THost>();
        }

        private static void RecordEntered(TestState from, TestState to)
        {
            CallLog.Record($"Handler:Entered:{from}:to:{to}");
        }

        private static void RecordExited(TestState from, TestState to)
        {
            CallLog.Record($"Handler:Exited:{from}:to:{to}");
        }

        // ---- Lifecycle -----------------------------------------------------

        /// <summary>
        /// The full <c>Awake</c> + <c>Start</c> pair leaves the machine initialized and sitting on
        /// the configured initial state, with exactly one entry hook run.
        /// </summary>
        [Test]
        public void Behaviour_AfterAwakeAndStart_IsInitializedInInitialState()
        {
            var host = CreateHost();

            host.InvokeAwake();
            host.InvokeStart();

            Assert.IsTrue(host.IsInitialized);
            Assert.AreEqual(TestState.A, host.CurrentStateType);

            // Initialize is not a transition: it enters with the initial state as its own "previous"
            // and leaves PreviousStateType null, which is how a state detects the very first entry.
            CollectionAssert.AreEqual(new[] { "Enter:A:from:A" }, CallLog.Entries);
            Assert.IsNull(host.PreviousStateType);
        }

        /// <summary>
        /// Stopping after <c>Awake</c> leaves an unconfigured, uninitialized machine, whose forwarded
        /// properties answer with their pre-initialization values instead of throwing.
        /// </summary>
        [Test]
        public void Behaviour_AfterAwakeOnly_IsNotConfiguredOrInitialized()
        {
            var host = CreateHost();

            host.InvokeAwake();

            Assert.IsFalse(host.IsInitialized);
            Assert.IsNotNull(host.MachineForTests);
            CollectionAssert.IsEmpty(CallLog.Entries);

            // Configuration happens inside Initialize(), so the machine Awake leaves behind has no
            // states and an empty transition table: the pair Start will open is still closed.
            Assert.IsFalse(host.CanChangeState(TestState.A, TestState.B));

            // Null is what actually proves no state was entered: this host's initial state is A,
            // which is also default(TestState), so the enum-default answer alone would not.
            Assert.IsNull(host.CurrentState);
            Assert.AreEqual(default(TestState), host.CurrentStateType);
        }

        /// <summary>
        /// Subscribing before <c>Awake</c> is legal and effective: the machine is built in the
        /// constructor, so a handler attached from another component's <c>Awake</c> — which may run
        /// first — still observes the initial entry raised by <c>Start</c>.
        /// </summary>
        [Test]
        public void Behaviour_BeforeAwake_EventSubscriptionDoesNotThrow()
        {
            var host = CreateHost();

            Assert.DoesNotThrow(() => host.StateEntered += RecordEntered);

            host.InvokeAwake();
            host.InvokeStart();

            // Both arguments are the initial state, and the event trails the state's own hook.
            CollectionAssert.AreEqual(
                new[] { "Enter:A:from:A", "Handler:Entered:A:to:A" },
                CallLog.Entries);

            host.StateEntered -= RecordEntered;
        }

        /// <summary>
        /// <c>Start</c> initializes the very machine the behaviour hosts rather than a fresh one, and
        /// each host owns a separate machine.
        /// </summary>
        [Test]
        public void Behaviour_Start_InitializesOwnHostedMachine()
        {
            var host = CreateHost();
            var machine = host.MachineForTests;

            Assert.IsNotNull(machine);

            host.InvokeAwake();
            host.InvokeStart();

            // Same instance before and after: Start configured and initialized the machine built by
            // the constructor, in place, instead of replacing it.
            Assert.AreSame(machine, host.MachineForTests);
            Assert.IsTrue(machine.IsInitialized);
            Assert.AreEqual(TestState.A, machine.CurrentStateType);

            // A shared machine would make two hosts fight over one state registry, and the second
            // Start would throw on duplicate registration rather than initialize cleanly.
            var other = CreateHost();
            other.InvokeAwake();
            other.InvokeStart();

            Assert.AreNotSame(machine, other.MachineForTests);
            Assert.IsTrue(other.IsInitialized);
        }

        // ---- Ticking -------------------------------------------------------

        /// <summary>
        /// <c>Update</c> pumps the machine once per call, and follows the machine when the current
        /// state changes rather than the state it started on.
        /// </summary>
        [Test]
        public void Behaviour_Update_TicksCurrentState()
        {
            var host = CreateHost();
            host.InvokeAwake();
            host.InvokeStart();

            // Drop the initial entry so the remaining entries are the ticks and nothing else.
            CallLog.Clear();

            host.InvokeUpdate();
            host.InvokeUpdate();

            CollectionAssert.AreEqual(new[] { "Update:A", "Update:A" }, CallLog.Entries);

            host.ChangeState(TestState.B);
            CallLog.Clear();

            host.InvokeUpdate();

            CollectionAssert.AreEqual(new[] { "Update:B" }, CallLog.Entries);
        }

        /// <summary>
        /// <c>Update</c> is inert until the machine is initialized, before and after <c>Awake</c>
        /// alike, which is what lets a subclass override <c>Start</c> and initialize later.
        /// </summary>
        [Test]
        public void Behaviour_Update_BeforeInitialize_DoesNothing()
        {
            var host = CreateHost();

            // An unguarded forward to Tick() would throw StateConfigurationException here, so the
            // calls themselves are half the assertion.
            host.InvokeUpdate();
            host.InvokeAwake();
            host.InvokeUpdate();

            Assert.IsFalse(host.IsInitialized);
            CollectionAssert.IsEmpty(CallLog.Entries);
        }

        // ---- Delegating members --------------------------------------------

        /// <summary>
        /// <c>ChangeState</c> moves the hosted machine itself, running the real transition sequence,
        /// and lets the machine's rejections through unchanged.
        /// </summary>
        [Test]
        public void Behaviour_ChangeState_DelegatesToMachine()
        {
            var host = CreateHost();
            host.InvokeAwake();
            host.InvokeStart();
            CallLog.Clear();

            host.ChangeState(TestState.B);

            Assert.AreEqual(TestState.B, host.MachineForTests.CurrentStateType);
            CollectionAssert.AreEqual(new[] { "Exit:A:to:B", "Enter:B:from:A" }, CallLog.Entries);

            // D is never registered by TestFlowBehaviour: the adapter must not swallow or re-wrap the
            // machine's own validation.
            Assert.Throws<StateConfigurationException>(() => host.ChangeState(TestState.D));
        }

        /// <summary>
        /// <c>RestartState</c> runs the machine's restart, which is an exit/enter pair against the
        /// current state and leaves current/previous untouched.
        /// </summary>
        [Test]
        public void Behaviour_RestartState_DelegatesToMachine()
        {
            var host = CreateHost();
            host.InvokeAwake();
            host.InvokeStart();
            CallLog.Clear();

            host.RestartState();

            CollectionAssert.AreEqual(new[] { "Exit:A:to:A", "Enter:A:from:A" }, CallLog.Entries);
            Assert.AreEqual(TestState.A, host.MachineForTests.CurrentStateType);

            // Restart is not a transition, so it must not have recorded a previous state.
            Assert.IsNull(host.MachineForTests.PreviousStateType);

            var uninitialized = CreateHost();
            uninitialized.InvokeAwake();

            Assert.Throws<StateConfigurationException>(() => uninitialized.RestartState());
        }

        /// <summary>
        /// The behaviour's current/previous properties read straight through to the machine, both
        /// before the first transition (previous is null) and after one.
        /// </summary>
        [Test]
        public void Behaviour_CurrentAndPreviousProperties_MirrorMachine()
        {
            var host = CreateHost();
            host.InvokeAwake();
            host.InvokeStart();
            var machine = host.MachineForTests;

            Assert.AreEqual(machine.CurrentStateType, host.CurrentStateType);
            Assert.AreSame(machine.CurrentState, host.CurrentState);
            Assert.IsNull(host.PreviousStateType);
            Assert.IsNull(host.PreviousState);

            host.ChangeState(TestState.B);

            Assert.AreEqual(TestState.B, host.CurrentStateType);
            Assert.AreEqual(machine.CurrentStateType, host.CurrentStateType);
            Assert.AreSame(machine.CurrentState, host.CurrentState);

            Assert.AreEqual(TestState.A, host.PreviousStateType);
            Assert.AreEqual(machine.PreviousStateType, host.PreviousStateType);
            Assert.AreSame(machine.PreviousState, host.PreviousState);
        }

        /// <summary>
        /// <c>CanChangeState</c> answers from the machine's transition table, including for the pairs
        /// the table closes: reverse-only, self-transition and unregistered-state.
        /// </summary>
        [Test]
        public void Behaviour_CanChangeState_DelegatesToMachine()
        {
            var host = CreateHost();
            host.InvokeAwake();

            // Asked before Start as well: the machine's query is pure, so the adapter must answer
            // from an empty table rather than adding a lifecycle guard of its own.
            Assert.DoesNotThrow(() => host.CanChangeState(TestState.A, TestState.B));

            host.InvokeStart();
            var machine = host.MachineForTests;

            Assert.IsTrue(host.CanChangeState(TestState.A, TestState.B));
            Assert.IsTrue(host.CanChangeState(TestState.B, TestState.A));
            Assert.IsFalse(host.CanChangeState(TestState.A, TestState.A));
            Assert.IsFalse(host.CanChangeState(TestState.A, TestState.D));

            Assert.AreEqual(
                machine.CanChangeState(TestState.A, TestState.B),
                host.CanChangeState(TestState.A, TestState.B));
            Assert.AreEqual(
                machine.CanChangeState(TestState.A, TestState.A),
                host.CanChangeState(TestState.A, TestState.A));
        }

        // ---- Event forwarding ----------------------------------------------

        /// <summary>
        /// A handler added through the behaviour lands on the machine's own <c>StateEntered</c> —
        /// proved by transitioning the machine directly — and removing it reaches the machine too.
        /// </summary>
        [Test]
        public void Behaviour_StateEntered_ForwardsMachineEvent()
        {
            var host = CreateHost();
            host.InvokeAwake();
            host.InvokeStart();
            CallLog.Clear();

            host.StateEntered += RecordEntered;

            host.MachineForTests.ChangeState(TestState.B);

            // Entered trails the new state's own hook, and carries (from, to).
            CollectionAssert.AreEqual(
                new[] { "Exit:A:to:B", "Enter:B:from:A", "Handler:Entered:A:to:B" },
                CallLog.Entries);

            host.StateEntered -= RecordEntered;
            CallLog.Clear();

            host.ChangeState(TestState.A);

            CollectionAssert.AreEqual(new[] { "Exit:B:to:A", "Enter:A:from:B" }, CallLog.Entries);
        }

        /// <summary>
        /// The same for <c>StateExited</c>, which additionally pins the forwarded event's position:
        /// between the old state's exit hook and the new state's entry.
        /// </summary>
        [Test]
        public void Behaviour_StateExited_ForwardsMachineEvent()
        {
            var host = CreateHost();
            host.InvokeAwake();
            host.InvokeStart();
            CallLog.Clear();

            host.StateExited += RecordExited;

            host.MachineForTests.ChangeState(TestState.B);

            CollectionAssert.AreEqual(
                new[] { "Exit:A:to:B", "Handler:Exited:A:to:B", "Enter:B:from:A" },
                CallLog.Entries);

            host.StateExited -= RecordExited;
            CallLog.Clear();

            host.ChangeState(TestState.A);

            CollectionAssert.AreEqual(new[] { "Exit:B:to:A", "Enter:A:from:B" }, CallLog.Entries);
        }

        // ---- Typed manager -------------------------------------------------

        /// <summary>
        /// A state closed over the host behaviour sees that exact behaviour instance as its manager,
        /// and can move the machine through it from its update hook.
        /// </summary>
        [Test]
        public void Behaviour_TypedState_SeesBehaviourAsManagerAndDrivesTransitions()
        {
            var host = CreateHost<TypedFlowBehaviour>();

            // Nothing is attached until the configuration hooks run, which is what makes the
            // reference check below a statement about registration rather than about construction.
            Assert.IsNull(host.StateA.ObservedManager);

            host.InvokeAwake();
            host.InvokeStart();

            // The behaviour, not the machine it delegates to: states added from a behaviour's hooks
            // are attached to the host so they can be authored against it.
            Assert.AreSame(host, host.StateA.ObservedManager);
            Assert.AreSame(host, host.StateB.ObservedManager);
            Assert.AreNotSame(host.MachineForTests, host.StateA.ObservedManager);

            CallLog.Clear();

            host.InvokeUpdate();

            Assert.AreEqual(TestState.B, host.CurrentStateType);
            CollectionAssert.AreEqual(
                new[] { "Update:A", "Exit:A:to:B", "Enter:B:from:A" },
                CallLog.Entries);
        }

        /// <summary>
        /// A state closed over <see cref="IStateManager{TState}"/> attaches to a behaviour as readily
        /// as to a plain machine, and sees the behaviour as its manager.
        /// </summary>
        [Test]
        public void Behaviour_InterfaceTypedState_AttachesWithBehaviourAsManager()
        {
            var host = CreateHost();
            var shared = new AnyManagerState(TestState.C);

            // Registering from outside also proves AddState hands the behaviour back, so external
            // chains keep targeting the host rather than the machine behind it.
            Assert.AreSame(host, host.AddState(shared));

            host.InvokeAwake();
            host.InvokeStart();

            Assert.AreSame(host, shared.ObservedManager);

            // C registered first, but the host's own hook names A: external registration combines
            // with the hooks without overriding them.
            Assert.AreEqual(TestState.A, host.CurrentStateType);
        }

        /// <summary>
        /// The behaviour is a complete <see cref="IStateManager{TState}"/>: a caller holding only the
        /// interface can configure nothing yet still initialize, transition and tick it.
        /// </summary>
        [Test]
        public void Behaviour_AsInterface_DrivesFullLifecycle()
        {
            var host = CreateHost();
            IStateManager<TestState> manager = host;

            Assert.IsFalse(manager.IsInitialized);

            manager.Initialize();

            Assert.IsTrue(manager.IsInitialized);
            Assert.AreEqual(TestState.A, manager.CurrentStateType);
            Assert.IsNull(manager.PreviousStateType);
            Assert.IsTrue(manager.CanChangeState(TestState.A, TestState.B));

            manager.ChangeState(TestState.B);

            Assert.AreEqual(TestState.B, manager.CurrentStateType);
            Assert.AreEqual(TestState.A, manager.PreviousStateType);

            manager.Tick();

            CollectionAssert.AreEqual(
                new[] { "Enter:A:from:A", "Exit:A:to:B", "Enter:B:from:A", "Update:B" },
                CallLog.Entries);

            // The interface-typed calls reached the behaviour's own machine rather than some copy.
            Assert.AreSame(host.MachineForTests.CurrentState, manager.CurrentState);
        }

        // ---- Self-configuration --------------------------------------------

        /// <summary>
        /// Initializing a behaviour runs its own <c>OnInitialize</c> then its own
        /// <c>InsertTransitions</c>, once each, and a second initialization is refused.
        /// </summary>
        [Test]
        public void Behaviour_Initialize_InvokesOwnHooksInOrderOnce()
        {
            var host = CreateHost<SelfConfiguringBehaviour>();

            host.InvokeAwake();
            CollectionAssert.IsEmpty(host.HookLog, "Awake must not configure the machine.");

            host.InvokeStart();

            CollectionAssert.AreEqual(
                new[]
                {
                    SelfConfiguringBehaviour.OnInitializeEntry,
                    SelfConfiguringBehaviour.InsertTransitionsEntry
                },
                host.HookLog);

            // A host only configures itself once, so the rejected second call must not re-run either
            // hook and hand the states dictionary a duplicate registration.
            Assert.Throws<StateConfigurationException>(() => host.Initialize());
            Assert.AreEqual(2, host.HookLog.Count);
        }

        /// <summary>
        /// Without <c>SetInitialState</c>, a behaviour enters the first state its hook registered.
        /// </summary>
        [Test]
        public void Behaviour_GetInitialState_DefaultsToFirstRegisteredState()
        {
            // B registers first while A is the enum's default value, so a host that fell back to
            // default(TState) instead of the first registration would fail here.
            var host = CreateHost<DefaultInitialBehaviour>();

            host.InvokeAwake();
            host.InvokeStart();

            Assert.AreEqual(TestState.B, host.CurrentStateType);
            CollectionAssert.AreEqual(new[] { "Enter:B:from:B" }, CallLog.Entries);
        }

        /// <summary>
        /// <c>SetInitialState</c> called from inside a behaviour's <c>OnInitialize</c> picks the
        /// state <c>Initialize</c> enters.
        /// </summary>
        [Test]
        public void Behaviour_SetInitialStateInOnInitialize_EntersDeclaredState()
        {
            var host = CreateHost<SelfConfiguringBehaviour>();

            host.InvokeAwake();
            host.InvokeStart();

            // C is neither the enum's default nor the first state registered, so only the hook's own
            // choice explains it.
            Assert.AreEqual(TestState.C, host.CurrentStateType);
            CollectionAssert.AreEqual(new[] { "Enter:C:from:C" }, CallLog.Entries);
        }

        /// <summary>
        /// The pairs a behaviour declares in its own <c>InsertTransitions</c> are the pairs its
        /// machine enforces — in that direction only.
        /// </summary>
        [Test]
        public void Behaviour_InsertTransitions_DeclaredPairsAreEnforced()
        {
            var host = CreateHost<SelfConfiguringBehaviour>();

            host.InvokeAwake();
            host.InvokeStart();
            CallLog.Clear();

            Assert.IsTrue(host.CanChangeState(TestState.C, TestState.B));
            Assert.IsFalse(host.CanChangeState(TestState.B, TestState.C));
            Assert.IsFalse(host.CanChangeState(TestState.C, TestState.A));

            host.ChangeState(TestState.B);

            Assert.AreEqual(TestState.B, host.CurrentStateType);
            CollectionAssert.AreEqual(new[] { "Exit:C:to:B", "Enter:B:from:C" }, CallLog.Entries);

            Assert.Throws<InvalidTransitionException>(() => host.ChangeState(TestState.C));
        }
    }
}
