using System;

namespace UnityEssentials.Extensions
{
    public static class NumericExtensions
    {
        public const int MaxTimeStringLength = 32;

        private const int MaxDecimals = 3;
        private const int SecondsPerMinute = 60;
        private const int SecondsPerHour = 3600;

        public static string ToTimeString(
            this float seconds,
            bool? hours = null,
            bool minutes = true,
            int decimals = 0,
            bool roundUp = false)
        {
            return FormatToString(seconds, hours, minutes, decimals, roundUp);
        }

        /// <inheritdoc cref="ToTimeString(float, bool?, bool, int, bool)"/>
        public static string ToTimeString(
            this double seconds,
            bool? hours = null,
            bool minutes = true,
            int decimals = 0,
            bool roundUp = false)
        {
            return FormatToString(seconds, hours, minutes, decimals, roundUp);
        }

        /// <inheritdoc cref="ToTimeString(float, bool?, bool, int, bool)"/>
        public static string ToTimeString(
            this int seconds,
            bool? hours = null,
            bool minutes = true,
            int decimals = 0,
            bool roundUp = false)
        {
            return FormatToString(seconds, hours, minutes, decimals, roundUp);
        }

        /// <inheritdoc cref="ToTimeString(float, bool?, bool, int, bool)"/>
        public static string ToTimeString(
            this long seconds,
            bool? hours = null,
            bool minutes = true,
            int decimals = 0,
            bool roundUp = false)
        {
            return FormatToString(seconds, hours, minutes, decimals, roundUp);
        }

        /// <summary>
        /// Writes the clock string of <paramref name="seconds"/> into <paramref name="buffer"/> and
        /// returns how many characters it wrote, allocating nothing. Same formatting and arguments
        /// as <see cref="ToTimeString(float, bool?, bool, int, bool)"/>.
        /// </summary>
        /// <param name="buffer">
        /// A buffer you own and keep — hold it in a field and reuse it every update. Size it with
        /// <see cref="MaxTimeStringLength"/> to fit any argument. Only the returned number of
        /// characters is written; the rest is left alone.
        /// </param>
        /// <exception cref="ArgumentNullException"><paramref name="buffer"/> is <c>null</c>.</exception>
        /// <exception cref="ArgumentException"><paramref name="buffer"/> is too short for the clock string.</exception>
        public static int ToTimeChars(
            this float seconds,
            char[] buffer,
            bool? hours = null,
            bool minutes = true,
            int decimals = 0,
            bool roundUp = false)
        {
            return FormatToBuffer(seconds, buffer, hours, minutes, decimals, roundUp);
        }

        /// <inheritdoc cref="ToTimeChars(float, char[], bool?, bool, int, bool)"/>
        public static int ToTimeChars(
            this double seconds,
            char[] buffer,
            bool? hours = null,
            bool minutes = true,
            int decimals = 0,
            bool roundUp = false)
        {
            return FormatToBuffer(seconds, buffer, hours, minutes, decimals, roundUp);
        }

        /// <inheritdoc cref="ToTimeChars(float, char[], bool?, bool, int, bool)"/>
        public static int ToTimeChars(
            this int seconds,
            char[] buffer,
            bool? hours = null,
            bool minutes = true,
            int decimals = 0,
            bool roundUp = false)
        {
            return FormatToBuffer(seconds, buffer, hours, minutes, decimals, roundUp);
        }

        /// <inheritdoc cref="ToTimeChars(float, char[], bool?, bool, int, bool)"/>
        public static int ToTimeChars(
            this long seconds,
            char[] buffer,
            bool? hours = null,
            bool minutes = true,
            int decimals = 0,
            bool roundUp = false)
        {
            return FormatToBuffer(seconds, buffer, hours, minutes, decimals, roundUp);
        }

        private static string FormatToString(double seconds, bool? hours, bool minutes, int decimals, bool roundUp)
        {
            Span<char> written = stackalloc char[MaxTimeStringLength];
            var length = Format(seconds, written, hours, minutes, decimals, roundUp);

            // The one allocation on this path, and the only one: no boxed arguments, no temporaries.
            return new string(written.Slice(0, length));
        }

        private static int FormatToBuffer(double seconds, char[] buffer, bool? hours, bool minutes, int decimals, bool roundUp)
        {
            if (buffer == null)
                throw new ArgumentNullException(nameof(buffer));

            Span<char> written = stackalloc char[MaxTimeStringLength];
            var length = Format(seconds, written, hours, minutes, decimals, roundUp);

            if (length > buffer.Length)
            {
                throw new ArgumentException(
                    $"A {length}-character clock string does not fit a buffer of {buffer.Length}. " +
                    $"Size the buffer with NumericExtensions.{nameof(MaxTimeStringLength)} to fit any argument.",
                    nameof(buffer));
            }

            written.Slice(0, length).CopyTo(buffer);
            return length;
        }

        // Every overload funnels here, so a float and the double it widens to always print the same
        // string, and the buffer and string paths can never drift apart.
        private static int Format(double seconds, Span<char> destination, bool? hours, bool minutes, int decimals, bool roundUp)
        {
            if (double.IsNaN(seconds) || double.IsInfinity(seconds))
                throw new ArgumentOutOfRangeException(
                    nameof(seconds),
                    seconds,
                    "A time value must be finite to be formatted as a clock string.");

            if (decimals < 0 || decimals > MaxDecimals)
                throw new ArgumentOutOfRangeException(
                    nameof(decimals),
                    decimals,
                    $"A clock string shows between 0 and {MaxDecimals} fractional second digits.");

            if (seconds < 0d)
                seconds = 0d;

            var unitsPerSecond = UnitsPerSecond(decimals);

            var scaled = Math.Round(seconds * unitsPerSecond, 6);

            if (scaled > long.MaxValue)
                throw new ArgumentOutOfRangeException(
                    nameof(seconds),
                    seconds,
                    "A time value this large cannot be counted in the units the clock string prints.");

            var units = (long)(roundUp ? Math.Ceiling(scaled) : Math.Floor(scaled));
            var totalSeconds = units / unitsPerSecond;
            var fraction = units - (totalSeconds * unitsPerSecond);

            var position = 0;

            if (!minutes)
            {
                position = WriteNumber(destination, position, totalSeconds, 1);
            }
            else if (hours ?? (totalSeconds >= SecondsPerHour))
            {
                position = WriteNumber(destination, position, totalSeconds / SecondsPerHour, 1);
                destination[position++] = ':';
                position = WriteNumber(destination, position, (totalSeconds % SecondsPerHour) / SecondsPerMinute, 2);
                destination[position++] = ':';
                position = WriteNumber(destination, position, totalSeconds % SecondsPerMinute, 2);
            }
            else
            {
                position = WriteNumber(destination, position, totalSeconds / SecondsPerMinute, 1);
                destination[position++] = ':';
                position = WriteNumber(destination, position, totalSeconds % SecondsPerMinute, 2);
            }

            if (decimals > 0)
            {
                destination[position++] = '.';
                position = WriteNumber(destination, position, fraction, decimals);
            }

            return position;
        }

        private static int WriteNumber(Span<char> destination, int position, long value, int minimumDigits)
        {
            var digits = 1;
            for (var remaining = value / 10; remaining > 0; remaining /= 10)
                digits++;

            if (digits < minimumDigits)
                digits = minimumDigits;

            for (var index = digits - 1; index >= 0; index--)
            {
                destination[position + index] = (char)('0' + (int)(value % 10));
                value /= 10;
            }

            return position + digits;
        }

        private static int UnitsPerSecond(int decimals)
        {
            switch (decimals)
            {
                case 1:
                    return 10;

                case 2:
                    return 100;

                case 3:
                    return 1000;

                default:
                    return 1;
            }
        }
    }
}
