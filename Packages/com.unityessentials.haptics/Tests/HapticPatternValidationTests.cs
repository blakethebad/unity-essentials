using System;
using System.Collections.Generic;
using NUnit.Framework;

namespace UnityEssentials.Haptics.Tests
{
    /// <summary>
    /// Covers <see cref="HapticPattern.Create"/> — the runtime factory — and with it the whole
    /// normalization and validation contract every pattern is held to: what is rejected outright, what
    /// is repaired silently, and what ordering the resulting <see cref="HapticPattern.Events"/> array
    /// is guaranteed to be in. <c>Events</c> is internal and reachable here through InternalsVisibleTo.
    /// </summary>
    /// <remarks>
    /// The two validation tiers are deliberately asymmetric, and these tests pin the seam between them.
    /// Out-of-range values (a negative time, an intensity of 3) have an obvious intended meaning and are
    /// clamped; non-finite values do not and are reported. A pattern that mixes both must still throw,
    /// because clamping must never launder NaN into a plausible-looking number.
    /// <para>
    /// A pattern is a ScriptableObject, so dropping the last managed reference does not free it. Every
    /// instance built here is tracked and destroyed in <see cref="TearDown"/>; the fixture would
    /// otherwise leak a Unity object per test into the Editor's object list for the rest of the session.
    /// Throwing calls create nothing, so only the successful ones ever reach the tracking list.
    /// </para>
    /// </remarks>
    [TestFixture]
    public class HapticPatternValidationTests
    {
        /// <summary>
        /// Float comparison slack. Normalization copies and clamps but never arithmetically transforms
        /// values, so the expected numbers come back bit-identical; the tolerance guards the two
        /// computed-intensity cases rather than papering over drift.
        /// </summary>
        private const float Tolerance = 1e-6f;

        /// <summary>Every pattern built during a test, destroyed in <see cref="TearDown"/>.</summary>
        private readonly List<HapticPattern> _created = new List<HapticPattern>();

        [SetUp]
        public void SetUp()
        {
            _created.Clear();
        }

        [TearDown]
        public void TearDown()
        {
            for (var i = 0; i < _created.Count; i++)
            {
                // Unity's overloaded comparison, so an already-destroyed instance is skipped rather
                // than destroyed twice. DestroyImmediate because EditMode tests run outside play mode,
                // where Destroy is not allowed.
                if (_created[i] != null)
                {
                    UnityEngine.Object.DestroyImmediate(_created[i]);
                }
            }

            _created.Clear();
        }

        // ---------------------------------------------------------------------
        // Rejected input: null and empty.
        // ---------------------------------------------------------------------

        /// <summary>A null array is a caller mistake about the argument itself, not about pattern data.</summary>
        [Test]
        public void Create_NullEvents_ThrowsArgumentNullException()
        {
            var exception = Assert.Throws<ArgumentNullException>(() => HapticPattern.Create(null));

            Assert.AreEqual("events", exception.ParamName);
        }

        /// <summary>An empty timeline is well-formed but unplayable, so it fails validation, not argument checking.</summary>
        [Test]
        public void Create_EmptyEvents_ThrowsHapticPatternInvalidException()
        {
            Assert.Throws<HapticPatternInvalidException>(() => HapticPattern.Create(new HapticEvent[0]));
        }

        // ---------------------------------------------------------------------
        // Rejected input: non-finite values, in every field.
        // ---------------------------------------------------------------------

        /// <summary>NaN in <see cref="HapticEvent.Time"/> is reported rather than sorted into some arbitrary slot.</summary>
        [Test]
        public void Create_NaNTime_ThrowsHapticPatternInvalidException()
        {
            var source = new[] { new HapticEvent(float.NaN, 0.5f) };

            var exception = Assert.Throws<HapticPatternInvalidException>(() => HapticPattern.Create(source));

            StringAssert.Contains(nameof(HapticEvent.Time), exception.Message);
        }

        /// <summary>NaN in <see cref="HapticEvent.Intensity"/> survives clamping and is reported.</summary>
        [Test]
        public void Create_NaNIntensity_ThrowsHapticPatternInvalidException()
        {
            var source = new[] { new HapticEvent(0f, float.NaN) };

            var exception = Assert.Throws<HapticPatternInvalidException>(() => HapticPattern.Create(source));

            StringAssert.Contains(nameof(HapticEvent.Intensity), exception.Message);
        }

        /// <summary>NaN in <see cref="HapticEvent.Sharpness"/> is reported even though Android ignores the field.</summary>
        [Test]
        public void Create_NaNSharpness_ThrowsHapticPatternInvalidException()
        {
            var source = new[] { new HapticEvent(0f, 0.5f, float.NaN) };

            var exception = Assert.Throws<HapticPatternInvalidException>(() => HapticPattern.Create(source));

            StringAssert.Contains(nameof(HapticEvent.Sharpness), exception.Message);
        }

        /// <summary>NaN in <see cref="HapticEvent.Duration"/> is reported rather than treated as transient.</summary>
        [Test]
        public void Create_NaNDuration_ThrowsHapticPatternInvalidException()
        {
            var source = new[] { new HapticEvent(0f, 0.5f, 0.5f, float.NaN) };

            var exception = Assert.Throws<HapticPatternInvalidException>(() => HapticPattern.Create(source));

            StringAssert.Contains(nameof(HapticEvent.Duration), exception.Message);
        }

        /// <summary>Positive infinity in <see cref="HapticEvent.Time"/> is reported, not accepted as "very late".</summary>
        [Test]
        public void Create_InfiniteTime_ThrowsHapticPatternInvalidException()
        {
            var source = new[] { new HapticEvent(float.PositiveInfinity, 0.5f) };

            Assert.Throws<HapticPatternInvalidException>(() => HapticPattern.Create(source));
        }

        /// <summary>Infinite intensity is reported rather than clamped to 1, which would hide the arithmetic that produced it.</summary>
        [Test]
        public void Create_InfiniteIntensity_ThrowsHapticPatternInvalidException()
        {
            var source = new[] { new HapticEvent(0f, float.PositiveInfinity) };

            Assert.Throws<HapticPatternInvalidException>(() => HapticPattern.Create(source));
        }

        /// <summary>Negative infinity is caught in the same field clamping would otherwise raise to 0.</summary>
        [Test]
        public void Create_NegativeInfiniteSharpness_ThrowsHapticPatternInvalidException()
        {
            var source = new[] { new HapticEvent(0f, 0.5f, float.NegativeInfinity) };

            Assert.Throws<HapticPatternInvalidException>(() => HapticPattern.Create(source));
        }

        /// <summary>Negative infinity in a duration is reported rather than clamped to 0 alongside ordinary negatives.</summary>
        [Test]
        public void Create_NegativeInfiniteDuration_ThrowsHapticPatternInvalidException()
        {
            var source = new[] { new HapticEvent(0f, 0.5f, 0.5f, float.NegativeInfinity) };

            Assert.Throws<HapticPatternInvalidException>(() => HapticPattern.Create(source));
        }

        /// <summary>
        /// One non-finite event poisons the whole pattern: clamping the other events does not make the
        /// array playable, so validation still wins.
        /// </summary>
        [Test]
        public void Create_OneNonFiniteEventAmongValidOnes_ThrowsHapticPatternInvalidException()
        {
            var source = new[]
            {
                new HapticEvent(0f, 0.5f),
                new HapticEvent(0.1f, float.NaN),
                new HapticEvent(0.2f, 2f)
            };

            Assert.Throws<HapticPatternInvalidException>(() => HapticPattern.Create(source));
        }

        // ---------------------------------------------------------------------
        // Repaired input: clamping.
        // ---------------------------------------------------------------------

        /// <summary>A negative time means "at the start", so it is raised to 0 instead of rejected.</summary>
        [Test]
        public void Create_NegativeTime_IsClampedToZero()
        {
            var pattern = CreateTracked(new[] { new HapticEvent(-1.5f, 0.5f) });

            Assert.AreEqual(0f, pattern.Events[0].Time, Tolerance);
        }

        /// <summary>A negative duration collapses to 0, which is the transient case.</summary>
        [Test]
        public void Create_NegativeDuration_IsClampedToZero()
        {
            var pattern = CreateTracked(new[] { new HapticEvent(0f, 0.5f, 0.5f, -0.25f) });

            Assert.AreEqual(0f, pattern.Events[0].Duration, Tolerance);
        }

        /// <summary>Intensity above the documented range saturates at full strength.</summary>
        [Test]
        public void Create_IntensityAboveOne_IsClampedToOne()
        {
            var pattern = CreateTracked(new[] { new HapticEvent(0f, 3.5f) });

            Assert.AreEqual(1f, pattern.Events[0].Intensity, Tolerance);
        }

        /// <summary>Intensity below the documented range collapses to silence rather than wrapping or negating.</summary>
        [Test]
        public void Create_IntensityBelowZero_IsClampedToZero()
        {
            var pattern = CreateTracked(new[] { new HapticEvent(0f, -2f) });

            Assert.AreEqual(0f, pattern.Events[0].Intensity, Tolerance);
        }

        /// <summary>Sharpness is clamped at both ends, in one pattern so the two bounds cannot pass independently.</summary>
        [Test]
        public void Create_SharpnessOutsideUnitRange_IsClampedIntoRange()
        {
            var source = new[]
            {
                new HapticEvent(0f, 0.5f, -4f),
                new HapticEvent(0.1f, 0.5f, 9f)
            };

            var pattern = CreateTracked(source);

            Assert.AreEqual(0f, pattern.Events[0].Sharpness, Tolerance);
            Assert.AreEqual(1f, pattern.Events[1].Sharpness, Tolerance);
        }

        /// <summary>
        /// Every field of a single event is repaired in one pass, so no field is left un-normalized by a
        /// clamp that only ran on its neighbours.
        /// </summary>
        [Test]
        public void Create_AllFieldsOutOfRange_AreClampedTogether()
        {
            var source = new[] { new HapticEvent(-3f, 7f, -7f, -3f) };

            var pattern = CreateTracked(source);

            Assert.AreEqual(0f, pattern.Events[0].Time, Tolerance);
            Assert.AreEqual(1f, pattern.Events[0].Intensity, Tolerance);
            Assert.AreEqual(0f, pattern.Events[0].Sharpness, Tolerance);
            Assert.AreEqual(0f, pattern.Events[0].Duration, Tolerance);
        }

        /// <summary>Values already at the edges of their ranges pass through untouched — clamping is not an off-by-one.</summary>
        [Test]
        public void Create_BoundaryValues_ArePreservedExactly()
        {
            var source = new[]
            {
                new HapticEvent(0f, 0f, 0f, 0f),
                new HapticEvent(1f, 1f, 1f, 1f)
            };

            var pattern = CreateTracked(source);

            Assert.AreEqual(0f, pattern.Events[0].Intensity, Tolerance);
            Assert.AreEqual(0f, pattern.Events[0].Sharpness, Tolerance);
            Assert.AreEqual(1f, pattern.Events[1].Intensity, Tolerance);
            Assert.AreEqual(1f, pattern.Events[1].Sharpness, Tolerance);
            Assert.AreEqual(1f, pattern.Events[1].Duration, Tolerance);
        }

        // ---------------------------------------------------------------------
        // Ordering: sorted by time, stably.
        // ---------------------------------------------------------------------

        /// <summary>Callers may build a timeline in any order; the pattern hands the providers an ascending one.</summary>
        [Test]
        public void Create_OutOfOrderEvents_ReturnsEventsAscendingByTime()
        {
            var source = new[]
            {
                new HapticEvent(0.30f, 0.3f),
                new HapticEvent(0.10f, 0.1f),
                new HapticEvent(0.20f, 0.2f)
            };

            var pattern = CreateTracked(source);

            Assert.AreEqual(0.10f, pattern.Events[0].Time, Tolerance);
            Assert.AreEqual(0.20f, pattern.Events[1].Time, Tolerance);
            Assert.AreEqual(0.30f, pattern.Events[2].Time, Tolerance);

            // Intensity travels with its own event rather than being reassembled from a parallel array.
            Assert.AreEqual(0.1f, pattern.Events[0].Intensity, Tolerance);
            Assert.AreEqual(0.2f, pattern.Events[1].Intensity, Tolerance);
            Assert.AreEqual(0.3f, pattern.Events[2].Intensity, Tolerance);
        }

        /// <summary>
        /// Events sharing a time keep the order they were passed in — the intensities identify them, since
        /// the times alone cannot.
        /// </summary>
        [Test]
        public void Create_EqualTimeEvents_PreserveOriginalRelativeOrder()
        {
            var source = new[]
            {
                new HapticEvent(0.2f, 0.10f),
                new HapticEvent(0.1f, 0.20f),
                new HapticEvent(0.2f, 0.30f),
                new HapticEvent(0.1f, 0.40f),
                new HapticEvent(0.2f, 0.50f)
            };

            var pattern = CreateTracked(source);

            var expected = new[] { 0.20f, 0.40f, 0.10f, 0.30f, 0.50f };
            for (var i = 0; i < expected.Length; i++)
            {
                Assert.AreEqual(expected[i], pattern.Events[i].Intensity, Tolerance,
                    $"Event at index {i} is not the one the stable sort should have placed there.");
            }
        }

        /// <summary>
        /// Enough equal-time events to push the sort past its insertion-sort threshold into introsort,
        /// which is where an unstable comparison would actually start reordering them.
        /// </summary>
        [Test]
        public void Create_ManyEqualTimeEvents_PreserveOriginalRelativeOrder()
        {
            const int count = 32;
            var source = new HapticEvent[count];
            for (var i = 0; i < count; i++)
            {
                source[i] = new HapticEvent(0.5f, i / (count - 1f));
            }

            var pattern = CreateTracked(source);

            for (var i = 0; i < count; i++)
            {
                Assert.AreEqual(i / (count - 1f), pattern.Events[i].Intensity, Tolerance,
                    $"Event at index {i} moved; the sort is not stable at this size.");
            }
        }

        /// <summary>
        /// The same input always produces the same order, so a pattern's feel never depends on when it was
        /// last normalized.
        /// </summary>
        [Test]
        public void Create_EqualTimeEvents_OrderIdenticallyAcrossCalls()
        {
            var source = new[]
            {
                new HapticEvent(0.2f, 0.10f),
                new HapticEvent(0.2f, 0.20f),
                new HapticEvent(0.1f, 0.30f),
                new HapticEvent(0.2f, 0.40f),
                new HapticEvent(0.1f, 0.50f)
            };

            var first = CreateTracked(source);
            var second = CreateTracked(source);

            for (var i = 0; i < source.Length; i++)
            {
                Assert.AreEqual(first.Events[i].Intensity, second.Events[i].Intensity, Tolerance,
                    $"Two patterns built from the same array disagree at index {i}.");
            }
        }

        /// <summary>Clamping runs before the sort, so times that collapse onto 0 are still ordered against real zeros.</summary>
        [Test]
        public void Create_NegativeTimes_SortAsZeroAfterClamping()
        {
            var source = new[]
            {
                new HapticEvent(0.5f, 0.10f),
                new HapticEvent(-2f, 0.20f),
                new HapticEvent(0f, 0.30f)
            };

            var pattern = CreateTracked(source);

            Assert.AreEqual(0f, pattern.Events[0].Time, Tolerance);
            Assert.AreEqual(0f, pattern.Events[1].Time, Tolerance);
            Assert.AreEqual(0.5f, pattern.Events[2].Time, Tolerance);

            // The clamped event was authored before the genuine zero, so it stays before it.
            Assert.AreEqual(0.20f, pattern.Events[0].Intensity, Tolerance);
            Assert.AreEqual(0.30f, pattern.Events[1].Intensity, Tolerance);
        }

        // ---------------------------------------------------------------------
        // Copy semantics: the caller's array and the pattern's are separate.
        // ---------------------------------------------------------------------

        /// <summary>The pattern holds its own array, so the caller may reuse the buffer it passed in.</summary>
        [Test]
        public void Create_CopiesInputArray_SoLaterMutationDoesNotReachThePattern()
        {
            var source = new[]
            {
                new HapticEvent(0f, 0.25f),
                new HapticEvent(0.1f, 0.50f)
            };

            var pattern = CreateTracked(source);

            source[0].Intensity = 1f;
            source[1] = new HapticEvent(9f, 0f);

            Assert.AreNotSame(source, pattern.Events);
            Assert.AreEqual(0.25f, pattern.Events[0].Intensity, Tolerance);
            Assert.AreEqual(0.10f, pattern.Events[1].Time, Tolerance);
            Assert.AreEqual(0.50f, pattern.Events[1].Intensity, Tolerance);
        }

        /// <summary>
        /// Normalization happens on the copy, so a caller reusing a scratch buffer never finds it silently
        /// reordered or clamped underneath them.
        /// </summary>
        [Test]
        public void Create_DoesNotNormalizeTheCallerArray()
        {
            var source = new[]
            {
                new HapticEvent(0.3f, 2f),
                new HapticEvent(0.1f, -1f)
            };

            CreateTracked(source);

            Assert.AreEqual(0.3f, source[0].Time, Tolerance);
            Assert.AreEqual(2f, source[0].Intensity, Tolerance);
            Assert.AreEqual(0.1f, source[1].Time, Tolerance);
            Assert.AreEqual(-1f, source[1].Intensity, Tolerance);
        }

        /// <summary>Two patterns built from one array share nothing, so editing one cannot disturb the other.</summary>
        [Test]
        public void Create_CalledTwiceWithOneArray_ProducesIndependentPatterns()
        {
            var source = new[] { new HapticEvent(0f, 0.5f) };

            var first = CreateTracked(source);
            var second = CreateTracked(source);

            Assert.AreNotSame(first, second);
            Assert.AreNotSame(first.Events, second.Events);
        }

        // ---------------------------------------------------------------------
        // Helpers.
        // ---------------------------------------------------------------------

        /// <summary>
        /// Builds a pattern and registers it for destruction in <see cref="TearDown"/>. Use this rather
        /// than <see cref="HapticPattern.Create"/> directly for every call expected to succeed; calls
        /// expected to throw need no tracking, because a rejected array never becomes an instance.
        /// </summary>
        /// <param name="events">The events to build the pattern from.</param>
        /// <returns>The created pattern, already normalized and validated.</returns>
        private HapticPattern CreateTracked(HapticEvent[] events)
        {
            var pattern = HapticPattern.Create(events);
            _created.Add(pattern);
            return pattern;
        }
    }
}
