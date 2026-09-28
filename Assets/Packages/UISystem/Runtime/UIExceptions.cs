using System;

namespace UnityEssentials.UI
{
    /// <summary>
    /// Base type for every error raised by the UI system. All of them signal programming or
    /// authoring errors — fix the wiring rather than catching and continuing. Normal-operation
    /// conditions never throw: <c>Hide()</c> on a hidden element is a no-op, <c>TryGetUI</c>
    /// returns false where <c>GetUI</c> would throw, and closing or disposing twice does nothing.
    /// </summary>
    public class UIException : Exception
    {
        public UIException()
        {
        }

        public UIException(string message) : base(message)
        {
        }

        public UIException(string message, Exception innerException) : base(message, innerException)
        {
        }
    }

    /// <summary>
    /// Thrown when a <c>WindowData</c> asset cannot produce a window: a null entry in its UI Prefabs
    /// list, a prefab carrying no <c>UIBase</c> component, or two prefabs of the same concrete type.
    /// Raised before anything is built, so the live UI is left untouched.
    /// </summary>
    public sealed class WindowConfigurationException : UIException
    {
        public WindowConfigurationException()
        {
        }

        public WindowConfigurationException(string message) : base(message)
        {
        }

        public WindowConfigurationException(string message, Exception innerException) : base(message, innerException)
        {
        }
    }

    /// <summary>
    /// Thrown when a requested UI type is not present in the active window — the prefab is missing
    /// from the window's UI Prefabs list, or the wrong window is loaded. The message lists what the
    /// window does carry, so the fix is usually visible in the stack trace alone.
    /// </summary>
    public sealed class UIElementNotFoundException : UIException
    {
        public UIElementNotFoundException()
        {
        }

        public UIElementNotFoundException(string message) : base(message)
        {
        }

        public UIElementNotFoundException(string message, Exception innerException) : base(message, innerException)
        {
        }
    }

    /// <summary>
    /// Thrown when a resolve, show or hide call is made on a <c>UIService</c> that has no active
    /// window — before the first <c>SwitchWindow</c> or after <c>CloseWindow</c>. Query-shaped
    /// members answer the empty state instead of throwing this.
    /// </summary>
    public sealed class NoActiveWindowException : UIException
    {
        public NoActiveWindowException()
        {
        }

        public NoActiveWindowException(string message) : base(message)
        {
        }

        public NoActiveWindowException(string message, Exception innerException) : base(message, innerException)
        {
        }
    }

    /// <summary>
    /// Thrown when an element's binding to its <c>UIWindow</c> is wrong: showing an element that no
    /// window has bound, or binding an element to a second window. The rule is uniform — every
    /// <c>UIBase</c> must be bound before it can be shown, so an element dragged into a scene by
    /// hand instead of being listed on a <c>WindowData</c> fails loudly on its first show.
    /// </summary>
    public sealed class UIBindingException : UIException
    {
        public UIBindingException()
        {
        }

        public UIBindingException(string message) : base(message)
        {
        }

        public UIBindingException(string message, Exception innerException) : base(message, innerException)
        {
        }
    }
}
