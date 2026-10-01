using NUnit.Framework;

namespace UnityEssentials.States.Tests
{
    /// <summary>
    /// Covers <c>Tick</c>: which state a tick dispatches to, what a tick is allowed to do to the
    /// machine, and where ticking is refused outright.
    /// </summary>
    [TestFixture]
    public class TickTests
    {
        // Assertions go over CallLog rather than CurrentStateType alone, because the properties at
        // stake are how many hooks ran and in which order — a machine can end up on the right state
        // having updated the wrong one. Sequences include the initial Enter: entry rather than
        // clearing the log, so an unexpected extra hook fails a test instead of being discarded.

        [SetUp]
        public void SetUp()
        {
            CallLog.Clear();
        }

        // ---- Dispatch -----------------------------------------------------

        /// <summary>
        /// A tick runs the update hook on the state the machine is currently in, exactly once, and
        /// touches no other hook.
        /// </summary>
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

        /// <summary>
        /// After a transition, ticks follow the machine: the state that was left stops updating and
        /// the state that was entered starts.
        /// </summary>
        [Test]
        public void Tick_AfterTransition_InvokesUpdateStateOnNewState()
        {
            var machine = new PlainStateManager((TestState.A, TestState.B));
            machine.AddState(TestState.A, new RecordingState())
                .AddState(TestState.B, new RecordingState());
            machine.Initialize();

            machine.ChangeState(TestState.B);
            machine.Tick();

            // Tick reads the current state at call time rather than caching one, so this is the
            // assertion that would catch a stale captured reference.
            CollectionAssert.AreEqual(
                new[] { "Enter:A:from:A", "Exit:A:to:B", "Enter:B:from:A", "Update:B" },
                CallLog.Entries);
            Assert.AreEqual(TestState.B, machine.CurrentStateType);
        }

        /// <summary>
        /// Ticking a state that never overrode its update hook is a no-op rather than an error, so
        /// states that only care about entry and exit need no boilerplate override.
        /// </summary>
        [Test]
        public void Tick_StateWithDefaultUpdateState_Succeeds()
        {
            var machine = new PlainStateManager();
            machine.AddState(TestState.A, new SilentState());
            machine.Initialize();

            Assert.DoesNotThrow(() => machine.Tick());

            // SilentState overrides nothing, so an empty log is the proof that the inherited default
            // ran and did nothing — not that dispatch was skipped, which CurrentStateType pins.
            Assert.AreEqual(0, CallLog.Entries.Count);
            Assert.AreEqual(TestState.A, machine.CurrentStateType);
        }

        // ---- Transitions driven from a tick -------------------------------

        /// <summary>
        /// The update hook is the sanctioned place to drive the machine: a <c>ChangeState</c> issued
        /// from a tick completes normally.
        /// </summary>
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

            // ChangeOnUpdateState overrides no entry/exit hook, so the log holds only its own update
            // marker and the entry hook of the state it moved the machine to.
            CollectionAssert.AreEqual(
                new[] { "Update:A", "Enter:B:from:A" },
                CallLog.Entries);
            Assert.AreEqual(TestState.B, machine.CurrentStateType);
        }

        /// <summary>
        /// A state entered from inside a tick does not also update on that same tick; its first
        /// update is the following one.
        /// </summary>
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

            // The second tick too: the new state must be merely deferred, not skipped — otherwise a
            // chain of states that each transition on update would run unbounded updates per frame.
            machine.Tick();

            CollectionAssert.AreEqual(
                new[] { "Update:A", "Enter:B:from:A", "Update:B" },
                CallLog.Entries);
        }

        // ---- Re-entrancy --------------------------------------------------

        /// <summary>
        /// Ticking from an entry hook is rejected with a <see cref="StateManagerException"/>, which
        /// surfaces out of the <c>ChangeState</c> call that triggered the entry.
        /// </summary>
        [Test]
        public void Tick_CalledFromEnterState_ThrowsStateManagerException()
        {
            var machine = new PlainStateManager((TestState.A, TestState.B));
            machine.AddState(TestState.A, new RecordingState())
                .AddState(TestState.B, new TickOnEnterState());
            machine.Initialize();

            var exception = Assert.Throws<StateManagerException>(() => machine.ChangeState(TestState.B));

            // What the caller catches is the outer ChangeState wrapping what its entry hook threw,
            // so the origin is pinned by the inner message the wrapper embeds: both guarded members
            // share one template, and only the call that was actually refused names itself.
            StringAssert.Contains("Tick()", exception.Message);

            // Exit ran and nothing followed it: the throw came from the entry hook mid-transition,
            // which is the window the guard exists to cover.
            CollectionAssert.AreEqual(
                new[] { "Enter:A:from:A", "Exit:A:to:B" },
                CallLog.Entries);
        }
    }
}
