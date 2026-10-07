using System;
using System.Collections.Generic;

namespace UnityEssentials.States
{
    public interface IStateManager<TState> where TState : struct, Enum
    {
        bool IsInitialized { get; }

        TState CurrentStateType { get; }
        BaseState<TState> CurrentState { get; }
        TState? PreviousStateType { get; }
        BaseState<TState> PreviousState { get; }

        event Action<TState, TState> StateExited;
        event Action<TState, TState> StateEntered;

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

        /// <summary>Registers a state and the states it may move to. The first one registered is the default initial state.</summary>
        IStateManager<TState> AddState(
            TState stateType,
            BaseState<TState> state,
            IReadOnlyList<TState> availableTransitions = null);

        /// <summary>Chooses the state <see cref="Initialize"/> enters, overriding the first-registered default.</summary>
        IStateManager<TState> SetInitialState(TState state);
    }
}
