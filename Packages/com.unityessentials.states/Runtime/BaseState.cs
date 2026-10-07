using System;
using System.Collections.Generic;

namespace UnityEssentials.States
{
    /// <summary>
    /// The machine-facing base of one state. Not derivable directly — author states by deriving from
    /// <see cref="BaseState{TManager, TState}"/>, which adds the typed manager.
    /// </summary>
    public abstract class BaseState<TState> where TState : struct, Enum
    {
        private protected BaseState()
        {
        }

        /// <summary>The enum value this state answers for, assigned by the machine when <c>AddState</c> registers it.</summary>
        public TState StateType { get; private set; }

        internal IStateManager<TState> Owner { get; private set; }

        internal void Attach(IStateManager<TState> manager, TState stateType)
        {
            if (Owner != null)
            {
                if (!ReferenceEquals(Owner, manager))
                {
                    throw new StateConfigurationException(
                        $"State '{GetType().Name}' ({StateType}) is already attached to another state machine for '{typeof(TState).Name}'. " +
                        "Create a separate state instance for each machine.");
                }

                if (!EqualityComparer<TState>.Default.Equals(StateType, stateType))
                {
                    throw new StateConfigurationException(
                        $"State '{GetType().Name}' is already registered as '{StateType}' and cannot also answer for '{stateType}'. " +
                        "Create a separate state instance for each enum value.");
                }

                return;
            }

            StateType = stateType;
            OnAttach(manager);
            Owner = manager;
        }

        private protected abstract void OnAttach(IStateManager<TState> manager);

        /// <summary>Called by the machine after it has switched to this state. Override <see cref="OnEnterState"/> instead.</summary>
        public void EnterState(TState previousState)
        {
            OnEnterState(previousState);
        }

        /// <summary>Called by the machine before it switches away. Override <see cref="OnExitState"/> instead.</summary>
        public void ExitState(TState nextState)
        {
            OnExitState(nextState);
        }

        /// <summary>Called by the machine once per tick while this state is current. Override <see cref="OnUpdate"/> instead.</summary>
        public void UpdateState()
        {
            OnUpdate();
        }

        /// <summary>Sets the state up after the machine has switched to it; a transition requested here is deferred.</summary>
        protected virtual void OnEnterState(TState previousState)
        {
        }

        /// <summary>Tears the state down before the machine switches away; throwing here aborts the transition.</summary>
        protected virtual void OnExitState(TState nextState)
        {
        }

        /// <summary>Per-frame logic while this state is current. The usual place to request a transition.</summary>
        protected virtual void OnUpdate()
        {
        }

        /// <summary>Re-runs this state without leaving it: exit then enter, both passed <see cref="StateType"/>.</summary>
        public virtual void RestartState()
        {
            ExitState(StateType);
            EnterState(StateType);
        }
    }

    /// <summary>
    /// The class states are authored from: a state that knows its machine as a
    /// <typeparamref name="TManager"/> — a concrete manager, a <see cref="StateManagerBehaviour{TState}"/>
    /// subclass, or <see cref="IStateManager{TState}"/> for a state that works with any machine.
    /// </summary>
    public abstract class BaseState<TManager, TState> : BaseState<TState>
        where TManager : class, IStateManager<TState>
        where TState : struct, Enum
    {
        /// <summary>The machine this state was added to, or null until it is registered.</summary>
        protected TManager Manager { get; private set; }

        private protected sealed override void OnAttach(IStateManager<TState> manager)
        {
            var typed = manager as TManager;
            if (typed == null)
            {
                throw new StateConfigurationException(
                    $"State '{GetType().Name}' ({StateType}) expects a manager of type '{typeof(TManager).Name}', " +
                    $"but it was added to a machine owned by '{(manager == null ? "null" : manager.GetType().Name)}'. " +
                    "Derive the state from BaseState<> closed over the manager that hosts it.");
            }

            Manager = typed;
        }
    }
}
