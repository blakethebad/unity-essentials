using UnityEngine;
using UnityEssentials.States;

namespace UnityEssentials.States.Editor
{
    internal static class StateDebugPalette
    {
        private const uint Fnv1aOffsetBasis = 2166136261u;
        private const uint Fnv1aPrime = 16777619u;

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

        private const float StateSaturation = 0.55f;
        private const float StateValue = 0.95f;
        private const float AccentSaturation = 0.42f;
        private const float AccentValue = 0.95f;

        internal static Color StateColor(string stateName)
        {
            float hue = (Fnv1a(stateName) % 360u) / 360f;
            return Color.HSVToRGB(hue, StateSaturation, StateValue);
        }

        internal static Color AccentColor(StateMachineDebugEntry entry)
        {
            if (entry == null)
            {
                return AccentColor(null, null);
            }

            return AccentColor(entry.DisplayName, entry.EnumType?.FullName);
        }

        internal static Color AccentColor(string displayName, string enumTypeFullName)
        {
            float hue = (Fnv1a(displayName + "\0" + enumTypeFullName) % 360u) / 360f;
            return Color.HSVToRGB(hue, AccentSaturation, AccentValue);
        }

        private const float LightBackgroundThreshold = 0.62f;
        private static readonly Color DarkText = new Color(0.08f, 0.08f, 0.09f, 1f);
        private static readonly Color LightText = new Color(0.97f, 0.97f, 0.98f, 1f);

        internal static Color TextOn(Color background)
        {
            float luminance = (0.2126f * background.r) + (0.7152f * background.g) + (0.0722f * background.b);
            return luminance > LightBackgroundThreshold ? DarkText : LightText;
        }
    }
}
