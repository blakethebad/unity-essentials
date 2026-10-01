using System;
using NUnit.Framework;

namespace UnityEssentials.States.Tests
{
    /// <summary>
    /// Pins the observable sequence of a single <c>ChangeState</c>: which hooks and events run, in
    /// which order, with which arguments, and what the machine reports about itself from inside each
    /// handler. Assertions compare whole sequences, since ordering is the contract callers build on.
    /// </summary>
    [TestFixture]
    public class EventOrderingTests
    {
        [SetUp]
        public void SetUp()
        {
            CallLog.Clear();
        }

        // ---- Fixture setup ------------------------------------------------

        // Three states rather than two so the hook arguments are falsifiable: with only A and B, "the
        // exit hook received the next state" is indistinguishable from "the exit hook received the
        // only other state there is". The log is cleared after Initialize, whose own entry would
        // otherwise head every sequence below; subscribing later keeps handlers off the initial entry.
        private static BaseStateManager<TestState> CreateInitializedMachine()
        {
            var machine = new PlainStateManager(
                (TestState.A, TestState.B),
                (TestState.B, TestState.A),
                (TestState.A, TestState.C),
                (TestState.C, TestState.A),
                (TestState.B, TestState.C));

            machine.AddState(TestState.A, new RecordingState())
                .AddState(TestState.B, new RecordingState())
                .AddState(TestState.C, new RecordingState())
                .SetInitialState(TestState.A);

            machine.Initialize();
            CallLog.Clear();

            return machine;
        }

        // ---- Hook ordering ------------------------------------------------

        /// <summary>
        /// The state being left runs its exit hook before the state being entered runs its entry
        /// hook — the ordering that lets two states hand a shared resource over cleanly.
        /// </summary>
        [Test]
        public void ChangeState_CallsExitStateBeforeEnterState()
        {
            var machine = CreateInitializedMachine();

            machine.ChangeState(TestState.B);

            var exitIndex = CallLog.Entries.IndexOf("Exit:A:to:B");
            var enterIndex = CallLog.Entries.IndexOf("Enter:B:from:A");

            Assert.AreNotEqual(-1, exitIndex, "ExitState was never called on the state being left.");
            Assert.AreNotEqual(-1, enterIndex, "EnterState was never called on the state being entered.");
            Assert.Less(exitIndex, enterIndex);
        }

        /// <summary>
        /// The full documented sequence of one transition: exit hook, <c>StateExited</c>, entry hook,
        /// <c>StateEntered</c> — asserted as an exact list.
        /// </summary>
        [Test]
        public void ChangeState_OrderIsExitThenExitedEventThenEnterThenEnteredEvent()
        {
            var machine = CreateInitializedMachine();
            machine.StateExited += (from, to) => CallLog.Record($"Exited:{from}:to:{to}");
            machine.StateEntered += (from, to) => CallLog.Record($"Entered:{from}:to:{to}");

            machine.ChangeState(TestState.B);

            CollectionAssert.AreEqual(
                new[]
                {
                    "Exit:A:to:B",
                    "Exited:A:to:B",
                    "Enter:B:from:A",
                    "Entered:A:to:B"
                },
                CallLog.Entries);
        }

        // ---- Hook arguments -----------------------------------------------

        /// <summary>
        /// The exit hook receives the state about to be entered, so a state can vary its teardown by
        /// destination.
        /// </summary>
        [Test]
        public void ExitState_ReceivesNextStateType()
        {
            var machine = CreateInitializedMachine();

            // C rather than the conventional B, so a hook echoing a hard-coded or defaulted value
            // would be caught.
            machine.ChangeState(TestState.C);

            Assert.AreEqual(2, CallLog.Entries.Count);
            Assert.AreEqual("Exit:A:to:C", CallLog.Entries[0]);
        }

        /// <summary>
        /// The entry hook receives the state just left, so a state can vary its setup by origin.
        /// </summary>
        [Test]
        public void EnterState_ReceivesPreviousStateType()
        {
            var machine = CreateInitializedMachine();

            // Two hops: after a single transition from the initial state, "the previous state" and
            // "the initial state" are the same value.
            machine.ChangeState(TestState.B);
            machine.ChangeState(TestState.C);

            CollectionAssert.AreEqual(
                new[]
                {
                    "Exit:A:to:B",
                    "Enter:B:from:A",
                    "Exit:B:to:C",
                    "Enter:C:from:B"
                },
                CallLog.Entries);
        }

        // ---- Event arguments ----------------------------------------------

        /// <summary>
        /// <c>StateExited</c> is raised with <c>(from, to)</c> in that order — the source first, the
        /// destination second.
        /// </summary>
        [Test]
        public void StateExited_ReceivesFromAndToArguments()
        {
            var machine = CreateInitializedMachine();

            TestState? observedFrom = null;
            TestState? observedTo = null;
            machine.StateExited += (from, to) =>
            {
                observedFrom = from;
                observedTo = to;
            };

            // A and C are deliberately different values, so a transposed pair fails rather than
            // passing by coincidence.
            machine.ChangeState(TestState.C);

            Assert.AreEqual(TestState.A, observedFrom);
            Assert.AreEqual(TestState.C, observedTo);
        }

        /// <summary>
        /// <c>StateEntered</c> is raised with the same <c>(from, to)</c> pair, not with the
        /// destination twice, so an entry-only handler can still tell where the machine came from.
        /// </summary>
        [Test]
        public void StateEntered_ReceivesFromAndToArguments()
        {
            var machine = CreateInitializedMachine();

            TestState? observedFrom = null;
            TestState? observedTo = null;
            machine.StateEntered += (from, to) =>
            {
                observedFrom = from;
                observedTo = to;
            };

            machine.ChangeState(TestState.C);

            Assert.AreEqual(TestState.A, observedFrom);
            Assert.AreEqual(TestState.C, observedTo);
        }

        // ---- What handlers observe ----------------------------------------

        /// <summary>
        /// <c>StateExited</c> fires before the swap, so a handler still sees the state being left as
        /// current — and, on a first transition, still sees no previous state at all.
        /// </summary>
        [Test]
        public void StateExited_HandlerObservesOldStateAsCurrent()
        {
            var machine = CreateInitializedMachine();

            // The explicit flag matters: PreviousStateType is legitimately null here, so a handler
            // that never ran would produce the same null and the assertion would pass vacuously.
            var handlerRan = false;
            TestState currentInsideHandler = TestState.D;
            TestState? previousInsideHandler = null;
            machine.StateExited += (from, to) =>
            {
                handlerRan = true;
                currentInsideHandler = machine.CurrentStateType;
                previousInsideHandler = machine.PreviousStateType;
            };

            machine.ChangeState(TestState.B);

            Assert.IsTrue(handlerRan);
            Assert.AreEqual(TestState.A, currentInsideHandler);
            Assert.IsNull(previousInsideHandler);

            // The swap is only deferred, not skipped: it must have happened by the time the call returns.
            Assert.AreEqual(TestState.B, machine.CurrentStateType);
        }

        /// <summary>
        /// <c>StateEntered</c> fires after the swap and after the entry hook, so a handler sees a
        /// finished transition: current is the destination and previous is the source.
        /// </summary>
        [Test]
        public void StateEntered_HandlerObservesCompletedTransition()
        {
            var machine = CreateInitializedMachine();

            var handlerRan = false;
            TestState currentInsideHandler = TestState.D;
            TestState? previousInsideHandler = null;
            machine.StateEntered += (from, to) =>
            {
                handlerRan = true;
                currentInsideHandler = machine.CurrentStateType;
                previousInsideHandler = machine.PreviousStateType;
            };

            machine.ChangeState(TestState.B);

            Assert.IsTrue(handlerRan);
            Assert.AreEqual(TestState.B, currentInsideHandler);
            Assert.AreEqual(TestState.A, previousInsideHandler);
        }

        // ---- Subscription edges -------------------------------------------

        /// <summary>
        /// A transition on a machine nobody is listening to completes normally — raising an event
        /// against an empty invocation list must not throw or skip the hooks.
        /// </summary>
        [Test]
        public void ChangeState_WithNoSubscribers_Succeeds()
        {
            var machine = CreateInitializedMachine();

            Assert.DoesNotThrow(() => machine.ChangeState(TestState.B));

            Assert.AreEqual(TestState.B, machine.CurrentStateType);
            CollectionAssert.AreEqual(
                new[]
                {
                    "Exit:A:to:B",
                    "Enter:B:from:A"
                },
                CallLog.Entries);
        }

        /// <summary>
        /// Unsubscribing from <c>StateEntered</c> actually detaches the handler: it runs for the
        /// transition it was subscribed for and not for the one after removal.
        /// </summary>
        [Test]
        public void StateEntered_RemovedHandler_IsNotInvoked()
        {
            var machine = CreateInitializedMachine();

            Action<TestState, TestState> handler = (from, to) => CallLog.Record($"Entered:{from}:to:{to}");
            machine.StateEntered += handler;

            machine.ChangeState(TestState.B);

            machine.StateEntered -= handler;

            machine.ChangeState(TestState.A);

            // Both transitions in one sequence, so the test proves a removal rather than a
            // subscription that never took.
            CollectionAssert.AreEqual(
                new[]
                {
                    "Exit:A:to:B",
                    "Enter:B:from:A",
                    "Entered:A:to:B",
                    "Exit:B:to:A",
                    "Enter:A:from:B"
                },
                CallLog.Entries);
        }
    }
}
