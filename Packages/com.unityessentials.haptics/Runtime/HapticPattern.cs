using System;
using UnityEngine;

namespace UnityEssentials.Haptics
{
    /// <summary>
    /// An authored haptic timeline: the array of <see cref="HapticEvent"/>s that
    /// <c>HapticService.Play(HapticPattern)</c> hands to the active provider as one effect. Author
    /// assets through <c>Assets &gt; Create &gt; UnityEssentials &gt; Haptics &gt; Haptic Pattern</c>,
    /// or build one at runtime with <see cref="Create"/>.
    /// </summary>
    /// <remarks>
    /// Validation comes in two tiers, and they exist for different audiences. <see cref="OnValidate"/>
    /// serves the person editing the asset: it repairs ranges and ordering as they type and never
    /// throws, because Unity calls it mid-keystroke and an exception there would be noise rather than
    /// help. <see cref="ThrowIfInvalid()"/> serves the programmer: the manager calls it before every
    /// dispatch — including while haptics are muted, since bad data is a bug the mute gate has no
    /// business hiding — and it raises <see cref="HapticPatternInvalidException"/> for data no provider
    /// could ever play.
    /// <para>
    /// The division of labour between the tiers is deliberate. Out-of-range values (a negative time, an
    /// intensity of 3) have an obvious intended meaning, so they are clamped silently. Non-finite values
    /// (NaN, infinity) do not, so they pass through clamping untouched and are reported at dispatch with
    /// the offending event index and field named. Clamping infinity down to 1 would look like a fix
    /// while quietly discarding the evidence of the arithmetic that produced it.
    /// </para>
    /// <para>
    /// Playback fidelity depends on hardware. Devices without a pattern engine degrade a pattern to a
    /// single impact scaled to its peak intensity rather than playing nothing, and Android ignores
    /// <see cref="HapticEvent.Sharpness"/> entirely; check <c>HapticService.SupportsPatterns</c> when
    /// the distinction matters to the caller.
    /// </para>
    /// </remarks>
    [CreateAssetMenu(menuName = "UnityEssentials/Haptics/Haptic Pattern", fileName = "HapticPattern")]
    public sealed class HapticPattern : ScriptableObject
    {
        /// <summary>
        /// The authored timeline. Kept in the clamped, time-sorted form established by
        /// <see cref="OnValidate"/> and <see cref="Create"/>, and never null: Unity's serializer
        /// replaces array fields wholesale rather than nulling them, and the initializer covers the
        /// instant between <see cref="ScriptableObject.CreateInstance{T}"/> and deserialization.
        /// </summary>
        [Tooltip("Events making up the pattern. Times are seconds from the start of the pattern; " +
                 "intensity and sharpness run 0 to 1; a duration of 0 means a transient tap. " +
                 "Values are clamped and sorted by time automatically.")]
        [SerializeField] private HapticEvent[] events = new HapticEvent[0];

        /// <summary>
        /// The pattern's events, stably sorted by <see cref="HapticEvent.Time"/> and clamped to the
        /// documented ranges, ready to be translated into native calls without further checking.
        /// </summary>
        /// <remarks>
        /// This is the pattern's own array rather than a defensive copy, so that dispatching a pattern
        /// every frame allocates nothing. That makes it read-only by contract: the manager and the
        /// providers may walk it, and nothing may sort it, clamp it, write an element or hand it to
        /// something that will. A caller that writes here corrupts every later playback of the asset,
        /// permanently in a build and until the next reimport in the Editor. Copy the array first if you
        /// need to modify events.
        /// <para>
        /// The ordering and range invariants hold as of the last validation — <see cref="OnValidate"/>
        /// maintains them for authored assets, <see cref="Create"/> for procedural ones. Finiteness is
        /// deliberately not among them; <see cref="ThrowIfInvalid()"/> owns that check. The array is never
        /// null but may be empty, which is exactly the case <see cref="ThrowIfInvalid()"/> rejects at
        /// dispatch.
        /// </para>
        /// </remarks>
        internal HapticEvent[] Events => events;

        /// <summary>
        /// Builds a pattern in memory from events computed at runtime, for procedural haptics that have
        /// no authored asset behind them. The result is normalized and validated, so it is playable the
        /// moment it is returned.
        /// </summary>
        /// <param name="events">
        /// The events to play. Copied rather than retained, so the caller may reuse or overwrite its own
        /// buffer immediately afterwards without disturbing the pattern. Must not be null or empty, and
        /// every field of every event must be finite; ranges and ordering are fixed up rather than
        /// rejected.
        /// </param>
        /// <returns>A pattern whose events are clamped, stably sorted by time and known to be playable.</returns>
        /// <remarks>
        /// The caller owns the returned instance. Nothing in this package retains it, and a
        /// ScriptableObject is a Unity object rather than a plain managed one, so dropping the last
        /// reference does not free it — call <see cref="UnityEngine.Object.Destroy(UnityEngine.Object)"/>
        /// when done (<see cref="UnityEngine.Object.DestroyImmediate(UnityEngine.Object)"/> outside play
        /// mode, where <c>Destroy</c> is not allowed). Better still, build the patterns a system needs
        /// once and reuse them rather than allocating one per playback.
        /// <para>
        /// Rejected input is never turned into an instance: the copy is validated before the
        /// ScriptableObject is created, so a throwing call leaves no orphaned Unity object behind.
        /// </para>
        /// </remarks>
        /// <exception cref="ArgumentNullException"><paramref name="events"/> is null.</exception>
        /// <exception cref="HapticPatternInvalidException">
        /// <paramref name="events"/> is empty, or one of its events carries NaN or infinity in any field.
        /// </exception>
        public static HapticPattern Create(HapticEvent[] events)
        {
            if (events == null)
            {
                throw new ArgumentNullException(nameof(events));
            }

            // Copy first so later mutation of the caller's buffer cannot reach into the pattern, and so
            // normalization reorders our array rather than theirs.
            var copy = new HapticEvent[events.Length];
            Array.Copy(events, copy, events.Length);
            Normalize(copy);
            ThrowIfInvalid(copy, null);

            var pattern = CreateInstance<HapticPattern>();
            pattern.events = copy;
            return pattern;
        }

        /// <summary>
        /// Repairs inspector-authored data: negative times and durations become 0, intensity and
        /// sharpness are clamped to 0 through 1, and the events are stably sorted by time so equal-time
        /// events keep the order they were authored in.
        /// </summary>
        /// <remarks>
        /// Never throws, and must never start doing so. Unity calls this on every inspector edit and on
        /// asset load, where raising would interrupt the user mid-edit and, on load, on an asset they may
        /// not even have open. Out-of-range values are therefore repaired rather than reported.
        /// <para>
        /// NaN and infinity pass through untouched by design — clamping them would hide a bug behind
        /// plausible-looking data — and are reported instead by <see cref="ThrowIfInvalid()"/> when the
        /// pattern is actually dispatched.
        /// </para>
        /// </remarks>
        private void OnValidate()
        {
            Normalize(events);
        }

        /// <summary>
        /// Rejects a pattern no provider could play: one with no events, or one carrying NaN or infinity.
        /// The message names the offending event's index and field.
        /// </summary>
        /// <remarks>
        /// The manager calls this before every pattern dispatch, including while
        /// <c>HapticService.IsEnabled</c> is false, so that muting the package never masks broken data.
        /// Ranges and ordering are not checked here; <see cref="OnValidate"/> and <see cref="Create"/>
        /// have already fixed those, and re-checking them per dispatch would cost more than it catches.
        /// </remarks>
        /// <exception cref="HapticPatternInvalidException">
        /// The pattern has no events, or one of its events carries NaN or infinity in any field.
        /// </exception>
        internal void ThrowIfInvalid()
        {
            ThrowIfInvalid(events, this);
        }

        /// <summary>
        /// Clamps every event into its documented range and stably sorts the array by time. Shared by
        /// <see cref="OnValidate"/> and <see cref="Create"/> so an authored asset and a procedural
        /// pattern are normalized identically. Tolerates a null or empty array and never throws.
        /// </summary>
        /// <param name="events">The array to normalize in place.</param>
        private static void Normalize(HapticEvent[] events)
        {
            if (events == null || events.Length == 0)
            {
                return;
            }

            for (var i = 0; i < events.Length; i++)
            {
                // HapticEvent is a struct: read, fix up, write the whole element back.
                var haptic = events[i];
                haptic.Time = ClampNonNegative(haptic.Time);
                haptic.Intensity = ClampUnit(haptic.Intensity);
                haptic.Sharpness = ClampUnit(haptic.Sharpness);
                haptic.Duration = ClampNonNegative(haptic.Duration);
                events[i] = haptic;
            }

            StableSortByTime(events);
        }

        /// <summary>
        /// Sorts the array by <see cref="HapticEvent.Time"/>, keeping events that share a time in their
        /// original relative order.
        /// </summary>
        /// <param name="events">The array to sort in place. Must not be null.</param>
        /// <remarks>
        /// <see cref="Array.Sort{T}(T[], Comparison{T})"/> is an introsort and is not stable, so equal
        /// times would otherwise shuffle between validations and make a pattern's feel depend on when it
        /// was last touched. Sorting a permutation of indices with the index itself as the tiebreaker
        /// makes the comparison a strict total order, which pins the result regardless of the sort's
        /// internal behaviour.
        /// </remarks>
        private static void StableSortByTime(HapticEvent[] events)
        {
            if (events.Length < 2)
            {
                return;
            }

            var source = (HapticEvent[])events.Clone();
            var order = new int[events.Length];
            for (var i = 0; i < order.Length; i++)
            {
                order[i] = i;
            }

            Array.Sort(order, (left, right) =>
            {
                // CompareTo rather than a raw comparison: it gives NaN a defined place in the ordering
                // instead of answering "neither less, greater nor equal", which would violate the
                // comparer contract and let Array.Sort throw out of OnValidate.
                var comparison = source[left].Time.CompareTo(source[right].Time);
                return comparison != 0 ? comparison : left.CompareTo(right);
            });

            for (var i = 0; i < order.Length; i++)
            {
                events[i] = source[order[i]];
            }
        }

        /// <summary>
        /// Raises the value to 0 if it is negative, leaving non-finite values exactly as they were so
        /// <see cref="ThrowIfInvalid()"/> can still report them.
        /// </summary>
        /// <param name="value">The time or duration to clamp, in seconds.</param>
        /// <returns>The value, never negative unless it was non-finite to begin with.</returns>
        private static float ClampNonNegative(float value)
        {
            return IsNonFinite(value) ? value : Mathf.Max(0f, value);
        }

        /// <summary>
        /// Clamps the value into 0 through 1, leaving non-finite values exactly as they were so
        /// <see cref="ThrowIfInvalid()"/> can still report them.
        /// </summary>
        /// <param name="value">The intensity or sharpness to clamp.</param>
        /// <returns>The value, within 0 through 1 unless it was non-finite to begin with.</returns>
        private static float ClampUnit(float value)
        {
            return IsNonFinite(value) ? value : Mathf.Clamp01(value);
        }

        /// <summary>
        /// Whether the value is NaN or an infinity, and therefore cannot be turned into a native effect.
        /// </summary>
        /// <param name="value">The value to test.</param>
        /// <returns>True when the value is not a finite number.</returns>
        /// <remarks>
        /// Spelled out rather than using <c>float.IsFinite</c>, which does not exist in the .NET
        /// Framework 4.7.1 API profile Unity compiles this assembly against.
        /// </remarks>
        private static bool IsNonFinite(float value)
        {
            return float.IsNaN(value) || float.IsInfinity(value);
        }

        /// <summary>
        /// The validation both <see cref="ThrowIfInvalid()"/> and <see cref="Create"/> run, sharing one
        /// definition of "playable" between an asset about to be dispatched and an array that has not
        /// become an asset yet.
        /// </summary>
        /// <param name="events">The events to check. Null is treated as empty.</param>
        /// <param name="owner">
        /// The pattern the events belong to, used to name it in the message, or null when the events
        /// came from <see cref="Create"/> and no instance exists yet.
        /// </param>
        /// <exception cref="HapticPatternInvalidException">
        /// <paramref name="events"/> is null or empty, or one of its events carries NaN or infinity.
        /// </exception>
        private static void ThrowIfInvalid(HapticEvent[] events, HapticPattern owner)
        {
            if (events == null || events.Length == 0)
            {
                throw new HapticPatternInvalidException(
                    $"Haptic pattern {Describe(owner)} has no events and cannot be played. " +
                    "Add at least one event in the inspector, or pass a non-empty array to HapticPattern.Create.");
            }

            for (var i = 0; i < events.Length; i++)
            {
                var haptic = events[i];
                if (IsNonFinite(haptic.Time))
                {
                    throw CreateNonFinite(owner, i, nameof(HapticEvent.Time), haptic.Time);
                }

                if (IsNonFinite(haptic.Intensity))
                {
                    throw CreateNonFinite(owner, i, nameof(HapticEvent.Intensity), haptic.Intensity);
                }

                if (IsNonFinite(haptic.Sharpness))
                {
                    throw CreateNonFinite(owner, i, nameof(HapticEvent.Sharpness), haptic.Sharpness);
                }

                if (IsNonFinite(haptic.Duration))
                {
                    throw CreateNonFinite(owner, i, nameof(HapticEvent.Duration), haptic.Duration);
                }
            }
        }

        /// <summary>Builds the "non-finite value" message, naming the event index and the field.</summary>
        /// <param name="owner">The pattern the event belongs to, or null for a pattern being created.</param>
        /// <param name="index">Index of the offending event within the pattern.</param>
        /// <param name="field">Name of the offending <see cref="HapticEvent"/> field.</param>
        /// <param name="value">The offending value, echoed so NaN and infinity are distinguishable.</param>
        /// <returns>The exception to throw.</returns>
        private static HapticPatternInvalidException CreateNonFinite(HapticPattern owner, int index, string field, float value)
        {
            return new HapticPatternInvalidException(
                $"Haptic pattern {Describe(owner)} has a non-finite {field} of {value} at event index {index}. " +
                "Every event field must be a finite number — NaN and infinity cannot be turned into a native effect. " +
                "Check the arithmetic that produced the event.");
        }

        /// <summary>
        /// Names the pattern for a message: its asset name, or a note that it is still being created.
        /// </summary>
        /// <param name="owner">The pattern to describe, or null when called from <see cref="Create"/>.</param>
        /// <returns>A fragment reading either <c>'SomeName'</c> or <c>passed to HapticPattern.Create</c>.</returns>
        /// <remarks>
        /// Called only on the throwing path, so building the string costs nothing on the dispatch path
        /// that runs every frame. The null test is Unity's overloaded comparison, so a destroyed pattern
        /// is described as unnamed rather than throwing while composing an error message.
        /// </remarks>
        private static string Describe(HapticPattern owner)
        {
            return owner == null ? "passed to HapticPattern.Create" : $"'{owner.name}'";
        }
    }
}
