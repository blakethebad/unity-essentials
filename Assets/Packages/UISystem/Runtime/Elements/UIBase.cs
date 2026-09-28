using System;
using UnityEngine;

namespace UnityEssentials.UI
{
    /// <summary>
    /// The base class for every UI element: a four-state show/hide machine, the transition seam tweens
    /// plug into, and the reference back to the owning <see cref="UIWindow"/>. Derive from this
    /// directly — there is one element kind, so there is no intermediate base to pick between.
    /// </summary>
    /// <remarks>
    /// An element only works once it is bound: a <see cref="UIWindow"/> instantiates it from the prefab
    /// list on a <see cref="WindowData"/> asset and calls <see cref="Bind(UIWindow)"/>, which is what
    /// gives it a <see cref="Window"/> and therefore a <see cref="Service"/>. That makes this a UI
    /// element base, not a general-purpose base for animated components — a <see cref="UIBase"/>
    /// dropped into a scene by hand and shown from your own code throws
    /// <see cref="UIBindingException"/>. Use a plain <see cref="MonoBehaviour"/> for anything a window
    /// does not own.
    /// </remarks>
    [DisallowMultipleComponent]
    public abstract class UIBase : MonoBehaviour
    {
        private int _transitionToken;

        /// <summary>Where the element currently is in the <c>Hidden → Showing → Shown → Hiding</c> cycle.</summary>
        public UIElementState State { get; private set; }

        /// <summary>Whether the element is on screen or on its way there — <c>Showing</c> or <c>Shown</c>.</summary>
        public bool IsVisible => State == UIElementState.Showing || State == UIElementState.Shown;

        /// <summary>The window that instantiated and owns this element, or <c>null</c> until it is bound.</summary>
        public UIWindow Window { get; private set; }

        /// <summary>The service driving the owning window, or <c>null</c> while this element is unbound.</summary>
        protected UIService Service => Window != null ? Window.Service : null;

        /// <summary>
        /// Shows the element, running <see cref="OnShow(IUIData)"/> and then the show transition.
        /// </summary>
        /// <param name="uiData">Optional payload handed to <see cref="OnShow(IUIData)"/>; may be <c>null</c>.</param>
        /// <exception cref="UIBindingException">The element is not bound to a <see cref="UIWindow"/>.</exception>
        /// <remarks>
        /// This method is the designated expansion seam. The trimmed package has exactly one element
        /// kind, but the kind-specific bases (<c>ScreenBase</c>, <c>PopupBase</c>, <c>PanelBase</c>) are
        /// meant to come back as pure additions, and each of them needs to run registration around the
        /// show. When that happens <see cref="Show(IUIData)"/> becomes <c>virtual</c> and those bases
        /// <c>override</c> it. They must never declare their own <c>Show</c> with <c>new</c>: a <c>new</c>
        /// method would be silently bypassed, because <see cref="UIService.ShowUI{T}(IUIData)"/> resolves
        /// the element into a <see cref="UIBase"/>-typed local and calls <c>Show</c> through it, so the
        /// call binds to this declaration and the derived one never runs. Keeping the seam here, rather
        /// than making callers go through a kind-specific entry point, is what makes that later change
        /// additive.
        /// </remarks>
        public void Show(IUIData uiData = null)
        {
            if (Window == null)
            {
                throw new UIBindingException(
                    $"UI element '{GetType().Name}' is not bound to a UIWindow. Add its prefab to the " +
                    "WindowData asset for the window it belongs to and let the window instantiate it.");
            }

            ExecuteShow(uiData);
        }

        /// <summary>Hides the element. Hiding an already-hidden element is an idempotent no-op.</summary>
        /// <remarks>
        /// The already-hidden check runs ahead of any binding concern, so tearing down an element that
        /// was never shown — and possibly never bound — stays silent instead of throwing.
        /// </remarks>
        public void Hide()
        {
            if (State == UIElementState.Hidden || State == UIElementState.Hiding)
            {
                return;
            }

            ExecuteHide();
        }

        /// <summary>
        /// Attaches this element to the window that instantiated it. Called once, by
        /// <see cref="UIWindow.LoadWindow(UIService)"/>; re-binding to a different window is an error.
        /// </summary>
        /// <param name="window">The owning window.</param>
        /// <exception cref="ArgumentNullException"><paramref name="window"/> is <c>null</c>.</exception>
        /// <exception cref="UIBindingException">The element is already bound to a different window.</exception>
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
        protected abstract void OnShow(IUIData uiData);

        /// <summary>
        /// Teardown for a single showing: unsubscribe, stop coroutines. Runs at the start of hiding,
        /// while the GameObject is still active.
        /// </summary>
        protected abstract void OnHide();

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

        // Kept factored out of Show instead of inlined into it. When the kind-specific bases return they
        // override Show to register with the service, and they need to do that *between* the binding
        // check and the state change — routing through base.Show() would re-run the guard and leave no
        // slot for it. ExecuteHide is factored out for the same reason.
        private protected void ExecuteShow(IUIData uiData)
        {
            // A fresh token invalidates any in-flight hide, so a stale hide completion can never
            // deactivate the object underneath this show.
            var token = ++_transitionToken;
            State = UIElementState.Showing;
            if (!gameObject.activeSelf) { gameObject.SetActive(true); }
            OnShow(uiData);

            // The closure over `token` allocates a closure and a delegate per show, and that cost is
            // deliberate: it must not be "optimised" away by hoisting the delegate into a field that
            // reads a token *field*. With two hides in flight the stale hide's completion would then
            // read the field already advanced to the newer token, compare equal, and wrongly deactivate
            // the object. The token has to be captured per call for the comparison to mean anything.
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

            // Raised here, on entering Hiding, rather than from CompleteHide: that is where the deleted
            // service hooks raised it, so the observable timing is unchanged. Note this is deliberately
            // not the mirror of UIShown, which fires on reaching Shown — state flips at the start of a
            // transition, so UIHidden reports intent while the element is still on screen.
            Service?.RaiseHidden(this);
            OnHide();
            OnHideTransition(() => CompleteHide(token));
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
    /// Hidden</c>. The state flips at the start of a transition rather than at its end, so
    /// <see cref="UIBase.IsVisible"/> reports intent — an element whose exit tween is still playing
    /// already reads as not visible.
    /// </summary>
    public enum UIElementState : byte
    {
        Hidden = 0,
        Showing = 1,
        Shown = 2,
        Hiding = 3
    }
}
