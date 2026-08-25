using UnityEssentials.States;

public enum TestoState
{
    Idle,
    Active,
    Dead
}

public class Testo : BaseStateManager<TestoState>
{
    protected override void OnInitialize()
    {
        AddState(new IdleTestoState());
        AddState(new ActiveTestoState());
        AddState(new DeadTestoState());
    }

    protected override void InsertTransitions(in Transitions<TestoState> transitions)
    {
        transitions.Allow(TestoState.Idle, TestoState.Active);
        transitions.Allow(TestoState.Idle, TestoState.Dead);
    }
}

public class IdleTestoState : BaseState<Testo, TestoState>
{
    public override TestoState StateType => TestoState.Idle;

    protected override void OnEnterState(TestoState previousState)
    {
    }
}

public class ActiveTestoState : BaseState<Testo, TestoState>
{
    public override TestoState StateType => TestoState.Active;
}

public class DeadTestoState : BaseState<Testo, TestoState>
{
    public override TestoState StateType => TestoState.Dead;
}
