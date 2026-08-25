using System;
using UnityEngine;

namespace UnityEssentials.UI
{
    /// <summary>
    /// The root of the element hierarchy: a four-state show/hide machine, the transition seam tweens
    /// plug into, and the reference back to the owning <see cref="UIWindow"/>. Do not derive from
    /// this directly — derive from <c>ScreenBase</c>, <c>PopupBase</c> or <c>PanelBase</c>. Widgets
    /// are a separate hierarchy rooted at <see cref="UIWidget"/>.
    /// </summary>
    [DisallowMultipleComponent]
    public abstract class UIBase : MonoBehaviour
    {
        private int _transitionToken;

        public UIElementState State { get; private set; }

        public bool IsVisible => State == UIElementState.Showing || State == UIElementState.Shown;

        public UIWindow Window { get; private set; }

        protected WindowService Service => Window != null ? Window.Service : null;

        /// <summary>Shows the element, running <see cref="OnShow(IUIData)"/> and then the show transition.</summary>
        public abstract void Show(IUIData uiData = null);

        /// <summary>Hides the element. Hiding an already-hidden element is an idempotent no-op.</summary>
        public abstract void Hide();

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

        /// <summary>
        /// Per-show setup: bind data, reset scroll positions, subscribe to events. Runs after the
        /// GameObject is activated and before the show transition. Must tolerate a null payload.
        /// </summary>
        protected virtual void OnShow(IUIData uiData)
        {
        }

        /// <summary>
        /// Teardown for a single showing: unsubscribe, stop coroutines. Runs at the start of hiding,
        /// while the GameObject is still active.
        /// </summary>
        protected virtual void OnHide()
        {
        }

        /// <summary>
        /// The show-transition seam. The base calls <paramref name="complete"/> immediately;
        /// override to run a tween and invoke the callback when it ends.
        /// </summary>
        protected virtual void OnShowTransition(Action complete)
        {
            complete();
        }

        /// <summary>
        /// The hide-transition seam. The GameObject stays active until <paramref name="complete"/>
        /// is invoked, so an exit tween always runs on a live object.
        /// </summary>
        protected virtual void OnHideTransition(Action complete)
        {
            complete();
        }

        private protected void ExecuteShow(IUIData uiData)
        {
            // A fresh token invalidates any in-flight hide, so a stale hide completion can never
            // deactivate the object underneath this show.
            var token = ++_transitionToken;
            State = UIElementState.Showing;
            if (!gameObject.activeSelf) { gameObject.SetActive(true); }
            OnShow(uiData);
            AfterShowRequested();
            OnShowTransition(() => CompleteShow(token));
        }

        private protected void ExecuteHide()
        {
            if (State == UIElementState.Hidden || State == UIElementState.Hiding)
            {
                return;
            }

            var token = ++_transitionToken;
            State = UIElementState.Hiding;
            OnHide();
            OnHideTransition(() => CompleteHide(token));
        }

        // Runs between OnShow and the show transition; ScreenBase opens its AutoPanels here.
        private protected virtual void AfterShowRequested()
        {
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

    /// <summary>
    /// The four states a <see cref="UIBase"/> can be in: <c>Hidden → Showing → Shown → Hiding →
    /// Hidden</c>. The state flips at the start of a transition, so the active lists reflect intent
    /// — a Back press mid-transition targets what the user is actually looking at.
    /// </summary>
    public enum UIElementState : byte
    {
        Hidden = 0,
        Showing = 1,
        Shown = 2,
        Hiding = 3
    }
}
