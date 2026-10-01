using UnityEssentials.States;

public class InitializeState : BaseState<LifetimeStateManager, LifetimeState>
{
    protected override void OnEnterState(LifetimeState previousState)
    {
		//Load gameplay state for now, later we will connect main menu
		Manager.ChangeState(LifetimeState.MainMenuState);
    }
}

