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

		EventBus.Subscribe<EntityCollectedEvent>(OnEntityCollected);
		EventBus.Subscribe<EntityGrabbedEvent>(Test_OnEntityGrabbed);

		_levelTimer = levelScreenData.levelTimer;

		// No second is displayed yet, and -1 is a second the timer can never report, so the first
		// refresh always writes the text instead of matching a stale cache.
		_displayedSeconds = -1;
		RefreshTimerText();
    }

    protected override void OnHide()
    {
		EventBus.Unsubscribe<EntityCollectedEvent>(OnEntityCollected);
		EventBus.Unsubscribe<EntityGrabbedEvent>(Test_OnEntityGrabbed);

		_levelTimer = null;
    }

	private void Update()
	{
		RefreshTimerText();
	}

	private void RefreshTimerText()
	{
		if (_levelTimer == null)
		{
			return;
		}

		var remaining = _levelTimer.Remaining;

		// The text only changes once a second, so formatting every frame would allocate a string
		// per frame to write the same digits back.
		var wholeSeconds = Mathf.CeilToInt(remaining);
		if (wholeSeconds == _displayedSeconds)
		{
			return;
		}

		_displayedSeconds = wholeSeconds;
		timerText.SetText(remaining.ToTimeString(roundUp: true));
	}

	private void OnEntityCollected(EntityCollectedEvent entityCollectedEvent)
	{
		Log.Error("Entity got collected");
	}

	private void Test_OnEntityGrabbed(EntityGrabbedEvent entityGrabbedEvent)
	{
		Log.Error($"Entity with name {entityGrabbedEvent.EntityName}");
	}
}
