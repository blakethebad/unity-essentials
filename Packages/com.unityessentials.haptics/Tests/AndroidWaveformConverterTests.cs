using NUnit.Framework;

namespace UnityEssentials.Haptics.Tests
{
    /// <summary>
    /// Covers <see cref="AndroidWaveformConverter"/>, the pure translation from a haptic timeline to
    /// the parallel timing and amplitude arrays <c>VibrationEffect.createWaveform</c> consumes. The
    /// fixture pins the decisions that are cheap to regress and impossible to notice on a device:
    /// the transient minimum length, explicit silence for lead-ins and gaps, amplitude 0 for
    /// zero intensity, the rounding at both amplitude edges, and last-event-wins truncation.
    /// </summary>
    /// <remarks>
    /// Every test builds its <see cref="HapticEvent"/> values directly and touches no UnityEngine
    /// type, because the converter itself is pure and this fixture is also executed outside Unity by
    /// a plain reflection runner. Inputs honour the converter's documented contract — non-empty,
    /// sorted ascending by time, finite, non-negative, intensity already clamped — except where a
    /// test deliberately probes the defensive clamps in <see cref="AndroidWaveformConverter.ToAmplitude"/>.
    /// </remarks>
    [TestFixture]
    public class AndroidWaveformConverterTests
    {
        // -----------------------------------------------------------------
        // ToAmplitude: the intensity 0..1 -> amplitude 0..255 mapping.
        // -----------------------------------------------------------------

        [Test]
        public void ToAmplitude_ZeroIntensity_IsTrueSilence()
        {
            // 0, not 1: clamping silence up to the minimum audible amplitude would turn every
            // intended gap in a pattern into a faint continuous buzz.
            Assert.AreEqual(0, AndroidWaveformConverter.ToAmplitude(0f));
        }

        [Test]
        public void ToAmplitude_NegativeIntensity_IsTrueSilence()
        {
            Assert.AreEqual(0, AndroidWaveformConverter.ToAmplitude(-0.5f));
        }

        [Test]
        public void ToAmplitude_FullIntensity_IsMaximum()
        {
            Assert.AreEqual(255, AndroidWaveformConverter.ToAmplitude(1f));
        }

        [Test]
        public void ToAmplitude_JustAboveZero_ClampsUpToOne()
        {
            // 0.001 * 255 rounds to 0; a positive intensity must never be rounded out of existence.
            Assert.AreEqual(1, AndroidWaveformConverter.ToAmplitude(0.001f));
            Assert.AreEqual(1, AndroidWaveformConverter.ToAmplitude(0.0001f));
        }

        [Test]
        public void ToAmplitude_Midpoint_RoundsAwayFromZero()
        {
            // 0.5 * 255 == 127.5 exactly: banker's rounding would give 127.
            Assert.AreEqual(128, AndroidWaveformConverter.ToAmplitude(0.5f));
        }

        [Test]
        public void ToAmplitude_AboveOne_ClampsToMaximum()
        {
            // Defensive only — the manager clamps before dispatch — but the mapping must never
            // hand Android an out-of-range amplitude.
            Assert.AreEqual(255, AndroidWaveformConverter.ToAmplitude(1.5f));
        }

        // -----------------------------------------------------------------
        // Convert: segment construction.
        // -----------------------------------------------------------------

        [Test]
        public void MinimumEventDuration_IsTwentyMilliseconds()
        {
            Assert.AreEqual(20L, AndroidWaveformConverter.MinimumEventDurationMs);
        }

        [Test]
        public void Convert_SingleTransientAtZero_IsOneMinimumLengthFullAmplitudePulse()
        {
            var events = new[] { new HapticEvent(0f, 1f) };

            AndroidWaveformConverter.Convert(events, out var timings, out var amplitudes);

            CollectionAssert.AreEqual(new long[] { 20 }, timings);
            CollectionAssert.AreEqual(new[] { 255 }, amplitudes);
        }

        [Test]
        public void Convert_TransientEvents_AlwaysGetTheMinimumDuration()
        {
            // Two transients far enough apart that neither truncates the other, so both segments
            // show the substituted minimum rather than a truncated remainder.
            var events = new[]
            {
                new HapticEvent(0f, 1f),
                new HapticEvent(0.1f, 1f)
            };

            AndroidWaveformConverter.Convert(events, out var timings, out var amplitudes);

            CollectionAssert.AreEqual(new long[] { 20, 80, 20 }, timings);
            CollectionAssert.AreEqual(new[] { 255, 0, 255 }, amplitudes);
        }

        [Test]
        public void Convert_SubMillisecondDuration_StillPlaysForOneMillisecond()
        {
            // Duration is non-zero, so the transient minimum does not apply; rounding it to 0 ms
            // would delete the event, so the length floors at 1 ms instead.
            var events = new[] { new HapticEvent(0f, 1f, 0f, 0.0001f) };

            AndroidWaveformConverter.Convert(events, out var timings, out var amplitudes);

            CollectionAssert.AreEqual(new long[] { 1 }, timings);
            CollectionAssert.AreEqual(new[] { 255 }, amplitudes);
        }

        [Test]
        public void Convert_EventStartingAfterZero_EmitsLeadingSilenceOfExactlyItsStart()
        {
            var events = new[] { new HapticEvent(0.05f, 1f) };

            AndroidWaveformConverter.Convert(events, out var timings, out var amplitudes);

            CollectionAssert.AreEqual(new long[] { 50, 20 }, timings);
            CollectionAssert.AreEqual(new[] { 0, 255 }, amplitudes);
        }

        [Test]
        public void Convert_FirstEventAtZero_EmitsNoLeadingSilence()
        {
            var events = new[] { new HapticEvent(0f, 1f, 0f, 0.1f) };

            AndroidWaveformConverter.Convert(events, out var timings, out var amplitudes);

            CollectionAssert.AreEqual(new long[] { 100 }, timings);
            CollectionAssert.AreEqual(new[] { 255 }, amplitudes);
        }

        [Test]
        public void Convert_GapBetweenEvents_EmitsZeroAmplitudeSegmentOfTheGapLength()
        {
            // A ends at 100 ms, B starts at 200 ms: the 100 ms hole must be spelled out, because a
            // waveform is a contiguous run of segments with no notion of a timestamp.
            var events = new[]
            {
                new HapticEvent(0f, 1f, 0f, 0.1f),
                new HapticEvent(0.2f, 1f, 0f, 0.05f)
            };

            AndroidWaveformConverter.Convert(events, out var timings, out var amplitudes);

            CollectionAssert.AreEqual(new long[] { 100, 100, 50 }, timings);
            CollectionAssert.AreEqual(new[] { 255, 0, 255 }, amplitudes);
        }

        [Test]
        public void Convert_AdjacentEvents_EmitNoSilenceBetweenThem()
        {
            var events = new[]
            {
                new HapticEvent(0f, 1f, 0f, 0.1f),
                new HapticEvent(0.1f, 0.5f, 0f, 0.1f)
            };

            AndroidWaveformConverter.Convert(events, out var timings, out var amplitudes);

            CollectionAssert.AreEqual(new long[] { 100, 100 }, timings);
            CollectionAssert.AreEqual(new[] { 255, 128 }, amplitudes);
        }

        [Test]
        public void Convert_ZeroIntensityEvent_IsSilentRatherThanFaint()
        {
            var events = new[] { new HapticEvent(0f, 0f) };

            AndroidWaveformConverter.Convert(events, out var timings, out var amplitudes);

            CollectionAssert.AreEqual(new long[] { 20 }, timings);
            CollectionAssert.AreEqual(new[] { 0 }, amplitudes);
        }

        [Test]
        public void Convert_OverlappingEvents_TruncatesTheEarlierEventAtTheNextStart()
        {
            // A would run 0-100 ms but B starts at 50 ms: last event wins, so A is cut to 50 ms and
            // B then plays its full length.
            var events = new[]
            {
                new HapticEvent(0f, 1f, 0f, 0.1f),
                new HapticEvent(0.05f, 0.5f)
            };

            AndroidWaveformConverter.Convert(events, out var timings, out var amplitudes);

            CollectionAssert.AreEqual(new long[] { 50, 20 }, timings);
            CollectionAssert.AreEqual(new[] { 255, 128 }, amplitudes);
        }

        [Test]
        public void Convert_FullyOverlappedEvent_IsDroppedEntirely()
        {
            // Both events start at 0, so the first truncates to zero length. A zero-length segment
            // is never emitted, leaving only the winner.
            var events = new[]
            {
                new HapticEvent(0f, 1f, 0f, 0.1f),
                new HapticEvent(0f, 0.5f)
            };

            AndroidWaveformConverter.Convert(events, out var timings, out var amplitudes);

            CollectionAssert.AreEqual(new long[] { 20 }, timings);
            CollectionAssert.AreEqual(new[] { 128 }, amplitudes);
        }

        [Test]
        public void Convert_FinalEvent_IsNeverTruncatedAndAddsNoTrailingSilence()
        {
            var events = new[]
            {
                new HapticEvent(0f, 1f),
                new HapticEvent(0.1f, 1f, 0f, 0.25f)
            };

            AndroidWaveformConverter.Convert(events, out var timings, out var amplitudes);

            CollectionAssert.AreEqual(new long[] { 20, 80, 250 }, timings);
            CollectionAssert.AreEqual(new[] { 255, 0, 255 }, amplitudes);
        }

        [Test]
        public void Convert_MixedTimeline_ProducesParallelArraysOfEqualLength()
        {
            // One continuous event truncated by an overlap, one event fully swallowed at the same
            // time, then a gap before a trailing transient — every branch of the segment builder in
            // a single pattern.
            var events = new[]
            {
                new HapticEvent(0f, 1f, 0f, 0.1f),
                new HapticEvent(0.05f, 0f),
                new HapticEvent(0.05f, 0.5f, 1f, 0.02f),
                new HapticEvent(0.5f, 1f)
            };

            AndroidWaveformConverter.Convert(events, out var timings, out var amplitudes);

            Assert.AreEqual(timings.Length, amplitudes.Length);
            CollectionAssert.AreEqual(new long[] { 50, 20, 430, 20 }, timings);
            CollectionAssert.AreEqual(new[] { 255, 128, 0, 255 }, amplitudes);

            foreach (var timing in timings)
            {
                Assert.Greater(timing, 0L, "Zero-length segments must never be emitted.");
            }
        }
    }
}
