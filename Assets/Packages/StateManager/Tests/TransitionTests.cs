using System;
using NUnit.Framework;

namespace UnityEssentials.States.Tests
{
    /// <summary>
    /// Covers <c>ChangeState</c> and the transition table behind it: permitted and denied moves,
    /// self-transitions and <c>AllowAny</c>, changes requested mid-transition, and what a throwing
    /// entry hook leaves behind.
    /// </summary>
    [TestFixture]
    public class TransitionTests
    {
        // The single entry Initialize leaves behind when the machine starts on a recording A.
        private const string InitialEntryOfA = "Enter:A:from:A";

        [SetUp]
        public void SetUp()
        {
            CallLog.Clear();
        }

        // D is deliberately never registered — the fixture's stand-in for a state nothing has heard of.
        private static BaseStateManager<TestState> AbcMachine(params (TestState from, TestState to)[] pairs)
        {
            var machine = new PlainStateManager(pairs);
            machine.AddState(TestState.A, new RecordingState())
                .AddState(TestState.B, new RecordingState())
                .AddState(TestState.C, new RecordingState());

            return machine;
        }

        private static BaseStateManager<TestState> AbcMachineAllowingAll()
        {
            var machine = PlainStateManager.AllowingAll();
            machine.AddState(TestState.A, new RecordingState())
                .AddState(TestState.B, new RecordingState())
                .AddState(TestState.C, new RecordingState());

            return machine;
        }

        [Test]
        public void ChangeState_AllowedTransition_UpdatesCurrentStateAndType()
        {
            var b = new RecordingState();
            var machine = new PlainStateManager((TestState.A, TestState.B));
            machine.AddState(TestState.A, new RecordingState())
                .AddState(TestState.B, b)
                .SetInitialState(TestState.A);
            machine.Initialize();

            machine.ChangeState(TestState.B);

            Assert.AreEqual(TestState.B, machine.CurrentStateType);
            Assert.AreSame(b, machine.CurrentState);
        }

        [Test]
        public void ChangeState_AllowedTransition_UpdatesPreviousStateAndType()
        {
            var a = new RecordingState();
            var machine = new PlainStateManager((TestState.A, TestState.B));
            machine.AddState(TestState.A, a)
                .AddState(TestState.B, new RecordingState())
                .SetInitialState(TestState.A);
            machine.Initialize();

            Assert.IsNull(machine.PreviousStateType);
            Assert.IsNull(machine.PreviousState);

            machine.ChangeState(TestState.B);

            Assert.AreEqual(TestState.A, machine.PreviousStateType);
            Assert.AreSame(a, machine.PreviousState);
        }

        [Test]
        public void ChangeState_ChainOfTransitions_TracksPreviousStateEachStep()
        {
            var a = new RecordingState();
            var b = new RecordingState();
            var c = new RecordingState();
            var machine = new PlainStateManager(
                (TestState.A, TestState.B),
                (TestState.B, TestState.C),
                (TestState.C, TestState.A));
            machine.AddState(TestState.A, a)
                .AddState(TestState.B, b)
                .AddState(TestState.C, c)
                .SetInitialState(TestState.A);
            machine.Initialize();

            machine.ChangeState(TestState.B);
            Assert.AreEqual(TestState.A, machine.PreviousStateType);
            Assert.AreSame(a, machine.PreviousState);

            machine.ChangeState(TestState.C);
            Assert.AreEqual(TestState.B, machine.PreviousStateType);
            Assert.AreSame(b, machine.PreviousState);

            // Closing the loop back to A: previous must follow the last hop, not the first.
            machine.ChangeState(TestState.A);
            Assert.AreEqual(TestState.C, machine.PreviousStateType);
            Assert.AreSame(c, machine.PreviousState);
        }

        [Test]
        public void ChangeState_WithNoConfiguredTransitions_ThrowsInvalidTransitionException()
        {
            var machine = AbcMachine();
            machine.SetInitialState(TestState.A);
            machine.Initialize();

            Assert.Throws<InvalidTransitionException>(() => machine.ChangeState(TestState.B));
        }

        [Test]
        public void ChangeState_DisallowedPair_ThrowsInvalidTransitionException()
        {
            // A does have an outgoing pair — the rejection has to be per-pair, not per-source.
            var machine = AbcMachine((TestState.A, TestState.B));
            machine.SetInitialState(TestState.A);
            machine.Initialize();

            Assert.Throws<InvalidTransitionException>(() => machine.ChangeState(TestState.C));
        }

        [Test]
        public void ChangeState_ReverseOfAllowedPair_ThrowsInvalidTransitionException()
        {
            var machine = AbcMachine((TestState.A, TestState.B));
            machine.SetInitialState(TestState.A);
            machine.Initialize();

            machine.ChangeState(TestState.B);

            Assert.Throws<InvalidTransitionException>(() => machine.ChangeState(TestState.A));
        }

        [Test]
        public void ChangeState_DisallowedPair_StateAndEventsUnchanged()
        {
            var machine = AbcMachine((TestState.A, TestState.B));
            machine.SetInitialState(TestState.A);
            machine.Initialize();

            var exitedFired = false;
            var enteredFired = false;
            machine.StateExited += (from, to) => exitedFired = true;
            machine.StateEntered += (from, to) => enteredFired = true;

            Assert.Throws<InvalidTransitionException>(() => machine.ChangeState(TestState.C));

            // Validation runs before any hook, so in particular there is no "Exit:A:to:C".
            CollectionAssert.AreEqual(new[] { InitialEntryOfA }, CallLog.Entries);
            Assert.IsFalse(exitedFired);
            Assert.IsFalse(enteredFired);
            Assert.AreEqual(TestState.A, machine.CurrentStateType);
            Assert.IsNull(machine.PreviousStateType);
        }

        [Test]
        public void ChangeState_UnregisteredTargetState_ThrowsStateConfigurationException()
        {
            var machine = AbcMachine((TestState.A, TestState.B));
            machine.SetInitialState(TestState.A);
            machine.Initialize();

            // D is registered nowhere, which is a broken machine rather than a denied request.
            Assert.Throws<StateConfigurationException>(() => machine.ChangeState(TestState.D));
        }

        [Test]
        public void ChangeState_SelfTransitionWithoutTableEntry_ThrowsInvalidTransitionException()
        {
            var machine = AbcMachine((TestState.A, TestState.B));
            machine.SetInitialState(TestState.A);
            machine.Initialize();

            Assert.Throws<InvalidTransitionException>(() => machine.ChangeState(TestState.A));
        }

        [Test]
        public void ChangeState_SelfTransitionWithTableEntry_ExitsAndReenters()
        {
            var machine = AbcMachine((TestState.A, TestState.A));
            machine.SetInitialState(TestState.A);
            machine.Initialize();

            machine.ChangeState(TestState.A);

            // A declared self-transition is an ordinary transition: a full exit/enter pair.
            CollectionAssert.AreEqual(
                new[] { InitialEntryOfA, "Exit:A:to:A", "Enter:A:from:A" },
                CallLog.Entries);
        }

        [Test]
        public void ChangeState_SelfTransition_SetsPreviousStateToSameType()
        {
            var a = new RecordingState();
            var machine = new PlainStateManager((TestState.A, TestState.A));
            machine.AddState(TestState.A, a)
                .AddState(TestState.B, new RecordingState())
                .SetInitialState(TestState.A);
            machine.Initialize();

            machine.ChangeState(TestState.A);

            Assert.AreEqual(TestState.A, machine.CurrentStateType);
            Assert.AreEqual(TestState.A, machine.PreviousStateType);
            Assert.AreSame(a, machine.PreviousState);
            Assert.AreSame(a, machine.CurrentState);
        }

        [Test]
        public void AllowAny_PermitsEveryPairIncludingSelf()
        {
            var machine = AbcMachineAllowingAll();
            machine.SetInitialState(TestState.A);
            machine.Initialize();

            var states = new[] { TestState.A, TestState.B, TestState.C };

            // The positioning hop is itself a self-transition whenever the machine already sits on
            // `from`, so the diagonal gets exercised from both directions.
            foreach (var from in states)
            {
                foreach (var to in states)
                {
                    machine.ChangeState(from);
                    Assert.AreEqual(from, machine.CurrentStateType);

                    Assert.IsTrue(machine.CanChangeState(from, to));
                    machine.ChangeState(to);
                    Assert.AreEqual(to, machine.CurrentStateType);
                }
            }
        }

        [Test]
        public void AllowAny_UnregisteredTarget_StillThrowsStateConfigurationException()
        {
            var machine = AbcMachineAllowingAll();
            machine.SetInitialState(TestState.A);
            machine.Initialize();

            // The table answers yes for D as it does for everything; registration is the check that
            // still stands, and it belongs to the machine rather than the table.
            Assert.IsTrue(machine.CanChangeState(TestState.A, TestState.D));
            Assert.Throws<StateConfigurationException>(() => machine.ChangeState(TestState.D));
        }

        [Test]
        public void CanChangeState_AllowedPair_ReturnsTrue()
        {
            var machine = AbcMachine((TestState.A, TestState.B));

            // Answers before Initialize without throwing, and the answer is no until then because
            // the pair is only declared once the hook has run.
            Assert.IsFalse(machine.CanChangeState(TestState.A, TestState.B));

            machine.SetInitialState(TestState.A);
            machine.Initialize();

            Assert.IsTrue(machine.CanChangeState(TestState.A, TestState.B));
        }

        [Test]
        public void CanChangeState_DisallowedPair_ReturnsFalse()
        {
            var machine = AbcMachine((TestState.A, TestState.B));
            machine.SetInitialState(TestState.A);
            machine.Initialize();

            // A to B is open, so these falses are the table answering rather than an empty one.
            Assert.IsTrue(machine.CanChangeState(TestState.A, TestState.B));

            Assert.IsFalse(machine.CanChangeState(TestState.B, TestState.A));
            Assert.IsFalse(machine.CanChangeState(TestState.A, TestState.A));
            Assert.IsFalse(machine.CanChangeState(TestState.A, TestState.C));
            Assert.IsFalse(machine.CanChangeState(TestState.A, TestState.D));
        }

        [Test]
        public void ChangeState_FromEnterState_DefersUntilTransitionCompletes()
        {
            var machine = new PlainStateManager(
                (TestState.A, TestState.B),
                (TestState.B, TestState.C));
            machine.AddState(TestState.A, new RecordingState())
                .AddState(TestState.B, new ChangeOnEnterState(TestState.C))
                .AddState(TestState.C, new RecordingState())
                .SetInitialState(TestState.A);
            machine.Initialize();

            machine.StateEntered += (from, to) => CallLog.Record($"Entered:{from}:to:{to}");

            machine.ChangeState(TestState.B);

            // The move to C ran after the move to B completed, StateEntered included: the entry
            // hook's request was deferred, not nested. B is the probe and records nothing itself.
            Assert.AreEqual(TestState.C, machine.CurrentStateType);
            Assert.AreEqual(TestState.B, machine.PreviousStateType);
            CollectionAssert.AreEqual(
                new[]
                {
                    InitialEntryOfA,
                    "Exit:A:to:B",
                    "Entered:A:to:B",
                    "Enter:C:from:B",
                    "Entered:B:to:C"
                },
                CallLog.Entries);
        }

        [Test]
        public void ChangeState_SecondRequestFromSameEntry_ThrowsStateManagerException()
        {
            var machine = new PlainStateManager(
                (TestState.A, TestState.B),
                (TestState.B, TestState.A),
                (TestState.B, TestState.C));
            machine.AddState(TestState.A, new RecordingState())
                .AddState(TestState.B, new DoubleChangeOnEnterState(TestState.A, TestState.C))
                .AddState(TestState.C, new RecordingState())
                .SetInitialState(TestState.A);
            machine.Initialize();

            // Both requests name permitted pairs, so only the one-deferral-per-transition rule can
            // reject the second.
            var exception = Assert.Throws<StateManagerException>(() => machine.ChangeState(TestState.B));
            Assert.IsInstanceOf<StateManagerException>(exception.InnerException);
            StringAssert.Contains("already deferred", exception.Message);

            // The first deferral died with the failed entry: the machine sits on B and stays there.
            Assert.AreEqual(TestState.B, machine.CurrentStateType);
        }

        [Test]
        public void ChangeState_DeferredChainNeverSettles_ThrowsStateManagerException()
        {
            // B and C defer into each other forever; the machine must turn that cycle into an
            // exception rather than spin inside one ChangeState call.
            var machine = new PlainStateManager(
                (TestState.A, TestState.B),
                (TestState.B, TestState.C),
                (TestState.C, TestState.B));
            machine.AddState(TestState.A, new RecordingState())
                .AddState(TestState.B, new ChangeOnEnterState(TestState.C))
                .AddState(TestState.C, new ChangeOnEnterState(TestState.B))
                .SetInitialState(TestState.A);
            machine.Initialize();

            var exception = Assert.Throws<StateManagerException>(() => machine.ChangeState(TestState.B));
            StringAssert.Contains("exceeded", exception.Message);
        }

        [Test]
        public void ChangeState_DeferredTargetNotAllowed_ThrowsInvalidTransitionInsideWrapper()
        {
            // B's entry hook requests A, but B -> A is never declared: the deferral validates at the
            // call site, so the rejection escapes the hook and reaches the caller wrapped.
            var machine = new PlainStateManager((TestState.A, TestState.B));
            machine.AddState(TestState.A, new RecordingState())
                .AddState(TestState.B, new ChangeOnEnterState(TestState.A))
                .SetInitialState(TestState.A);
            machine.Initialize();

            var exception = Assert.Throws<StateManagerException>(() => machine.ChangeState(TestState.B));
            Assert.IsInstanceOf<InvalidTransitionException>(exception.InnerException);
            Assert.AreEqual(TestState.B, machine.CurrentStateType);
        }

        [Test]
        public void ChangeState_FromExitState_ThrowsStateManagerException()
        {
            var machine = new PlainStateManager(
                (TestState.A, TestState.B),
                (TestState.A, TestState.C));
            machine.AddState(TestState.A, new ChangeOnExitState(TestState.C))
                .AddState(TestState.B, new RecordingState())
                .AddState(TestState.C, new RecordingState())
                .SetInitialState(TestState.A);
            machine.Initialize();

            Assert.IsTrue(machine.CanChangeState(TestState.A, TestState.C));
            Assert.Throws<StateManagerException>(() => machine.ChangeState(TestState.B));

            // Exit runs before the swap, so the rejected re-entrant call abandons the outer
            // transition with the machine still on A and B never entered.
            Assert.AreEqual(TestState.A, machine.CurrentStateType);
            Assert.IsNull(machine.PreviousStateType);
            CollectionAssert.IsEmpty(CallLog.Entries);
        }

        [Test]
        public void ChangeState_FromStateEnteredHandler_DefersUntilTransitionCompletes()
        {
            var machine = AbcMachine((TestState.A, TestState.B), (TestState.B, TestState.C));
            machine.SetInitialState(TestState.A);
            machine.Initialize();
            CallLog.Clear();

            // Subscribed after Initialize so the initial entry does not fire it, and conditional on B
            // because it fires for the deferred move to C as well.
            machine.StateEntered += (from, to) =>
            {
                if (to == TestState.B)
                {
                    machine.ChangeState(TestState.C);
                }
            };

            machine.ChangeState(TestState.B);

            Assert.AreEqual(TestState.C, machine.CurrentStateType);
            Assert.AreEqual(TestState.B, machine.PreviousStateType);
            CollectionAssert.AreEqual(
                new[] { "Exit:A:to:B", "Enter:B:from:A", "Exit:B:to:C", "Enter:C:from:B" },
                CallLog.Entries);
        }

        [Test]
        public void ChangeState_EnterStateThrows_MachineEndsInTargetState()
        {
            var machine = new PlainStateManager((TestState.A, TestState.B));
            machine.AddState(TestState.A, new RecordingState())
                .AddState(TestState.B, new ThrowingEnterState())
                .SetInitialState(TestState.A);
            machine.Initialize();

            var exception = Assert.Throws<StateManagerException>(
                () => machine.ChangeState(TestState.B));

            StringAssert.Contains("EnterState hook", exception.Message);
            Assert.IsInstanceOf<InvalidOperationException>(exception.InnerException);
            Assert.AreEqual(ThrowingEnterState.FailureMessage, exception.InnerException.Message);

            // No rollback by design: the swap happened before EnterState, and unwinding it would run
            // ExitState for setup that never completed.
            Assert.AreEqual(TestState.B, machine.CurrentStateType);
            Assert.AreEqual(TestState.A, machine.PreviousStateType);
        }

        [Test]
        public void ChangeState_EnterStateThrows_MachineRemainsUsable()
        {
            var machine = new PlainStateManager(
                (TestState.A, TestState.B),
                (TestState.B, TestState.A));
            machine.AddState(TestState.A, new RecordingState())
                .AddState(TestState.B, new ThrowingEnterState())
                .SetInitialState(TestState.A);
            machine.Initialize();

            Assert.Throws<StateManagerException>(() => machine.ChangeState(TestState.B));

            machine.ChangeState(TestState.A);

            Assert.AreEqual(TestState.A, machine.CurrentStateType);
            Assert.AreEqual(TestState.B, machine.PreviousStateType);

            // The recovery hop running its hooks normally is the observable proof that the guard was
            // released on the way out of the failed transition.
            CollectionAssert.AreEqual(
                new[] { InitialEntryOfA, "Exit:A:to:B", "Enter:A:from:B" },
                CallLog.Entries);
        }
    }
}
