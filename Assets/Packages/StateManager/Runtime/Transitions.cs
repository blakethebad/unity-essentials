using System;

namespace UnityEssentials.States
{
    /// <summary>
    /// The chainable handle a machine hands to its <c>InsertTransitions</c> hook — the only way to
    /// declare transitions. A thin façade over the manager's internal <c>Allow*</c> methods, which
    /// keep all the validation.
    /// </summary>
    public readonly struct Transitions<TState> where TState : struct, Enum
    {
        private readonly BaseStateManager<TState> _machine;

        internal Transitions(BaseStateManager<TState> machine)
        {
            _machine = machine;
        }

        /// <summary>Declares that <paramref name="from"/> may transition to <paramref name="to"/>.</summary>
        public Transitions<TState> Allow(TState from, TState to)
        {
            _machine.AllowTransition(from, to);
            return this;
        }

        /// <summary>Declares one source and several destinations in a single call.</summary>
        public Transitions<TState> Allow(TState from, params TState[] to)
        {
            _machine.AllowTransitions(from, to);
            return this;
        }

        /// <summary>Opens every pair, including self-transitions. A prototyping escape hatch.</summary>
        public Transitions<TState> AllowAny()
        {
            _machine.AllowAnyTransition();
            return this;
        }
    }
}
