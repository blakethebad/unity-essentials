using System;

namespace UnityEssentials.UI
{
    /// <summary>
    /// Base type for every error raised by the UI system. All of them signal programming or
    /// authoring errors — fix the wiring rather than catching and continuing. Normal-operation
    /// conditions never throw: <c>Back()</c> returns false, <c>Hide()</c> on a hidden element is a
    /// no-op, and <c>TryGetUI</c> returns false where <c>GetUI</c> would throw.
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
    /// Thrown when a <c>WindowData</c> asset cannot produce a window: a null entry in either list,
    /// a prefab carrying no UI component, two prefabs of the same concrete type, or a prefab in the
    /// wrong list. Raised before anything is built, so the live UI is left untouched.
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
    /// Thrown when a requested UI type is not present in the active window, when a widget type is
    /// passed to a resolve-shaped call, or when a widget spawn has no registered prototype. The
    /// message lists what the window does carry.
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
    /// Thrown when a resolve, show, hide or spawn call is made on a <c>WindowService</c> that has no
    /// active window — before the first <c>SwitchWindow</c> or after <c>CloseWindow</c>. Query-shaped
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
    /// Thrown when an element's binding to its <c>UIWindow</c> is wrong: showing or hiding an
    /// unbound screen, popup or panel, binding an element to a second window, or despawning a widget
    /// through a window that does not own it. Widgets are exempt from the show/hide check.
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
