using System;
using System.Collections.Generic;

namespace UnityEssentials.States.Tests
{
    /// <summary>
    /// The enum every fixture keys its machine on. By convention <c>D</c> is left unregistered, so it
    /// can stand in for a state the machine has never heard of.
    /// </summary>
    public enum TestState
    {
        A,
        B,
        C,
        D
    }

    /// <summary>
    /// Ordered sink every recording double writes to. Static, so every fixture must call
    /// <see cref="Clear"/> from its <c>[SetUp]</c>.
    /// </summary>
    public static class CallLog
    {
        public static readonly List<string> Entries = new List<string>();

        /// <summary>Empties the log. Call from <c>[SetUp]</c> before the machine under test is built.</summary>
        public static void Clear()
        {
            Entries.Clear();
        }

        /// <summary>Appends one entry. The formats are fixed by the doubles below — keep them stable.</summary>
        public static void Record(string entry)
        {
            Entries.Add(entry);
        }
    }

    /// <summary>
    /// The workhorse double: writes every lifecycle hook it receives, arguments included, to
    /// <see cref="CallLog"/>. Every hook allocates a string, so allocation tests use
    /// <see cref="SilentState"/> instead.
    /// </summary>
    public class RecordingState : BaseState<IStateManager<TestState>, TestState>
    {
        // RestartState is deliberately left alone, so restart fixtures observe the inherited
        // exit-then-enter default; CustomRestartState covers the overriding case.

        protected override void OnEnterState(TestState previousState)
        {
            CallLog.Record($"Enter:{StateType}:from:{previousState}");
        }

        protected override void OnExitState(TestState nextState)
        {
            CallLog.Record($"Exit:{StateType}:to:{nextState}");
        }

        protected override void OnUpdate()
        {
            CallLog.Record($"Update:{StateType}");
        }
    }

    /// <summary>Replaces the default restart sequence entirely, proving the restart hook is a real extension point.</summary>
    public sealed class CustomRestartState : RecordingState
    {
        public override void RestartState()
        {
            // No base call on purpose: the assertion is that no Exit:/Enter: entries appear beside it.
            CallLog.Record($"CustomRestart:{StateType}");
        }
    }

    /// <summary>A state that does nothing at all, for allocation tests where recording would dominate the measurement.</summary>
    public sealed class SilentState : BaseState<IStateManager<TestState>, TestState>
    {
    }

    /// <summary>Requests a transition from inside the entry hook — legal, and deferred until the entry completes.</summary>
    public sealed class ChangeOnEnterState : BaseState<IStateManager<TestState>, TestState>
    {
        private readonly TestState _target;

        public ChangeOnEnterState(TestState target)
        {
            _target = target;
        }

        protected override void OnEnterState(TestState previousState)
        {
            Manager.ChangeState(_target);
        }
    }

    /// <summary>
    /// Requests two transitions from one entry hook; the second is illegal, since only one follow-up
    /// change may be deferred per transition.
    /// </summary>
    public sealed class DoubleChangeOnEnterState : BaseState<IStateManager<TestState>, TestState>
    {
        private readonly TestState _first;
        private readonly TestState _second;

        public DoubleChangeOnEnterState(TestState first, TestState second)
        {
            _first = first;
            _second = second;
        }

        protected override void OnEnterState(TestState previousState)
        {
            Manager.ChangeState(_first);
            Manager.ChangeState(_second);
        }
    }

    /// <summary>
    /// Requests a transition from inside the exit hook, where a nested change is still illegal — so it
    /// also proves a rejected re-entrant call aborts the outer transition before the swap.
    /// </summary>
    public sealed class ChangeOnExitState : BaseState<IStateManager<TestState>, TestState>
    {
        private readonly TestState _target;

        public ChangeOnExitState(TestState target)
        {
            _target = target;
        }

        protected override void OnExitState(TestState nextState)
        {
            Manager.ChangeState(_target);
        }
    }

    /// <summary>Drives the machine from the update hook, which runs outside the guard entirely.</summary>
    public sealed class ChangeOnUpdateState : BaseState<IStateManager<TestState>, TestState>
    {
        private readonly TestState _target;

        public ChangeOnUpdateState(TestState target)
        {
            _target = target;
        }

        protected override void OnUpdate()
        {
            // Recorded before the request, so a fixture can prove the tick that triggered the change
            // was still attributed to this state.
            CallLog.Record($"Update:{StateType}");
            Manager.ChangeState(_target);
        }
    }

    /// <summary>Pumps <c>Tick</c> from inside the entry hook — illegal, unlike a nested ChangeState.</summary>
    public sealed class TickOnEnterState : BaseState<IStateManager<TestState>, TestState>
    {
        protected override void OnEnterState(TestState previousState)
        {
            Manager.Tick();
        }
    }

    /// <summary>
    /// Throws out of the entry hook, for the no-rollback contract: the failure reaches the caller
    /// wrapped, the guard is released anyway, and the machine is left on this state and usable.
    /// </summary>
    public sealed class ThrowingEnterState : BaseState<IStateManager<TestState>, TestState>
    {
        public const string FailureMessage = "ThrowingEnterState failed on purpose";

        protected override void OnEnterState(TestState previousState)
        {
            // Deliberately not a StateManagerException: the point is that the machine wraps a user
            // hook's own exception, which fixtures read back as the wrapper's inner exception.
            throw new InvalidOperationException(FailureMessage);
        }
    }

    /// <summary>A state closed over a concrete manager type, so a machine of any other type is refused.</summary>
    public sealed class PlainManagerState : BaseState<PlainStateManager, TestState>
    {
        public PlainStateManager ObservedManager => Manager;
    }

    /// <summary>A state closed over the machine interface, so one instance type attaches to a plain machine or a behaviour.</summary>
    public sealed class AnyManagerState : BaseState<IStateManager<TestState>, TestState>
    {
        public IStateManager<TestState> ObservedManager => Manager;
    }

    /// <summary>
    /// A state closed over <see cref="TypedFlowBehaviour"/>: it reports the behaviour it was added to
    /// and drives the machine through that behaviour from its update hook.
    /// </summary>
    public sealed class TypedBehaviourState : BaseState<TypedFlowBehaviour, TestState>
    {
        private readonly TestState _target;

        public TypedBehaviourState(TestState target)
        {
            _target = target;
        }

        public TypedFlowBehaviour ObservedManager => Manager;

        protected override void OnEnterState(TestState previousState)
        {
            CallLog.Record($"Enter:{StateType}:from:{previousState}");
        }

        protected override void OnExitState(TestState nextState)
        {
            CallLog.Record($"Exit:{StateType}:to:{nextState}");
        }

        protected override void OnUpdate()
        {
            CallLog.Record($"Update:{StateType}");
            Manager.ChangeState(_target);
        }
    }

    /// <summary>
    /// A machine whose states are registered from the outside with <c>AddState</c> and whose
    /// transitions are handed to the constructor, then replayed through <c>Transitions</c>.
    /// </summary>
    public sealed class PlainStateManager : BaseStateManager<TestState>
    {
        private readonly (TestState from, TestState to)[] _pairs;
        private readonly bool _allowAll;

        /// <summary>Creates a machine that will declare exactly <paramref name="pairs"/>.</summary>
        public PlainStateManager(params (TestState from, TestState to)[] pairs)
        {
            _pairs = pairs;
        }

        private PlainStateManager(bool allowAll)
        {
            _allowAll = allowAll;
            _pairs = new (TestState from, TestState to)[0];
        }

        /// <summary>Creates a machine whose hook opens every pair with <c>AllowAny</c>.</summary>
        public static PlainStateManager AllowingAll()
        {
            return new PlainStateManager(true);
        }

        protected override void OnInitialize()
        {
        }
    }

    /// <summary>
    /// Signature of <see cref="HookStateManager"/>'s transition-declaring callback. A named delegate
    /// because the <c>in</c> parameter cannot be written on an implicitly typed lambda.
    /// </summary>
    public delegate void TransitionsSetup(in Transitions<TestState> transitions);

    /// <summary>
    /// A machine whose two configuration hooks run caller-supplied callbacks and record that they ran.
    /// Either callback may be null, which makes that hook an empty override.
    /// </summary>
    public sealed class HookStateManager : BaseStateManager<TestState>
    {
        public const string OnInitializeEntry = "OnInitialize";

        public const string InsertTransitionsEntry = "InsertTransitions";

        private readonly Action<HookStateManager> _onInitialize;
        private readonly TransitionsSetup _insertTransitions;

        /// <summary>Creates the machine with the callbacks its two hooks will run.</summary>
        public HookStateManager(
            Action<HookStateManager> onInitialize = null,
            TransitionsSetup insertTransitions = null)
        {
            _onInitialize = onInitialize;
            _insertTransitions = insertTransitions;
        }

        public List<string> HookLog { get; } = new List<string>();

        protected override void OnInitialize()
        {
            HookLog.Add(OnInitializeEntry);
            _onInitialize?.Invoke(this);
        }
    }

    /// <summary>
    /// A machine that picks its own starting point. The override ignores base, and therefore also
    /// whatever <c>SetInitialState</c> recorded.
    /// </summary>
    public sealed class RedirectingStateManager : BaseStateManager<TestState>
    {
        private readonly TestState _initial;

        /// <summary>Creates the machine; <paramref name="initial"/> must still be registered.</summary>
        public RedirectingStateManager(TestState initial)
        {
            _initial = initial;
        }

        protected override TestState GetInitialState()
        {
            return _initial;
        }

        protected override void OnInitialize()
        {
        }
    }

    /// <summary>
    /// Concrete host used by the behaviour fixture. EditMode gives no player loop, so the
    /// <c>Invoke*</c> methods let a fixture drive the Unity lifecycle by hand.
    /// </summary>
    public sealed class TestFlowBehaviour : StateManagerBehaviour<TestState>
    {
        public BaseStateManager<TestState> MachineForTests => Machine;

        protected override void OnInitialize()
        {
            // The initial state is set explicitly even though A registers first: the behaviour
            // fixture should fail on a wiring change, not on a change to the first-registered default.
            AddState(TestState.A, new RecordingState())
                .AddState(TestState.B, new RecordingState())
                .SetInitialState(TestState.A);
        }

        protected override void InsertTransitions(in Transitions<TestState> transitions)
        {
            transitions.Allow(TestState.A, TestState.B)
                .Allow(TestState.B, TestState.A);
        }

        public void InvokeAwake()
        {
            Awake();
        }

        public void InvokeStart()
        {
            Start();
        }

        public void InvokeUpdate()
        {
            Update();
        }
    }

    /// <summary>
    /// A host whose states are closed over the host type itself, so a state authored against a
    /// behaviour sees that behaviour as its manager and can drive it.
    /// </summary>
    public sealed class TypedFlowBehaviour : StateManagerBehaviour<TestState>
    {
        public TypedBehaviourState StateA { get; } = new TypedBehaviourState(TestState.B);

        public TypedBehaviourState StateB { get; } = new TypedBehaviourState(TestState.A);

        public BaseStateManager<TestState> MachineForTests => Machine;

        protected override void OnInitialize()
        {
            AddState(TestState.A, StateA, new[] { TestState.B })
                .AddState(TestState.B, StateB, new[] { TestState.A })
                .SetInitialState(TestState.A);
        }

        public void InvokeAwake()
        {
            Awake();
        }

        public void InvokeStart()
        {
            Start();
        }

        public void InvokeUpdate()
        {
            Update();
        }
    }

    /// <summary>
    /// A host that configures itself entirely from its own hooks and records that they ran. Its
    /// initial state is neither the enum's default nor the first registered.
    /// </summary>
    public sealed class SelfConfiguringBehaviour : StateManagerBehaviour<TestState>
    {
        public const string OnInitializeEntry = "OnInitialize";

        public const string InsertTransitionsEntry = "InsertTransitions";

        public List<string> HookLog { get; } = new List<string>();

        protected override void OnInitialize()
        {
            HookLog.Add(OnInitializeEntry);

            AddState(TestState.A, new RecordingState())
                .AddState(TestState.B, new RecordingState())
                .AddState(TestState.C, new RecordingState())
                .SetInitialState(TestState.C);
        }

        protected override void InsertTransitions(in Transitions<TestState> transitions)
        {
            HookLog.Add(InsertTransitionsEntry);

            transitions.Allow(TestState.C, TestState.B);
        }

        public void InvokeAwake()
        {
            Awake();
        }

        public void InvokeStart()
        {
            Start();
        }
    }

    /// <summary>
    /// A host that never calls <c>SetInitialState</c> and registers <c>B</c> before <c>A</c>, so the
    /// state it enters can only be the first one registered.
    /// </summary>
    public sealed class DefaultInitialBehaviour : StateManagerBehaviour<TestState>
    {
        protected override void OnInitialize()
        {
            AddState(TestState.B, new RecordingState())
                .AddState(TestState.A, new RecordingState());
        }

        public void InvokeAwake()
        {
            Awake();
        }

        public void InvokeStart()
        {
            Start();
        }
    }
}
