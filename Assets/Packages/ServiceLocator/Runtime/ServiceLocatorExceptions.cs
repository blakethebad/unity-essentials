using System;

namespace UnityEssentials.Services
{
    /// <summary>
    /// Base type for every error raised by the service locator. Catching this type catches both
    /// <see cref="ServiceNotFoundException"/> and <see cref="ServiceRegistrationException"/>.
    /// </summary>
    /// <remarks>
    /// All locator exceptions signal programming errors (a missing registration, a duplicate key,
    /// a circular dependency, use from a background thread) rather than recoverable conditions.
    /// </remarks>
    public class ServiceLocatorException : Exception
    {
        /// <summary>Creates the exception with no message.</summary>
        public ServiceLocatorException()
        {
        }

        /// <summary>Creates the exception with the given message.</summary>
        /// <param name="message">Describes what went wrong.</param>
        public ServiceLocatorException(string message) : base(message)
        {
        }

        /// <summary>Creates the exception with the given message and inner exception.</summary>
        /// <param name="message">Describes what went wrong.</param>
        /// <param name="innerException">The exception that caused this one.</param>
        public ServiceLocatorException(string message, Exception innerException) : base(message, innerException)
        {
        }
    }

    /// <summary>
    /// Thrown by <see cref="ServiceLocator.Get{T}"/> and <see cref="ServiceScope.Get{T}"/> when no
    /// registration is published for the requested type. <see cref="ServiceLocator.TryGet{T}"/>
    /// returns <c>false</c> instead of throwing this.
    /// </summary>
    public sealed class ServiceNotFoundException : ServiceLocatorException
    {
        /// <summary>Creates the exception with no message.</summary>
        public ServiceNotFoundException()
        {
        }

        /// <summary>Creates the exception with the given message.</summary>
        /// <param name="message">Describes the type that could not be resolved.</param>
        public ServiceNotFoundException(string message) : base(message)
        {
        }

        /// <summary>Creates the exception with the given message and inner exception.</summary>
        /// <param name="message">Describes the type that could not be resolved.</param>
        /// <param name="innerException">The exception that caused this one.</param>
        public ServiceNotFoundException(string message, Exception innerException) : base(message, innerException)
        {
        }
    }

    /// <summary>
    /// Thrown while registering a service: duplicate key in the same scope, an interface the
    /// concrete type does not implement, more than four interface keys, <c>NonLazy()</c> on a
    /// transient registration, or a duplicate/invalid scope name.
    /// </summary>
    public sealed class ServiceRegistrationException : ServiceLocatorException
    {
        /// <summary>Creates the exception with no message.</summary>
        public ServiceRegistrationException()
        {
        }

        /// <summary>Creates the exception with the given message.</summary>
        /// <param name="message">Describes why the registration is invalid.</param>
        public ServiceRegistrationException(string message) : base(message)
        {
        }

        /// <summary>Creates the exception with the given message and inner exception.</summary>
        /// <param name="message">Describes why the registration is invalid.</param>
        /// <param name="innerException">The exception that caused this one.</param>
        public ServiceRegistrationException(string message, Exception innerException) : base(message, innerException)
        {
        }
    }
}
