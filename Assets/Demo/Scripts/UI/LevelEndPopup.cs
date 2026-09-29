using UnityEngine;
using TMPro;
using UnityEssentials.UI;
using UnityEngine.UI;
using UnityEssentials.Utilities;
using UnityEssentials.Extensions;
using UnityEssentials.Services;

public class LevelEndPopupData : IUIData
{
	public bool IsWin;
	public float Score;
	public float TimeRemained;
}

public class LevelEndPopup : UIBase
{
	[SerializeField] private TextMeshProUGUI resultText;
	[SerializeField] private TextMeshProUGUI levelScoreText;
	[SerializeField] private TextMeshProUGUI remainingTimeText;

	[SerializeField] private Button continueButton;
	[SerializeField] private Button mainMenuButton;

	private IFlowService _gameFlowHandler;

	private void Awake()
	{
		continueButton.onClick.AddListener(OnContinuePressed);
		mainMenuButton.onClick.AddListener(OnMainMenuPressed);
	}

	private void OnDestroy()
	{
		continueButton.onClick.RemoveAllListeners();
		mainMenuButton.onClick.RemoveAllListeners();
	}

    protected override void OnShow(IUIData uiData)
    {
		if(uiData is not LevelEndPopupData levelEndPopupData)
		{
			Log.Error("Level End Popup needs to be shown with matching data");
			return;
		}

		_gameFlowHandler = ServiceLocator.Get<IFlowService>();

		//TODO: Ofcourse its not victory and defeat but lets see how it goes.
		// resultText.SetText(levelEndPopupData.IsWin ? "VICTORY" : "DEFEAT");
		// remainingTimeText.SetText(levelEndPopupData.TimeRemained.ToTimeString());
		// levelScoreText.gameObject.SetActive(levelEndPopupData.IsWin);
		// levelScoreText.SetText(levelEndPopupData.Score.ToString());
    }

    protected override void OnHide()
    {
    }

	private void OnContinuePressed()
	{
		//Right now regardless for win lose lets move on with restarting the level
		Hide();
		_gameFlowHandler.StartLevel();
	}

	private void OnMainMenuPressed()
	{
		Hide();
		_gameFlowHandler.QuitLevel();
	}
}
