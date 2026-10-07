using System;

namespace UnityEssentials.Haptics
{
    /// <summary>
    /// Base type for every error raised by the haptics package. Catching this type catches
    /// <see cref="HapticPatternInvalidException"/> as well.
    /// </summary>
    /// <remarks>
    /// All haptic exceptions signal programming errors (a null or empty pattern, non-finite event
    /// data, use from a background thread) rather than recoverable conditions. Unsupported hardware
    /// is not an error and never throws — it degrades silently and is reported through
    /// <c>HapticService.IsSupported</c> and <c>HapticService.SupportsPatterns</c>.
    /// <para>
    /// Background-thread use throws only in the Editor and in development builds, where the
    /// main-thread assert is compiled in; a release player skips the check rather than paying for it
    /// on every call, so off-thread calls must be fixed before shipping.
    /// </para>
    /// </remarks>
    public class HapticException : Exception
    {
        /// <summary>Creates the exception with no message.</summary>
        public HapticException()
        {
        }

        /// <summary>Creates the exception with the given message.</summary>
        /// <param name="message">Describes what went wrong.</param>
        public HapticException(string message) : base(message)
        {
        }

        /// <summary>Creates the exception with the given message and inner exception.</summary>
        /// <param name="message">Describes what went wrong.</param>
        /// <param name="innerException">The exception that caused this one.</param>
        public HapticException(string message, Exception innerException) : base(message, innerException)
        {
        }
    }

    /// <summary>
    /// Thrown when a <see cref="HapticPattern"/> is dispatched with data no provider can play: no
    /// events at all, or an event carrying NaN or infinity.
    /// </summary>
    /// <remarks>
    /// Validation runs on every pattern dispatch, including while haptics are muted, because invalid
    /// data is a bug rather than something the mute gate should hide. Range problems are not reported
    /// here — the pattern's <c>OnValidate</c> clamps out-of-range intensity and sharpness and sorts
    /// events by time without ever throwing, following the Unity convention that inspector editing
    /// must not raise.
    /// </remarks>
    public sealed class HapticPatternInvalidException : HapticException
    {
        /// <summary>Creates the exception with no message.</summary>
        public HapticPatternInvalidException()
        {
        }

        /// <summary>Creates the exception with the given message.</summary>
        /// <param name="message">Describes why the pattern cannot be played.</param>
        public HapticPatternInvalidException(string message) : base(message)
        {
        }

        /// <summary>Creates the exception with the given message and inner exception.</summary>
        /// <param name="message">Describes why the pattern cannot be played.</param>
        /// <param name="innerException">The exception that caused this one.</param>
        public HapticPatternInvalidException(string message, Exception innerException) : base(message, innerException)
        {
        }
    }
}
