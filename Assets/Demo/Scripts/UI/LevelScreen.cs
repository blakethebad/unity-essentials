using TMPro;
using UnityEngine;
using UnityEssentials.Extensions;
using UnityEssentials.UI;
using UnityEssentials.Utilities;

public class LevelScreenData : IUIData
{
	//Not sure about the direct referencing here but we will see
	public Timer levelTimer;
}

public class LevelScreen : UIBase
{
	[SerializeField] private TextMeshProUGUI scoreText;
	[SerializeField] private TextMeshProUGUI timerText;

	private Timer _levelTimer;
	private int _displayedSeconds;

    protected override void OnShow(IUIData uiData)
    {
		if(uiData is not LevelScreenData levelScreenData)
		{
			Log.Error("Cannot show level screen without a valid LevelScreenData");
			return;
		}


		_levelTimer = levelScreenData.levelTimer;

		_displayedSeconds = -1;
		RefreshTimerText();
		scoreText.SetText("0");
		
		EventBus.Subscribe<EntityCollectedEvent>(OnEntityCollected);
    }

    protected override void OnHide()
    {
		_levelTimer = null;
		EventBus.Unsubscribe<EntityCollectedEvent>(OnEntityCollected);
    }

	private void Update()
	{
		RefreshTimerText();
	}

	private void RefreshTimerText()
	{
		if (_levelTimer == null)
			return;

		var remaining = _levelTimer.Remaining;

		var wholeSeconds = Mathf.CeilToInt(remaining);
		if (wholeSeconds == _displayedSeconds)
			return;

		_displayedSeconds = wholeSeconds;
		timerText.SetText(remaining.ToTimeString(roundUp: true));
	}

	private void OnEntityCollected(EntityCollectedEvent entityCollectedEvent)
	{
		scoreText.SetText(entityCollectedEvent.CurrentScore.ToString());
	}
}
