using System.Collections.Generic;
using UnityEngine;
using UnityEssentials.Services;
using UnityEssentials.UI;

public class Main : MonoBehaviour
{
	//TODO: Move these out later
	[SerializeField] private Board boardPrefab;
	[SerializeField] private WindowData mainWindowData;
	[SerializeField] private List<BoardEntity> tempObjectPool;

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

		_lifetimeStateManager = new LifetimeStateManager(boardPrefab, tempObjectPool);

		uiService.SwitchWindow(mainWindowData);
		_lifetimeStateManager.Initialize();

		_completedInstallation = true;
	}
}
