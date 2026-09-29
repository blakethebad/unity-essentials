using UnityEngine;
using UnityEngine.UI;
using UnityEssentials.Services;
using UnityEssentials.UI;

public class MainMenuScreen : UIBase
{
	[SerializeField] private Button button;

	private IFlowService _gameFlowHandler;

	private void Awake()
	{
		button.onClick.AddListener(OnPlayButtonPressed);
	}

	private void OnDestroy()
	{
		button.onClick.RemoveAllListeners();
	}

    protected override void OnShow(IUIData uiData)
    {
		_gameFlowHandler = ServiceLocator.Get<IFlowService>();
    }

    protected override void OnHide()
    {
		_gameFlowHandler = null;
    }

	private void OnPlayButtonPressed()
	{
		_gameFlowHandler.StartLevel();
	}
}
