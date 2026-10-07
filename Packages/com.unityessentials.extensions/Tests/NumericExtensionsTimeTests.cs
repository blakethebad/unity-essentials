using System;
using NUnit.Framework;
using UnityEngine.TestTools.Constraints;
using Is = UnityEngine.TestTools.Constraints.Is;

namespace UnityEssentials.Extensions.Tests
{
    /// <summary>
    /// Covers the time formatting on <see cref="NumericExtensions"/>: every layout the arguments
    /// can select, rounding, the float-noise guard, agreement across the numeric overloads and
    /// between the string and buffer paths, and the input contract.
    /// </summary>
    [TestFixture]
    public class NumericExtensionsTimeTests
    {
        [TestCase(0f, "0:00")]
        [TestCase(9f, "0:09")]
        [TestCase(59.9f, "0:59")]
        [TestCase(60f, "1:00")]
        [TestCase(95.432f, "1:35")]
        [TestCase(3599f, "59:59")]
        public void ToTimeString_Default_FormatsMinutesAndSeconds(float seconds, string expected)
        {
            Assert.AreEqual(expected, seconds.ToTimeString());
        }

        [Test]
        public void ToTimeString_Decimals_AddFractionalSecondDigits()
        {
            const float seconds = 95.5f;

            Assert.AreEqual("1:35", seconds.ToTimeString());
            Assert.AreEqual("1:35.5", seconds.ToTimeString(decimals: 1));
            Assert.AreEqual("1:35.50", seconds.ToTimeString(decimals: 2));
            Assert.AreEqual("1:35.500", seconds.ToTimeString(decimals: 3));
        }

        [Test]
        public void ToTimeString_NoMinutes_PrintsTotalSeconds()
        {
            Assert.AreEqual("95", 95.5f.ToTimeString(minutes: false));
            Assert.AreEqual("95.5", 95.5f.ToTimeString(minutes: false, decimals: 1));
            Assert.AreEqual("3903", 3903.25f.ToTimeString(minutes: false));
        }

        [Test]
        public void ToTimeString_NoMinutes_IgnoresHours()
        {
            Assert.AreEqual("3903", 3903.25f.ToTimeString(hours: true, minutes: false));
        }

        [Test]
        public void ToTimeString_Hours_PadsMinutesAndSeconds()
        {
            Assert.AreEqual("1:05:03", 3903.25f.ToTimeString(hours: true));
            Assert.AreEqual("0:01:35", 95.5f.ToTimeString(hours: true));
        }

        [Test]
        public void ToTimeString_WithoutHours_RollsThemIntoTheMinutes()
        {
            Assert.AreEqual("65:03", 3903.25f.ToTimeString(hours: false));
            Assert.AreEqual("65:03.2", 3903.25f.ToTimeString(hours: false, decimals: 1));
        }

        [Test]
        public void ToTimeString_HoursUnspecified_ShowsThemFromAnHourUp()
        {
            Assert.AreEqual("59:59", 3599f.ToTimeString());
            Assert.AreEqual("1:00:00", 3600f.ToTimeString());
        }

        [Test]
        public void ToTimeString_RoundingUp_PicksTheLayoutAfterSnapping()
        {
            // 3599.6 s rounded up is a full hour, so it must not print as 60:00.
            Assert.AreEqual("1:00:00", 3599.6f.ToTimeString(roundUp: true));
        }

        [TestCase(0f, "0:00")]
        [TestCase(0.1f, "0:01")]
        [TestCase(1f, "0:01")]
        [TestCase(59.1f, "1:00")]
        [TestCase(60f, "1:00")]
        public void ToTimeString_RoundingUp_HoldsTheUnitUntilItIsSpent(float seconds, string expected)
        {
            Assert.AreEqual(expected, seconds.ToTimeString(roundUp: true));
        }

        [Test]
        public void ToTimeString_RoundingUp_IgnoresFloatWideningNoise()
        {
            // 0.1f widens to 0.100000001490…, so ten of them must still be one second, not two.
            Assert.AreEqual("0:01", (0.1f * 10f).ToTimeString(roundUp: true));
            Assert.AreEqual("0:01.0", 1f.ToTimeString(decimals: 1, roundUp: true));
        }

        [Test]
        public void ToTimeString_RoundingUp_IgnoresAccumulatedDoubleNoise()
        {
            // 0.1 + 0.2 is 0.30000000000000004, which must not round up to a whole extra tenth.
            Assert.AreEqual("0:00.3", (0.1d + 0.2d).ToTimeString(decimals: 1, roundUp: true));
        }

        [Test]
        public void ToTimeString_RoundingDown_Truncates()
        {
            Assert.AreEqual("0:59", 59.99f.ToTimeString());
            Assert.AreEqual("0:59.9", 59.99f.ToTimeString(decimals: 1));
        }

        [Test]
        public void ToTimeString_DoubleOverload_KeepsPrecisionAFloatCannot()
        {
            Assert.AreEqual("1:35.432", 95.432d.ToTimeString(decimals: 3));
            Assert.AreEqual("1:35.431", 95.432f.ToTimeString(decimals: 3));
        }

        [Test]
        public void ToTimeString_EveryOverload_AgreesOnTheSameValue()
        {
            Assert.AreEqual("1:35", 95f.ToTimeString());
            Assert.AreEqual("1:35", 95d.ToTimeString());
            Assert.AreEqual("1:35", 95.ToTimeString());
            Assert.AreEqual("1:35", 95L.ToTimeString());
        }

        [Test]
        public void ToTimeString_IntegralOverloads_HaveNothingToRound()
        {
            Assert.AreEqual("0:09", 9.ToTimeString(roundUp: true));
            Assert.AreEqual("0:09", 9L.ToTimeString(roundUp: true));
            Assert.AreEqual("0:09.0", 9.ToTimeString(decimals: 1, roundUp: true));
        }

        [Test]
        public void ToTimeString_NegativeSeconds_FormatsAsZero()
        {
            Assert.AreEqual("0:00", (-12f).ToTimeString());
            Assert.AreEqual("0:00", (-12f).ToTimeString(roundUp: true));
            Assert.AreEqual("0:00", (-12d).ToTimeString());
            Assert.AreEqual("0:00", (-12).ToTimeString());
            Assert.AreEqual("0:00", (-12L).ToTimeString());
        }

        [Test]
        public void ToTimeString_NonFiniteSeconds_ThrowsArgumentOutOfRangeException()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => float.NaN.ToTimeString());
            Assert.Throws<ArgumentOutOfRangeException>(() => float.PositiveInfinity.ToTimeString());
            Assert.Throws<ArgumentOutOfRangeException>(() => float.NegativeInfinity.ToTimeString());
            Assert.Throws<ArgumentOutOfRangeException>(() => double.NaN.ToTimeString());
            Assert.Throws<ArgumentOutOfRangeException>(() => double.PositiveInfinity.ToTimeString());
            Assert.Throws<ArgumentOutOfRangeException>(() => double.NegativeInfinity.ToTimeString());
        }

        [Test]
        public void ToTimeString_DecimalsOutOfRange_ThrowsArgumentOutOfRangeException()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => 1f.ToTimeString(decimals: -1));
            Assert.Throws<ArgumentOutOfRangeException>(() => 1f.ToTimeString(decimals: 4));
        }

        [Test]
        public void ToTimeString_SecondsBeyondTheUnitRange_ThrowsArgumentOutOfRangeException()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => 1e18d.ToTimeString(decimals: 3));
            Assert.Throws<ArgumentOutOfRangeException>(() => long.MaxValue.ToTimeString(decimals: 1));
        }

        [Test]
        public void ToTimeChars_WritesTheClockString_AndReturnsItsLength()
        {
            var buffer = new char[NumericExtensions.MaxTimeStringLength];

            var length = 95.5f.ToTimeChars(buffer, decimals: 1);

            Assert.AreEqual(6, length);
            Assert.AreEqual("1:35.5", new string(buffer, 0, length));
        }

        [Test]
        public void ToTimeChars_LeavesTheRestOfTheBufferAlone()
        {
            var buffer = new char[NumericExtensions.MaxTimeStringLength];
            for (var index = 0; index < buffer.Length; index++)
            {
                buffer[index] = '#';
            }

            var length = 95f.ToTimeChars(buffer);

            Assert.AreEqual("1:35", new string(buffer, 0, length));
            Assert.AreEqual('#', buffer[length]);
            Assert.AreEqual('#', buffer[buffer.Length - 1]);
        }

        [Test]
        public void ToTimeChars_ShorterValueIntoAUsedBuffer_LeavesTheTailOfTheLongerOne()
        {
            var buffer = new char[NumericExtensions.MaxTimeStringLength];

            var first = 3600f.ToTimeChars(buffer);
            var firstText = new string(buffer, 0, first);

            var second = 95f.ToTimeChars(buffer);
            var secondText = new string(buffer, 0, second);

            Assert.AreEqual("1:00:00", firstText);
            Assert.AreEqual("1:35", secondText);

            // The tail of the longer value is still there — which is why a caller renders exactly
            // the returned length and never the whole buffer.
            Assert.AreEqual("1:35:00", new string(buffer, 0, first));
        }

        [Test]
        public void ToTimeChars_EveryOverload_MatchesItsStringOverload()
        {
            var buffer = new char[NumericExtensions.MaxTimeStringLength];

            Assert.AreEqual(95.5f.ToTimeString(decimals: 2), Written(95.5f.ToTimeChars(buffer, decimals: 2), buffer));
            Assert.AreEqual(95.5d.ToTimeString(decimals: 2), Written(95.5d.ToTimeChars(buffer, decimals: 2), buffer));
            Assert.AreEqual(3903.ToTimeString(hours: true), Written(3903.ToTimeChars(buffer, hours: true), buffer));
            Assert.AreEqual(3903L.ToTimeString(hours: false), Written(3903L.ToTimeChars(buffer, hours: false), buffer));
            Assert.AreEqual(3903L.ToTimeString(minutes: false), Written(3903L.ToTimeChars(buffer, minutes: false), buffer));
            Assert.AreEqual(0.1f.ToTimeString(roundUp: true), Written(0.1f.ToTimeChars(buffer, roundUp: true), buffer));
        }

        [Test]
        public void ToTimeChars_BufferTooShort_ThrowsArgumentException()
        {
            var buffer = new char[3];

            Assert.Throws<ArgumentException>(() => 95f.ToTimeChars(buffer));
        }

        [Test]
        public void ToTimeChars_NullBuffer_ThrowsArgumentNullException()
        {
            Assert.Throws<ArgumentNullException>(() => 95f.ToTimeChars(null));
        }

        [Test]
        public void ToTimeChars_MaxTimeStringLength_FitsTheLongestClockString()
        {
            var buffer = new char[NumericExtensions.MaxTimeStringLength];

            Assert.DoesNotThrow(() => 999_999_999_999L.ToTimeChars(buffer, hours: true, decimals: 3));
            Assert.DoesNotThrow(() => 999_999_999_999L.ToTimeChars(buffer, minutes: false, decimals: 3));
            Assert.DoesNotThrow(() => long.MaxValue.ToTimeChars(buffer, minutes: false));
        }

        [Test]
        public void ToTimeChars_DoesNotAllocate()
        {
            var buffer = new char[NumericExtensions.MaxTimeStringLength];

            // Warm up: the first call JITs the formatter, which allocates on its own account.
            95.5f.ToTimeChars(buffer, decimals: 1, roundUp: true);

            Assert.That(() => 95.5f.ToTimeChars(buffer, decimals: 1, roundUp: true), Is.Not.AllocatingGCMemory());
        }

        private static string Written(int length, char[] buffer)
        {
            return new string(buffer, 0, length);
        }
    }
}
