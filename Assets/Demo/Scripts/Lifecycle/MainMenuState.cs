using UnityEssentials.States;

public class MainMenuState : BaseState<LifetimeStateManager, LifetimeState>
{
    public override LifetimeState StateType => LifetimeState.MainMenuState;

    protected override void OnEnterState(LifetimeState previousState)
    {
    }
}

