namespace UnityEssentials.UI
{
    /// <summary>
    /// Base class for a piece of screen composition — a nav bar, a currency header. Panels are
    /// tracked only so they can be closed with the screen that owns them; they never enter the
    /// history, so <c>Back()</c> can never close one. Override <c>OnShow</c>/<c>OnHide</c> —
    /// <c>Show</c>/<c>Hide</c> are sealed.
    /// </summary>
    public abstract class PanelBase : UIBase
    {
        /// <summary>Opens the panel. Sibling order is left as authored — panels are layout, not a stack.</summary>
        public sealed override void Show(IUIData uiData = null)
        {
            if (Window == null)
            {
                throw new UIBindingException(
                    $"UI element '{GetType().Name}' is not bound to a UIWindow. Add its prefab to the WindowData " +
                    "asset for the window it belongs to and let the window instantiate it.");
            }

            Service.OnPanelShowing(this);
            ExecuteShow(uiData);
        }

        /// <summary>Closes the panel. No history is touched — a panel was never in one.</summary>
        public sealed override void Hide()
        {
            if (State == UIElementState.Hidden || State == UIElementState.Hiding) { return; }

            if (Window == null)
            {
                throw new UIBindingException(
                    $"UI element '{GetType().Name}' is not bound to a UIWindow. Add its prefab to the WindowData " +
                    "asset for the window it belongs to and let the window instantiate it.");
            }

            Service.OnPanelHiding(this);
            ExecuteHide();
        }
    }
}
