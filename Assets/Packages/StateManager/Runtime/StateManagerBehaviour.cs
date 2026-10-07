using System;
using System.Collections.Generic;
using UnityEngine;

namespace UnityEssentials.States
{
    public abstract partial class StateManagerBehaviour<TState> : MonoBehaviour, IStateManager<TState>
        where TState : struct, Enum
    {
        private readonly BaseStateManager<TState> _machine;

        protected StateManagerBehaviour()
        {
            _machine = new HostedMachine(this);
        }

        protected BaseStateManager<TState> Machine => _machine;

        partial void DebugAttachOwner();
        partial void DebugUnregister();

        /// <summary>Names the machine's owner for the editor debug window. Override calling <c>base.Awake()</c>.</summary>
        protected virtual void Awake()
        {
            DebugAttachOwner();
        }

        /// <summary>Initializes the machine. Override without calling base to initialize it yourself later.</summary>
        protected virtual void Start()
        {
            _machine.Initialize();
        }

        /// <summary>Ticks the current state once per frame, and does nothing until the machine is initialized.</summary>
        protected virtual void Update()
        {
            if (_machine.IsInitialized)
            {
                _machine.Tick();
            }
        }

        /// <summary>Releases the machine from the editor debug window's registry; empty in player builds.</summary>
        protected virtual void OnDestroy()
        {
            DebugUnregister();
        }

        /// <summary>Registers this machine's states and their moves. Called once by <see cref="Initialize"/>.</summary>
        protected abstract void OnInitialize();

        /// <summary>Declares moves <see cref="AddState"/> did not cover. Optional; called once by <see cref="Initialize"/>.</summary>
        protected virtual void InsertTransitions(in Transitions<TState> transitions)
        {
        }

        /// <summary>Decides which state <see cref="Initialize"/> enters; the default honours <see cref="SetInitialState"/>.</summary>
        protected virtual TState GetInitialState()
        {
            return _machine.DefaultInitialState();
        }

        public event Action<TState, TState> StateExited
        {
            add => _machine.StateExited += value;
            remove => _machine.StateExited -= value;
        }

        public event Action<TState, TState> StateEntered
        {
            add => _machine.StateEntered += value;
            remove => _machine.StateEntered -= value;
        }

        public bool IsInitialized => _machine.IsInitialized;

        public TState CurrentStateType => _machine.CurrentStateType;

        public BaseState<TState> CurrentState => _machine.CurrentState;

        public TState? PreviousStateType => _machine.PreviousStateType;

        public BaseState<TState> PreviousState => _machine.PreviousState;

        /// <summary>Forwards <see cref="IStateManager{TState}.Initialize"/>. Called for you by <c>Start</c>.</summary>
        public void Initialize()
        {
            _machine.Initialize();
        }

        /// <summary>Forwards <see cref="IStateManager{TState}.ChangeState"/>.</summary>
        public void ChangeState(TState nextState)
        {
            _machine.ChangeState(nextState);
        }

        /// <summary>Forwards <see cref="IStateManager{TState}.RestartState"/>.</summary>
        public void RestartState()
        {
            _machine.RestartState();
        }

        /// <summary>Forwards <see cref="IStateManager{TState}.Tick"/>. Called for you by <c>Update</c>.</summary>
        public void Tick()
        {
            _machine.Tick();
        }

        /// <summary>Forwards <see cref="IStateManager{TState}.CanChangeState"/>.</summary>
        public bool CanChangeState(TState from, TState to)
        {
            return _machine.CanChangeState(from, to);
        }

        /// <summary>Forwards <see cref="IStateManager{TState}.AddState"/>, returning this behaviour so chains keep targeting it.</summary>
        public IStateManager<TState> AddState(
            TState stateType,
            BaseState<TState> state,
            IReadOnlyList<TState> availableTransitions = null)
        {
            _machine.AddState(stateType, state, availableTransitions);
            return this;
        }

        /// <summary>Forwards <see cref="IStateManager{TState}.SetInitialState"/>, returning this behaviour so chains keep targeting it.</summary>
        public IStateManager<TState> SetInitialState(TState state)
        {
            _machine.SetInitialState(state);
            return this;
        }

        private sealed class HostedMachine : BaseStateManager<TState>
        {
            private readonly StateManagerBehaviour<TState> _owner;

            internal HostedMachine(StateManagerBehaviour<TState> owner)
            {
                _owner = owner;
                SetAttachOwner(owner);
            }

            protected override void OnInitialize()
            {
                _owner.OnInitialize();
            }

            protected override TState GetInitialState()
            {
                return _owner.GetInitialState();
            }
        }
    }
}
