using System;

namespace UnityEssentials.Haptics
{
    /// <summary>
    /// Splits a haptic timeline into the four parallel <see cref="float"/> arrays the iOS bridge
    /// expects, so <c>IOSHapticProvider</c> can hand a pattern across the P/Invoke boundary as four
    /// blittable buffers plus a count instead of an array of managed structs.
    /// </summary>
    /// <remarks>
    /// The conversion is a straight per-field copy: order is preserved exactly, values are passed
    /// through untouched, and nothing is clamped, sorted, merged or rescaled. Every iOS-specific
    /// decision — transient versus continuous, parameter ranges, the peak-intensity degrade path —
    /// lives in <c>UnityEssentialsHaptics.mm</c>, which owns the CoreHaptics semantics. Keeping this
    /// class a dumb copy is what makes it worth testing: the assertions are about layout, not feel.
    /// <para>
    /// The class deliberately depends on nothing but <c>System</c> and <see cref="HapticEvent"/>, and
    /// carries no platform guards, so it compiles and runs in the Editor and under a plain EditMode
    /// test on any build target.
    /// </para>
    /// </remarks>
    internal static class IOSPatternFlattener
    {
        /// <summary>
        /// Copies each field of <paramref name="events"/> into its own array, one output element per
        /// input event at the same index.
        /// </summary>
        /// <param name="events">
        /// The pattern's events, in the order they should play. Normally supplied by a validated
        /// <c>HapticPattern</c>, so this method assumes finite, clamped, time-sorted data and checks
        /// none of it. The array is only read, never written to or reordered.
        /// </param>
        /// <param name="times">Receives <see cref="HapticEvent.Time"/> for every event, in seconds from the start of the pattern.</param>
        /// <param name="intensities">Receives <see cref="HapticEvent.Intensity"/> for every event.</param>
        /// <param name="sharpnesses">Receives <see cref="HapticEvent.Sharpness"/> for every event.</param>
        /// <param name="durations">Receives <see cref="HapticEvent.Duration"/> for every event, where 0 marks a transient event.</param>
        /// <remarks>
        /// All four outputs are freshly allocated and always the same length as
        /// <paramref name="events"/>, including the empty case, which yields four empty arrays rather
        /// than nulls — the caller may index them in lockstep without a length check. An empty pattern
        /// is not rejected here because emptiness is the manager's validation error to raise, not a
        /// marshalling concern.
        /// </remarks>
        /// <exception cref="ArgumentNullException"><paramref name="events"/> is null.</exception>
        internal static void Flatten(
            HapticEvent[] events,
            out float[] times,
            out float[] intensities,
            out float[] sharpnesses,
            out float[] durations)
        {
            if (events == null)
            {
                throw new ArgumentNullException(nameof(events));
            }

            int count = events.Length;
            times = new float[count];
            intensities = new float[count];
            sharpnesses = new float[count];
            durations = new float[count];

            for (int i = 0; i < count; i++)
            {
                HapticEvent hapticEvent = events[i];
                times[i] = hapticEvent.Time;
                intensities[i] = hapticEvent.Intensity;
                sharpnesses[i] = hapticEvent.Sharpness;
                durations[i] = hapticEvent.Duration;
            }
        }
    }
}
