using Unity.VisualScripting;
using UnityEssentials.Services;
using UnityEssentials.States;
using UnityEssentials.UI;

public class MainMenuState : BaseState<LifetimeStateManager, LifetimeState>
{
	private UIService _uiService;

	public MainMenuState()
	{
		ServiceLocator.TryGet<UIService>(out _uiService);
	}

    protected override void OnEnterState(LifetimeState previousState)
    {
		_uiService.ShowUI<MainMenuScreen>();
    }

    protected override void OnExitState(LifetimeState nextState)
    {
		_uiService.HideUI<MainMenuScreen>();
    }
}

