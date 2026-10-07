using System;

namespace UnityEssentials.UI
{
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
