using System;
using NUnit.Framework;

namespace UnityEssentials.States.Tests
{
    /// <summary>
    /// Covers <c>ChangeState</c> and the transition table behind it: what a permitted move updates,
    /// what a denied move must leave untouched, how self-transitions and <c>AllowAny</c> behave, how
    /// a change requested mid-transition is deferred or rejected, and what a throwing entry hook
    /// leaves behind.
    /// </summary>
    [TestFixture]
    public class TransitionTests
    {
        // The single entry Initialize leaves behind when the machine starts on a recording A: entry
        // is passed the initial state as its own previous. Log assertions start from this.
        private const string InitialEntryOfA = "Enter:A:from:A";

        [SetUp]
        public void SetUp()
        {
            CallLog.Clear();
        }

        // ---- Fixture helpers ----------------------------------------------

        // Recording states for A, B and C, declaring `pairs` when initialized, with no initial state
        // chosen yet. Tests that compare against a specific state instance build their machine
        // inline instead, since the instances created here are not handed back. D is deliberately
        // never registered — it is the fixture's stand-in for a state nothing has heard of.
        private static BaseStateManager<TestState> AbcMachine(params (TestState from, TestState to)[] pairs)
        {
            var machine = new PlainStateManager(pairs);
            machine.AddState(new RecordingState(TestState.A))
                .AddState(new RecordingState(TestState.B))
                .AddState(new RecordingState(TestState.C));

            return machine;
        }

        // The same three-state machine, with every pair opened by AllowAny instead of a declared list.
        private static BaseStateManager<TestState> AbcMachineAllowingAll()
        {
            var machine = PlainStateManager.AllowingAll();
            machine.AddState(new RecordingState(TestState.A))
                .AddState(new RecordingState(TestState.B))
                .AddState(new RecordingState(TestState.C));

            return machine;
        }

        // ---- Allowed transitions ------------------------------------------

        [Test]
        public void ChangeState_AllowedTransition_UpdatesCurrentStateAndType()
        {
            var b = new RecordingState(TestState.B);
            var machine = new PlainStateManager((TestState.A, TestState.B));
            machine.AddState(new RecordingState(TestState.A))
                .AddState(b)
                .SetInitialState(TestState.A);
            machine.Initialize();

            machine.ChangeState(TestState.B);

            Assert.AreEqual(TestState.B, machine.CurrentStateType);
            Assert.AreSame(b, machine.CurrentState);
        }

        [Test]
        public void ChangeState_AllowedTransition_UpdatesPreviousStateAndType()
        {
            var a = new RecordingState(TestState.A);
            var machine = new PlainStateManager((TestState.A, TestState.B));
            machine.AddState(a)
                .AddState(new RecordingState(TestState.B))
                .SetInitialState(TestState.A);
            machine.Initialize();

            // Null before the first transition is the documented "never moved" sentinel, so the
            // assertion after the move only means something once this one has held.
            Assert.IsNull(machine.PreviousStateType);
            Assert.IsNull(machine.PreviousState);

            machine.ChangeState(TestState.B);

            Assert.AreEqual(TestState.A, machine.PreviousStateType);
            Assert.AreSame(a, machine.PreviousState);
        }

        [Test]
        public void ChangeState_ChainOfTransitions_TracksPreviousStateEachStep()
        {
            var a = new RecordingState(TestState.A);
            var b = new RecordingState(TestState.B);
            var c = new RecordingState(TestState.C);
            var machine = new PlainStateManager(
                (TestState.A, TestState.B),
                (TestState.B, TestState.C),
                (TestState.C, TestState.A));
            machine.AddState(a)
                .AddState(b)
                .AddState(c)
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

        // ---- Denied transitions -------------------------------------------

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

            // Validation runs before any hook, so the log must still hold nothing but the initial
            // entry — in particular no "Exit:A:to:C".
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

            // D is registered nowhere, which is a broken machine rather than a denied request —
            // hence the configuration exception in place of InvalidTransitionException.
            Assert.Throws<StateConfigurationException>(() => machine.ChangeState(TestState.D));
        }

        // ---- Self-transitions ---------------------------------------------

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

            // A declared self-transition is an ordinary transition: full exit/enter pair, both hooks
            // naming A on either side of the swap.
            CollectionAssert.AreEqual(
                new[] { InitialEntryOfA, "Exit:A:to:A", "Enter:A:from:A" },
                CallLog.Entries);
        }

        [Test]
        public void ChangeState_SelfTransition_SetsPreviousStateToSameType()
        {
            var a = new RecordingState(TestState.A);
            var machine = new PlainStateManager((TestState.A, TestState.A));
            machine.AddState(a)
                .AddState(new RecordingState(TestState.B))
                .SetInitialState(TestState.A);
            machine.Initialize();

            machine.ChangeState(TestState.A);

            Assert.AreEqual(TestState.A, machine.CurrentStateType);
            Assert.AreEqual(TestState.A, machine.PreviousStateType);
            Assert.AreSame(a, machine.PreviousState);
            Assert.AreSame(a, machine.CurrentState);
        }

        // ---- Blanket permission -------------------------------------------

        [Test]
        public void AllowAny_PermitsEveryPairIncludingSelf()
        {
            var machine = AbcMachineAllowingAll();
            machine.SetInitialState(TestState.A);
            machine.Initialize();

            var states = new[] { TestState.A, TestState.B, TestState.C };

            // Walking every ordered pair. The positioning hop is itself a self-transition whenever the
            // machine already sits on `from`, so the diagonal gets exercised from both directions.
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

        // ---- Transition queries -------------------------------------------

        [Test]
        public void CanChangeState_AllowedPair_ReturnsTrue()
        {
            var machine = AbcMachine((TestState.A, TestState.B));

            // A pure query that never throws: it answers before Initialize as readily as after,
            // which is what makes it usable from UI code deciding whether to offer a button. The
            // answer is no until then, because the pair is only declared once the hook has run.
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

            // Unregistered states are not special-cased: the table simply never opened the pair.
            Assert.IsFalse(machine.CanChangeState(TestState.A, TestState.D));
        }

        // ---- Re-entrancy --------------------------------------------------

        [Test]
        public void ChangeState_FromEnterState_DefersUntilTransitionCompletes()
        {
            var machine = new PlainStateManager(
                (TestState.A, TestState.B),
                (TestState.B, TestState.C));
            machine.AddState(new RecordingState(TestState.A))
                .AddState(new ChangeOnEnterState(TestState.B, TestState.C))
                .AddState(new RecordingState(TestState.C))
                .SetInitialState(TestState.A);
            machine.Initialize();

            machine.StateEntered += (from, to) => CallLog.Record($"Entered:{from}:to:{to}");

            machine.ChangeState(TestState.B);

            // B's entry hook requested C, which ran as its own full transition — after the move to B
            // completed, StateEntered included: the entry hook's request was deferred, not nested.
            // B is the probe, which records nothing itself, so its own hooks leave no entries.
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
            machine.AddState(new RecordingState(TestState.A))
                .AddState(new DoubleChangeOnEnterState(TestState.B, TestState.A, TestState.C))
                .AddState(new RecordingState(TestState.C))
                .SetInitialState(TestState.A);
            machine.Initialize();

            // Both of the hook's requests name pairs the table permits, so the one-deferral-per-
            // transition rule is the only thing that can reject the second. Its rejection escapes
            // the entry hook and comes back wrapped by the transition that ran it.
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
            // exception rather than spin inside one ChangeState call for the rest of the frame.
            var machine = new PlainStateManager(
                (TestState.A, TestState.B),
                (TestState.B, TestState.C),
                (TestState.C, TestState.B));
            machine.AddState(new RecordingState(TestState.A))
                .AddState(new ChangeOnEnterState(TestState.B, TestState.C))
                .AddState(new ChangeOnEnterState(TestState.C, TestState.B))
                .SetInitialState(TestState.A);
            machine.Initialize();

            var exception = Assert.Throws<StateManagerException>(() => machine.ChangeState(TestState.B));
            StringAssert.Contains("exceeded", exception.Message);
        }

        [Test]
        public void ChangeState_DeferredTargetNotAllowed_ThrowsInvalidTransitionInsideWrapper()
        {
            // B's entry hook requests A, but B -> A is never declared. The deferral validates at the
            // call site, so the entry hook is what the InvalidTransitionException escapes from — and
            // it reaches the caller wrapped by the transition that ran the hook.
            var machine = new PlainStateManager((TestState.A, TestState.B));
            machine.AddState(new RecordingState(TestState.A))
                .AddState(new ChangeOnEnterState(TestState.B, TestState.A))
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
            machine.AddState(new ChangeOnExitState(TestState.A, TestState.C))
                .AddState(new RecordingState(TestState.B))
                .AddState(new RecordingState(TestState.C))
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

            // Subscribed after Initialize so the handler is not fired by the initial entry, and
            // conditional on B because it fires for the deferred move to C as well.
            machine.StateEntered += (from, to) =>
            {
                if (to == TestState.B)
                {
                    machine.ChangeState(TestState.C);
                }
            };

            machine.ChangeState(TestState.B);

            // The handler runs after the entry hook, so the move to B is complete in the log before
            // the deferred move to C begins.
            Assert.AreEqual(TestState.C, machine.CurrentStateType);
            Assert.AreEqual(TestState.B, machine.PreviousStateType);
            CollectionAssert.AreEqual(
                new[] { "Exit:A:to:B", "Enter:B:from:A", "Exit:B:to:C", "Enter:C:from:B" },
                CallLog.Entries);
        }

        // ---- Entry failures -----------------------------------------------

        [Test]
        public void ChangeState_EnterStateThrows_MachineEndsInTargetState()
        {
            var machine = new PlainStateManager((TestState.A, TestState.B));
            machine.AddState(new RecordingState(TestState.A))
                .AddState(new ThrowingEnterState(TestState.B))
                .SetInitialState(TestState.A);
            machine.Initialize();

            var exception = Assert.Throws<StateManagerException>(
                () => machine.ChangeState(TestState.B));

            // The hook's failure is reported, not swallowed and not replaced: the wrapper names the
            // stage that failed, and the hook's own exception is kept underneath it intact.
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
            machine.AddState(new RecordingState(TestState.A))
                .AddState(new ThrowingEnterState(TestState.B))
                .SetInitialState(TestState.A);
            machine.Initialize();

            Assert.Throws<StateManagerException>(() => machine.ChangeState(TestState.B));

            machine.ChangeState(TestState.A);

            Assert.AreEqual(TestState.A, machine.CurrentStateType);
            Assert.AreEqual(TestState.B, machine.PreviousStateType);

            // The recovery hop ran its hooks normally, which is the observable proof that the guard
            // was released by the finally block on the way out of the failed transition.
            CollectionAssert.AreEqual(
                new[] { InitialEntryOfA, "Exit:A:to:B", "Enter:A:from:B" },
                CallLog.Entries);
        }
    }
}
