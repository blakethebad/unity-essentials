using UnityEditor;
using UnityEngine;
using UnityEssentials.Services;
using UnityEssentials.States;
using UnityEssentials.UI;
using UnityEssentials.Utilities;

public class GameplayState : BaseState<LifetimeStateManager, LifetimeState>
{
	private UIService _uiService;
	private Timer _levelTimer;
	private Board _activeBoard;

	public GameplayState()
	{
		ServiceLocator.TryGet<UIService>(out _uiService);
	}

    protected override void OnEnterState(LifetimeState previousState)
    {
		_levelTimer = new Timer(60f);

		_uiService.ShowUI<LevelScreen>(new LevelScreenData()
		{
			levelTimer = _levelTimer
		});

		_activeBoard = Object.Instantiate(Manager.boardPrefab);
		_activeBoard.SpawnLevel(Manager.CurrentLevel);

		_levelTimer.Completed += OnTimerCompleted;
		_levelTimer.Start();
    }

    protected override void OnExitState(LifetimeState nextState)
    {
		Object.Destroy(_activeBoard.gameObject);
		//clear the board here
		_uiService.HideUI<LevelScreen>();
		_activeBoard.ClearBoard();
    }

	private void OnTimerCompleted()
	{
		_uiService.ShowUI<LevelEndPopup>(new LevelEndPopupData());
	}
}

