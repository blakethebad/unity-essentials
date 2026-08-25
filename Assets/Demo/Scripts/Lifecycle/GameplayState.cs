using UnityEngine;
using UnityEssentials.States;
using UnityEssentials.Utilities;

public class GameplayState : BaseState<LifetimeStateManager, LifetimeState>
{
    public override LifetimeState StateType => LifetimeState.GameplayState;

	private Timer _levelTimer;

    protected override void OnEnterState(LifetimeState previousState)
    {
		_levelTimer = new Timer();

		_levelTimer.Start();
		_levelTimer.Completed += OnTimerCompleted;

		BoardController board = Object.Instantiate(Manager.board);

		board.GenerateObjects(Manager.tempObjectPool);
    }

	private void OnTimerCompleted()
	{
		
	}
}

