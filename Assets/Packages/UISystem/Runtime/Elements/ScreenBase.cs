using System;

namespace UnityEssentials.UI
{
    /// <summary>
    /// Base class for a full-screen, mutually exclusive view. Exactly one screen is active at a
    /// time: showing a screen supersedes the previous one, closes every open popup and panel, and
    /// appends a step to the back trail that <c>WindowService.Back()</c> walks. Override
    /// <c>OnShow</c>/<c>OnHide</c> — <c>Show</c>/<c>Hide</c> are sealed.
    /// </summary>
    public abstract class ScreenBase : UIBase
    {
        /// <summary>
        /// Makes this the active screen. Re-showing the already-active screen is a refresh: only
        /// <c>OnShow</c> re-runs, with no history churn and no panel flicker.
        /// </summary>
        public sealed override void Show(IUIData uiData = null)
        {
            if (Window == null)
            {
                throw new UIBindingException(
                    $"UI element '{GetType().Name}' is not bound to a UIWindow. Add its prefab to the WindowData " +
                    "asset for the window it belongs to and let the window instantiate it.");
            }

            var service = Service;

            if (IsVisible && service.ActiveScreen == this)
            {
                service.OnScreenDataRefreshed(this, uiData);
                OnShow(uiData);
                return;
            }

            service.OnScreenShowing(this, uiData);
            ExecuteShow(uiData);
        }

        /// <summary>Closes this screen, leaving no screen active. The trail retreats accordingly.</summary>
        public sealed override void Hide()
        {
            if (State == UIElementState.Hidden || State == UIElementState.Hiding) { return; }

            if (Window == null)
            {
                throw new UIBindingException(
                    $"UI element '{GetType().Name}' is not bound to a UIWindow. Add its prefab to the WindowData " +
                    "asset for the window it belongs to and let the window instantiate it.");
            }

            Service.OnScreenHiding(this);
            ExecuteHide();
        }

        /// <summary>
        /// The panel types this screen opens automatically on every show, in declaration order.
        /// Return a <c>static readonly</c> array — this is read on every show.
        /// </summary>
        protected virtual Type[] AutoPanels => Array.Empty<Type>();

        /// <summary>Supplies the payload for each auto-panel's <c>OnShow</c>. Default: null for every panel.</summary>
        protected virtual IUIData GetAutoPanelData(Type panelType)
        {
            return null;
        }

        private protected override void AfterShowRequested()
        {
            var panels = AutoPanels;
            if (panels == null) { return; }

            var service = Service;
            foreach (var panelType in panels)
            {
                service.ShowAutoPanel(this, panelType, GetAutoPanelData(panelType));
            }
        }
    }
}
