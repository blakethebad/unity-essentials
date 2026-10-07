using System;

namespace UnityEssentials.States
{
    public class StateManagerException : Exception
    {
        public StateManagerException()
        {
        }

        public StateManagerException(string message) : base(message)
        {
        }

        public StateManagerException(string message, Exception innerException) : base(message, innerException)
        {
        }
    }

    public sealed class InvalidTransitionException : StateManagerException
    {
        public InvalidTransitionException()
        {
        }

        public InvalidTransitionException(string message) : base(message)
        {
        }

        public InvalidTransitionException(string message, Exception innerException) : base(message, innerException)
        {
        }
    }

    public sealed class StateConfigurationException : StateManagerException
    {
        public StateConfigurationException()
        {
        }

        public StateConfigurationException(string message) : base(message)
        {
        }

        public StateConfigurationException(string message, Exception innerException) : base(message, innerException)
        {
        }
    }
}
