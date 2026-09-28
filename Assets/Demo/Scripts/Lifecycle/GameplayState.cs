using UnityEditor;
using UnityEngine;
using UnityEssentials.Services;
using UnityEssentials.States;
using UnityEssentials.UI;
using UnityEssentials.Utilities;

public class GameplayState : BaseState<LifetimeStateManager, LifetimeState>
{
    public override LifetimeState StateType => LifetimeState.GameplayState;

	private Timer _levelTimer;

    protected override void OnEnterState(LifetimeState previousState)
    {
		var uiService = ServiceLocator.Get<UIService>();
		uiService.SwitchWindow(Manager.mainWindowData);

		_levelTimer = new Timer(60f);

		uiService.ShowUI<LevelScreen>(new LevelScreenData()
		{
			levelTimer = _levelTimer
		});

		_levelTimer.Completed += OnTimerCompleted;
		_levelTimer.Start();

		Board board = Object.Instantiate(Manager.board);

		board.GenerateObjects(Manager.tempObjectPool);
    }

	private void OnTimerCompleted()
	{
		//TODO: Implement this later
		EditorApplication.ExitPlaymode();
		// var uiService = ServiceLocator.Get<UIService>();
		// uiService.ShowUI<LevelEndPopup>();
	}
}

