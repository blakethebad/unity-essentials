using System;
using System.Collections.Generic;

namespace UnityEssentials.States
{
    internal sealed class TransitionTable<TState> where TState : struct, Enum
    {
        private readonly Dictionary<TState, HashSet<TState>> _allowed = new Dictionary<TState, HashSet<TState>>();

        private bool _allowAll;

        internal void Allow(TState from, TState to)
        {
            if (!_allowed.TryGetValue(from, out var destinations))
            {
                destinations = new HashSet<TState>();
                _allowed[from] = destinations;
            }

            destinations.Add(to);
        }

        internal void AllowAll()
        {
            _allowAll = true;
        }

        internal bool IsAllowed(TState from, TState to)
        {
            if (_allowAll)
            {
                return true;
            }

            return _allowed.TryGetValue(from, out var destinations) && destinations.Contains(to);
        }
    }
}
