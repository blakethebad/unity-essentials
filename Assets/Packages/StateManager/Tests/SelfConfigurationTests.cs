using System;
using NUnit.Framework;

namespace UnityEssentials.States.Tests
{
    /// <summary>
    /// Covers the configuration hooks <c>Initialize</c> invokes on a plain
    /// <see cref="BaseStateManager{TState}"/>: their order and count, what they may declare through
    /// <see cref="Transitions{TState}"/>, and how a failing or re-entrant hook is reported.
    /// </summary>
    [TestFixture]
    public class SelfConfigurationTests
    {
        [SetUp]
        public void SetUp()
        {
            CallLog.Clear();
        }

        [Test]
        public void Initialize_InvokesOnInitializeThenInsertTransitions_Once()
        {
            var machine = new HookStateManager(AddAb);

            machine.Initialize();

            CollectionAssert.AreEqual(
                new[] { HookStateManager.OnInitializeEntry, HookStateManager.InsertTransitionsEntry },
                machine.HookLog);

            // The rejected second call must not re-run either hook and hand the states dictionary a
            // duplicate registration.
            Assert.Throws<StateConfigurationException>(() => machine.Initialize());
            Assert.AreEqual(2, machine.HookLog.Count);
        }

        [Test]
        public void Initialize_StatesAddedInOnInitialize_EntersFirstRegisteredState()
        {
            // B registers first while A is the enum's default, so a machine that ignored the hook's
            // registrations and fell back to default(TState) would fail here.
            var machine = new HookStateManager(m =>
            {
                m.AddState(TestState.B, new RecordingState())
                    .AddState(TestState.A, new RecordingState());
            });

            machine.Initialize();

            Assert.IsTrue(machine.IsInitialized);
            Assert.AreEqual(TestState.B, machine.CurrentStateType);
            CollectionAssert.AreEqual(new[] { "Enter:B:from:B" }, CallLog.Entries);
        }

        [Test]
        public void Initialize_SetInitialStateInOnInitialize_EntersDeclaredState()
        {
            var machine = new HookStateManager(m =>
            {
                m.AddState(TestState.A, new RecordingState())
                    .AddState(TestState.B, new RecordingState())
                    .AddState(TestState.C, new RecordingState())
                    .SetInitialState(TestState.C);
            });

            machine.Initialize();

            Assert.AreEqual(TestState.C, machine.CurrentStateType);
            CollectionAssert.AreEqual(new[] { "Enter:C:from:C" }, CallLog.Entries);
        }

        [Test]
        public void InsertTransitions_DeclaredPair_ChangeStateSucceeds()
        {
            // Also pins the hook order from the other side: A and B are registered by OnInitialize,
            // so declaring the pair would throw if InsertTransitions ran first.
            var machine = new HookStateManager(
                AddAb,
                delegate(in Transitions<TestState> transitions)
                {
                    transitions.Allow(TestState.A, TestState.B);
                });

            machine.Initialize();

            Assert.IsTrue(machine.CanChangeState(TestState.A, TestState.B));

            machine.ChangeState(TestState.B);

            Assert.AreEqual(TestState.B, machine.CurrentStateType);
            CollectionAssert.AreEqual(
                new[] { "Enter:A:from:A", "Exit:A:to:B", "Enter:B:from:A" },
                CallLog.Entries);
        }

        [Test]
        public void InsertTransitions_UndeclaredPair_ThrowsInvalidTransitionException()
        {
            var machine = new HookStateManager(
                AddAb,
                delegate(in Transitions<TestState> transitions)
                {
                    transitions.Allow(TestState.A, TestState.B);
                });

            machine.Initialize();
            machine.ChangeState(TestState.B);

            Assert.IsFalse(machine.CanChangeState(TestState.B, TestState.A));
            Assert.Throws<InvalidTransitionException>(() => machine.ChangeState(TestState.A));
        }

        [Test]
        public void InsertTransitions_AllowFanOut_PermitsEveryTarget()
        {
            var machine = new HookStateManager(
                AddAbc,
                delegate(in Transitions<TestState> transitions)
                {
                    transitions.Allow(TestState.A, TestState.B, TestState.C);
                });

            machine.Initialize();

            Assert.IsTrue(machine.CanChangeState(TestState.A, TestState.B));
            Assert.IsTrue(machine.CanChangeState(TestState.A, TestState.C));

            // Fan-out names one source, so nothing gained a way back, sideways, or onto itself.
            Assert.IsFalse(machine.CanChangeState(TestState.B, TestState.A));
            Assert.IsFalse(machine.CanChangeState(TestState.B, TestState.C));
            Assert.IsFalse(machine.CanChangeState(TestState.A, TestState.A));
        }

        [Test]
        public void InsertTransitions_AllowAny_PermitsEveryPair()
        {
            var machine = new HookStateManager(
                AddAbc,
                delegate(in Transitions<TestState> transitions)
                {
                    transitions.AllowAny();
                });

            machine.Initialize();

            var states = new[] { TestState.A, TestState.B, TestState.C };
            foreach (var from in states)
            {
                foreach (var to in states)
                {
                    Assert.IsTrue(machine.CanChangeState(from, to), $"{from} to {to} must be allowed.");
                }
            }

            machine.ChangeState(TestState.C);
            Assert.AreEqual(TestState.C, machine.CurrentStateType);
        }

        [Test]
        public void InsertTransitions_ChainedAllows_DeclareEveryPair()
        {
            var machine = new HookStateManager(
                AddAbc,
                delegate(in Transitions<TestState> transitions)
                {
                    transitions.Allow(TestState.A, TestState.B)
                        .Allow(TestState.B, TestState.C)
                        .Allow(TestState.C, TestState.A);
                });

            machine.Initialize();

            machine.ChangeState(TestState.B);
            machine.ChangeState(TestState.C);
            machine.ChangeState(TestState.A);

            Assert.AreEqual(TestState.A, machine.CurrentStateType);

            // The whole chain landed, and only the chain: the reverse of each hop is still closed.
            Assert.IsFalse(machine.CanChangeState(TestState.B, TestState.A));
            Assert.IsFalse(machine.CanChangeState(TestState.C, TestState.B));
        }

        [Test]
        public void InsertTransitions_AllowWithUnregisteredState_ThrowsStateConfigurationException()
        {
            var machine = new HookStateManager(
                AddAb,
                delegate(in Transitions<TestState> transitions)
                {
                    transitions.Allow(TestState.A, TestState.D);
                });

            Assert.Throws<StateConfigurationException>(() => machine.Initialize());

            Assert.IsFalse(machine.IsInitialized);
            CollectionAssert.IsEmpty(CallLog.Entries);
        }

        [Test]
        public void Initialize_OnInitializeThrows_PropagatesUnchanged()
        {
            var failure = new InvalidOperationException("OnInitialize failed on purpose");
            var machine = new HookStateManager(m => { throw failure; });

            // Assert.Throws matches the type exactly, so a StateManagerException wrapper would fail
            // here before the identity check below could.
            var thrown = Assert.Throws<InvalidOperationException>(() => machine.Initialize());

            Assert.AreSame(failure, thrown);
            Assert.IsFalse(machine.IsInitialized);

            // The second hook never ran: a failed OnInitialize abandons configuration outright.
            CollectionAssert.AreEqual(new[] { HookStateManager.OnInitializeEntry }, machine.HookLog);
        }

        [Test]
        public void Initialize_InsertTransitionsThrows_PropagatesUnchanged()
        {
            var failure = new InvalidOperationException("InsertTransitions failed on purpose");
            var machine = new HookStateManager(
                AddAb,
                delegate(in Transitions<TestState> transitions)
                {
                    throw failure;
                });

            var thrown = Assert.Throws<InvalidOperationException>(() => machine.Initialize());

            Assert.AreSame(failure, thrown);
            Assert.IsFalse(machine.IsInitialized);
            CollectionAssert.IsEmpty(CallLog.Entries);

            // No state was entered, so the machine is still in its configuration phase.
            Assert.IsNull(machine.CurrentState);
            Assert.Throws<StateConfigurationException>(() => machine.ChangeState(TestState.B));
        }

        [Test]
        public void Initialize_CalledFromOnInitialize_ThrowsStateConfigurationException()
        {
            var machine = new HookStateManager(m =>
            {
                m.AddState(TestState.A, new RecordingState());
                m.Initialize();
            });

            var thrown = Assert.Throws<StateConfigurationException>(() => machine.Initialize());

            StringAssert.Contains("Configuration hooks must only configure the machine.", thrown.Message);
            Assert.IsFalse(machine.IsInitialized);
            CollectionAssert.IsEmpty(CallLog.Entries);
        }

        [Test]
        public void Initialize_CalledFromInsertTransitions_ThrowsStateConfigurationException()
        {
            HookStateManager machine = null;
            machine = new HookStateManager(
                AddAb,
                delegate(in Transitions<TestState> transitions)
                {
                    machine.Initialize();
                });

            var thrown = Assert.Throws<StateConfigurationException>(() => machine.Initialize());

            StringAssert.Contains("Configuration hooks must only configure the machine.", thrown.Message);
            Assert.IsFalse(machine.IsInitialized);
            CollectionAssert.IsEmpty(CallLog.Entries);
        }

        [Test]
        public void Initialize_HooksConfigureNothing_ThrowsStateConfigurationException()
        {
            var machine = new HookStateManager();

            Assert.Throws<StateConfigurationException>(() => machine.Initialize());

            Assert.IsFalse(machine.IsInitialized);

            // The empty-states check runs after the hooks, which is the whole reason a derived
            // manager may register its states from OnInitialize.
            CollectionAssert.AreEqual(
                new[] { HookStateManager.OnInitializeEntry, HookStateManager.InsertTransitionsEntry },
                machine.HookLog);
        }

        [Test]
        public void Initialize_ExternalStateRegistration_CombinesWithHookConfiguration()
        {
            var machine = new HookStateManager(
                m => m.AddState(TestState.C, new RecordingState()),
                delegate(in Transitions<TestState> transitions)
                {
                    // A and B are registered externally, below: a hook declares transitions over
                    // every state the machine has by then, not only over the ones it added itself.
                    transitions.Allow(TestState.A, TestState.B)
                        .Allow(TestState.B, TestState.C);
                });

            machine.AddState(TestState.A, new RecordingState())
                .AddState(TestState.B, new RecordingState())
                .SetInitialState(TestState.A);

            machine.Initialize();

            Assert.AreEqual(TestState.A, machine.CurrentStateType);

            machine.ChangeState(TestState.B);
            machine.ChangeState(TestState.C);

            Assert.AreEqual(TestState.C, machine.CurrentStateType);
            CollectionAssert.AreEqual(
                new[]
                {
                    "Enter:A:from:A",
                    "Exit:A:to:B",
                    "Enter:B:from:A",
                    "Exit:B:to:C",
                    "Enter:C:from:B"
                },
                CallLog.Entries);
        }

        private static void AddAb(HookStateManager machine)
        {
            machine.AddState(TestState.A, new RecordingState())
                .AddState(TestState.B, new RecordingState());
        }

        private static void AddAbc(HookStateManager machine)
        {
            machine.AddState(TestState.A, new RecordingState())
                .AddState(TestState.B, new RecordingState())
                .AddState(TestState.C, new RecordingState());
        }
    }
}
