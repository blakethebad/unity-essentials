using TMPro;
using UnityEngine;
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

    protected override void OnShow(IUIData uiData)
    {
		if(uiData is not LevelScreenData levelScreenData)
		{
			Log.Error("Cannot show level screen without a valid LevelScreenData");
			return;
		}

		EventBus.Subscribe<EntityCollectedEvent>(OnEntityCollected);
		EventBus.Subscribe<EntityGrabbedEvent>(Test_OnEntityGrabbed);
		var timer = levelScreenData.levelTimer;
    }

    protected override void OnHide()
    {
		EventBus.Unsubscribe<EntityCollectedEvent>(OnEntityCollected);
		EventBus.Unsubscribe<EntityGrabbedEvent>(Test_OnEntityGrabbed);
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
