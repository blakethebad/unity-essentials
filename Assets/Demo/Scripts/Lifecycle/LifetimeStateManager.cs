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
	public LevelData CurrentLevel { get; private set; }
	public Board boardPrefab;

	private ILevelService _levelService;

	public LifetimeStateManager(Board boardPrefab)
	{
		this.boardPrefab = boardPrefab;
		_levelService = ServiceLocator.Get<ILevelService>(); //TODO: Normally using this on constructor is really bad. Lets find a better way.
	}

    protected override void OnInitialize()
    {
		var initializeState = new InitializeState();
		var mainMenuState = new MainMenuState();
		var gameplayState = new GameplayState();

		var initializeStateTransitions = new List<LifetimeState> { LifetimeState.MainMenuState, LifetimeState.GameplayState };
		var mainMenuTransitions = new List<LifetimeState> { LifetimeState.GameplayState };
		var gameplayTransitions = new List<LifetimeState> { LifetimeState.MainMenuState, LifetimeState.GameplayState };

		AddState(LifetimeState.InitializeState, initializeState, initializeStateTransitions);
		AddState(LifetimeState.MainMenuState, mainMenuState, mainMenuTransitions);
		AddState(LifetimeState.GameplayState, gameplayState, gameplayTransitions);
	}

    public void StartNextLevel()
    {
		var levelData = _levelService.GetLevelWithIndex(0); //TODO: Right now we don't have a saving system. So lets move on with only using the first level.
		CurrentLevel = levelData;
		ChangeState(LifetimeState.GameplayState);
    }

    public void RestartLastLevel()
    {
		var levelData = _levelService.GetLevelWithIndex(0); //TODO: Right now we don't have a saving system. So lets move on with only using the first level.
		CurrentLevel = levelData;
		ChangeState(LifetimeState.GameplayState);
    }

    public void QuitLevel()
    {
		ChangeState(LifetimeState.MainMenuState);
    }
}

