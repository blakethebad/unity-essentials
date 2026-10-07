using System;
using System.Collections.Generic;

namespace UnityEssentials.States
{
    public abstract partial class BaseStateManager<TState> : IStateManager<TState> where TState : struct, Enum
    {
        private readonly Dictionary<TState, BaseState<TState>> _states = new Dictionary<TState, BaseState<TState>>();
        private readonly TransitionTable<TState> _transitions = new TransitionTable<TState>();
        private readonly List<(TState from, TState to)> _declaredTransitions = new List<(TState from, TState to)>();

        private TState? _initialState;
        private TState? _firstRegistered;

        private BaseState<TState> _currentState;
        private BaseState<TState> _previousState;
        private TState? _previousStateType;

        private IStateManager<TState> _attachOwner;

        private bool _isInitialized;
        private bool _configuring;
        private bool _transitioning;
        private bool _entering;

        private TState? _pendingState;

        private const int MaxDeferredChanges = 100;

        partial void DebugRegister();

        public bool IsInitialized => _isInitialized;

        public TState CurrentStateType => _currentState == null ? default : _currentState.StateType;
        public BaseState<TState> CurrentState => _currentState;

        public TState? PreviousStateType => _previousStateType;
        public BaseState<TState> PreviousState => _previousState;

        public event Action<TState, TState> StateExited;
        public event Action<TState, TState> StateEntered;

        internal void SetAttachOwner(IStateManager<TState> owner)
        {
            _attachOwner = owner;
        }

        /// <summary>Runs the configuration hooks, seals the machine and enters the state <see cref="GetInitialState"/> picks.</summary>
        public void Initialize()
        {
            if (_isInitialized)
                throw new StateConfigurationException(
                    "This BaseStateManager is already initialized. Initialize() may only be called once.");

            if (_configuring)
                throw new StateConfigurationException(
                    "Initialize() was called from inside OnInitialize(). " +
                    "Configuration hooks must only configure the machine.");

            _configuring = true;
            try
            {
                OnInitialize();

				for (var i = 0; i < _declaredTransitions.Count; i++)
				{
					var (from, to) = _declaredTransitions[i];
					if (!_states.ContainsKey(to))
					{
						throw new StateConfigurationException(
							$"AddState('{from}') lists '{to}' as an available transition, but no state is registered for it. " +
							"Register it with AddState() from OnInitialize(), or drop it from the list.");
					}

					_transitions.Allow(from, to);
				}

				_declaredTransitions.Clear();
            }
            finally
            {
                _configuring = false;
            }

            if (_states.Count == 0)
                throw new StateConfigurationException(
                    "Cannot initialize a BaseStateManager with no states. Register at least one state with AddState() from OnInitialize().");

            var initial = GetInitialState();
            if (!_states.TryGetValue(initial, out var state))
                throw new StateConfigurationException(
                    $"GetInitialState() returned '{initial}', which is not registered. " +
                    "Register it with AddState(), or return a registered state from the override.");

            _isInitialized = true;
            _currentState = state;

            DebugRegister();

            var stage = "initial EnterState";

            _transitioning = true;
            _entering = true;
            try
            {
                state.EnterState(initial);

                stage = "StateEntered handler";
                StateEntered?.Invoke(initial, initial);
            }
            catch (Exception inner)
            {
                _pendingState = null;
                throw new StateManagerException(
                    $"{stage} failed while entering the initial state '{initial}'. " +
                    $"{inner.GetType().Name}: {inner.Message}",
                    inner);
            }
            finally
            {
                _transitioning = false;
                _entering = false;
            }

            RunDeferredChanges();
        }

        /// <summary>Transitions to <paramref name="nextState"/>: validate, exit, swap, enter. A rejected call changes nothing.</summary>
        public void ChangeState(TState nextState)
        {
            if (!_isInitialized)
                throw new StateConfigurationException(
                    "ChangeState is not available until the machine is initialized. Call Initialize() after configuring it.");

            if (_transitioning)
            {
                DeferChangeState(nextState);
                return;
            }

            PerformChangeState(nextState);
            RunDeferredChanges();
        }

        private void DeferChangeState(TState nextState)
        {
            if (!_entering)
                throw new StateManagerException(
                    "ChangeState() was called during the exit half of a transition or during a restart. " +
                    "A follow-up change may be requested from EnterState or a StateEntered handler — it is " +
                    "performed once the transition completes — but not from ExitState, RestartState or a " +
                    "StateExited handler.");

            if (_pendingState != null)
                throw new StateManagerException(
                    $"ChangeState('{nextState}') was called while a change to '{_pendingState}' was already deferred. " +
                    "Only one follow-up change may be requested per transition.");

            if (!_states.ContainsKey(nextState))
                throw new StateConfigurationException(
                    $"Cannot change to '{nextState}' because no state is registered for it. " +
                    "Register it with AddState() before initializing the machine.");

            var from = _currentState.StateType;
            if (!_transitions.IsAllowed(from, nextState))
                throw new InvalidTransitionException(
                    $"Transition from '{from}' to '{nextState}' is not allowed. " +
                    $"Add '{nextState}' to the available transitions of AddState({from}, ...) in OnInitialize().");

            _pendingState = nextState;
        }

        private void RunDeferredChanges()
        {
            var performed = 0;
            while (_pendingState != null)
            {
                var next = _pendingState.Value;
                _pendingState = null;

                if (++performed > MaxDeferredChanges)
                {
                    throw new StateManagerException(
                        $"A chain of deferred state changes exceeded {MaxDeferredChanges} transitions without settling " +
                        $"('{next}' was next). The states are deferring into each other in a cycle; break it, " +
                        "for example by requesting one of the changes from UpdateState instead.");
                }

                PerformChangeState(next);
            }
        }

        private void PerformChangeState(TState nextState)
        {
            if (!_states.TryGetValue(nextState, out var target))
            {
                throw new StateConfigurationException(
                    $"Cannot change to '{nextState}' because no state is registered for it. " +
                    "Register it with AddState() before initializing the machine.");
            }

            var previous = _currentState;
            var from = previous.StateType;
            if (!_transitions.IsAllowed(from, nextState))
            {
                throw new InvalidTransitionException(
                    $"Transition from '{from}' to '{nextState}' is not allowed. " +
                    $"Add '{nextState}' to the available transitions of AddState({from}, ...) in OnInitialize().");
            }

            var stage = "ExitState hook";

            _transitioning = true;
            try
            {
                previous.ExitState(nextState);

                stage = "StateExited handler";
                StateExited?.Invoke(from, nextState);

                // The swap sits between the two halves so exit observers see the state being left and
                // entry observers see a completed transition.
                _previousState = previous;
                _previousStateType = from;
                _currentState = target;

                stage = "EnterState hook";
                _entering = true;
                target.EnterState(from);

                stage = "StateEntered handler";
                StateEntered?.Invoke(from, nextState);
            }
            catch (Exception inner)
            {
                // A change deferred by the failed entry dies with it, or it would run out of nowhere
                // on the next unrelated call.
                _pendingState = null;
                throw new StateManagerException(
                    $"{stage} failed while changing state from '{from}' to '{nextState}'. " +
                    $"{inner.GetType().Name}: {inner.Message}",
                    inner);
            }
            finally
            {
                _transitioning = false;
                _entering = false;
            }
        }

        /// <summary>Re-runs the current state in place. Not a transition: no table check, no events, no previous update.</summary>
        public void RestartState()
        {
            if (!_isInitialized)
            {
                throw new StateConfigurationException(
                    "RestartState is not available until the machine is initialized. Call Initialize() after configuring it.");
            }

            if (_transitioning)
            {
                throw new StateManagerException(
                    "RestartState() was called while a transition was already in progress. " +
                    "Restarts cannot be nested inside EnterState, ExitState, RestartState or an event handler; " +
                    "request them from UpdateState instead.");
            }

            var state = _currentState;

            _transitioning = true;
            try
            {
                state.RestartState();
            }
            catch (Exception inner)
            {
                throw new StateManagerException(
                    $"RestartState hook failed while restarting state '{state.StateType}'. " +
                    $"{inner.GetType().Name}: {inner.Message}",
                    inner);
            }
            finally
            {
                _transitioning = false;
            }
        }

        /// <summary>Pumps one frame of the current state's update hook, from which <see cref="ChangeState"/> is legal.</summary>
        public void Tick()
        {
            if (!_isInitialized)
                throw new StateConfigurationException(
                    "Tick is not available until the machine is initialized. Call Initialize() after configuring it.");
            

            if (_transitioning)
                throw new StateManagerException(
                    "Tick() was called while a transition was already in progress. " +
                    "Ticking cannot be nested inside EnterState, ExitState, RestartState or an event handler.");
            

            var state = _currentState;
            state.UpdateState();
        }

        /// <summary>Reports whether the transition table permits a pair. A pure query that never throws.</summary>
        public bool CanChangeState(TState from, TState to)
        {
            return _transitions.IsAllowed(from, to);
        }

        /// <summary>Registers this machine's states and their moves. Called once by <see cref="Initialize"/>.</summary>
        protected abstract void OnInitialize();

        /// <summary>Registers a state and the moves it allows; the first one registered is the default initial state.</summary>
        public IStateManager<TState> AddState(
            TState stateType,
            BaseState<TState> state,
            IReadOnlyList<TState> availableTransitions = null)
        {
            if (state == null)
                throw new ArgumentNullException(nameof(state));

            if (_isInitialized)
                throw new StateConfigurationException(
                    "AddState() cannot be called after Initialize(). Configure the machine completely, then initialize it.");

            if (_states.ContainsKey(stateType))
                throw new StateConfigurationException(
                    $"A state is already registered for '{stateType}'. Register one state instance per enum value.");

            state.Attach(_attachOwner ?? this, stateType);
            _states.Add(stateType, state);

            if (_firstRegistered == null)
                _firstRegistered = stateType;

            if (availableTransitions != null)
            {
                for (var i = 0; i < availableTransitions.Count; i++)
                {
                    _declaredTransitions.Add((stateType, availableTransitions[i]));
                }
            }

            return this;
        }

        internal void AllowTransition(TState from, TState to)
        {
            if (_isInitialized)
                throw new StateConfigurationException(
                    "Transitions cannot be declared after Initialize(). Declare them with AddState() from OnInitialize().");

            if (!_states.ContainsKey(from))
                throw new StateConfigurationException(
                    $"Allow() refers to '{from}', which is not registered. Add it with AddState() from OnInitialize() first.");

            if (!_states.ContainsKey(to))
                throw new StateConfigurationException(
                    $"Allow() refers to '{to}', which is not registered. Add it with AddState() from OnInitialize() first.");

            _transitions.Allow(from, to);
        }

        internal void AllowTransitions(TState from, params TState[] to)
        {
            if (_isInitialized)
                throw new StateConfigurationException(
                    "Transitions cannot be declared after Initialize(). Declare them with AddState() from OnInitialize().");

            if (to == null)
                throw new ArgumentNullException(nameof(to));

            if (to.Length == 0)
                throw new StateConfigurationException(
                    $"Allow('{from}') was called without any destination states. " +
                    "Pass at least one target, or drop the call.");

            if (!_states.ContainsKey(from))
                throw new StateConfigurationException(
                    $"Allow() refers to '{from}', which is not registered. Add it with AddState() from OnInitialize() first.");

            for (var i = 0; i < to.Length; i++)
            {
                if (!_states.ContainsKey(to[i]))
                    throw new StateConfigurationException(
                        $"Allow() refers to '{to[i]}', which is not registered. Add it with AddState() from OnInitialize() first.");

                _transitions.Allow(from, to[i]);
            }
        }

        internal void AllowAnyTransition()
        {
            if (_isInitialized)
                throw new StateConfigurationException(
                    "Transitions cannot be declared after Initialize(). Declare them with AddState() from OnInitialize().");

            _transitions.AllowAll();
        }

        /// <summary>Chooses the state <see cref="Initialize"/> enters, overriding the first-registered default.</summary>
        public IStateManager<TState> SetInitialState(TState state)
        {
            if (_isInitialized)
                throw new StateConfigurationException(
                    "SetInitialState() cannot be called after Initialize(). Configure the machine completely, then initialize it.");

            if (!_states.ContainsKey(state))
                throw new StateConfigurationException(
                    $"SetInitialState() refers to '{state}', which is not registered. Add it with AddState() first.");

            _initialState = state;
            return this;
        }

        internal TState DefaultInitialState() => _initialState ?? _firstRegistered.Value;

        /// <summary>Decides which state <see cref="Initialize"/> enters; the default honours <see cref="SetInitialState"/>.</summary>
        protected virtual TState GetInitialState() => DefaultInitialState();
    }
}
