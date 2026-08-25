using System;

namespace UnityEssentials.Haptics
{
    /// <summary>
    /// One point on a haptic timeline: when it fires, how strong it is, how crisp it feels and how
    /// long it lasts. A <see cref="HapticPattern"/> is nothing but an ordered array of these.
    /// </summary>
    /// <remarks>
    /// All times are in seconds, measured from the start of the pattern rather than from the previous
    /// event. <see cref="Intensity"/> and <see cref="Sharpness"/> are meaningful over 0 to 1; values
    /// outside that range are clamped by <see cref="HapticPattern"/> when the asset is validated, not
    /// by this struct, so a hand-constructed event may legitimately hold out-of-range values until it
    /// reaches a pattern.
    /// <para>
    /// The members are public fields rather than properties on purpose: this is an inspector-authored
    /// serializable data struct, and Unity's serializer and property drawers only see fields.
    /// </para>
    /// </remarks>
    [Serializable]
    public struct HapticEvent
    {
        /// <summary>
        /// Seconds from the start of the pattern at which this event fires. Must be finite and
        /// non-negative; <see cref="HapticPattern"/> sorts its events by this value, keeping events
        /// that share a time in their authored order.
        /// </summary>
        public float Time;

        /// <summary>
        /// How strong the event feels, over 0 (silent) to 1 (full strength). An intensity of 0 is a
        /// true gap rather than a faint buzz — the Android converter emits amplitude 0 for it.
        /// </summary>
        public float Intensity;

        /// <summary>
        /// How crisp the event feels, over 0 (dull and rounded) to 1 (sharp and clicky). This is an
        /// iOS-only CoreHaptics parameter; Android has no equivalent and ignores it entirely, so a
        /// pattern must not depend on sharpness alone to be distinguishable.
        /// </summary>
        public float Sharpness;

        /// <summary>
        /// How long the event lasts, in seconds. Zero means transient: an instantaneous tap on iOS,
        /// and a minimum-length pulse (20 ms) on Android, which has no instantaneous primitive.
        /// </summary>
        public float Duration;

        /// <summary>
        /// Creates an event at the given time with the given feel. Nothing is validated or clamped
        /// here; <see cref="HapticPattern"/> owns both.
        /// </summary>
        /// <param name="time">Seconds from the start of the pattern. Finite and non-negative.</param>
        /// <param name="intensity">Strength over 0 to 1.</param>
        /// <param name="sharpness">Crispness over 0 to 1. iOS-only; ignored on Android.</param>
        /// <param name="duration">Length in seconds, or 0 for a transient event.</param>
        public HapticEvent(float time, float intensity, float sharpness = 0f, float duration = 0f)
        {
            Time = time;
            Intensity = intensity;
            Sharpness = sharpness;
            Duration = duration;
        }
    }
}
