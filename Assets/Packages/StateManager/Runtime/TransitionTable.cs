using System;
using System.Collections.Generic;

namespace UnityEssentials.States
{
    /// <summary>
    /// The set of legal <c>(from, to)</c> pairs behind a <see cref="BaseStateManager{TState}"/>.
    /// Deny-all by default: <see cref="IsAllowed"/> answers <c>false</c> for every pair until
    /// <see cref="Allow"/> or <see cref="AllowAll"/> opens one.
    /// </summary>
    /// <remarks>
    /// Deliberately a dumb pair store: it never asks whether a state is registered, because the
    /// machine owns that knowledge and validates registration before it ever reaches the table.
    /// Keeping the two concerns apart is what makes <see cref="BaseStateManager{TState}.CanChangeState"/>
    /// a pure query that is safe to call at any point in the lifecycle, including before
    /// <see cref="BaseStateManager{TState}.Initialize"/>.
    /// <para>
    /// Both collections use <see cref="EqualityComparer{T}.Default"/>, which the runtime specializes
    /// for enums — no boxing, so <see cref="IsAllowed"/> allocates nothing on the hot path.
    /// </para>
    /// </remarks>
    /// <typeparam name="TState">The enum keying the machine's states.</typeparam>
    internal sealed class TransitionTable<TState> where TState : struct, Enum
    {
        /// <summary>Source state to the set of destinations reachable from it.</summary>
        private readonly Dictionary<TState, HashSet<TState>> _allowed = new Dictionary<TState, HashSet<TState>>();

        /// <summary>Short-circuits every query once <see cref="AllowAll"/> has been called.</summary>
        private bool _allowAll;

        /// <summary>
        /// Opens the directional pair <paramref name="from"/> to <paramref name="to"/>. Idempotent:
        /// registering the same pair twice is a no-op, and the reverse pair stays closed.
        /// </summary>
        /// <param name="from">The source state.</param>
        /// <param name="to">The destination state.</param>
        internal void Allow(TState from, TState to)
        {
            if (!_allowed.TryGetValue(from, out var destinations))
            {
                destinations = new HashSet<TState>();
                _allowed[from] = destinations;
            }

            destinations.Add(to);
        }

        /// <summary>
        /// Opens every pair, including self-transitions, without enumerating the enum. Backs the
        /// prototyping escape hatch <see cref="BaseStateManager{TState}.AllowAnyTransition"/>; the flag
        /// is one-way, since nothing in the public API removes permissions.
        /// </summary>
        internal void AllowAll()
        {
            _allowAll = true;
        }

        /// <summary>
        /// Reports whether moving from <paramref name="from"/> to <paramref name="to"/> is permitted.
        /// </summary>
        /// <param name="from">The source state.</param>
        /// <param name="to">The destination state.</param>
        /// <returns>
        /// True when <see cref="AllowAll"/> was called, or when the exact pair was opened by
        /// <see cref="Allow"/>. False otherwise — including for unknown states, which this type
        /// makes no attempt to distinguish from known-but-closed ones.
        /// </returns>
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
