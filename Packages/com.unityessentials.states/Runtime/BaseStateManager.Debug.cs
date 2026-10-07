#if UNITY_EDITOR
using System;
using System.Collections.Generic;

namespace UnityEssentials.States
{
    public partial class BaseStateManager<TState> : IStateMachineDebugSource where TState : struct, Enum
    {
        internal UnityEngine.Object DebugOwner;
        private string[] _debugStateNames;
        private TState[] _debugStateKeys;
        private Dictionary<TState, string> _debugStateNamesByKey;

        partial void DebugRegister()
        {
            StateMachineDebugRegistry.Register(this, DebugOwner);
        }

        bool IStateMachineDebugSource.IsInitialized => _isInitialized;

        Type IStateMachineDebugSource.StateEnumType => typeof(TState);

        string IStateMachineDebugSource.CurrentStateName => _currentState?.StateType.ToString();
        string IStateMachineDebugSource.PreviousStateName => _previousStateType?.ToString();

        IReadOnlyList<string> IStateMachineDebugSource.StateNames => EnsureDebugStateCache();

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

        private string[] EnsureDebugStateCache()
        {
            if (_debugStateNames != null && _debugStateNames.Length == _states.Count)
            {
                return _debugStateNames;
            }

            var count = _states.Count;
            var names = new string[count];
            var keys = new TState[count];
            var namesByKey = new Dictionary<TState, string>(count);

            var index = 0;
            foreach (var key in _states.Keys)
            {
                keys[index] = key;
                names[index] = key.ToString();
                namesByKey[key] = names[index];
                index++;
            }

            _debugStateKeys = keys;
            _debugStateNamesByKey = namesByKey;
            _debugStateNames = names;
            return _debugStateNames;
        }

        // The registry records every transition through StateEntered, so the names come from the
        // cache above: Enum.ToString() would allocate on a path the machine promises not to.
        internal string DebugStateName(TState state)
        {
            EnsureDebugStateCache();

            return _debugStateNamesByKey.TryGetValue(state, out var name) ? name : state.ToString();
        }
    }
}
#endif
