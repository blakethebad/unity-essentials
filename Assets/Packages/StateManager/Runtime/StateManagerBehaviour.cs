using System;
using System.Collections.Generic;
using UnityEngine;

namespace UnityEssentials.States
{
    /// <summary>
    /// MonoBehaviour host for a self-configuring state machine: it owns a
    /// <see cref="BaseStateManager{TState}"/>, wires it into Unity's lifecycle (initialize in
    /// <c>Start</c>, tick in <c>Update</c>) and mirrors its API. States registered here see the
    /// behaviour itself as their manager, not the machine behind it.
    /// </summary>
    public abstract partial class StateManagerBehaviour<TState> : MonoBehaviour, IStateManager<TState>
        where TState : struct, Enum
    {
        // ---- Composition --------------------------------------------------

        // Built in the constructor because a field initializer cannot reference `this`, and the
        // machine has to exist before any Unity callback for pre-Awake subscription to be safe.
        private readonly BaseStateManager<TState> _machine;

        protected StateManagerBehaviour()
        {
            _machine = new HostedMachine(this);
        }

        protected BaseStateManager<TState> Machine => _machine;

        // ---- Editor hooks -------------------------------------------------

        // Unimplemented partial methods outside UNITY_EDITOR: the compiler erases the declarations
        // and the call sites together, so players pay nothing.
        partial void DebugAttachOwner();

        partial void DebugUnregister();

        // ---- Unity lifecycle ----------------------------------------------

        /// <summary>
        /// Names the machine's owner for the editor debug window. Override to run your own setup,
        /// calling <c>base.Awake()</c>.
        /// </summary>
        protected virtual void Awake()
        {
            // Editor-only and erased in players. Must precede initialization, which happens in
            // Start, because the machine reads its owner once as it registers.
            DebugAttachOwner();
        }

        /// <summary>
        /// Initializes the machine, which configures it and enters the initial state. Override
        /// without calling base to defer that and call <c>Initialize()</c> yourself later.
        /// </summary>
        protected virtual void Start()
        {
            _machine.Initialize();
        }

        /// <summary>
        /// Ticks the current state once per frame, and does nothing until the machine is initialized.
        /// Override to add per-frame work, calling <c>base.Update()</c> so states keep updating.
        /// </summary>
        protected virtual void Update()
        {
            if (_machine.IsInitialized)
            {
                _machine.Tick();
            }
        }

        /// <summary>
        /// Releases the machine from the editor debug window's registry; empty in player builds.
        /// Override to add your own cleanup, calling <c>base.OnDestroy()</c>.
        /// </summary>
        protected virtual void OnDestroy()
        {
            DebugUnregister();
        }

        // ---- Configuration ------------------------------------------------

        /// <summary>
        /// Registers this machine's states with <see cref="AddState"/> — each with the states it may
        /// move to — and optionally picks the starting one with <see cref="SetInitialState"/>.
        /// Called once by <see cref="Initialize"/>.
        /// </summary>
        protected abstract void OnInitialize();

        /// <summary>
        /// Declares moves that <see cref="AddState"/> did not cover — the escape hatches
        /// <c>AllowAny</c> and late additions. Optional; called once by <see cref="Initialize"/>.
        /// </summary>
        protected virtual void InsertTransitions(in Transitions<TState> transitions)
        {
        }

        /// <summary>
        /// Decides which state <see cref="Initialize"/> enters. The default honours
        /// <see cref="SetInitialState"/> and otherwise falls back to the first state registered.
        /// </summary>
        protected virtual TState GetInitialState()
        {
            return _machine.DefaultInitialState();
        }

        // ---- Machine surface ----------------------------------------------

        /// <summary>Forwards <see cref="IStateManager{TState}.StateExited"/>.</summary>
        public event Action<TState, TState> StateExited
        {
            add => _machine.StateExited += value;
            remove => _machine.StateExited -= value;
        }

        /// <summary>Forwards <see cref="IStateManager{TState}.StateEntered"/>.</summary>
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

        // ---- Hosted machine -----------------------------------------------

        // The engine the behaviour delegates to. It owns no configuration of its own: every hook
        // forwards to the behaviour, and SetAttachOwner makes states attach to the behaviour so
        // BaseState<TManager, TState> can be closed over the behaviour type.
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

            protected override void InsertTransitions(in Transitions<TState> transitions)
            {
                _owner.InsertTransitions(transitions);
            }

            protected override TState GetInitialState()
            {
                return _owner.GetInitialState();
            }
        }
    }
}
