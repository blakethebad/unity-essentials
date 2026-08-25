using System;
using System.Collections.Generic;

namespace UnityEssentials.States
{
    /// <summary>
    /// A finite state machine keyed by your own enum: a derived manager registers its states in
    /// <see cref="OnInitialize"/> and declares its legal moves in <see cref="InsertTransitions"/>,
    /// then <see cref="Initialize"/> seals it and <see cref="ChangeState"/> and <see cref="Tick"/>
    /// drive it. Plain C# — no Unity types, no scene dependency.
    /// </summary>
    public abstract partial class BaseStateManager<TState> : IStateManager<TState> where TState : struct, Enum
    {
        // ---- Fields -------------------------------------------------------

        private readonly Dictionary<TState, BaseState<TState>> _states = new Dictionary<TState, BaseState<TState>>();

        private readonly TransitionTable<TState> _transitions = new TransitionTable<TState>();

        private TState? _initialState;
        private TState? _firstRegistered;

        private BaseState<TState> _currentState;
        private BaseState<TState> _previousState;
        private TState? _previousStateType;

        // The manager states are attached to; null means this machine. A host that only delegates to
        // this engine — StateManagerBehaviour — sets itself here so states see the host as their
        // manager instead of the hidden machine behind it.
        private IStateManager<TState> _attachOwner;

        private bool _isInitialized;

        // Set while the configuration hooks run, so an Initialize() call made from inside one is a
        // loud exception rather than a half-configured machine.
        private bool _configuring;

        // Set while hooks and events for a transition, the initial entry or a restart are running.
        // Everything that would mutate the machine checks it: a ChangeState made under it is either
        // deferred (entry half) or a loud exception (everywhere else), never corrupted state.
        private bool _transitioning;

        // Set for the entry half of a transition — the entry hook and StateEntered — which is the
        // only window where a nested ChangeState is legal. By then the swap has happened, so the
        // requester's own state is what the deferred change validates against.
        private bool _entering;

        // The single change deferred from the entry half of the transition in flight, performed by
        // RunDeferredChanges once that transition has completed.
        private TState? _pendingState;

        // Deferred changes chaining into each other run inside one ChangeState or Initialize call,
        // so a cycle of states deferring forever would hang the frame; this turns it into an exception.
        private const int MaxDeferredChanges = 100;

        // ---- Editor hooks -------------------------------------------------

        // A partial method with no implementing part is erased by the compiler along with every call
        // to it, so outside UNITY_EDITOR this declaration and the call in Initialize both vanish.
        partial void DebugRegister();

        // ---- Properties ---------------------------------------------------

        public bool IsInitialized => _isInitialized;

        public TState CurrentStateType => _currentState == null ? default : _currentState.StateType;

        public BaseState<TState> CurrentState => _currentState;

        public TState? PreviousStateType => _previousStateType;

        public BaseState<TState> PreviousState => _previousState;

        // ---- Events -------------------------------------------------------

        /// <summary>
        /// Raised after the old state's exit hook and before the swap, so <see cref="CurrentStateType"/>
        /// still reports the state being left. Arguments are <c>(from, to)</c>.
        /// </summary>
        public event Action<TState, TState> StateExited;

        /// <summary>
        /// Raised after the new state's entry hook, once the transition is complete. Arguments are
        /// <c>(from, to)</c>; on the initial entry both are the initial state.
        /// </summary>
        public event Action<TState, TState> StateEntered;

        // ---- Hosting ------------------------------------------------------

        // Redirects state ownership to a host that wraps this machine. Must be called before any
        // AddState, i.e. from the host's constructor.
        internal void SetAttachOwner(IStateManager<TState> owner)
        {
            _attachOwner = owner;
        }

        // ---- Runtime ------------------------------------------------------

        /// <summary>
        /// Runs the configuration hooks, seals the machine and enters the state chosen by
        /// <see cref="GetInitialState"/> — bypassing the transition table, and firing no exit events.
        /// </summary>
        public void Initialize()
        {
            if (_isInitialized)
            {
                throw new StateConfigurationException(
                    "This BaseStateManager is already initialized. Initialize() may only be called once.");
            }

            if (_configuring)
            {
                throw new StateConfigurationException(
                    "Initialize() was called from inside OnInitialize() or InsertTransitions(). " +
                    "Configuration hooks must only configure the machine.");
            }

            // Hook failures propagate unchanged and leave the machine uninitialized, so a manager
            // that throws while configuring is still a configurable machine.
            _configuring = true;
            try
            {
                OnInitialize();
                InsertTransitions(new Transitions<TState>(this));
            }
            finally
            {
                _configuring = false;
            }

            if (_states.Count == 0)
            {
                throw new StateConfigurationException(
                    "Cannot initialize a BaseStateManager with no states. Register at least one state with AddState() from OnInitialize().");
            }

            var initial = GetInitialState();
            if (!_states.TryGetValue(initial, out var state))
            {
                throw new StateConfigurationException(
                    $"GetInitialState() returned '{initial}', which is not registered. " +
                    "Register it with AddState(), or return a registered state from the override.");
            }

            _isInitialized = true;
            _currentState = state;

            // Editor-only, and compiled away entirely in players. Placed before the guarded entry
            // below so the debug entry's subscription observes the initial StateEntered.
            DebugRegister();

            // Stage literals are interned, so tracking them costs nothing on the success path;
            // the message is only ever built inside the catch.
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
                // A change deferred by the failed entry dies with it, or it would run out of
                // nowhere on the next unrelated call.
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

            // A change the initial entry deferred runs now, as an ordinary transition with the full
            // exit/enter sequence and events — its failures are its own, not the initial entry's.
            RunDeferredChanges();
        }

        /// <summary>
        /// Transitions to <paramref name="nextState"/>: validate, exit hook, <see cref="StateExited"/>,
        /// swap, entry hook, <see cref="StateEntered"/>. A rejected call leaves the machine untouched.
        /// Called from the entry half of a transition — <c>EnterState</c> or a <see cref="StateEntered"/>
        /// handler — the change is deferred and performed as soon as that transition completes.
        /// </summary>
        public void ChangeState(TState nextState)
        {
            if (!_isInitialized)
            {
                throw new StateConfigurationException(
                    "ChangeState is not available until the machine is initialized. Call Initialize() after configuring it.");
            }

            if (_transitioning)
            {
                DeferChangeState(nextState);
                return;
            }

            PerformChangeState(nextState);
            RunDeferredChanges();
        }

        // Records a change requested from the entry half of a transition. The swap has already made
        // the requester's state current, so the request validates exactly like a top-level call —
        // failures point at the call site, not at the drain loop that would have performed it.
        private void DeferChangeState(TState nextState)
        {
            if (!_entering)
            {
                throw new StateManagerException(
                    "ChangeState() was called during the exit half of a transition or during a restart. " +
                    "A follow-up change may be requested from EnterState or a StateEntered handler — it is " +
                    "performed once the transition completes — but not from ExitState, RestartState or a " +
                    "StateExited handler.");
            }

            if (_pendingState != null)
            {
                throw new StateManagerException(
                    $"ChangeState('{nextState}') was called while a change to '{_pendingState}' was already deferred. " +
                    "Only one follow-up change may be requested per transition.");
            }

            if (!_states.ContainsKey(nextState))
            {
                throw new StateConfigurationException(
                    $"Cannot change to '{nextState}' because no state is registered for it. " +
                    "Register it with AddState() before initializing the machine.");
            }

            var from = _currentState.StateType;
            if (!_transitions.IsAllowed(from, nextState))
            {
                throw new InvalidTransitionException(
                    $"Transition from '{from}' to '{nextState}' is not allowed. " +
                    $"Declare it with transitions.Allow({from}, {nextState}) in InsertTransitions().");
            }

            _pendingState = nextState;
        }

        // Performs changes deferred while a transition ran, each of which may defer the next.
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

        // One full transition: validate, exit hook, StateExited, swap, entry hook, StateEntered.
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
                    $"Declare it with transitions.Allow({from}, {nextState}) in InsertTransitions().");
            }

            // Stage literals are interned, so tracking them costs nothing on the success path;
            // the message is only ever built inside the catch.
            var stage = "ExitState hook";

            _transitioning = true;
            try
            {
                previous.ExitState(nextState);

                stage = "StateExited handler";
                StateExited?.Invoke(from, nextState);

                // The swap sits between the two halves so exit observers see the state being left
                // and entry observers see a completed transition.
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
                // A change deferred by the failed entry dies with it, or it would run out of
                // nowhere on the next unrelated call.
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

        /// <summary>
        /// Re-runs the current state through <see cref="BaseState{TState}.RestartState"/>. Not a
        /// transition: the table is bypassed, no events fire, current and previous stay as they are.
        /// </summary>
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

        /// <summary>
        /// Pumps one frame of the current state's update hook. Calling <see cref="ChangeState"/> from
        /// there is legal — the guard is deliberately not held — and update failures are not wrapped.
        /// </summary>
        public void Tick()
        {
            if (!_isInitialized)
            {
                throw new StateConfigurationException(
                    "Tick is not available until the machine is initialized. Call Initialize() after configuring it.");
            }

            if (_transitioning)
            {
                throw new StateManagerException(
                    "Tick() was called while a transition was already in progress. " +
                    "Ticking cannot be nested inside EnterState, ExitState, RestartState or an event handler.");
            }

            var state = _currentState;
            state.UpdateState();
        }

        /// <summary>
        /// Reports whether the transition table permits a pair. A pure query: it ignores the current
        /// state, ignores whether the machine is initialized, and never throws.
        /// </summary>
        public bool CanChangeState(TState from, TState to)
        {
            return _transitions.IsAllowed(from, to);
        }

        // ---- Configuration ------------------------------------------------

        /// <summary>
        /// Registers this machine's states with <see cref="AddState"/>, and optionally picks the
        /// starting one with <see cref="SetInitialState"/>. Called once by <see cref="Initialize"/>.
        /// </summary>
        protected abstract void OnInitialize();

        /// <summary>
        /// Declares the legal moves between the states registered by <see cref="OnInitialize"/>.
        /// Called once by <see cref="Initialize"/>, right after <see cref="OnInitialize"/>.
        /// </summary>
        protected abstract void InsertTransitions(in Transitions<TState> transitions);

        /// <summary>
        /// Registers a state under its own <see cref="BaseState{TState}.StateType"/> and takes
        /// ownership of it. The first state registered is the default initial state.
        /// </summary>
        public IStateManager<TState> AddState(BaseState<TState> state)
        {
            if (state == null)
            {
                throw new ArgumentNullException(nameof(state));
            }

            if (_isInitialized)
            {
                throw new StateConfigurationException(
                    "AddState() cannot be called after Initialize(). Configure the machine completely, then initialize it.");
            }

            var key = state.StateType;
            if (_states.ContainsKey(key))
            {
                throw new StateConfigurationException(
                    $"A state is already registered for '{key}'. Register one state instance per enum value.");
            }

            // Attach last, so a rejected registration leaves the state unowned and reusable.
            state.Attach(_attachOwner ?? this);
            _states.Add(key, state);

            if (_firstRegistered == null)
            {
                _firstRegistered = key;
            }

            return this;
        }

        // The three declaration paths behind Transitions<TState>, internal so InsertTransitions is
        // the only way user code can reach the table.
        internal void AllowTransition(TState from, TState to)
        {
            if (_isInitialized)
            {
                throw new StateConfigurationException(
                    "Transitions cannot be declared after Initialize(). Declare them inside InsertTransitions().");
            }

            if (!_states.ContainsKey(from))
            {
                throw new StateConfigurationException(
                    $"Allow() refers to '{from}', which is not registered. Add it with AddState() from OnInitialize() first.");
            }

            if (!_states.ContainsKey(to))
            {
                throw new StateConfigurationException(
                    $"Allow() refers to '{to}', which is not registered. Add it with AddState() from OnInitialize() first.");
            }

            _transitions.Allow(from, to);
        }

        internal void AllowTransitions(TState from, params TState[] to)
        {
            if (_isInitialized)
            {
                throw new StateConfigurationException(
                    "Transitions cannot be declared after Initialize(). Declare them inside InsertTransitions().");
            }

            if (to == null)
            {
                throw new ArgumentNullException(nameof(to));
            }

            if (to.Length == 0)
            {
                throw new StateConfigurationException(
                    $"Allow('{from}') was called without any destination states. " +
                    "Pass at least one target, or drop the call.");
            }

            if (!_states.ContainsKey(from))
            {
                throw new StateConfigurationException(
                    $"Allow() refers to '{from}', which is not registered. Add it with AddState() from OnInitialize() first.");
            }

            for (var i = 0; i < to.Length; i++)
            {
                if (!_states.ContainsKey(to[i]))
                {
                    throw new StateConfigurationException(
                        $"Allow() refers to '{to[i]}', which is not registered. Add it with AddState() from OnInitialize() first.");
                }

                _transitions.Allow(from, to[i]);
            }
        }

        internal void AllowAnyTransition()
        {
            if (_isInitialized)
            {
                throw new StateConfigurationException(
                    "Transitions cannot be declared after Initialize(). Declare them inside InsertTransitions().");
            }

            _transitions.AllowAll();
        }

        /// <summary>
        /// Chooses the state <see cref="Initialize"/> enters. Optional — without it the first state
        /// passed to <see cref="AddState"/> is used, and the last call wins.
        /// </summary>
        public IStateManager<TState> SetInitialState(TState state)
        {
            if (_isInitialized)
            {
                throw new StateConfigurationException(
                    "SetInitialState() cannot be called after Initialize(). Configure the machine completely, then initialize it.");
            }

            if (!_states.ContainsKey(state))
            {
                throw new StateConfigurationException(
                    $"SetInitialState() refers to '{state}', which is not registered. Add it with AddState() first.");
            }

            _initialState = state;
            return this;
        }

        // The stock choice, kept reachable so a host that overrides GetInitialState can fall back to
        // it without recursing through its own override.
        internal TState DefaultInitialState()
        {
            return _initialState ?? _firstRegistered.Value;
        }

        /// <summary>
        /// Decides which state <see cref="Initialize"/> enters. The default honours
        /// <see cref="SetInitialState"/> and otherwise falls back to the first state registered.
        /// </summary>
        protected virtual TState GetInitialState()
        {
            return DefaultInitialState();
        }
    }
}
