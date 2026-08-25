using UnityEngine;
using UnityEssentials.States;

namespace UnityEssentials.States.Editor
{
    /// <summary>
    /// Derives the debug window's colors from names rather than from a fixed palette: the same state
    /// name always paints the same color, in every card, in every session, on every machine.
    /// </summary>
    /// <remarks>
    /// A hand-picked palette would have to be cycled by index, which makes a color mean "the third
    /// state of this machine" instead of "Idle". Hashing the name instead makes the color an identity:
    /// two machines that both have an <c>Idle</c> state show it in the same hue, so a glance across
    /// several cards reads as one picture. The cost is no control over which hue a name lands on and
    /// the occasional near-collision between two unrelated names — acceptable for a debug view, where
    /// the labels are always visible next to the color.
    /// <para>
    /// The hash is FNV-1a rather than <see cref="string.GetHashCode()"/> deliberately: .NET randomizes
    /// string hash seeds per process, so <c>GetHashCode</c> would repaint the whole window every time
    /// the Editor restarts or the domain reloads, destroying exactly the stability this class exists
    /// to provide. FNV-1a is a handful of instructions, defined by the algorithm rather than by the
    /// runtime, and identical everywhere.
    /// </para>
    /// <para>
    /// Saturation and value are fixed instead of hashed. Only the hue carries information; letting a
    /// hash choose brightness too would produce unreadable near-black or washed-out chips. The chosen
    /// constants are tuned for the dark Editor skin and stay legible on the light one, and pairing
    /// them with <see cref="TextOn"/> keeps text on a colored background readable at every hue.
    /// </para>
    /// </remarks>
    internal static class StateDebugPalette
    {
        // ---- Hashing ------------------------------------------------------

        /// <summary>The FNV-1a 32-bit offset basis, as specified by the algorithm.</summary>
        private const uint Fnv1aOffsetBasis = 2166136261u;

        /// <summary>The FNV-1a 32-bit prime, as specified by the algorithm.</summary>
        private const uint Fnv1aPrime = 16777619u;

        /// <summary>
        /// Hashes <paramref name="value"/> with FNV-1a (32-bit), the stable-across-sessions
        /// alternative to <see cref="string.GetHashCode()"/> that every color here is derived from.
        /// </summary>
        /// <param name="value">The text to hash; <see langword="null"/> hashes to the offset basis.</param>
        /// <returns>The 32-bit FNV-1a hash of <paramref name="value"/>.</returns>
        /// <remarks>
        /// Each <see cref="char"/> is folded in as its two bytes, low byte first, so the result matches
        /// a byte-wise FNV-1a over the string's UTF-16 representation rather than over some
        /// char-at-a-time variant. The distinction only matters for reproducing the number elsewhere;
        /// what the window needs is that the same input always yields the same output, which either
        /// form would give — the byte-wise walk is used because it also mixes the two halves of
        /// non-ASCII characters instead of letting the high byte dominate.
        /// <para>
        /// Unchecked arithmetic is intentional: FNV-1a is defined modulo 2^32 and the multiply is
        /// expected to overflow on nearly every iteration.
        /// </para>
        /// </remarks>
        internal static uint Fnv1a(string value)
        {
            unchecked
            {
                uint hash = Fnv1aOffsetBasis;
                if (value == null)
                {
                    return hash;
                }

                for (int i = 0; i < value.Length; i++)
                {
                    char c = value[i];
                    hash = (hash ^ (byte)(c & 0xFF)) * Fnv1aPrime;
                    hash = (hash ^ (byte)((c >> 8) & 0xFF)) * Fnv1aPrime;
                }

                return hash;
            }
        }

        // ---- Colors -------------------------------------------------------

        /// <summary>Saturation for state colors: vivid enough to separate hues, not so vivid it glares.</summary>
        private const float StateSaturation = 0.55f;

        /// <summary>Value for state colors: bright, because chips sit on a dark card background.</summary>
        private const float StateValue = 0.95f;

        /// <summary>
        /// Saturation for manager accents. Lower than <see cref="StateSaturation"/> so the accent bar
        /// reads as a quiet identifier and never competes with the state chips beside it.
        /// </summary>
        private const float AccentSaturation = 0.42f;

        /// <summary>Value for manager accents, matched to the state colors so cards feel like one set.</summary>
        private const float AccentValue = 0.95f;

        /// <summary>
        /// The color that represents <paramref name="stateName"/> everywhere in the window — pill,
        /// strip chip, previous-state dot and history rows all call this, which is what makes a state
        /// recognizable by color alone.
        /// </summary>
        /// <param name="stateName">The state's name, exactly as the enum member is spelled.</param>
        /// <returns>A saturated, bright color whose hue is determined solely by the name.</returns>
        /// <remarks>
        /// The hue is <c>hash % 360</c> degrees. Taking a modulo of a hash is a slightly biased mapping
        /// (2^32 is not a multiple of 360), but the bias is under one part in ten million and utterly
        /// invisible in a color wheel — the readable arithmetic is worth more here than perfect
        /// uniformity.
        /// </remarks>
        internal static Color StateColor(string stateName)
        {
            float hue = (Fnv1a(stateName) % 360u) / 360f;
            return Color.HSVToRGB(hue, StateSaturation, StateValue);
        }

        /// <summary>
        /// The accent color identifying one registered machine, used for the card's left bar.
        /// </summary>
        /// <param name="entry">The registry entry describing the machine.</param>
        /// <returns>A muted color unique to this machine's display name and state enum.</returns>
        /// <remarks>
        /// Keyed on the display name plus the enum's full name rather than on the entry's id: ids are
        /// assigned in registration order, so an id-derived accent would shuffle every time play mode
        /// starts, whereas this one survives restarts and lets you recognize "the enemy AI card" by its
        /// stripe. Two identically named objects running the same machine do collide — deliberately, as
        /// they are genuinely the same kind of thing.
        /// </remarks>
        internal static Color AccentColor(StateMachineDebugEntry entry)
        {
            if (entry == null)
            {
                return AccentColor(null, null);
            }

            return AccentColor(entry.DisplayName, entry.EnumType?.FullName);
        }

        /// <summary>
        /// The name-based half of <see cref="AccentColor(StateMachineDebugEntry)"/>, split out so the
        /// color can be computed without an entry (tests, previews) and so the composition rule lives
        /// in exactly one place.
        /// </summary>
        /// <param name="displayName">The machine's display name.</param>
        /// <param name="enumTypeFullName">The namespace-qualified name of the state enum.</param>
        /// <returns>A muted color derived from both inputs.</returns>
        /// <remarks>
        /// The two parts are joined with a separator that cannot occur in a type name, so
        /// <c>("Ab", "c")</c> and <c>("A", "bc")</c> cannot hash to the same string.
        /// </remarks>
        internal static Color AccentColor(string displayName, string enumTypeFullName)
        {
            float hue = (Fnv1a(displayName + " " + enumTypeFullName) % 360u) / 360f;
            return Color.HSVToRGB(hue, AccentSaturation, AccentValue);
        }

        // ---- Contrast -----------------------------------------------------

        /// <summary>
        /// Luminance above which a background counts as "light" and needs dark text. Sits above the
        /// midpoint because the palette's fixed high value makes most hues fairly bright; a 0.5 cut
        /// would put dark text on colors that still read better with light text.
        /// </summary>
        private const float LightBackgroundThreshold = 0.62f;

        /// <summary>Near-black rather than pure black — softer against a saturated chip.</summary>
        private static readonly Color DarkText = new Color(0.08f, 0.08f, 0.09f, 1f);

        /// <summary>Near-white rather than pure white, for the same reason.</summary>
        private static readonly Color LightText = new Color(0.97f, 0.97f, 0.98f, 1f);

        /// <summary>
        /// Picks the text color that stays legible on <paramref name="background"/>, for the badges and
        /// pills that print a state name directly on its own color.
        /// </summary>
        /// <param name="background">The color the text will sit on.</param>
        /// <returns>Near-black on light backgrounds, near-white on dark ones.</returns>
        /// <remarks>
        /// Uses the Rec. 709 luma weights (0.2126 / 0.7152 / 0.0722) applied to the sRGB components
        /// directly, without linearizing first. That is the cheap approximation, not the
        /// WCAG-correct relative luminance, and it errs on the side of calling greens light and blues
        /// dark — which is exactly how those hues read to the eye at this saturation. Since the only
        /// decision made from the number is a binary text color, the extra precision of a proper
        /// gamma decode would change nothing visible.
        /// </remarks>
        internal static Color TextOn(Color background)
        {
            float luminance = (0.2126f * background.r) + (0.7152f * background.g) + (0.0722f * background.b);
            return luminance > LightBackgroundThreshold ? DarkText : LightText;
        }
    }
}
