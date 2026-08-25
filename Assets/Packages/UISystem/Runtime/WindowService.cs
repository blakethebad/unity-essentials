using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using UnityEngine;

namespace UnityEssentials.UI
{
    /// <summary>
    /// The package's entry point: load a window, then show, hide, resolve and navigate the UI inside
    /// it. One service owns the parent its windows live under, one active <see cref="UIWindow"/>,
    /// the active screen, the open popups and panels, and the navigation history that
    /// <see cref="Back"/> walks.
    /// </summary>
    public sealed class WindowService
    {
        internal const string WindowParentName = "UnityEssentials.WindowParent";

        private readonly UIHistory<UIBase> _history = new UIHistory<UIBase>();
        private readonly List<PopupBase> _activePopups = new List<PopupBase>();
        private readonly List<PanelBase> _activePanels = new List<PanelBase>();
        private readonly ReadOnlyCollection<PopupBase> _popupsView;
        private readonly ReadOnlyCollection<PanelBase> _panelsView;
        private Transform _windowParent;
        private UIWindow _activeWindow;
        private ScreenBase _activeScreen;

        /// <summary>Creates a service with no window loaded. Nothing exists in the scene until <see cref="SwitchWindow"/>.</summary>
        public WindowService()
        {
            _popupsView = new ReadOnlyCollection<PopupBase>(_activePopups);
            _panelsView = new ReadOnlyCollection<PanelBase>(_activePanels);
        }

        public UIWindow ActiveWindow => _activeWindow;

        public WindowData ActiveWindowData => _activeWindow != null ? _activeWindow.Data : null;

        public bool IsWindowLoaded => _activeWindow != null;

        public ScreenBase ActiveScreen => _activeScreen;

        public ScreenBase PreviousScreen => _history.PreviousScreen(_activeScreen) as ScreenBase;

        public IReadOnlyList<PopupBase> ActivePopups => _popupsView;

        public IReadOnlyList<PanelBase> ActivePanels => _panelsView;

        internal Transform WindowParent
        {
            get
            {
                // Unity's overloaded ==: a parent destroyed by a scene teardown reports itself as
                // null, so the next access builds a fresh one instead of handing out a dead object.
                if (_windowParent != null)
                {
                    return _windowParent;
                }

                var host = new GameObject(WindowParentName);
                if (Application.isPlaying)
                {
                    // Illegal outside play mode, where scene teardown cleans up instead.
                    UnityEngine.Object.DontDestroyOnLoad(host);
                }

                _windowParent = host.transform;
                return _windowParent;
            }
        }

        /// <summary>
        /// Raised after a window switch completes, with the outgoing and incoming assets.
        /// <c>from</c> is null on the first load; <c>to</c> is null when the window was closed.
        /// </summary>
        public event Action<WindowData, WindowData> WindowSwitched;

        /// <summary>Raised when an element finishes showing — when it enters <see cref="UIElementState.Shown"/>.</summary>
        public event Action<UIBase> UIShown;

        /// <summary>Raised when an element starts hiding — when it enters <see cref="UIElementState.Hiding"/>.</summary>
        public event Action<UIBase> UIHidden;

        /// <summary>
        /// Generates and loads a window from <paramref name="windowData"/> as the active window,
        /// unloading whatever was loaded before. Shows nothing: follow it with <see cref="ShowUI{T}"/>.
        /// </summary>
        public UIWindow SwitchWindow(WindowData windowData)
        {
            if (windowData == null)
            {
                throw new ArgumentNullException(nameof(windowData));
            }

            // Identity, not Unity's ==: two different assets are never "the same window", and a
            // destroyed asset must not be mistaken for a null one.
            if (_activeWindow != null && ReferenceEquals(_activeWindow.Data, windowData))
            {
                return _activeWindow;
            }

            // Validate before anything is unloaded or built: a throw here must leave the live
            // window untouched.
            windowData.ThrowIfInvalid();

            var previousData = ActiveWindowData;
            UnloadActiveWindow();

            _activeWindow = windowData.GenerateWindow(WindowParent);
            _activeWindow.LoadWindow(this);

            WindowSwitched?.Invoke(previousData, windowData);
            return _activeWindow;
        }

        /// <summary>
        /// Closes the active window: hides its popups, panels and screen, clears the history,
        /// unloads everything the window created and destroys the window parent.
        /// </summary>
        public void CloseWindow()
        {
            if (_activeWindow == null)
            {
                DestroyWindowParent();
                return;
            }

            var previousData = ActiveWindowData;
            UnloadActiveWindow();
            DestroyWindowParent();
            WindowSwitched?.Invoke(previousData, null);
        }

        /// <summary>
        /// Resolves <typeparamref name="T"/> in the active window and shows it, forwarding
        /// <paramref name="uiData"/> to its <c>OnShow</c>.
        /// </summary>
        public T ShowUI<T>(IUIData uiData = null) where T : UIBase
        {
            var element = Resolve(typeof(T));
            element.Show(uiData);
            return (T)element;
        }

        /// <summary>Resolves <typeparamref name="T"/> in the active window and hides it.</summary>
        public void HideUI<T>() where T : UIBase
        {
            Resolve(typeof(T)).Hide();
        }

        /// <summary>Returns the active window's instance of <typeparamref name="T"/>, visible or not.</summary>
        public T GetUI<T>() where T : UIBase
        {
            return (T)Resolve(typeof(T));
        }

        /// <summary>
        /// Tries to resolve <typeparamref name="T"/>, returning false instead of throwing when it is
        /// not there — including when no window is loaded.
        /// </summary>
        public bool TryGetUI<T>(out T ui) where T : UIBase
        {
            ui = null;

            var window = _activeWindow;
            if (window == null)
            {
                return false;
            }

            if (!window.TryGetUI(typeof(T), out var element) || element == null)
            {
                return false;
            }

            ui = (T)element;
            return true;
        }

        /// <summary>
        /// Spawns a widget from the prototype the active window lists, parents it (null means the
        /// window itself), binds it and shows it. Hand it back with <see cref="ReturnWidget"/>.
        /// </summary>
        public T GetWidget<T>(Transform parent = null, IUIData uiData = null) where T : WidgetBase
        {
            var window = _activeWindow;
            if (window == null)
            {
                throw new NoActiveWindowException(
                    "Cannot get a widget: this WindowService has no active window — either SwitchWindow " +
                    "has not been called yet, or CloseWindow has since run. Load a window first: " +
                    "SwitchWindow(windowData); ShowUI<MainMenuScreen>();");
            }

            return (T)window.GetWidget(typeof(T), parent, uiData);
        }

        /// <summary>Hands a spawned widget back to the window that spawned it: hidden, untracked and destroyed.</summary>
        public void ReturnWidget(WidgetBase widget)
        {
            var window = _activeWindow;
            if (window == null)
            {
                throw new NoActiveWindowException(
                    "Cannot return a widget: this WindowService has no active window — either SwitchWindow " +
                    "has not been called yet, or CloseWindow has since run. Load a window first: " +
                    "SwitchWindow(windowData); ShowUI<MainMenuScreen>();");
            }

            window.ReturnWidget(widget);
        }

        /// <summary>
        /// Performs one back step: closes the topmost popup if one is open, otherwise returns to the
        /// previous screen. False when there is nothing to go back to — never throws.
        /// </summary>
        public bool Back()
        {
            if (_activeWindow == null)
            {
                return false;
            }

            var top = _history.Top;
            if (top == null)
            {
                return false;
            }

            if (!(top is ScreenBase))
            {
                top.Hide();
                return true;
            }

            return ShowPreviousScreen();
        }

        /// <summary>
        /// Returns to the screen the user came from, re-showing it with the data it was last opened
        /// with. Works even when no screen is currently visible.
        /// </summary>
        public bool ShowPreviousScreen()
        {
            var previous = _history.PreviousScreen(_activeScreen, out var data) as ScreenBase;
            if (previous == null)
            {
                return false;
            }

            _history.TruncateTopScreen(_activeScreen);
            previous.Show(data);
            return true;
        }

        /// <summary>Hides every open popup, newest first. A no-op when none are open.</summary>
        public void HideAllPopups()
        {
            HideAllInReverse(_activePopups);
        }

        /// <summary>Hides every open panel, newest first. A no-op when none are open.</summary>
        public void HideAllPanels()
        {
            HideAllInReverse(_activePanels);
        }

        internal void OnScreenShowing(ScreenBase screen, IUIData uiData)
        {
            if (screen == null)
            {
                return;
            }

            var previous = _activeScreen;
            _activeScreen = screen;

            // PUSH BEFORE HIDE. The trail prunes a hidden screen only when it is the trail top, so
            // pushing the incoming screen first is what keeps the outgoing one as PreviousScreen.
            _history.PushScreen(screen, uiData);

            HideAllInReverse(_activePopups);
            HideAllInReverse(_activePanels);

            if (previous != null && !ReferenceEquals(previous, screen))
            {
                previous.Hide();
            }
        }

        internal void OnScreenDataRefreshed(ScreenBase screen, IUIData uiData)
        {
            _history.UpdateData(screen, uiData);
        }

        internal void OnScreenHiding(ScreenBase screen)
        {
            if (screen == null)
            {
                return;
            }

            _history.Hide(screen, true);

            if (ReferenceEquals(_activeScreen, screen))
            {
                _activeScreen = null;
            }

            UIHidden?.Invoke(screen);
        }

        internal void OnPopupShowing(PopupBase popup, IUIData uiData)
        {
            if (popup == null)
            {
                return;
            }

            RemoveByIdentity(_activePopups, popup);
            _activePopups.Add(popup);
            _history.PushOverlay(popup, uiData);
        }

        internal void OnPopupHiding(PopupBase popup)
        {
            if (popup == null)
            {
                return;
            }

            RemoveByIdentity(_activePopups, popup);
            _history.Hide(popup, false);
            UIHidden?.Invoke(popup);
        }

        internal void OnPanelShowing(PanelBase panel)
        {
            if (panel == null)
            {
                return;
            }

            RemoveByIdentity(_activePanels, panel);
            _activePanels.Add(panel);
        }

        internal void OnPanelHiding(PanelBase panel)
        {
            if (panel == null)
            {
                return;
            }

            RemoveByIdentity(_activePanels, panel);
            UIHidden?.Invoke(panel);
        }

        internal void ShowAutoPanel(ScreenBase owner, Type panelType, IUIData data)
        {
            var ownerName = owner != null ? owner.GetType().Name : "ScreenBase";

            if (panelType == null)
            {
                throw new WindowConfigurationException(
                    $"Screen '{ownerName}' declares a null entry in its AutoPanels array. Every entry must be a " +
                    "concrete type deriving from PanelBase — check for a missing typeof(...) or a stale element " +
                    "left behind after an edit.");
            }

            if (!typeof(PanelBase).IsAssignableFrom(panelType))
            {
                throw new WindowConfigurationException(
                    $"Screen '{ownerName}' declares '{panelType.FullName}' in its AutoPanels array, but that type " +
                    "does not derive from PanelBase. Auto-panels may only be panels: screens are shown through " +
                    "ShowUI<T>(), popups open on demand, and widgets are spawned.");
            }

            var window = _activeWindow;
            if (window == null)
            {
                throw new NoActiveWindowException(
                    $"Screen '{ownerName}' tried to open auto-panel '{panelType.Name}' with no active window. " +
                    "Load a window first: SwitchWindow(windowData); ShowUI<MainMenuScreen>();");
            }

            if (!window.TryGetUI(panelType, out var element) || element == null)
            {
                throw new UIElementNotFoundException(
                    $"Screen '{ownerName}' declares auto-panel '{panelType.Name}', but the active window " +
                    $"('{window.name}') carries no element of that type. Add the panel's prefab to the UI Prefabs " +
                    "list of the WindowData this screen belongs to.");
            }

            ((PanelBase)element).Show(data);
        }

        internal void RaiseShown(UIBase element)
        {
            if (element == null)
            {
                return;
            }

            UIShown?.Invoke(element);
        }

        private void UnloadActiveWindow()
        {
            var window = _activeWindow;
            if (window == null)
            {
                ClearBookkeeping();
                return;
            }

            // Hide before destroy, popups first: OnHide is the author's teardown hook and must run
            // while the elements are still alive.
            HideAllInReverse(_activePopups);
            HideAllInReverse(_activePanels);

            var screen = _activeScreen;
            if (screen != null)
            {
                screen.Hide();
            }

            ClearBookkeeping();
            _activeWindow = null;
            window.UnloadWindow();
        }

        private void DestroyWindowParent()
        {
            if (_windowParent != null)
            {
                UIWindow.SafeDestroy(_windowParent.gameObject);
            }

            _windowParent = null;
        }

        private void ClearBookkeeping()
        {
            _activeScreen = null;
            _activePopups.Clear();
            _activePanels.Clear();
            _history.Clear();
        }

        private UIBase Resolve(Type uiType)
        {
            var window = _activeWindow;
            if (window == null)
            {
                throw new NoActiveWindowException(
                    "Cannot resolve a UI element: this WindowService has no active window — either " +
                    "SwitchWindow has not been called yet, or CloseWindow has since run; the stack trace " +
                    "names the operation that needed it. Load a window first: SwitchWindow(windowData); " +
                    "ShowUI<MainMenuScreen>();");
            }

            return window.GetUI(uiType);
        }

        private static void HideAllInReverse<TElement>(List<TElement> active) where TElement : UIBase
        {
            if (active.Count == 0)
            {
                return;
            }

            // Snapshot: each Hide calls back into OnPopupHiding/OnPanelHiding, which mutates the
            // live list. A destroyed element cannot deregister itself, so it is dropped here.
            var snapshot = active.ToArray();
            for (var i = snapshot.Length - 1; i >= 0; i--)
            {
                UIBase element = snapshot[i];
                if (element == null)
                {
                    RemoveByIdentity(active, snapshot[i]);
                    continue;
                }

                element.Hide();
            }
        }

        // List.Remove routes through Unity's overridden equality, under which a destroyed object
        // equals null — identity is the only comparison that stays correct across destruction.
        private static void RemoveByIdentity<TElement>(List<TElement> list, TElement element) where TElement : class
        {
            for (var i = 0; i < list.Count; i++)
            {
                if (ReferenceEquals(list[i], element))
                {
                    list.RemoveAt(i);
                    return;
                }
            }
        }
    }
}
