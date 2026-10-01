using System.Collections.Generic;
using UnityEngine;
using UnityEssentials.Services;
using UnityEssentials.UI;

public class Main : MonoBehaviour
{
	//TODO: Move these out later
	[SerializeField] private Board boardPrefab; //TODO: Move to asset management later
	[SerializeField] private WindowData mainWindowData;
	[SerializeField] private List<LevelData> levels; //TODO: Move to asset management later

	private LifetimeStateManager _lifetimeStateManager;
	private bool _completedInstallation = false;

    private void Awake()
	{
		InstallAndInitialize();
	}

	private void Update()
	{
		if(!_completedInstallation)
			return;

		_lifetimeStateManager.Tick();
	}

	private void InstallAndInitialize()
	{
		Object.DontDestroyOnLoad(this.transform);

		var uiService = new UIService();
		ServiceLocator.Register(uiService).As<UIService>();

		var levelRepo = new LevelRepo(levels);
		ServiceLocator.Register(levelRepo).As<ILevelService>();

		_lifetimeStateManager = new LifetimeStateManager(boardPrefab);
		ServiceLocator.Register(_lifetimeStateManager).As<IFlowService>();

		uiService.SwitchWindow(mainWindowData);
		_lifetimeStateManager.Initialize();

		_completedInstallation = true;
	}
}
