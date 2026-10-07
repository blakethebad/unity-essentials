using NUnit.Framework;

namespace UnityEssentials.States.Tests
{
    /// <summary>
    /// Covers <c>Tick</c>: which state a tick dispatches to, what a tick is allowed to do to the
    /// machine, and where ticking is refused outright. Sequences include the initial <c>Enter:</c>
    /// entry, so an unexpected extra hook fails a test instead of being discarded.
    /// </summary>
    [TestFixture]
    public class TickTests
    {
        [SetUp]
        public void SetUp()
        {
            CallLog.Clear();
        }

        [Test]
        public void Tick_InvokesUpdateStateOnCurrentState()
        {
            var machine = new PlainStateManager();
            machine.AddState(TestState.A, new RecordingState());
            machine.Initialize();

            machine.Tick();

            CollectionAssert.AreEqual(
                new[] { "Enter:A:from:A", "Update:A" },
                CallLog.Entries);
        }

        [Test]
        public void Tick_AfterTransition_InvokesUpdateStateOnNewState()
        {
            var machine = new PlainStateManager((TestState.A, TestState.B));
            machine.AddState(TestState.A, new RecordingState())
                .AddState(TestState.B, new RecordingState());
            machine.Initialize();

            machine.ChangeState(TestState.B);
            machine.Tick();

            // Tick reads the current state at call time, so this is what would catch a stale
            // captured reference.
            CollectionAssert.AreEqual(
                new[] { "Enter:A:from:A", "Exit:A:to:B", "Enter:B:from:A", "Update:B" },
                CallLog.Entries);
            Assert.AreEqual(TestState.B, machine.CurrentStateType);
        }

        [Test]
        public void Tick_StateWithDefaultUpdateState_Succeeds()
        {
            var machine = new PlainStateManager();
            machine.AddState(TestState.A, new SilentState());
            machine.Initialize();

            Assert.DoesNotThrow(() => machine.Tick());

            // SilentState overrides nothing, so an empty log means the inherited default ran and did
            // nothing — CurrentStateType pins that dispatch was not skipped.
            Assert.AreEqual(0, CallLog.Entries.Count);
            Assert.AreEqual(TestState.A, machine.CurrentStateType);
        }

        [Test]
        public void ChangeState_FromUpdateState_Succeeds()
        {
            var machine = new PlainStateManager((TestState.A, TestState.B));
            machine.AddState(TestState.A, new ChangeOnUpdateState(TestState.B))
                .AddState(TestState.B, new RecordingState());
            machine.Initialize();

            // The contrast with Tick_CalledFromEnterState is the point: the re-entrancy guard is
            // deliberately not held across a tick.
            Assert.DoesNotThrow(() => machine.Tick());

            CollectionAssert.AreEqual(
                new[] { "Update:A", "Enter:B:from:A" },
                CallLog.Entries);
            Assert.AreEqual(TestState.B, machine.CurrentStateType);
        }

        [Test]
        public void Tick_StateChangedDuringUpdateState_DoesNotUpdateNewStateInSameCall()
        {
            var machine = new PlainStateManager((TestState.A, TestState.B));
            machine.AddState(TestState.A, new ChangeOnUpdateState(TestState.B))
                .AddState(TestState.B, new RecordingState());
            machine.Initialize();

            machine.Tick();

            CollectionAssert.AreEqual(
                new[] { "Update:A", "Enter:B:from:A" },
                CallLog.Entries);
            CollectionAssert.DoesNotContain(CallLog.Entries, "Update:B");

            // The second tick too: the new state must be merely deferred, not skipped.
            machine.Tick();

            CollectionAssert.AreEqual(
                new[] { "Update:A", "Enter:B:from:A", "Update:B" },
                CallLog.Entries);
        }

        [Test]
        public void Tick_CalledFromEnterState_ThrowsStateManagerException()
        {
            var machine = new PlainStateManager((TestState.A, TestState.B));
            machine.AddState(TestState.A, new RecordingState())
                .AddState(TestState.B, new TickOnEnterState());
            machine.Initialize();

            var exception = Assert.Throws<StateManagerException>(() => machine.ChangeState(TestState.B));

            // Both guarded members share one message template, so only the call that was actually
            // refused names itself.
            StringAssert.Contains("Tick()", exception.Message);

            // Exit ran and nothing followed it: the throw came from the entry hook mid-transition.
            CollectionAssert.AreEqual(
                new[] { "Enter:A:from:A", "Exit:A:to:B" },
                CallLog.Entries);
        }
    }
}
