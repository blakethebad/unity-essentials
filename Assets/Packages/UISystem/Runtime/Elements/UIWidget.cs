using System;
using UnityEngine;

namespace UnityEssentials.UI
{
    /// <summary>
    /// The root of the widget hierarchy, deliberately separate from <see cref="UIBase"/>: widgets
    /// are spawned through <c>GetWidget</c> and handed back through <c>ReturnWidget</c>, never
    /// resolved, and they touch none of the navigation machinery. Do not derive from this directly —
    /// derive from <see cref="WidgetBase"/>.
    /// </summary>
    [DisallowMultipleComponent]
    public abstract class UIWidget : MonoBehaviour
    {
        private int _transitionToken;

        public UIElementState State { get; private set; }

        public bool IsVisible => State == UIElementState.Showing || State == UIElementState.Shown;

        public UIWindow Window { get; private set; }

        /// <summary>Shows the widget, running <see cref="OnShow(IUIData)"/> and then the show transition.</summary>
        public abstract void Show(IUIData uiData = null);

        /// <summary>Hides the widget. Hiding an already-hidden widget is an idempotent no-op.</summary>
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
                    $"Widget '{GetType().Name}' is already bound to window '{Window.name}' and cannot be " +
                    $"bound to '{window.name}'. A widget belongs to the window that spawned it for its whole " +
                    "lifetime; return this instance and get a fresh one from the other window instead.");
            }

            Window = window;
        }

        /// <summary>
        /// Per-show setup: bind data, reset state, subscribe to events. Runs after the GameObject is
        /// activated and before the show transition. Must tolerate a null payload.
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

        private void CompleteShow(int token)
        {
            if (token != _transitionToken) { return; }
            if (State == UIElementState.Shown) { return; }
            State = UIElementState.Shown;
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
