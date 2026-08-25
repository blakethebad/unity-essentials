using System;

namespace UnityEssentials.States
{
    /// <summary>
    /// Base type for every error raised by the state machine. Catching this type catches both
    /// <see cref="InvalidTransitionException"/> and <see cref="StateConfigurationException"/>, and
    /// is also thrown directly for re-entrancy violations (a hook or event handler driving the
    /// machine while a transition is still in flight).
    /// </summary>
    /// <remarks>
    /// It is also the wrapper for anything that escapes a state hook
    /// (<see cref="BaseState{TState}.EnterState"/>, <see cref="BaseState{TState}.ExitState"/>,
    /// <see cref="BaseState{TState}.RestartState"/>) or a
    /// <see cref="BaseStateManager{TState}.StateExited"/>/<see cref="BaseStateManager{TState}.StateEntered"/>
    /// handler during <see cref="BaseStateManager{TState}.Initialize"/>,
    /// <see cref="BaseStateManager{TState}.ChangeState"/> or
    /// <see cref="BaseStateManager{TState}.RestartState"/>: the message names the failing stage and the
    /// states involved and repeats the original exception's type and message, and the original is
    /// kept as <see cref="Exception.InnerException"/>. Failures inside
    /// <see cref="BaseStateManager{TState}.Tick"/> are not wrapped — ordinary per-frame code propagates
    /// as written.
    /// <para>
    /// All state-machine exceptions signal programming errors — an unregistered state, a transition
    /// the table never allowed, configuration after <see cref="BaseStateManager{TState}.Initialize"/> —
    /// rather than recoverable conditions. Guard with
    /// <see cref="BaseStateManager{TState}.CanChangeState"/> instead of catching.
    /// </para>
    /// </remarks>
    public class StateManagerException : Exception
    {
        /// <summary>Creates the exception with no message.</summary>
        public StateManagerException()
        {
        }

        /// <summary>Creates the exception with the given message.</summary>
        /// <param name="message">Describes what went wrong.</param>
        public StateManagerException(string message) : base(message)
        {
        }

        /// <summary>Creates the exception with the given message and inner exception.</summary>
        /// <param name="message">Describes what went wrong.</param>
        /// <param name="innerException">The exception that caused this one.</param>
        public StateManagerException(string message, Exception innerException) : base(message, innerException)
        {
        }
    }

    /// <summary>
    /// Thrown by <see cref="BaseStateManager{TState}.ChangeState"/> when the requested pair is not in
    /// the transition table. The table denies everything until
    /// <see cref="Transitions{TState}.Allow(TState, TState)"/> (or
    /// <see cref="Transitions{TState}.AllowAny"/>) opens a pair, and it is directional:
    /// allowing <c>(a, b)</c> does not allow <c>(b, a)</c>, nor does it allow the self-transition
    /// <c>(a, a)</c>.
    /// </summary>
    /// <remarks>
    /// This is the only exception that means "the machine is configured correctly, the caller asked
    /// for something the design forbids". <see cref="BaseStateManager{TState}.CanChangeState"/> answers
    /// the same question without throwing.
    /// </remarks>
    public sealed class InvalidTransitionException : StateManagerException
    {
        /// <summary>Creates the exception with no message.</summary>
        public InvalidTransitionException()
        {
        }

        /// <summary>Creates the exception with the given message.</summary>
        /// <param name="message">Describes the rejected transition.</param>
        public InvalidTransitionException(string message) : base(message)
        {
        }

        /// <summary>Creates the exception with the given message and inner exception.</summary>
        /// <param name="message">Describes the rejected transition.</param>
        /// <param name="innerException">The exception that caused this one.</param>
        public InvalidTransitionException(string message, Exception innerException) : base(message, innerException)
        {
        }
    }

    /// <summary>
    /// Thrown when the machine is built or used wrongly: a duplicate state, a state already attached
    /// to another machine, a transition or initial state naming an unregistered value, configuration
    /// after <see cref="BaseStateManager{TState}.Initialize"/>, initializing twice or with no states, or
    /// touching runtime members before initialization.
    /// </summary>
    /// <remarks>
    /// Distinct from <see cref="InvalidTransitionException"/>: this one means the machine itself is
    /// wrong, not that a legal-looking request was denied.
    /// </remarks>
    public sealed class StateConfigurationException : StateManagerException
    {
        /// <summary>Creates the exception with no message.</summary>
        public StateConfigurationException()
        {
        }

        /// <summary>Creates the exception with the given message.</summary>
        /// <param name="message">Describes why the configuration or call is invalid.</param>
        public StateConfigurationException(string message) : base(message)
        {
        }

        /// <summary>Creates the exception with the given message and inner exception.</summary>
        /// <param name="message">Describes why the configuration or call is invalid.</param>
        /// <param name="innerException">The exception that caused this one.</param>
        public StateConfigurationException(string message, Exception innerException) : base(message, innerException)
        {
        }
    }
}
