namespace UnityEssentials.UI
{
    /// <summary>
    /// Base class for a spawned, repeatable element — health bars, damage numbers, toasts. Widgets
    /// are created on demand through <c>WindowService.GetWidget&lt;T&gt;()</c> and handed back with
    /// <c>ReturnWidget</c>; they register with nothing, are invisible to navigation, and work
    /// unbound — which lets this double as the base class for any plain animated element.
    /// </summary>
    public abstract class WidgetBase : UIWidget
    {
        /// <summary>Shows the widget. No binding required and no service registration.</summary>
        public sealed override void Show(IUIData uiData = null)
        {
            ExecuteShow(uiData);
        }

        /// <summary>Hides the widget without destroying it; <c>ReturnWidget</c> is what destroys.</summary>
        public sealed override void Hide()
        {
            ExecuteHide();
        }
    }
}
