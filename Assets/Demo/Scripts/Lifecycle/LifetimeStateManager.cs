using System.Collections.Generic;
using UnityEssentials.Services;
using UnityEssentials.States;
using UnityEssentials.UI;

public enum LifetimeState
{
	InitializeState = 0,
	MainMenuState = 1,
	GameplayState = 2
}

public class LifetimeStateManager : StateManagerBehaviour<LifetimeState>
{
	public Board board;
	public WindowData mainWindowData;
	public List<BoardEntity> tempObjectPool;

    protected override void OnInitialize()
    {
		AddState(new InitializeState());
		AddState(new MainMenuState());
		AddState(new GameplayState());

		//TODO: Maybe move it somewhere else
		ServiceLocator.Register<UIService>(new UIService());
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

