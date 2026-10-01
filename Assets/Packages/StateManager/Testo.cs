using System.Collections.Generic;
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
        AddState(TestoState.Idle, new IdleTestoState(), new List<TestoState>
        {
            TestoState.Active,
            TestoState.Dead
        });

        AddState(TestoState.Active, new ActiveTestoState());
        AddState(TestoState.Dead, new DeadTestoState());
    }
}

public class IdleTestoState : BaseState<Testo, TestoState>
{
    protected override void OnEnterState(TestoState previousState)
    {
    }
}

public class ActiveTestoState : BaseState<Testo, TestoState>
{
}

public class DeadTestoState : BaseState<Testo, TestoState>
{
}
