using System;
using System.Collections.Generic;

namespace UnityEssentials.States
{
    /// <summary>
    /// The state machine surface shared by the plain-C# <see cref="BaseStateManager{TState}"/> and
    /// the MonoBehaviour host <see cref="StateManagerBehaviour{TState}"/>. States reach their
    /// machine through this interface, so a state can be written against either host.
    /// </summary>
    public interface IStateManager<TState> where TState : struct, Enum
    {
        // ---- Properties ---------------------------------------------------

        /// <summary>True once the machine has been sealed by <see cref="Initialize"/>.</summary>
        bool IsInitialized { get; }

        /// <summary>The state the machine is in, as an enum value and as the registered instance.</summary>
        TState CurrentStateType { get; }

        BaseState<TState> CurrentState { get; }

        /// <summary>The state last left, null until the first transition completes.</summary>
        TState? PreviousStateType { get; }

        BaseState<TState> PreviousState { get; }

        // ---- Events -------------------------------------------------------

        /// <summary>
        /// Raised after the old state's exit hook and before the swap, so <see cref="CurrentStateType"/>
        /// still reports the state being left. Arguments are <c>(from, to)</c>.
        /// </summary>
        event Action<TState, TState> StateExited;

        /// <summary>
        /// Raised after the new state's entry hook, once the transition is complete. Arguments are
        /// <c>(from, to)</c>; on the initial entry both are the initial state.
        /// </summary>
        event Action<TState, TState> StateEntered;

        // ---- Runtime ------------------------------------------------------

        /// <summary>Configures the machine, seals it and enters the initial state. Call once.</summary>
        void Initialize();

        /// <summary>Transitions to <paramref name="nextState"/> if the transition table allows it.</summary>
        void ChangeState(TState nextState);

        /// <summary>Re-runs the current state in place. Not a transition: no events, no table check.</summary>
        void RestartState();

        /// <summary>Pumps one frame of the current state's update hook.</summary>
        void Tick();

        /// <summary>Reports whether the transition table permits a pair. A pure query that never throws.</summary>
        bool CanChangeState(TState from, TState to);

        // ---- Configuration ------------------------------------------------

        /// <summary>
        /// Registers a state under <paramref name="stateType"/> and declares the states it may move
        /// to. The first one registered is the default initial state.
        /// </summary>
        IStateManager<TState> AddState(
            TState stateType,
            BaseState<TState> state,
            IReadOnlyList<TState> availableTransitions = null);

        /// <summary>Chooses the state <see cref="Initialize"/> enters, overriding the first-registered default.</summary>
        IStateManager<TState> SetInitialState(TState state);
    }
}
