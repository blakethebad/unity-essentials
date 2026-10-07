using System;
using UnityEngine;

namespace UnityEssentials.UI
{
    public enum UIElementState : byte
    {
        Hidden = 0,
        Showing = 1,
        Shown = 2,
        Hiding = 3
    }

    [DisallowMultipleComponent]
    public abstract class UIBase : MonoBehaviour
    {
        private int _transitionToken;

        public UIElementState State { get; private set; }

        public bool IsVisible => State == UIElementState.Showing || State == UIElementState.Shown;

        public UIWindow Window { get; private set; }

        protected UIService Service => Window != null ? Window.Service : null;

        public void Show(IUIData uiData = null)
        {
            if (Window == null)
            {
                throw new UIBindingException(
                    $"UI element '{GetType().Name}' is not bound to a UIWindow. Add its prefab to the " +
                    "WindowData asset for the window it belongs to and let the window instantiate it.");
            }

            var token = ++_transitionToken;
            State = UIElementState.Showing;
            if (!gameObject.activeSelf) { gameObject.SetActive(true); }
            OnShow(uiData);

            OnShowTransition(() => CompleteShow(token));
        }

        public void Hide()
        {
            if (State == UIElementState.Hidden || State == UIElementState.Hiding)
            {
                return;
            }

            var token = ++_transitionToken;
            State = UIElementState.Hiding;
            Service?.RaiseHidden(this);
            OnHide();
            OnHideTransition(() => CompleteHide(token));
        }

        internal void Bind(UIWindow window)
        {
            if (window == null)
            {
                throw new ArgumentNullException(nameof(window));
            }

            if (Window != null && Window != window)
            {
                throw new UIBindingException(
                    $"UI element '{GetType().Name}' is already bound to window '{Window.name}' and cannot be " +
                    $"bound to '{window.name}'. An element belongs to exactly one window for its whole lifetime; " +
                    "list its prefab in the other window's WindowData instead of re-binding this instance.");
            }

            Window = window;
        }

        protected abstract void OnShow(IUIData uiData);

        protected abstract void OnHide();

        protected virtual void OnShowTransition(Action complete)
        {
            complete();
        }

        protected virtual void OnHideTransition(Action complete)
        {
            complete();
        }

        private void CompleteShow(int token)
        {
            if (token != _transitionToken) { return; }
            if (State == UIElementState.Shown) { return; }
            State = UIElementState.Shown;
            Service?.RaiseShown(this);
        }

        private void CompleteHide(int token)
        {
            if (token != _transitionToken) { return; }
            if (State == UIElementState.Hidden) { return; }
            State = UIElementState.Hidden;
            gameObject.SetActive(false);
        }
    }
}
