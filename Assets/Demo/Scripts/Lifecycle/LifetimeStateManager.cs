using System.Collections.Generic;
using UnityEssentials.States;

public enum LifetimeState
{
	InitializeState = 0,
	MainMenuState = 1,
	GameplayState = 2
}

public class LifetimeStateManager : StateManagerBehaviour<LifetimeState>
{
	public BoardController board;
	public List<BoardEntity> tempObjectPool;

    protected override void OnInitialize()
    {
		AddState(new InitializeState());
		AddState(new MainMenuState());
		AddState(new GameplayState());
    }

    protected override void InsertTransitions(in Transitions<LifetimeState> transitions)
    {
		transitions.Allow(LifetimeState.InitializeState, LifetimeState.MainMenuState);
		//Direct to gameplay for now, until the main menu is connected
		transitions.Allow(LifetimeState.InitializeState, LifetimeState.GameplayState);
		transitions.Allow(LifetimeState.MainMenuState, LifetimeState.GameplayState);
		transitions.Allow(LifetimeState.GameplayState, LifetimeState.MainMenuState);
    }
}

