// Editor-only half of BaseStateManager<TState>. The main file documents the machine as plain C#
// with no Unity types, and that stays true where it counts: this file — including the
// UnityEngine.Object owner field and everything it drags in — is wrapped entirely in
// #if UNITY_EDITOR, so a player build compiles none of it. The only trace left in the main file is a
// call to the partial method DebugRegister(), which the compiler erases along with the call site
// when no part implements it.
#if UNITY_EDITOR
using System;
using System.Collections.Generic;

namespace UnityEssentials.States
{
    /// <summary>
    /// Editor-only debug surface of the machine: the explicit
    /// <see cref="IStateMachineDebugSource"/> implementation the <c>Essentials/StateManager</c>
    /// window reads, plus the registration hook. Implemented explicitly so none of it lands on the
    /// machine's public API.
    /// </summary>
    public partial class BaseStateManager<TState> : IStateMachineDebugSource where TState : struct, Enum
    {
        // ---- Debug state --------------------------------------------------

        // The Unity object to credit this machine to in the window — set by StateManagerBehaviour in
        // Awake, null for a machine created in plain C#. A field because it is written exactly once,
        // from one place, before Initialize reads it.
        internal UnityEngine.Object DebugOwner;

        // Registered names and their matching keys, built once on first request: the window re-reads
        // the strip on every refresh and enum-to-string is not free.
        private string[] _debugStateNames;

        private TState[] _debugStateKeys;

        // ---- Registration hook --------------------------------------------

        // Runs before the initial StateEntered is raised, so the registry's subscription observes it
        // and the machine's very first state shows up in the history like any later one.
        partial void DebugRegister()
        {
            StateMachineDebugRegistry.Register(this, DebugOwner);
        }

        // ---- IStateMachineDebugSource -------------------------------------

        bool IStateMachineDebugSource.IsInitialized => _isInitialized;

        Type IStateMachineDebugSource.StateEnumType => typeof(TState);

        // Reads _currentState rather than CurrentStateType so "not initialized" comes back as null
        // instead of the enum's zero value, which the window has to tell apart.
        string IStateMachineDebugSource.CurrentStateName => _currentState?.StateType.ToString();

        string IStateMachineDebugSource.PreviousStateName => _previousStateType?.ToString();

        IReadOnlyList<string> IStateMachineDebugSource.StateNames => EnsureDebugStateCache();

        // Delegates to the same TransitionTable the machine validates against, so a chip lit in the
        // window and a transition ChangeState would accept can never disagree.
        bool IStateMachineDebugSource.IsTransitionAllowed(int fromIndex, int toIndex)
        {
            EnsureDebugStateCache();

            var keys = _debugStateKeys;
            if (keys == null ||
                fromIndex < 0 || fromIndex >= keys.Length ||
                toIndex < 0 || toIndex >= keys.Length)
            {
                return false;
            }

            return _transitions.IsAllowed(keys[fromIndex], keys[toIndex]);
        }

        // Keyed off _states.Count: states can only be added before Initialize and the count is the
        // only way that set can change, so a length mismatch is a complete staleness check. Both
        // arrays are published together, so a caller never sees a half-built key array.
        private string[] EnsureDebugStateCache()
        {
            if (_debugStateNames != null && _debugStateNames.Length == _states.Count)
            {
                return _debugStateNames;
            }

            var count = _states.Count;
            var names = new string[count];
            var keys = new TState[count];

            var index = 0;
            foreach (var key in _states.Keys)
            {
                keys[index] = key;
                names[index] = key.ToString();
                index++;
            }

            _debugStateKeys = keys;
            _debugStateNames = names;
            return _debugStateNames;
        }
    }
}
#endif
