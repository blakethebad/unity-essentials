using System;
using NUnit.Framework;

namespace UnityEssentials.States.Tests
{
    /// <summary>
    /// Pins the observable sequence of a single <c>ChangeState</c>: which hooks and events run, in
    /// which order, with which arguments, and what the machine reports about itself from inside each
    /// handler.
    /// </summary>
    [TestFixture]
    public class EventOrderingTests
    {
        [SetUp]
        public void SetUp()
        {
            CallLog.Clear();
        }

        // Three states rather than two so the hook arguments are falsifiable: with only A and B, "the
        // exit hook received the next state" cannot be told from "it received the only other state".
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

        [Test]
        public void ExitState_ReceivesNextStateType()
        {
            var machine = CreateInitializedMachine();

            // C rather than the conventional B, so a hook echoing a defaulted value would be caught.
            machine.ChangeState(TestState.C);

            Assert.AreEqual(2, CallLog.Entries.Count);
            Assert.AreEqual("Exit:A:to:C", CallLog.Entries[0]);
        }

        [Test]
        public void EnterState_ReceivesPreviousStateType()
        {
            var machine = CreateInitializedMachine();

            // Two hops, because after a single transition from the initial state "the previous state"
            // and "the initial state" are the same value.
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

            machine.ChangeState(TestState.C);

            Assert.AreEqual(TestState.A, observedFrom);
            Assert.AreEqual(TestState.C, observedTo);
        }

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

        [Test]
        public void StateExited_HandlerObservesOldStateAsCurrent()
        {
            var machine = CreateInitializedMachine();

            // The explicit flag matters: PreviousStateType is legitimately null here, so a handler
            // that never ran would make the assertion pass vacuously.
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
            Assert.AreEqual(TestState.B, machine.CurrentStateType);
        }

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

        [Test]
        public void StateEntered_RemovedHandler_IsNotInvoked()
        {
            var machine = CreateInitializedMachine();

            Action<TestState, TestState> handler = (from, to) => CallLog.Record($"Entered:{from}:to:{to}");
            machine.StateEntered += handler;

            machine.ChangeState(TestState.B);

            machine.StateEntered -= handler;

            machine.ChangeState(TestState.A);

            // Both transitions in one sequence, so this proves a removal rather than a subscription
            // that never took.
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
