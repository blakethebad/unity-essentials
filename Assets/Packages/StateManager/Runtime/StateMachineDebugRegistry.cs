// Editor-only: the whole file is compiled out of player builds, so the debug window's bookkeeping
// costs a shipped game exactly nothing — no list, no weak references, no event subscription. The
// hook that reaches this code (BaseStateManager<TState>.DebugRegister) is an unimplemented partial
// method outside the Editor, and calls to it are erased by the compiler.
#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using UnityEngine;

namespace UnityEssentials.States
{
    /// <summary>
    /// The non-generic face of a live <see cref="BaseStateManager{TState}"/>, as the debug window needs
    /// to see it: names and indices instead of <typeparamref name="TState"/> values.
    /// </summary>
    /// <remarks>
    /// The window has to display machines closed over different enums side by side, which generic
    /// types cannot express — <c>StateManager&lt;GameState&gt;</c> and <c>StateManager&lt;UiState&gt;</c>
    /// share no common base beyond <see cref="object"/>. This interface is that missing base: the
    /// debug partial of <see cref="BaseStateManager{TState}"/> implements it explicitly, so none of it
    /// leaks into the public API surface users see on the machine.
    /// <para>
    /// Every member is a pure read that must never throw and never mutate the machine: implementations
    /// are called from an editor repaint loop against machines that may be in any lifecycle phase,
    /// including "configured but never initialized".
    /// </para>
    /// </remarks>
    internal interface IStateMachineDebugSource
    {
        /// <summary>Whether the machine has been initialized; false machines still have registered states worth showing.</summary>
        bool IsInitialized { get; }

        /// <summary>The enum type keying the machine, for the type label on the card and for color hashing.</summary>
        Type StateEnumType { get; }

        /// <summary>The current state's name, or null before <see cref="BaseStateManager{TState}.Initialize"/> — the window draws a dimmed placeholder for null.</summary>
        string CurrentStateName { get; }

        /// <summary>The previous state's name, or null if the machine has never transitioned.</summary>
        string PreviousStateName { get; }

        /// <summary>
        /// Every registered state's name, in registration order. Cached by the implementation: the
        /// window re-reads this on each refresh, so allocating per call would churn the editor heap.
        /// </summary>
        IReadOnlyList<string> StateNames { get; }

        /// <summary>
        /// Whether the transition table permits moving between two entries of
        /// <see cref="StateNames"/>, used to light up the reachable chips in the state strip.
        /// </summary>
        /// <param name="fromIndex">Index into <see cref="StateNames"/> of the source state.</param>
        /// <param name="toIndex">Index into <see cref="StateNames"/> of the destination state.</param>
        /// <returns>True if the pair is allowed. Out-of-range indices answer false rather than throwing.</returns>
        bool IsTransitionAllowed(int fromIndex, int toIndex);
    }

    /// <summary>
    /// One entered state, recorded by the registry's <see cref="BaseStateManager{TState}.StateEntered"/>
    /// subscription: where the machine came from, where it went, and when.
    /// </summary>
    /// <remarks>
    /// A readonly struct held in a fixed-size array, so a busy machine adds no garbage-collector
    /// pressure while the window is open. Names rather than enum values, because the history buffer
    /// is shared by machines closed over different enums.
    /// <para>
    /// <see cref="Timestamp"/> is <see cref="Time.realtimeSinceStartupAsDouble"/> — wall time, which
    /// keeps advancing while the editor is paused or <see cref="Time.timeScale"/> is zero. That is
    /// deliberate: the window answers "how long ago did I see this", not "how much game time passed".
    /// </para>
    /// </remarks>
    internal readonly struct StateTransitionRecord
    {
        /// <summary>The state the machine left. Equals <see cref="To"/> for the initial entry, which has no origin.</summary>
        internal readonly string From;

        /// <summary>The state the machine entered.</summary>
        internal readonly string To;

        /// <summary>Wall-clock time of the entry, from <see cref="Time.realtimeSinceStartupAsDouble"/>.</summary>
        internal readonly double Timestamp;

        /// <summary>Creates a record. Called only by <see cref="StateMachineDebugEntry.OnStateEntered"/>.</summary>
        /// <param name="from">The state the machine left.</param>
        /// <param name="to">The state the machine entered.</param>
        /// <param name="timestamp">Wall-clock time of the entry.</param>
        internal StateTransitionRecord(string from, string to, double timestamp)
        {
            From = from;
            To = to;
            Timestamp = timestamp;
        }
    }

    /// <summary>
    /// The registry's record of one machine: identity and display metadata for the card, a weak
    /// handle back to the machine, and a small ring buffer of recent entries.
    /// </summary>
    /// <remarks>
    /// The machine is held through a <see cref="WeakReference{T}"/> on purpose. Registration happens
    /// automatically inside <see cref="BaseStateManager{TState}.Initialize"/>, so a strong reference here
    /// would make every state machine ever initialized immortal in the editor — a debugging aid that
    /// leaks the thing it debugs. The reference chain that does exist runs the other way (machine to
    /// event delegate to closure to entry), which roots nothing: drop the machine and the entry goes
    /// <see cref="IsDead"/> and is pruned by <see cref="StateMachineDebugRegistry.CollectAlive"/>.
    /// <para>
    /// <see cref="Owner"/> gives a second, deterministic death signal for the common case. Unity's
    /// managed wrapper for a destroyed object stays alive as a C# object but compares equal to null,
    /// so a destroyed behaviour prunes its card on the next refresh even when the GC has not run and
    /// even when a subclass overrode <c>OnDestroy</c> without calling base.
    /// </para>
    /// </remarks>
    internal sealed class StateMachineDebugEntry
    {
        /// <summary>
        /// How many entries the history keeps. Ten is enough to read the shape of a bug without the
        /// card growing taller than the window; older entries fall off the back silently.
        /// </summary>
        internal const int HistoryCapacity = 10;

        /// <summary>Weak on purpose — see the type remarks. Never null itself, though its target may be gone.</summary>
        private readonly WeakReference<IStateMachineDebugSource> _machine;

        /// <summary>Fixed-size ring buffer of recent entries; never resized, never reallocated.</summary>
        private readonly StateTransitionRecord[] _history = new StateTransitionRecord[HistoryCapacity];

        /// <summary>Index the next record is written to.</summary>
        private int _historyNext;

        /// <summary>How many slots of <see cref="_history"/> hold real records, capped at <see cref="HistoryCapacity"/>.</summary>
        private int _historyCount;

        /// <summary>
        /// The name used when there is no live owner to ask: the owner's name captured at
        /// registration, or the machine's type signature for a bare machine.
        /// </summary>
        private readonly string _fallbackDisplayName;

        /// <summary>
        /// Stable, unique, never reused within a session. The window keys its cards by this so a
        /// rebuild reuses the right card. Purely an identity — the accent color is derived from the
        /// display name and enum type instead, so it survives across sessions.
        /// </summary>
        internal readonly int Id;

        /// <summary>The enum the machine is closed over, for the dim type label on the card.</summary>
        internal readonly Type EnumType;

        /// <summary>
        /// The behaviour hosting the machine, or null for a bare machine. Kept as a strong reference
        /// because Unity objects are owned by the scene, not by us — and because a destroyed one is
        /// exactly the signal <see cref="IsDead"/> needs.
        /// </summary>
        internal readonly UnityEngine.Object Owner;

        /// <summary>Whether an owner was supplied at registration; distinguishes "bare machine" from "owner destroyed".</summary>
        internal readonly bool HasOwner;

        /// <summary>
        /// Wall-clock time the current state was entered, from
        /// <see cref="Time.realtimeSinceStartupAsDouble"/>. Seeded at registration so a machine whose
        /// initial <c>EnterState</c> threw still shows a sane elapsed time.
        /// </summary>
        internal double CurrentStateEnteredAt;

        /// <summary>
        /// Creates an entry. Called only by
        /// <see cref="StateMachineDebugRegistry.Register{TState}"/>, which subscribes the machine's
        /// <see cref="BaseStateManager{TState}.StateEntered"/> to <see cref="OnStateEntered"/> immediately
        /// afterwards.
        /// </summary>
        /// <param name="id">The stable id assigned by the registry.</param>
        /// <param name="machine">The machine to observe, held weakly.</param>
        /// <param name="enumType">The enum the machine is closed over.</param>
        /// <param name="owner">The hosting behaviour, or null for a bare machine.</param>
        internal StateMachineDebugEntry(int id, IStateMachineDebugSource machine, Type enumType, UnityEngine.Object owner)
        {
            Id = id;
            EnumType = enumType;
            _machine = new WeakReference<IStateMachineDebugSource>(machine);

            // A fake-null Unity object is a real C# reference, so normalize it to a plain null here
            // rather than making every later read repeat the check.
            HasOwner = owner != null;
            Owner = HasOwner ? owner : null;

            _fallbackDisplayName = HasOwner
                ? owner.name
                : $"BaseStateManager<{(enumType != null ? enumType.Name : "?")}>";

            CurrentStateEnteredAt = SafeNow();
        }

        /// <summary>
        /// What the card's header shows: the owner's current name while it is alive, otherwise the
        /// name captured at registration (or the machine's type signature for a bare machine).
        /// </summary>
        /// <remarks>
        /// Read live rather than cached so renaming a GameObject is reflected immediately, but guarded
        /// by the fake-null check because touching <c>name</c> on a destroyed object throws.
        /// </remarks>
        internal string DisplayName => HasOwner && Owner != null ? Owner.name : _fallbackDisplayName;

        /// <summary>
        /// Whether this entry should be pruned: the machine has been collected, or its owning Unity
        /// object has been destroyed.
        /// </summary>
        /// <remarks>
        /// Two independent signals because they cover different failure modes. The weak target going
        /// away is the general case (bare machines dropped by user code) but depends on the GC, which
        /// under Boehm is conservative and may take a while. The destroyed-owner check is immediate
        /// and covers the case that actually matters in the editor: a GameObject deleted while the
        /// window is open.
        /// </remarks>
        internal bool IsDead
        {
            get
            {
                if (!_machine.TryGetTarget(out var machine) || machine == null)
                {
                    return true;
                }

                return HasOwner && Owner == null;
            }
        }

        /// <summary>How many records the history currently holds, from zero up to <see cref="HistoryCapacity"/>.</summary>
        internal int HistoryCount => _historyCount;

        /// <summary>
        /// Reads one history record, newest first: index 0 is the most recent entry,
        /// <c>HistoryCount - 1</c> the oldest still retained.
        /// </summary>
        /// <param name="index">Zero-based index from the newest record.</param>
        /// <returns>The record, or <c>default</c> if <paramref name="index"/> is out of range.</returns>
        /// <remarks>
        /// Newest-first because that is the order the window draws rows in, and it keeps the ring
        /// buffer's wrap arithmetic in one place instead of at every call site.
        /// </remarks>
        internal StateTransitionRecord GetHistory(int index)
        {
            if (index < 0 || index >= _historyCount)
            {
                return default;
            }

            // _historyNext points at the slot the *next* record will overwrite, i.e. one past the
            // newest; step backwards from there, wrapping.
            var slot = _historyNext - 1 - index;
            if (slot < 0)
            {
                slot += HistoryCapacity;
            }

            return _history[slot];
        }

        /// <summary>
        /// Copies the whole history into <paramref name="buffer"/>, newest first, clearing it first.
        /// A convenience over <see cref="GetHistory"/> for callers that want to iterate a list.
        /// </summary>
        /// <param name="buffer">The list to fill. Reused across refreshes so nothing is allocated.</param>
        internal void CopyHistoryTo(List<StateTransitionRecord> buffer)
        {
            if (buffer == null)
            {
                return;
            }

            buffer.Clear();
            for (var i = 0; i < _historyCount; i++)
            {
                buffer.Add(GetHistory(i));
            }
        }

        /// <summary>Empties the history without touching identity or timing, backing the window's "Clear History" button.</summary>
        internal void ClearHistory()
        {
            Array.Clear(_history, 0, _history.Length);
            _historyNext = 0;
            _historyCount = 0;
        }

        /// <summary>
        /// Dereferences the weak handle to the machine.
        /// </summary>
        /// <param name="machine">The machine, or null if it has been collected.</param>
        /// <returns>True if the machine is still alive.</returns>
        internal bool TryGetMachine(out IStateMachineDebugSource machine)
        {
            return _machine.TryGetTarget(out machine) && machine != null;
        }

        /// <summary>
        /// Records an entered state. Wired to the machine's
        /// <see cref="BaseStateManager{TState}.StateEntered"/> event by the registry.
        /// </summary>
        /// <param name="from">The state left, or the initial state itself on the machine's first entry.</param>
        /// <param name="to">The state entered.</param>
        /// <remarks>
        /// The entire body is wrapped in a bare <c>try/catch</c>, and that is the single most
        /// important line of this file. The machine raises <c>StateEntered</c> from inside its
        /// re-entrancy guard, and anything a handler throws there aborts the user's transition and
        /// surfaces as a <see cref="StateManagerException"/> from their <c>ChangeState</c> call. A
        /// debugging aid must never be able to change program behavior, so every failure here —
        /// including <see cref="Time"/> being touched off the main thread — is swallowed at the cost
        /// of one missing history row.
        /// </remarks>
        internal void OnStateEntered(string from, string to)
        {
            try
            {
                var now = SafeNow();

                _history[_historyNext] = new StateTransitionRecord(from, to, now);
                _historyNext = (_historyNext + 1) % HistoryCapacity;
                if (_historyCount < HistoryCapacity)
                {
                    _historyCount++;
                }

                CurrentStateEnteredAt = now;
            }
            catch
            {
                // Deliberately empty: see remarks. Never let the monitor break the machine.
            }
        }

        /// <summary>
        /// Wall-clock now: the engine's clock when it is available, otherwise a
        /// <see cref="System.Diagnostics.Stopwatch"/>-derived reading. Never throws.
        /// </summary>
        /// <remarks>
        /// The engine read lives in <see cref="ReadEngineClock"/>, a separate non-inlinable method,
        /// and that split is load-bearing: when this assembly runs outside Unity (the package's
        /// reflection-driven test workflow), resolving the <see cref="Time"/> internal call fails at
        /// JIT time of the method that contains it. Inlined — or written directly in this body — that
        /// failure would be raised while compiling <c>SafeNow</c> itself, before the <c>try</c> ever
        /// executes, and would escape to the caller. Kept as a real call, the exception surfaces at
        /// the call site inside the <c>try</c> and is caught.
        /// <para>
        /// The fallback is a real clock rather than zero so timestamps and time-in-state stay
        /// meaningful wherever the entry is exercised. A process uses one clock or the other for its
        /// whole lifetime — the engine call either always works or never does — so recorded values
        /// are always mutually comparable.
        /// </para>
        /// </remarks>
        private static double SafeNow()
        {
            try
            {
                return ReadEngineClock();
            }
            catch
            {
                return System.Diagnostics.Stopwatch.GetTimestamp() * StopwatchTickToSeconds;
            }
        }

        /// <summary>Seconds per <see cref="System.Diagnostics.Stopwatch"/> tick, for the fallback clock.</summary>
        private static readonly double StopwatchTickToSeconds = 1d / System.Diagnostics.Stopwatch.Frequency;

        /// <summary>
        /// Reads <see cref="Time.realtimeSinceStartupAsDouble"/>. Not inlinable, so a failure to
        /// resolve the engine call is raised here — inside <see cref="SafeNow"/>'s <c>try</c> — rather
        /// than while compiling the caller. See <see cref="SafeNow"/>.
        /// </summary>
        [MethodImpl(MethodImplOptions.NoInlining)]
        private static double ReadEngineClock()
        {
            return Time.realtimeSinceStartupAsDouble;
        }
    }

    /// <summary>
    /// The editor-only list of live state machines behind the <c>Essentials/StateManager</c> window.
    /// Machines add themselves from <see cref="BaseStateManager{TState}.Initialize"/>; nobody has to opt
    /// in, and nothing in a player build references this type.
    /// </summary>
    /// <remarks>
    /// Deliberately dumb and main-thread only, like the rest of the library: a list, an id counter and
    /// a version stamp. No locks, no events, no editor dependencies — the window polls.
    /// <para>
    /// <see cref="Version"/> is the cheap change signal. The window ticks ten times a second; comparing
    /// one int tells it whether the set of machines changed (rebuild cards) or merely their contents
    /// did (refresh in place), which keeps a busy scene from rebuilding UI Toolkit hierarchies at
    /// 10 Hz.
    /// </para>
    /// <para>
    /// Every field has an inline initializer and <see cref="ResetStatics"/> re-establishes them on
    /// <see cref="RuntimeInitializeLoadType.SubsystemRegistration"/>: the house pattern for statics
    /// that must survive both a domain reload and "Enter Play Mode Options" with reload disabled. The
    /// initializers matter on their own account, because EditMode tests run without any
    /// <see cref="RuntimeInitializeOnLoadMethodAttribute"/> ever firing.
    /// </para>
    /// </remarks>
    internal static class StateMachineDebugRegistry
    {
        /// <summary>
        /// Every registered machine, in registration order — which is the order the window draws
        /// cards in, so machines do not jump around between refreshes.
        /// </summary>
        internal static readonly List<StateMachineDebugEntry> Entries = new List<StateMachineDebugEntry>();

        /// <summary>Monotonic id source; never reuses a value within a session so card keys stay unique.</summary>
        private static int _nextId = 1;

        /// <summary>
        /// Bumped whenever the set of entries changes — register, unregister, prune, clear. The
        /// window keeps the last value it saw and rebuilds only when it differs.
        /// </summary>
        internal static int Version = 0;

        /// <summary>
        /// Adds <paramref name="machine"/> to the registry and starts recording its state entries.
        /// Called from <see cref="BaseStateManager{TState}.Initialize"/> before the initial
        /// <see cref="BaseStateManager{TState}.StateEntered"/> fires, so the initial entry lands in the
        /// history like any other.
        /// </summary>
        /// <typeparam name="TState">The enum the machine is closed over.</typeparam>
        /// <param name="machine">The machine to track. Null is ignored rather than throwing.</param>
        /// <param name="owner">The behaviour hosting it, or null for a bare machine.</param>
        /// <remarks>
        /// The subscription closes over the <see cref="StateMachineDebugEntry"/>, not over the
        /// registry, so the only strong reference runs machine to entry. Nothing here roots the
        /// machine — see the entry's remarks for the full reasoning — which is what lets a dropped
        /// machine be collected and its entry pruned.
        /// <para>
        /// The lambda converts enum values to strings, which allocates once per transition. That cost
        /// exists only in the editor and only for machines that were initialized while this assembly
        /// was compiled with <c>UNITY_EDITOR</c>; it is the price of showing a heterogeneous list.
        /// </para>
        /// </remarks>
        internal static void Register<TState>(BaseStateManager<TState> machine, UnityEngine.Object owner)
            where TState : struct, Enum
        {
            if (machine == null)
            {
                return;
            }

            var entry = new StateMachineDebugEntry(_nextId++, machine, typeof(TState), owner);
            Entries.Add(entry);
            Version++;

            machine.StateEntered += (from, to) => entry.OnStateEntered(from.ToString(), to.ToString());
        }

        /// <summary>
        /// Removes the entry tracking <paramref name="machine"/>, if there is one. Called from
        /// <c>StateManagerBehaviour.OnDestroy</c>; a machine that is never explicitly unregistered is
        /// pruned later by <see cref="CollectAlive"/> instead.
        /// </summary>
        /// <param name="machine">The machine to release. Unknown or null machines are a silent no-op.</param>
        /// <remarks>
        /// Typed as <see cref="object"/> because the caller is a generic behaviour and the registry
        /// stores machines behind a non-generic interface; matching is by reference identity, so no
        /// equality override can make it remove the wrong entry. A linear scan is fine — the list
        /// holds the machines alive in a scene, and this runs once per destroyed behaviour.
        /// </remarks>
        internal static void Unregister(object machine)
        {
            if (machine == null)
            {
                return;
            }

            for (var i = 0; i < Entries.Count; i++)
            {
                if (Entries[i].TryGetMachine(out var tracked) && ReferenceEquals(tracked, machine))
                {
                    Entries.RemoveAt(i);
                    Version++;
                    return;
                }
            }
        }

        /// <summary>
        /// Prunes dead entries and copies the survivors into <paramref name="results"/>, in
        /// registration order. The window's per-tick entry point.
        /// </summary>
        /// <param name="results">A caller-owned buffer, cleared first and reused across ticks so polling allocates nothing.</param>
        /// <remarks>
        /// Pruning here rather than on a timer keeps the registry passive: with the window closed
        /// nothing runs, and dead entries simply sit in the list costing one weak reference each until
        /// something asks.
        /// </remarks>
        internal static void CollectAlive(List<StateMachineDebugEntry> results)
        {
            var pruned = false;
            for (var i = Entries.Count - 1; i >= 0; i--)
            {
                if (Entries[i].IsDead)
                {
                    Entries.RemoveAt(i);
                    pruned = true;
                }
            }

            if (pruned)
            {
                Version++;
            }

            if (results == null)
            {
                return;
            }

            results.Clear();
            for (var i = 0; i < Entries.Count; i++)
            {
                results.Add(Entries[i]);
            }
        }

        /// <summary>
        /// Drops every entry. Called by the editor bootstrap when play mode ends, so the window does
        /// not keep showing machines from a session that is over, and by tests in teardown.
        /// </summary>
        /// <remarks>
        /// Machines are not notified and their event subscriptions are not removed: the delegates
        /// point at entries that nothing references any more, and both die together. A machine that
        /// somehow survives play mode simply re-registers if it is initialized again.
        /// </remarks>
        internal static void Clear()
        {
            Entries.Clear();
            Version++;
        }

        /// <summary>
        /// Hard-resets every static, restoring the state a freshly loaded assembly would have.
        /// </summary>
        /// <remarks>
        /// The house pattern for domain-reload safety: with "Enter Play Mode Options" set to skip the
        /// reload, statics keep their values from the previous session, so entering play mode would
        /// otherwise show cards for machines that no longer exist and hand out ids continuing from
        /// the last run. Ids restart at one so per-manager accent colors are reproducible across runs.
        /// </remarks>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        internal static void ResetStatics()
        {
            Entries.Clear();
            _nextId = 1;
            Version++;
        }
    }
}
#endif
