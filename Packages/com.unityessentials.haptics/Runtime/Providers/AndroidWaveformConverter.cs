using System;
using System.Collections.Generic;

namespace UnityEssentials.Haptics
{
    /// <summary>
    /// Flattens a haptic timeline into the two parallel arrays Android's
    /// <c>VibrationEffect.createWaveform(long[] timings, int[] amplitudes, -1)</c> expects:
    /// <c>timings[i]</c> is the length in milliseconds of a segment played at <c>amplitudes[i]</c>.
    /// </summary>
    /// <remarks>
    /// The class is deliberately pure — no UnityEngine types, no JNI, no state — so the whole
    /// timeline-to-waveform translation is unit-testable in EditMode on any machine, and every JNI
    /// call site stays confined to <c>AndroidHapticProvider</c>. It carries no <c>#if</c> guards for
    /// the same reason: the tests must compile and run with any active build target.
    /// <para>
    /// Input contract, guaranteed by <c>HapticManager</c> before dispatch: the array is non-null and
    /// non-empty, every value is finite, <see cref="HapticEvent.Intensity"/> and
    /// <see cref="HapticEvent.Sharpness"/> are already clamped to 0 to 1,
    /// <see cref="HapticEvent.Time"/> and <see cref="HapticEvent.Duration"/> are non-negative, and
    /// the events are stably sorted ascending by time. Nothing is revalidated here and the input
    /// array is only read, never written to. <see cref="HapticEvent.Sharpness"/> is ignored outright:
    /// Android's waveform API has no equivalent parameter.
    /// </para>
    /// <para>
    /// Segment construction. Every event contributes at most one non-silent segment, and silence is
    /// emitted explicitly because a waveform is a contiguous run of segments with no notion of a
    /// timestamp. A pattern that starts after zero opens with one zero-amplitude segment covering the
    /// lead-in; a hole between one event ending and the next beginning becomes a zero-amplitude
    /// segment of exactly that length. Overlaps are resolved as documented "last event wins": an
    /// event whose segment would run into the next event's start is truncated at that start, and an
    /// event truncated to nothing — two events sharing a time, for instance — is dropped entirely.
    /// Zero-length segments are never emitted, so the output is always a strictly positive timing per
    /// entry. No trailing silence is appended: the waveform ends when the last event ends.
    /// </para>
    /// </remarks>
    internal static class AndroidWaveformConverter
    {
        /// <summary>
        /// The length in milliseconds given to a transient event (<see cref="HapticEvent.Duration"/>
        /// of zero). Android has no instantaneous vibration primitive, so a transient must still
        /// occupy real time; 20 ms is short enough to read as a tap and long enough for the motor to
        /// actually move.
        /// </summary>
        internal const long MinimumEventDurationMs = 20;

        /// <summary>
        /// Converts an intensity of 0 to 1 into the 0 to 255 amplitude
        /// <c>VibrationEffect.createWaveform</c> takes.
        /// </summary>
        /// <param name="intensity">Strength over 0 to 1. Values at or below 0 mean silence.</param>
        /// <returns>0 for silence, otherwise the scaled amplitude clamped to 1 through 255.</returns>
        /// <remarks>
        /// An intensity at or below zero maps to amplitude 0 — true silence — rather than to the
        /// minimum audible amplitude of 1, because clamping it up would turn every intended gap in a
        /// pattern into a faint continuous buzz. Any positive intensity, however small, therefore
        /// maps to at least 1: rounding a barely-there event down to 0 would silently delete it.
        /// Rounding is away from zero at the midpoint so the mapping is symmetric and independent of
        /// the platform's banker's-rounding default.
        /// <para>
        /// Devices whose motor reports no <c>hasAmplitudeControl()</c> coerce every non-zero
        /// amplitude to their default strength. That is a documented fidelity caveat, not a case to
        /// branch on: the waveform's timing still plays correctly, only its dynamics are lost.
        /// </para>
        /// </remarks>
        internal static int ToAmplitude(float intensity)
        {
            if (intensity <= 0f)
            {
                return 0;
            }

            var amplitude = (int)Math.Round(intensity * 255.0, MidpointRounding.AwayFromZero);
            if (amplitude < 1)
            {
                return 1;
            }

            return amplitude > 255 ? 255 : amplitude;
        }

        /// <summary>
        /// Flattens <paramref name="events"/> into the parallel timing and amplitude arrays for
        /// <c>VibrationEffect.createWaveform(timings, amplitudes, -1)</c>, following the segment
        /// rules described on this class.
        /// </summary>
        /// <param name="events">
        /// The pattern's events, meeting the input contract described on this class. Read only.
        /// </param>
        /// <param name="timings">Receives one strictly positive segment length in milliseconds per entry.</param>
        /// <param name="amplitudes">
        /// Receives the amplitude for each segment, in lockstep with <paramref name="timings"/>: 0 for
        /// the silences the converter inserts and for zero-intensity events, 1 to 255 otherwise.
        /// </param>
        /// <remarks>
        /// The two arrays always have the same length, and for the non-empty input the contract
        /// requires that length is never zero: the final event has no successor to be truncated by,
        /// so it always contributes at least one millisecond of segment.
        /// </remarks>
        internal static void Convert(HapticEvent[] events, out long[] timings, out int[] amplitudes)
        {
            // Worst case is one silence plus one pulse per event; sizing for it up front means the
            // buffers never regrow mid-conversion.
            var capacity = events.Length * 2 + 1;
            var segmentLengths = new List<long>(capacity);
            var segmentAmplitudes = new List<int>(capacity);

            // Milliseconds already committed to the waveform. Because a segment is truncated at the
            // next event's start, the cursor can never run past the event being processed.
            var cursorMs = 0L;

            for (var i = 0; i < events.Length; i++)
            {
                var startMs = ToMilliseconds(events[i].Time);

                // Lead-in before the first event, or a hole between two events: both are the same
                // thing once the timeline is expressed as consecutive segments.
                if (startMs > cursorMs)
                {
                    segmentLengths.Add(startMs - cursorMs);
                    segmentAmplitudes.Add(0);
                    cursorMs = startMs;
                }

                var lengthMs = events[i].Duration == 0f
                    ? MinimumEventDurationMs
                    : Math.Max(1L, ToMilliseconds(events[i].Duration));

                var endMs = startMs + lengthMs;

                // Last event wins: hand the timeline over at the next event's start rather than
                // mixing the two, which a single-channel waveform cannot express.
                if (i + 1 < events.Length)
                {
                    var nextStartMs = ToMilliseconds(events[i + 1].Time);
                    if (nextStartMs < endMs)
                    {
                        endMs = nextStartMs;
                    }
                }

                var segmentMs = endMs - cursorMs;
                if (segmentMs > 0)
                {
                    segmentLengths.Add(segmentMs);
                    segmentAmplitudes.Add(ToAmplitude(events[i].Intensity));
                    cursorMs = endMs;
                }
            }

            timings = segmentLengths.ToArray();
            amplitudes = segmentAmplitudes.ToArray();
        }

        /// <summary>
        /// Converts seconds to whole milliseconds, rounding away from zero at the midpoint so the
        /// result matches the amplitude mapping's rounding and never depends on the runtime's
        /// banker's-rounding default.
        /// </summary>
        /// <param name="seconds">A finite, non-negative time or duration in seconds.</param>
        /// <returns>The value in whole milliseconds.</returns>
        private static long ToMilliseconds(float seconds)
        {
            return (long)Math.Round(seconds * 1000.0, MidpointRounding.AwayFromZero);
        }
    }
}
