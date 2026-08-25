namespace UnityEssentials.UI
{
    /// <summary>
    /// Base class for a transient overlay stacked on top of everything else — dialogs, modals. Any
    /// number can be open at once, the newest draws on top, and they are the first thing
    /// <c>WindowService.Back()</c> closes. Override <c>OnShow</c>/<c>OnHide</c> —
    /// <c>Show</c>/<c>Hide</c> are sealed.
    /// </summary>
    public abstract class PopupBase : UIBase
    {
        /// <summary>Opens the popup and sorts it in front of its siblings, so the newest draws on top.</summary>
        public sealed override void Show(IUIData uiData = null)
        {
            if (Window == null)
            {
                throw new UIBindingException(
                    $"UI element '{GetType().Name}' is not bound to a UIWindow. Add its prefab to the WindowData " +
                    "asset for the window it belongs to and let the window instantiate it.");
            }

            Service.OnPopupShowing(this, uiData);
            ExecuteShow(uiData);
            transform.SetAsLastSibling();
        }

        /// <summary>Closes the popup. Out-of-order closing is supported; the rest of the stack keeps its order.</summary>
        public sealed override void Hide()
        {
            if (State == UIElementState.Hidden || State == UIElementState.Hiding) { return; }

            if (Window == null)
            {
                throw new UIBindingException(
                    $"UI element '{GetType().Name}' is not bound to a UIWindow. Add its prefab to the WindowData " +
                    "asset for the window it belongs to and let the window instantiate it.");
            }

            Service.OnPopupHiding(this);
            ExecuteHide();
        }
    }
}
