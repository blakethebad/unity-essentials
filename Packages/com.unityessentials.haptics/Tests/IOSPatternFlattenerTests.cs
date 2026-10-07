using System;
using NUnit.Framework;

namespace UnityEssentials.Haptics.Tests
{
    /// <summary>
    /// Covers <see cref="IOSPatternFlattener"/>, the split of a haptic timeline into the four
    /// parallel float arrays the iOS bridge marshals. Because the flattener is deliberately a dumb
    /// per-field copy — every CoreHaptics decision lives in the <c>.mm</c> — the assertions are about
    /// layout, not feel: one entry per event, same index, same value, same order, nothing clamped,
    /// sorted, merged or rescaled on the way through.
    /// </summary>
    /// <remarks>
    /// The fixture builds <see cref="HapticEvent"/> values directly and touches no UnityEngine type,
    /// matching the flattener's own purity so both can also run outside Unity under a plain
    /// reflection runner.
    /// </remarks>
    [TestFixture]
    public class IOSPatternFlattenerTests
    {
        [Test]
        public void Flatten_SingleEvent_ProducesOneEntryInEachArray()
        {
            var events = new[] { new HapticEvent(0.25f, 0.75f, 0.5f, 0.1f) };

            IOSPatternFlattener.Flatten(
                events,
                out var times,
                out var intensities,
                out var sharpnesses,
                out var durations);

            CollectionAssert.AreEqual(new[] { 0.25f }, times);
            CollectionAssert.AreEqual(new[] { 0.75f }, intensities);
            CollectionAssert.AreEqual(new[] { 0.5f }, sharpnesses);
            CollectionAssert.AreEqual(new[] { 0.1f }, durations);
        }

        [Test]
        public void Flatten_MultipleEvents_CopiesEveryFieldVerbatimInOrder()
        {
            // Deliberately distinct values per field per event, so a swapped field or a transposed
            // index cannot pass.
            var events = new[]
            {
                new HapticEvent(0f, 0.1f, 0.2f, 0.3f),
                new HapticEvent(0.4f, 0.5f, 0.6f, 0.7f),
                new HapticEvent(0.8f, 0.9f, 1f, 0f)
            };

            IOSPatternFlattener.Flatten(
                events,
                out var times,
                out var intensities,
                out var sharpnesses,
                out var durations);

            CollectionAssert.AreEqual(new[] { 0f, 0.4f, 0.8f }, times);
            CollectionAssert.AreEqual(new[] { 0.1f, 0.5f, 0.9f }, intensities);
            CollectionAssert.AreEqual(new[] { 0.2f, 0.6f, 1f }, sharpnesses);
            CollectionAssert.AreEqual(new[] { 0.3f, 0.7f, 0f }, durations);
        }

        [Test]
        public void Flatten_MultipleEvents_ProducesFourArraysOfTheEventCount()
        {
            var events = new[]
            {
                new HapticEvent(0f, 1f),
                new HapticEvent(0.1f, 1f),
                new HapticEvent(0.2f, 1f),
                new HapticEvent(0.3f, 1f)
            };

            IOSPatternFlattener.Flatten(
                events,
                out var times,
                out var intensities,
                out var sharpnesses,
                out var durations);

            Assert.AreEqual(events.Length, times.Length);
            Assert.AreEqual(events.Length, intensities.Length);
            Assert.AreEqual(events.Length, sharpnesses.Length);
            Assert.AreEqual(events.Length, durations.Length);
        }

        [Test]
        public void Flatten_DoesNotReorderEvents()
        {
            // Ordering is the caller's business — the flattener copies index for index even when the
            // times are not ascending, so a sorting bug here cannot silently rewrite a pattern.
            var events = new[]
            {
                new HapticEvent(0.9f, 1f),
                new HapticEvent(0.1f, 1f),
                new HapticEvent(0.5f, 1f)
            };

            IOSPatternFlattener.Flatten(events, out var times, out _, out _, out _);

            CollectionAssert.AreEqual(new[] { 0.9f, 0.1f, 0.5f }, times);
        }

        [Test]
        public void Flatten_DoesNotClampOrRescaleValues()
        {
            // Out-of-range input reaches the bridge untouched: clamping belongs to HapticPattern, and
            // silently repairing it here would hide an authoring bug.
            var events = new[] { new HapticEvent(2f, 1.5f, -0.5f, 3f) };

            IOSPatternFlattener.Flatten(
                events,
                out var times,
                out var intensities,
                out var sharpnesses,
                out var durations);

            Assert.AreEqual(2f, times[0]);
            Assert.AreEqual(1.5f, intensities[0]);
            Assert.AreEqual(-0.5f, sharpnesses[0]);
            Assert.AreEqual(3f, durations[0]);
        }

        [Test]
        public void Flatten_DoesNotMutateTheInputArray()
        {
            var events = new[]
            {
                new HapticEvent(0.1f, 0.2f, 0.3f, 0.4f),
                new HapticEvent(0.5f, 0.6f, 0.7f, 0.8f)
            };

            IOSPatternFlattener.Flatten(events, out _, out _, out _, out _);

            Assert.AreEqual(0.1f, events[0].Time);
            Assert.AreEqual(0.2f, events[0].Intensity);
            Assert.AreEqual(0.3f, events[0].Sharpness);
            Assert.AreEqual(0.4f, events[0].Duration);
            Assert.AreEqual(0.5f, events[1].Time);
            Assert.AreEqual(0.6f, events[1].Intensity);
            Assert.AreEqual(0.7f, events[1].Sharpness);
            Assert.AreEqual(0.8f, events[1].Duration);
        }

        [Test]
        public void Flatten_EmptyEvents_ProducesFourEmptyArraysRatherThanNulls()
        {
            // Emptiness is the manager's validation error to raise, not a marshalling concern, so the
            // caller may still index the outputs in lockstep without a null check.
            var events = new HapticEvent[0];

            IOSPatternFlattener.Flatten(
                events,
                out var times,
                out var intensities,
                out var sharpnesses,
                out var durations);

            CollectionAssert.IsEmpty(times);
            CollectionAssert.IsEmpty(intensities);
            CollectionAssert.IsEmpty(sharpnesses);
            CollectionAssert.IsEmpty(durations);
        }

        [Test]
        public void Flatten_NullEvents_Throws()
        {
            Assert.Throws<ArgumentNullException>(
                () => IOSPatternFlattener.Flatten(null, out _, out _, out _, out _));
        }
    }
}
