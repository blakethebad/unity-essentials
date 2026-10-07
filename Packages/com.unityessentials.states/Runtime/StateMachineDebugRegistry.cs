#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using UnityEngine;

namespace UnityEssentials.States
{
    internal interface IStateMachineDebugSource
    {
        bool IsInitialized { get; }

        Type StateEnumType { get; }

        string CurrentStateName { get; }

        string PreviousStateName { get; }

        IReadOnlyList<string> StateNames { get; }

        bool IsTransitionAllowed(int fromIndex, int toIndex);
    }

    internal readonly struct StateTransitionRecord
    {
        internal readonly string From;

        internal readonly string To;

        internal readonly double Timestamp;

        internal StateTransitionRecord(string from, string to, double timestamp)
        {
            From = from;
            To = to;
            Timestamp = timestamp;
        }
    }

    /// <summary>
    /// The registry's record of one machine: identity and display metadata for the card, a weak handle
    /// back to the machine, and a small ring buffer of recent entries.
    /// </summary>
    internal sealed class StateMachineDebugEntry
    {
        internal const int HistoryCapacity = 10;

        private readonly WeakReference<IStateMachineDebugSource> _machine;

        private readonly StateTransitionRecord[] _history = new StateTransitionRecord[HistoryCapacity];

        private int _historyNext;
        private int _historyCount;
        private readonly string _fallbackDisplayName;

        internal readonly int Id;
        internal readonly Type EnumType;
        internal readonly UnityEngine.Object Owner;
        internal readonly bool HasOwner;
        internal double CurrentStateEnteredAt;

        internal StateMachineDebugEntry(int id, IStateMachineDebugSource machine, Type enumType, UnityEngine.Object owner)
        {
            Id = id;
            EnumType = enumType;
            _machine = new WeakReference<IStateMachineDebugSource>(machine);

            HasOwner = owner != null;
            Owner = HasOwner ? owner : null;

            _fallbackDisplayName = HasOwner
                ? owner.name
                : $"BaseStateManager<{(enumType != null ? enumType.Name : "?")}>";

            CurrentStateEnteredAt = SafeNow();
        }

        internal string DisplayName => HasOwner && Owner != null ? Owner.name : _fallbackDisplayName;

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

        internal int HistoryCount => _historyCount;

        internal StateTransitionRecord GetHistory(int index)
        {
            if (index < 0 || index >= _historyCount)
            {
                return default;
            }

            var slot = _historyNext - 1 - index;
            if (slot < 0)
            {
                slot += HistoryCapacity;
            }

            return _history[slot];
        }

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

        internal void ClearHistory()
        {
            Array.Clear(_history, 0, _history.Length);
            _historyNext = 0;
            _historyCount = 0;
        }

        internal bool TryGetMachine(out IStateMachineDebugSource machine)
        {
            return _machine.TryGetTarget(out machine) && machine != null;
        }

        internal void OnStateEntered(string from, string to)
        {
            try
            {
                var now = SafeNow();

                _history[_historyNext] = new StateTransitionRecord(from, to, now);
                _historyNext = (_historyNext + 1) % HistoryCapacity;
                if (_historyCount < HistoryCapacity)
                    _historyCount++;

                CurrentStateEnteredAt = now;
            }
            catch
            {
            }
        }

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

        private static readonly double StopwatchTickToSeconds = 1d / System.Diagnostics.Stopwatch.Frequency;

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static double ReadEngineClock()
        {
            return Time.realtimeSinceStartupAsDouble;
        }
    }

    /// <summary>
    /// The editor-only list of live state machines behind the <c>Essentials/StateManager</c> window.
    /// Machines add themselves from <see cref="BaseStateManager{TState}.Initialize"/>; nothing in a
    /// player build references this type.
    /// </summary>
    internal static class StateMachineDebugRegistry
    {
        internal static readonly List<StateMachineDebugEntry> Entries = new List<StateMachineDebugEntry>();

        private static int _nextId = 1;
        internal static int Version = 0;

        internal static void Register<TState>(BaseStateManager<TState> machine, UnityEngine.Object owner)
            where TState : struct, Enum
        {
            if (machine == null)
                return;

            var entry = new StateMachineDebugEntry(_nextId++, machine, typeof(TState), owner);
            Entries.Add(entry);
            Version++;

            // Closes over the entry, not the registry, so the only strong reference runs machine to
            // entry and nothing here roots the machine.
            machine.StateEntered += (from, to) =>
                entry.OnStateEntered(machine.DebugStateName(from), machine.DebugStateName(to));
        }

        // Typed as object because the caller is a generic behaviour and the registry stores machines
        // behind a non-generic interface; matching is by reference identity.
        internal static void Unregister(object machine)
        {
            if (machine == null)
                return;

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

        // Pruning here rather than on a timer keeps the registry passive: with the window closed
        // nothing runs.
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
                Version++;

            if (results == null)
                return;

            results.Clear();
            for (var i = 0; i < Entries.Count; i++)
            {
                results.Add(Entries[i]);
            }
        }

        internal static void Clear()
        {
            Entries.Clear();
            Version++;
        }

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
