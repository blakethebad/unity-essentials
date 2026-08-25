using System;

namespace UnityEssentials.States
{
    /// <summary>
    /// The machine-facing base of one state: the enum value it implements, the hooks the machine
    /// calls and the ownership it is attached under. Not derivable directly — author states by
    /// deriving from <see cref="BaseState{TManager, TState}"/>, which adds the typed manager.
    /// </summary>
    public abstract class BaseState<TState> where TState : struct, Enum
    {
        // ---- Identity -----------------------------------------------------

        // Only the typed subclass in this assembly can derive, which is what forces authoring
        // through BaseState<TManager, TState>.
        private protected BaseState()
        {
        }

        /// <summary>
        /// The enum value this state implements: the key its machine stores it under, so it must be
        /// constant for the lifetime of the object and unique within a machine.
        /// </summary>
        public abstract TState StateType { get; }

        internal IStateManager<TState> Owner { get; private set; }

        // Called by AddState. Re-attaching to the same manager is a no-op so a failed registration
        // can be retried; a different manager is a configuration error.
        internal void Attach(IStateManager<TState> manager)
        {
            if (Owner != null)
            {
                if (!ReferenceEquals(Owner, manager))
                {
                    throw new StateConfigurationException(
                        $"State '{GetType().Name}' ({StateType}) is already attached to another state machine for '{typeof(TState).Name}'. " +
                        "Create a separate state instance for each machine.");
                }

                return;
            }

            // OnAttach runs before Owner is stored, so a rejected manager type leaves the state
            // unattached and reusable.
            OnAttach(manager);
            Owner = manager;
        }

        private protected abstract void OnAttach(IStateManager<TState> manager);

        // ---- Lifecycle hooks ----------------------------------------------

        /// <summary>
        /// Called by the machine after it has switched to this state. Non-virtual on purpose:
        /// override <see cref="OnEnterState"/> instead.
        /// </summary>
        public void EnterState(TState previousState)
        {
            OnEnterState(previousState);
        }

        /// <summary>
        /// Called by the machine before it switches away, while this state is still current.
        /// Non-virtual on purpose: override <see cref="OnExitState"/> instead.
        /// </summary>
        public void ExitState(TState nextState)
        {
            OnExitState(nextState);
        }

        /// <summary>
        /// Called by the machine once per tick while this state is current. Non-virtual on purpose:
        /// override <see cref="OnUpdate"/> instead.
        /// </summary>
        public void UpdateState()
        {
            OnUpdate();
        }

        /// <summary>
        /// Sets the state up after the machine has switched to it. On the machine's very first entry
        /// <paramref name="previousState"/> is this state itself; check PreviousStateType for null.
        /// A transition requested from here is deferred and performed once this entry completes.
        /// </summary>
        protected virtual void OnEnterState(TState previousState)
        {
        }

        /// <summary>
        /// Tears the state down before the machine switches away. Throwing here aborts the transition
        /// before the swap and leaves the machine on this state.
        /// </summary>
        protected virtual void OnExitState(TState nextState)
        {
        }

        /// <summary>
        /// Per-frame logic while this state is current. The usual place to request a transition;
        /// the state entered runs its own update on the next tick, not this one.
        /// </summary>
        protected virtual void OnUpdate()
        {
        }

        /// <summary>
        /// Re-runs this state without leaving it: exit then enter, both passed <see cref="StateType"/>.
        /// Override when a state can reset more cheaply than a full teardown and rebuild.
        /// </summary>
        public virtual void RestartState()
        {
            ExitState(StateType);
            EnterState(StateType);
        }
    }

    /// <summary>
    /// The class states are authored from: a state that knows its machine as a
    /// <typeparamref name="TManager"/>. That can be a concrete manager type, a
    /// <see cref="StateManagerBehaviour{TState}"/> subclass, or <see cref="IStateManager{TState}"/>
    /// itself for a state that works with any machine.
    /// </summary>
    public abstract class BaseState<TManager, TState> : BaseState<TState>
        where TManager : class, IStateManager<TState>
        where TState : struct, Enum
    {
        /// <summary>
        /// The machine this state was added to, or null until it is registered. Use it to request
        /// transitions from <see cref="BaseState{TState}.OnUpdate"/>.
        /// </summary>
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
