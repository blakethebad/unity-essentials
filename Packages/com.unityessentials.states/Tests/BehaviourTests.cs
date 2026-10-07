using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

namespace UnityEssentials.States.Tests
{
    /// <summary>
    /// Covers <see cref="StateManagerBehaviour{TState}"/>: the Unity lifecycle wiring, every member
    /// that forwards to the hosted machine, and the typed-manager surface. EditMode gives no player
    /// loop, so the hosts' <c>Invoke*</c> methods drive Awake/Start/Update by hand.
    /// </summary>
    [TestFixture]
    public class BehaviourTests
    {
        // EditMode tests share one scene for the whole run, so leaked hosts would accumulate across
        // the fixture and outlive it.
        private readonly List<GameObject> _hosts = new List<GameObject>();

        [SetUp]
        public void SetUp()
        {
            CallLog.Clear();
        }

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

        [Test]
        public void Behaviour_AfterAwakeAndStart_IsInitializedInInitialState()
        {
            var host = CreateHost();

            host.InvokeAwake();
            host.InvokeStart();

            Assert.IsTrue(host.IsInitialized);
            Assert.AreEqual(TestState.A, host.CurrentStateType);

            // Initialize is not a transition: it enters with the initial state as its own "previous"
            // and leaves PreviousStateType null.
            CollectionAssert.AreEqual(new[] { "Enter:A:from:A" }, CallLog.Entries);
            Assert.IsNull(host.PreviousStateType);
        }

        [Test]
        public void Behaviour_AfterAwakeOnly_IsNotConfiguredOrInitialized()
        {
            var host = CreateHost();

            host.InvokeAwake();

            Assert.IsFalse(host.IsInitialized);
            Assert.IsNotNull(host.MachineForTests);
            CollectionAssert.IsEmpty(CallLog.Entries);

            // Configuration happens inside Initialize(), so the pair Start will open is still closed.
            Assert.IsFalse(host.CanChangeState(TestState.A, TestState.B));

            // Null is what actually proves no state was entered: this host's initial state is A,
            // which is also default(TestState).
            Assert.IsNull(host.CurrentState);
            Assert.AreEqual(default(TestState), host.CurrentStateType);
        }

        [Test]
        public void Behaviour_BeforeAwake_EventSubscriptionDoesNotThrow()
        {
            var host = CreateHost();

            // The machine is built in the constructor, so a handler attached from another
            // component's Awake — which may run first — still observes the initial entry.
            Assert.DoesNotThrow(() => host.StateEntered += RecordEntered);

            host.InvokeAwake();
            host.InvokeStart();

            CollectionAssert.AreEqual(
                new[] { "Enter:A:from:A", "Handler:Entered:A:to:A" },
                CallLog.Entries);

            host.StateEntered -= RecordEntered;
        }

        [Test]
        public void Behaviour_Start_InitializesOwnHostedMachine()
        {
            var host = CreateHost();
            var machine = host.MachineForTests;

            Assert.IsNotNull(machine);

            host.InvokeAwake();
            host.InvokeStart();

            // Same instance before and after: Start configured the machine built by the constructor
            // in place, instead of replacing it.
            Assert.AreSame(machine, host.MachineForTests);
            Assert.IsTrue(machine.IsInitialized);
            Assert.AreEqual(TestState.A, machine.CurrentStateType);

            // A shared machine would make two hosts fight over one state registry, and the second
            // Start would throw on duplicate registration.
            var other = CreateHost();
            other.InvokeAwake();
            other.InvokeStart();

            Assert.AreNotSame(machine, other.MachineForTests);
            Assert.IsTrue(other.IsInitialized);
        }

        [Test]
        public void Behaviour_Update_TicksCurrentState()
        {
            var host = CreateHost();
            host.InvokeAwake();
            host.InvokeStart();

            CallLog.Clear();

            host.InvokeUpdate();
            host.InvokeUpdate();

            CollectionAssert.AreEqual(new[] { "Update:A", "Update:A" }, CallLog.Entries);

            host.ChangeState(TestState.B);
            CallLog.Clear();

            host.InvokeUpdate();

            CollectionAssert.AreEqual(new[] { "Update:B" }, CallLog.Entries);
        }

        [Test]
        public void Behaviour_Update_BeforeInitialize_DoesNothing()
        {
            var host = CreateHost();

            // An unguarded forward to Tick() would throw here, so the calls are half the assertion.
            host.InvokeUpdate();
            host.InvokeAwake();
            host.InvokeUpdate();

            Assert.IsFalse(host.IsInitialized);
            CollectionAssert.IsEmpty(CallLog.Entries);
        }

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

        [Test]
        public void Behaviour_StateEntered_ForwardsMachineEvent()
        {
            var host = CreateHost();
            host.InvokeAwake();
            host.InvokeStart();
            CallLog.Clear();

            host.StateEntered += RecordEntered;

            // Transitioning the machine directly is what proves the handler landed on the machine.
            host.MachineForTests.ChangeState(TestState.B);

            CollectionAssert.AreEqual(
                new[] { "Exit:A:to:B", "Enter:B:from:A", "Handler:Entered:A:to:B" },
                CallLog.Entries);

            host.StateEntered -= RecordEntered;
            CallLog.Clear();

            host.ChangeState(TestState.A);

            CollectionAssert.AreEqual(new[] { "Exit:B:to:A", "Enter:A:from:B" }, CallLog.Entries);
        }

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

        [Test]
        public void Behaviour_TypedState_SeesBehaviourAsManagerAndDrivesTransitions()
        {
            var host = CreateHost<TypedFlowBehaviour>();

            // Nothing is attached until the configuration hooks run, which makes the check below a
            // statement about registration rather than about construction.
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

        [Test]
        public void Behaviour_InterfaceTypedState_AttachesWithBehaviourAsManager()
        {
            var host = CreateHost();
            var shared = new AnyManagerState();

            // Registering from outside also proves AddState hands the behaviour back, so external
            // chains keep targeting the host rather than the machine behind it.
            Assert.AreSame(host, host.AddState(TestState.C, shared));

            host.InvokeAwake();
            host.InvokeStart();

            Assert.AreSame(host, shared.ObservedManager);

            // C registered first, but the host's own hook names A: external registration combines
            // with the hooks without overriding them.
            Assert.AreEqual(TestState.A, host.CurrentStateType);
        }

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

        [Test]
        public void Behaviour_Initialize_InvokesOwnHookOnce()
        {
            var host = CreateHost<SelfConfiguringBehaviour>();

            host.InvokeAwake();
            CollectionAssert.IsEmpty(host.HookLog, "Awake must not configure the machine.");

            host.InvokeStart();

            CollectionAssert.AreEqual(
                new[] { SelfConfiguringBehaviour.OnInitializeEntry },
                host.HookLog);

            // The rejected second call must not re-run the hook and hand the states dictionary a
            // duplicate registration.
            Assert.Throws<StateConfigurationException>(() => host.Initialize());
            Assert.AreEqual(1, host.HookLog.Count);
        }

        [Test]
        public void Behaviour_GetInitialState_DefaultsToFirstRegisteredState()
        {
            // B registers first while A is the enum's default, so a host that fell back to
            // default(TState) instead of the first registration would fail here.
            var host = CreateHost<DefaultInitialBehaviour>();

            host.InvokeAwake();
            host.InvokeStart();

            Assert.AreEqual(TestState.B, host.CurrentStateType);
            CollectionAssert.AreEqual(new[] { "Enter:B:from:B" }, CallLog.Entries);
        }

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

        [Test]
        public void Behaviour_DeclaredTransitions_AreEnforced()
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
