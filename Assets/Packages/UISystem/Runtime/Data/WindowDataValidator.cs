using System;
using System.Collections.Generic;
using UnityEngine;

namespace UnityEssentials.UI
{
    internal static class WindowDataValidator
    {
        private const string UIPrefabsList = "UI Prefabs";

        /// <summary>Throws <see cref="WindowConfigurationException"/> on the first list rule violation.</summary>
        internal static void ThrowIfInvalid(string assetName, IReadOnlyList<GameObject> uiPrefabs)
        {
            ValidateUIPrefabs(assetName, uiPrefabs);
        }

        private static void ValidateUIPrefabs(string assetName, IReadOnlyList<GameObject> uiPrefabs)
        {
            if (uiPrefabs == null)
            {
                return;
            }

            var seen = new Dictionary<Type, int>();

            for (var i = 0; i < uiPrefabs.Count; i++)
            {
                var prefab = uiPrefabs[i];

                if (prefab == null)
                {
                    throw new WindowConfigurationException(
                        $"WindowData {Describe(assetName)} has a null entry in {UIPrefabsList} at index {i}. " +
                        "Assign a UI prefab, or remove the empty slot.");
                }

                var element = prefab.GetComponent<UIBase>();
                if (element == null)
                {
                    throw new WindowConfigurationException(
                        $"WindowData {Describe(assetName)} has prefab '{prefab.name}' in {UIPrefabsList} at index {i} " +
                        "with no UI component on its root. Every entry must carry a component deriving from " +
                        "UIBase, and it must be on the prefab's root GameObject.");
                }

                // Elements are resolved by concrete type, so a duplicate has no way to be addressed.
                var type = element.GetType();
                if (seen.TryGetValue(type, out var first))
                {
                    throw new WindowConfigurationException(
                        $"WindowData {Describe(assetName)} lists two prefabs of type {type.Name} in " +
                        $"{UIPrefabsList}, at indices {first} and {i}. Elements are resolved by their concrete " +
                        "type, so a window can hold only one of each; remove one, or give the second its own " +
                        "type deriving from the same base.");
                }

                seen.Add(type, i);
            }
        }

        private static string Describe(string assetName)
        {
            return string.IsNullOrEmpty(assetName) ? "(unnamed)" : $"'{assetName}'";
        }
    }
}
