using System;
using UnityEngine;

namespace UnityEssentials.UI
{
    public sealed class UIService : IDisposable
    {
        internal const string WindowParentName = "UnityEssentials.WindowParent";

        public event Action<UIBase> UIShown;

        public event Action<UIBase> UIHidden;

        public UIWindow ActiveWindow => _activeWindow;

        public WindowData ActiveWindowData => _activeWindow != null ? _activeWindow.Data : null;

        public bool IsWindowLoaded => _activeWindow != null;

        internal Transform WindowParent
        {
            get
            {
                if (_windowParent != null)
                    return _windowParent;

                var host = new GameObject(WindowParentName);
                if (Application.isPlaying)
                    UnityEngine.Object.DontDestroyOnLoad(host);

                _windowParent = host.transform;
                return _windowParent;
            }
        }

        private Transform _windowParent;
        private UIWindow _activeWindow;

        public UIWindow SwitchWindow(WindowData windowData)
        {
            if (windowData == null)
                throw new ArgumentNullException(nameof(windowData));

            if (_activeWindow != null && ReferenceEquals(_activeWindow.Data, windowData))
                return _activeWindow;

            windowData.ThrowIfInvalid();

            UnloadActiveWindow();

            _activeWindow = windowData.GenerateWindow(WindowParent);
            _activeWindow.LoadWindow(this);

            return _activeWindow;
        }

        public void CloseWindow()
        {
            if (_activeWindow == null)
            {
                DestroyWindowParent();
                return;
            }

            UnloadActiveWindow();
            DestroyWindowParent();
        }

        public T ShowUI<T>(IUIData uiData = null) where T : UIBase
        {
            var element = Resolve(typeof(T));
            element.Show(uiData);
            return (T)element;
        }

        public void HideUI<T>() where T : UIBase
        {
            Resolve(typeof(T)).Hide();
        }

        public T GetUI<T>() where T : UIBase
        {
            return (T)Resolve(typeof(T));
        }

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

        public void Dispose()
        {
            CloseWindow();
        }

        internal void RaiseShown(UIBase element)
        {
            if (element == null)
            {
                return;
            }

            UIShown?.Invoke(element);
        }

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
