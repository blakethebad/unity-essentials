using System;
using UnityEngine;

namespace UnityEssentials.UI
{
    /// <summary>
    /// The package's entry point: load a window, then show, hide and resolve the UI inside it. One
    /// service owns the parent its windows live under and one active <see cref="UIWindow"/>, which
    /// holds exactly one cached instance of every element type its <see cref="WindowData"/> lists.
    /// </summary>
    /// <remarks>
    /// The service deliberately keeps no record of which elements are visible or in what order: an
    /// element's own <see cref="UIBase.State"/> is the single source of truth, so there is no
    /// bookkeeping that can drift out of sync with the scene. Observe lifecycle from the outside
    /// through <see cref="UIShown"/> and <see cref="UIHidden"/>.
    /// </remarks>
    public sealed class UIService : IDisposable
    {
        internal const string WindowParentName = "UnityEssentials.WindowParent";

        private Transform _windowParent;
        private UIWindow _activeWindow;

        /// <summary>
        /// Raised after a window switch completes, with the outgoing and incoming assets.
        /// <c>from</c> is null on the first load; <c>to</c> is null when the window was closed.
        /// </summary>
        /// <remarks>
        /// This is what makes caching a <see cref="GetUI{T}"/> result safe: the instances belong to
        /// the window, so every cached reference dies with it. Drop your caches here and re-resolve
        /// against the incoming window.
        /// </remarks>
        public event Action<WindowData, WindowData> WindowSwitched;

        /// <summary>Raised when an element finishes showing — when it enters <see cref="UIElementState.Shown"/>.</summary>
        /// <remarks>
        /// With no open-element lists on the service, this and <see cref="UIHidden"/> are the only
        /// way to watch UI lifecycle from outside an element — the hook for analytics, audio or
        /// input-blocking that must not live inside the elements themselves.
        /// </remarks>
        public event Action<UIBase> UIShown;

        /// <summary>Raised when an element starts hiding — when it enters <see cref="UIElementState.Hiding"/>.</summary>
        /// <remarks>
        /// Fires at the start of the hide, not at its end, so a listener sees the element leave
        /// while its transition is still running and can react in the same frame the user did.
        /// </remarks>
        public event Action<UIBase> UIHidden;

        /// <summary>The window currently loaded, or null when none is.</summary>
        public UIWindow ActiveWindow => _activeWindow;

        /// <summary>The asset the active window was generated from, or null when no window is loaded.</summary>
        public WindowData ActiveWindowData => _activeWindow != null ? _activeWindow.Data : null;

        /// <summary>True while a window is loaded. False before the first <see cref="SwitchWindow"/> and after <see cref="CloseWindow"/>.</summary>
        public bool IsWindowLoaded => _activeWindow != null;

        /// <summary>The lazily created scene object every window of this service is parented under.</summary>
        /// <remarks>
        /// Created on first use rather than in the constructor so that a service which never loads a
        /// window leaves nothing behind in the scene.
        /// </remarks>
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
        /// Generates and loads a window from <paramref name="windowData"/> as the active window,
        /// unloading whatever was loaded before. Shows nothing: follow it with <see cref="ShowUI{T}"/>.
        /// </summary>
        /// <param name="windowData">The asset describing the window to build.</param>
        /// <returns>The loaded window — the one that was already active when <paramref name="windowData"/> is the asset it was built from.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="windowData"/> is null.</exception>
        /// <exception cref="WindowConfigurationException">The asset cannot produce a window.</exception>
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
        /// Closes the active window: unloads everything the window created and destroys the window
        /// parent, leaving the service reusable and the scene clean.
        /// </summary>
        /// <remarks>
        /// A no-op beyond clearing the parent when no window is loaded, and <see cref="WindowSwitched"/>
        /// only fires when there really was a window to close.
        /// </remarks>
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
        /// <typeparam name="T">The concrete element type listed by the active window's asset.</typeparam>
        /// <param name="uiData">Optional payload handed to the element's <c>OnShow</c>.</param>
        /// <returns>The window's instance of <typeparamref name="T"/>.</returns>
        /// <exception cref="NoActiveWindowException">No window is loaded.</exception>
        /// <exception cref="UIElementNotFoundException">The active window carries no element of that type.</exception>
        public T ShowUI<T>(IUIData uiData = null) where T : UIBase
        {
            var element = Resolve(typeof(T));
            element.Show(uiData);
            return (T)element;
        }

        /// <summary>Resolves <typeparamref name="T"/> in the active window and hides it.</summary>
        /// <typeparam name="T">The concrete element type listed by the active window's asset.</typeparam>
        /// <exception cref="NoActiveWindowException">No window is loaded.</exception>
        /// <exception cref="UIElementNotFoundException">The active window carries no element of that type.</exception>
        public void HideUI<T>() where T : UIBase
        {
            Resolve(typeof(T)).Hide();
        }

        /// <summary>Returns the active window's instance of <typeparamref name="T"/>, visible or not.</summary>
        /// <typeparam name="T">The concrete element type listed by the active window's asset.</typeparam>
        /// <returns>The single cached instance the window holds for that type.</returns>
        /// <exception cref="NoActiveWindowException">No window is loaded.</exception>
        /// <exception cref="UIElementNotFoundException">The active window carries no element of that type.</exception>
        public T GetUI<T>() where T : UIBase
        {
            return (T)Resolve(typeof(T));
        }

        /// <summary>
        /// Tries to resolve <typeparamref name="T"/>, returning false instead of throwing when it is
        /// not there — including when no window is loaded.
        /// </summary>
        /// <typeparam name="T">The concrete element type to look for.</typeparam>
        /// <param name="ui">The resolved element, or null when the call returns false.</param>
        /// <returns>True when the active window carries an instance of <typeparamref name="T"/>.</returns>
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

        /// <summary>Closes the active window and releases the scene objects this service owns.</summary>
        /// <remarks>
        /// Identical to <see cref="CloseWindow"/>, and idempotent: disposing twice is a no-op rather
        /// than a throw. A service is cheap to reconstruct and carries no unmanaged state, so an
        /// <see cref="ObjectDisposedException"/> here would only be a hazard in test teardown and
        /// <c>using</c> blocks that also close explicitly.
        /// </remarks>
        public void Dispose()
        {
            CloseWindow();
        }

        /// <summary>Raises <see cref="UIShown"/> for <paramref name="element"/>. Called by <see cref="UIBase"/> as it finishes showing.</summary>
        /// <param name="element">The element that became visible.</param>
        internal void RaiseShown(UIBase element)
        {
            if (element == null)
            {
                return;
            }

            UIShown?.Invoke(element);
        }

        /// <summary>Raises <see cref="UIHidden"/> for <paramref name="element"/>. Called by <see cref="UIBase"/> as it starts hiding.</summary>
        /// <param name="element">The element that began hiding.</param>
        internal void RaiseHidden(UIBase element)
        {
            if (element == null)
            {
                return;
            }

            UIHidden?.Invoke(element);
        }

        private void UnloadActiveWindow()
        {
            var window = _activeWindow;
            if (window == null)
            {
                return;
            }

            _activeWindow = null;

            // Ordering guarantee lives in UnloadWindow: it runs OnHide on every visible element
            // while those elements are still alive, then destroys them.
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

        private UIBase Resolve(Type uiType)
        {
            var window = _activeWindow;
            if (window == null)
            {
                throw new NoActiveWindowException(
                    "Cannot resolve a UI element: this UIService has no active window — either " +
                    "SwitchWindow has not been called yet, or CloseWindow has since run; the stack trace " +
                    "names the operation that needed it. Load a window first: SwitchWindow(windowData); " +
                    "ShowUI<MainMenu>();");
            }

            return window.GetUI(uiType);
        }
    }
}
