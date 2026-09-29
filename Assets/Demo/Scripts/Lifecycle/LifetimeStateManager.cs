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

public class LifetimeStateManager : BaseStateManager<LifetimeState>, IFlowService
{
	public Board boardPrefab;
	public List<BoardEntity> tempObjectPool;

	public LifetimeStateManager(Board boardPrefab, List<BoardEntity> tempObjectPool)
	{
		this.boardPrefab = boardPrefab;
		this.tempObjectPool = tempObjectPool;
	}

    protected override void OnInitialize()
    {
		AddState(new InitializeState());
		AddState(new MainMenuState());
		AddState(new GameplayState());
	}

    protected override void InsertTransitions(in Transitions<LifetimeState> transitions)
    {
		transitions.Allow(LifetimeState.InitializeState, LifetimeState.MainMenuState);
		transitions.Allow(LifetimeState.InitializeState, LifetimeState.GameplayState);
		transitions.Allow(LifetimeState.MainMenuState, LifetimeState.GameplayState);
		transitions.Allow(LifetimeState.GameplayState, LifetimeState.MainMenuState);
    }

    public void StartLevel()
    {
        throw new System.NotImplementedException();
    }

    public void QuitLevel()
    {
        throw new System.NotImplementedException();
    }
}

