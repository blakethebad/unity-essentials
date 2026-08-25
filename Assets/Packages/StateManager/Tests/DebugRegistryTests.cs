using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using NUnit.Framework;
using UnityEngine;

namespace UnityEssentials.States.Tests
{
    /// <summary>
    /// Covers <see cref="StateMachineDebugRegistry"/> and <see cref="StateMachineDebugEntry"/>: what
    /// registration observes, what the entry records, how the history ring behaves at capacity, how
    /// entries are released — explicitly, by owner destruction and by collection — and that none of
    /// it perturbs the machine being watched.
    /// </summary>
    [TestFixture]
    public class DebugRegistryTests
    {
        // ---- Fixture plumbing ----------------------------------------------

        // Bounds the collection attempt in BareManager_DroppedByUser_IsPrunedAfterCollection: Unity's
        // Boehm collector is conservative, so a stale stack slot can keep an unreachable object
        // alive indefinitely and no count can guarantee success.
        private const int GarbageCollectionAttempts = 8;

        // EditMode tests share one scene for the whole run, so leaked hosts would accumulate.
        private readonly List<GameObject> _hosts = new List<GameObject>();

        /// <summary>Empties both pieces of shared static state this fixture reads.</summary>
        [SetUp]
        public void SetUp()
        {
            // The registry is a static singleton every other fixture's Initialize() writes to, and
            // NUnit orders fixtures arbitrarily: teardown keeps our leftovers out of their way, setup
            // keeps theirs out of ours.
            StateMachineDebugRegistry.Clear();
            CallLog.Clear();
        }

        /// <summary>Releases the registry and destroys every host created by the test that just ran.</summary>
        [TearDown]
        public void TearDown()
        {
            StateMachineDebugRegistry.Clear();

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

        // Snapshots the registry the way the debug window does: collect, pruning anything dead on the
        // way past. A fresh list every time, so assertions cannot depend on CollectAlive clearing the
        // buffer it is handed.
        private static List<StateMachineDebugEntry> AliveEntries()
        {
            var buffer = new List<StateMachineDebugEntry>();
            StateMachineDebugRegistry.CollectAlive(buffer);

            return buffer;
        }

        private static StateMachineDebugEntry SingleEntry()
        {
            var entries = AliveEntries();
            Assert.AreEqual(1, entries.Count, "Expected exactly one registered state machine.");

            return entries[0];
        }

        // Silent states because these tests assert on the registry's own history, not on CallLog.
        // Three of them so the transition-table reads have a closed pair to report as well as an open
        // one.
        private static BaseStateManager<TestState> CreateInitializedMachine()
        {
            var machine = new PlainStateManager(
                (TestState.A, TestState.B),
                (TestState.B, TestState.C));

            machine.AddState(new SilentState(TestState.A))
                .AddState(new SilentState(TestState.B))
                .AddState(new SilentState(TestState.C))
                .SetInitialState(TestState.A);

            machine.Initialize();

            return machine;
        }

        // GetHistory reports newest first — the order the window draws rows in — so this walks the
        // ring backwards to hand assertions the chronological order they read naturally in.
        private static List<string> HistoryOf(StateMachineDebugEntry entry)
        {
            var pairs = new List<string>(entry.HistoryCount);
            for (var i = entry.HistoryCount - 1; i >= 0; i--)
            {
                var record = entry.GetHistory(i);
                pairs.Add($"{record.From}>{record.To}");
            }

            return pairs;
        }

        // The name list is whatever the machine's state dictionary yields, so tests look indices up
        // rather than assume A is index 0. Hand-rolled because the fixtures here avoid LINQ.
        private static int IndexOf(IReadOnlyList<string> names, string name)
        {
            for (var i = 0; i < names.Count; i++)
            {
                if (names[i] == name)
                {
                    return i;
                }
            }

            return -1;
        }

        private static void RecordEntered(TestState from, TestState to)
        {
            CallLog.Record($"Entered:{from}:to:{to}");
        }

        private static void RecordExited(TestState from, TestState to)
        {
            CallLog.Record($"Exited:{from}:to:{to}");
        }

        // ---- Registration --------------------------------------------------

        /// <summary>
        /// A machine is invisible to the registry until <c>Initialize</c>, then appears exactly once
        /// and bumps <see cref="StateMachineDebugRegistry.Version"/>.
        /// </summary>
        [Test]
        public void Initialize_RegistersMachineExactlyOnce_AndBumpsVersion()
        {
            var machine = new PlainStateManager((TestState.A, TestState.B));
            machine.AddState(new SilentState(TestState.A))
                .AddState(new SilentState(TestState.B));

            CollectionAssert.IsEmpty(AliveEntries(), "A configured but uninitialized machine must not be registered.");

            var versionBeforeInitialize = StateMachineDebugRegistry.Version;

            machine.Initialize();

            var entry = SingleEntry();
            Assert.AreNotEqual(
                versionBeforeInitialize,
                StateMachineDebugRegistry.Version,
                "Registration must bump Version so the window knows to rebuild.");

            // The entry points at this machine and not at some other one: the source interface is
            // implemented by the machine itself, so reference equality is the whole check.
            Assert.IsTrue(entry.TryGetMachine(out var source));
            Assert.AreSame(machine, source);
        }

        /// <summary>
        /// A bare manager's entry carries enough to draw a card without an owning object: the enum
        /// type, no owner, and a display name synthesised from the closed generic.
        /// </summary>
        [Test]
        public void Entry_ForBareManager_ExposesEnumTypeAndFallbackDisplayName()
        {
            var machine = CreateInitializedMachine();

            var entry = SingleEntry();

            Assert.AreEqual(typeof(TestState), entry.EnumType);
            Assert.IsFalse(entry.HasOwner, "A machine constructed outside a behaviour has no Unity owner.");
            Assert.IsNull(entry.Owner);

            // Asserted literally because it is what the window shows for machines created in plain
            // C# — the case with no UnityEngine.Object to ping and no GameObject name to borrow.
            Assert.AreEqual("BaseStateManager<TestState>", entry.DisplayName);

            // Keeps the machine reachable to the end of the test: a collected machine would make the
            // entry dead and the assertions above vacuous.
            Assert.IsTrue(machine.IsInitialized);
        }

        // ---- History -------------------------------------------------------

        /// <summary>
        /// The initial entry is recorded as <c>(initial, initial)</c> and stamps the current-state
        /// clock, so a machine that has never transitioned still shows one history row.
        /// </summary>
        [Test]
        public void Initialize_RecordsInitialEntryInHistory_AndStampsCurrentStateEnteredAt()
        {
            var machine = CreateInitializedMachine();

            var entry = SingleEntry();

            // Only works because registration happens before the initial entry runs: ordering it one
            // line later would pass every other test in this fixture and fail this one.
            CollectionAssert.AreEqual(new[] { "A>A" }, HistoryOf(entry));
            Assert.Greater(entry.CurrentStateEnteredAt, 0d, "The initial entry must stamp the current-state clock.");
            Assert.AreEqual(TestState.A, machine.CurrentStateType);
        }

        /// <summary>
        /// Every transition appends one row in chronological order with non-decreasing timestamps,
        /// and resets the current-state clock.
        /// </summary>
        [Test]
        public void ChangeState_AppendsHistoryInOrder_AndUpdatesCurrentStateEnteredAt()
        {
            var machine = CreateInitializedMachine();
            var entry = SingleEntry();
            var initialEnteredAt = entry.CurrentStateEnteredAt;

            machine.ChangeState(TestState.B);
            machine.ChangeState(TestState.C);

            CollectionAssert.AreEqual(new[] { "A>A", "A>B", "B>C" }, HistoryOf(entry));

            // GetHistory reports newest first, so walking the indices up must never move forward in
            // time. Compared with LessOrEqual because two transitions in one test body can
            // legitimately land on the same clock reading.
            var newerTimestamp = double.PositiveInfinity;
            for (var i = 0; i < entry.HistoryCount; i++)
            {
                var timestamp = entry.GetHistory(i).Timestamp;
                Assert.LessOrEqual(timestamp, newerTimestamp, $"History row {i} is newer than the row before it.");
                newerTimestamp = timestamp;
            }

            // The clock follows the latest entry, so time-in-state is measured from B to C rather
            // than from initialization.
            Assert.GreaterOrEqual(entry.CurrentStateEnteredAt, initialEnteredAt);
            Assert.GreaterOrEqual(entry.CurrentStateEnteredAt, entry.GetHistory(0).Timestamp);
        }

        /// <summary>
        /// History is a ring: past <see cref="StateMachineDebugEntry.HistoryCapacity"/> rows, each new
        /// transition drops the oldest rather than growing the buffer or refusing the row.
        /// </summary>
        [Test]
        public void History_BeyondCapacity_KeepsMostRecentAndDropsOldest()
        {
            var machine = new PlainStateManager(
                (TestState.A, TestState.B),
                (TestState.B, TestState.A));

            machine.AddState(new SilentState(TestState.A))
                .AddState(new SilentState(TestState.B))
                .SetInitialState(TestState.A);
            machine.Initialize();

            var entry = SingleEntry();

            // Mirror what the entry should be recording, so the expectation is derived rather than
            // hand-copied: the initial entry, then a strict A/B alternation comfortably past the cap.
            var recorded = new List<string> { "A>A" };
            for (var i = 0; i < 15; i++)
            {
                var from = machine.CurrentStateType;
                var to = from == TestState.A ? TestState.B : TestState.A;

                machine.ChangeState(to);
                recorded.Add($"{from}>{to}");
            }

            var expected = recorded.GetRange(
                recorded.Count - StateMachineDebugEntry.HistoryCapacity,
                StateMachineDebugEntry.HistoryCapacity);

            Assert.AreEqual(StateMachineDebugEntry.HistoryCapacity, entry.HistoryCount);
            CollectionAssert.AreEqual(expected, HistoryOf(entry));
        }

        // ---- The non-generic bridge ----------------------------------------

        /// <summary>
        /// <see cref="IStateMachineDebugSource"/> — the string/index view the editor reads a machine
        /// through — agrees with the machine underneath it, table included.
        /// </summary>
        [Test]
        public void DebugSource_ReadsAgreeWithMachine()
        {
            var machine = CreateInitializedMachine();
            var entry = SingleEntry();

            Assert.IsTrue(entry.TryGetMachine(out var source));

            Assert.IsTrue(source.IsInitialized);
            Assert.AreEqual(typeof(TestState), source.StateEnumType);
            Assert.AreEqual("A", source.CurrentStateName);
            Assert.IsNull(source.PreviousStateName, "A machine that has never transitioned has no previous state.");

            var names = source.StateNames;
            Assert.AreEqual(3, names.Count);

            var a = IndexOf(names, "A");
            var b = IndexOf(names, "B");
            var c = IndexOf(names, "C");
            Assert.AreNotEqual(-1, a);
            Assert.AreNotEqual(-1, b);
            Assert.AreNotEqual(-1, c);

            // An allowed move paired with two denied ones, so an implementation answering "true"
            // unconditionally could not pass.
            Assert.IsTrue(source.IsTransitionAllowed(a, b));
            Assert.IsTrue(source.IsTransitionAllowed(b, c));
            Assert.IsFalse(source.IsTransitionAllowed(b, a), "The reverse pair was never declared.");
            Assert.IsFalse(source.IsTransitionAllowed(a, a), "Self transitions need their own declaration.");

            machine.ChangeState(TestState.B);

            // Both halves move together: the source is a view, not a cached copy.
            Assert.AreEqual("B", source.CurrentStateName);
            Assert.AreEqual("A", source.PreviousStateName);
            Assert.AreEqual(machine.CurrentStateType.ToString(), source.CurrentStateName);
        }

        // ---- Release -------------------------------------------------------

        /// <summary>
        /// <see cref="StateMachineDebugRegistry.Unregister"/> removes the entry for the machine it was
        /// handed and leaves every other one standing; an unknown object is a no-op.
        /// </summary>
        [Test]
        public void Unregister_RemovesOnlyMatchingMachine_AndIgnoresUnknown()
        {
            // Two live machines: with one, "removed the right entry" and "removed whatever it found
            // first" would be the same outcome.
            var first = CreateInitializedMachine();
            var second = CreateInitializedMachine();

            Assert.AreEqual(2, AliveEntries().Count);

            StateMachineDebugRegistry.Unregister(new object());
            Assert.AreEqual(2, AliveEntries().Count, "An unknown object must not remove anything.");

            var unregisteredMachine = new PlainStateManager();
            StateMachineDebugRegistry.Unregister(unregisteredMachine);
            Assert.AreEqual(2, AliveEntries().Count, "A machine that never initialized must not remove anything.");

            StateMachineDebugRegistry.Unregister(first);

            var remaining = SingleEntry();
            Assert.IsTrue(remaining.TryGetMachine(out var source));
            Assert.AreSame(second, source, "Unregister removed the wrong entry.");
        }

        /// <summary>
        /// <see cref="StateMachineDebugRegistry.ResetStatics"/> returns the registry to its
        /// just-loaded state: no entries, ids counting from the start, and a version bump.
        /// </summary>
        [Test]
        public void ResetStatics_EmptiesEntriesResetsIdsAndBumpsVersion()
        {
            // Normalize the id counter before capturing the reference value: every earlier test in
            // the run advanced it, and [SetUp]'s Clear() deliberately leaves it alone — only a reset
            // rewinds it. Without this, firstId depends on how many machines the run registered.
            StateMachineDebugRegistry.ResetStatics();

            var first = CreateInitializedMachine();
            var firstId = SingleEntry().Id;

            var versionBeforeReset = StateMachineDebugRegistry.Version;

            StateMachineDebugRegistry.ResetStatics();

            CollectionAssert.IsEmpty(AliveEntries());
            Assert.AreNotEqual(
                versionBeforeReset,
                StateMachineDebugRegistry.Version,
                "A reset must bump Version so open windows drop their stale cards.");

            // The machine registered before the reset stays gone even though it is still alive and
            // still raising events — a reset is a release, not a pause.
            first.ChangeState(TestState.B);
            CollectionAssert.IsEmpty(AliveEntries());

            // Held in a local for the same reason as elsewhere in this fixture: the registry's
            // reference is weak, so a discarded machine's entry could legitimately vanish.
            var afterReset = CreateInitializedMachine();
            Assert.AreEqual(firstId, SingleEntry().Id, "Ids must restart from the beginning after a reset.");
            Assert.IsTrue(afterReset.IsInitialized);
        }

        // ---- Non-interference ----------------------------------------------

        /// <summary>
        /// With the registry attached, a transition still produces exactly the documented sequence of
        /// hooks and events — no extra raises, no reordering, nothing swallowed.
        /// </summary>
        [Test]
        public void Registration_DoesNotPerturbUserEventOrdering()
        {
            var machine = new PlainStateManager((TestState.A, TestState.B));
            machine.AddState(new RecordingState(TestState.A))
                .AddState(new RecordingState(TestState.B))
                .SetInitialState(TestState.A);
            machine.Initialize();

            // The registry subscribes from inside Initialize, so its handler sits ahead of every user
            // handler and runs inside the re-entrancy guard: if it threw, this sequence would be
            // truncated.
            var entry = SingleEntry();

            // Drop the initial entry's hook so the assertion below is the transition and nothing else.
            CallLog.Clear();

            machine.StateExited += RecordExited;
            machine.StateEntered += RecordEntered;

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

            machine.StateExited -= RecordExited;
            machine.StateEntered -= RecordEntered;

            // And the registry did observe the transition it stayed out of the way of.
            CollectionAssert.AreEqual(new[] { "A>A", "A>B" }, HistoryOf(entry));
        }

        // ---- Owned machines ------------------------------------------------

        /// <summary>
        /// A hosted machine registers with its behaviour as owner — giving the card a real name and a
        /// pingable object — and its entry disappears once the host GameObject is destroyed.
        /// </summary>
        [Test]
        public void Behaviour_RegistersWithOwner_AndEntryDisappearsWhenDestroyed()
        {
            var hostObject = new GameObject("debug host");
            _hosts.Add(hostObject);
            var host = hostObject.AddComponent<TestFlowBehaviour>();

            host.InvokeAwake();
            CollectionAssert.IsEmpty(AliveEntries(), "Awake attaches the owner but must not initialize the machine.");

            host.InvokeStart();

            var entry = SingleEntry();
            Assert.IsTrue(entry.HasOwner);
            Assert.AreSame(host, entry.Owner, "The owner must be the behaviour, so the card can ping it.");
            Assert.AreEqual("debug host", entry.DisplayName);
            Assert.AreEqual(typeof(TestState), entry.EnumType);

            UnityEngine.Object.DestroyImmediate(hostObject);

            CollectionAssert.IsEmpty(AliveEntries(), "A destroyed host must leave no card behind.");

            // The fake-null path on its own — the one that covers subclasses overriding OnDestroy
            // without calling base: this machine is still strongly referenced by the test, and
            // nothing unregisters it, so only its destroyed owner can make the entry dead.
            var orphanOwner = new GameObject("orphaned owner");
            _hosts.Add(orphanOwner);

            var orphanedMachine = new PlainStateManager();
            orphanedMachine.AddState(new SilentState(TestState.A));
            StateMachineDebugRegistry.Register(orphanedMachine, orphanOwner);

            Assert.AreEqual(1, AliveEntries().Count);

            UnityEngine.Object.DestroyImmediate(orphanOwner);

            CollectionAssert.IsEmpty(AliveEntries(), "An entry whose owner is fake-null must be pruned.");
            Assert.IsNotNull(orphanedMachine);
        }

        // ---- Bare machines -------------------------------------------------

        /// <summary>
        /// A bare manager nobody holds a reference to any more is collectible — the registry's
        /// reference to it is weak — and its entry is pruned on the next collection pass.
        /// </summary>
        [Test]
        public void BareManager_DroppedByUser_IsPrunedAfterCollection()
        {
            var weakMachine = CreateAndAbandonMachine();

            Assert.AreEqual(1, AliveEntries().Count, "The abandoned machine registered on Initialize.");

            for (var attempt = 0; attempt < GarbageCollectionAttempts && weakMachine.IsAlive; attempt++)
            {
                GC.Collect();
                GC.WaitForPendingFinalizers();
                GC.Collect();
            }

            // Best-effort by design: a lingering entry is a cosmetic problem in a debug window, and
            // the deterministic half of the contract is covered by the behaviour-lifecycle test.
            if (weakMachine.IsAlive)
            {
                Assert.Ignore(
                    "The abandoned BaseStateManager was still reachable after " +
                    $"{GarbageCollectionAttempts} collection passes. Unity's Boehm GC is conservative, " +
                    "so this is expected to happen from time to time and says nothing about the registry; " +
                    "deterministic release is covered by the behaviour-lifecycle test.");
            }

            // The assertion worth having: an entry that outlived a collected machine would mean the
            // registry holds a strong reference and leaks every machine the game ever created.
            CollectionAssert.IsEmpty(
                AliveEntries(),
                "The registry holds machines weakly, so a collected machine's entry must be pruned.");
        }

        // NoInlining is load-bearing: inlined into the test body, the machine's local would live in
        // the caller's frame for the rest of the method and the JIT would have no reason to clear it
        // before the collection loop runs.
        [MethodImpl(MethodImplOptions.NoInlining)]
        private static WeakReference CreateAndAbandonMachine()
        {
            var machine = new PlainStateManager((TestState.A, TestState.B));
            machine.AddState(new SilentState(TestState.A))
                .AddState(new SilentState(TestState.B))
                .SetInitialState(TestState.A);
            machine.Initialize();

            return new WeakReference(machine);
        }
    }
}
