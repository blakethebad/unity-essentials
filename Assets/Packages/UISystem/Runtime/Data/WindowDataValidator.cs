using System;
using System.Collections.Generic;
using UnityEngine;

namespace UnityEssentials.UI
{
    /// <summary>
    /// Every check the two prefab lists of a <see cref="WindowData"/> must pass before a window can
    /// be built from it. Takes plain lists so the rules can be tested without a ScriptableObject;
    /// messages name the asset, the list and the index so they are actionable from a build log.
    /// </summary>
    internal static class WindowDataValidator
    {
        private const string UIPrefabsList = "UI Prefabs";
        private const string WidgetsList = "Widgets";

        /// <summary>Throws <see cref="WindowConfigurationException"/> on the first list rule violation.</summary>
        internal static void ThrowIfInvalid(string assetName, IReadOnlyList<GameObject> uiPrefabs, IReadOnlyList<WidgetData> widgets)
        {
            ValidateUIPrefabs(assetName, uiPrefabs);
            ValidateWidgets(assetName, widgets);
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
                        "Assign a screen, popup or panel prefab, or remove the empty slot.");
                }

                var element = prefab.GetComponent<UIBase>();
                if (element == null)
                {
                    var widget = prefab.GetComponent<UIWidget>();
                    if (widget != null)
                    {
                        throw new WindowConfigurationException(
                            $"WindowData {Describe(assetName)} has widget prefab '{prefab.name}' " +
                            $"({widget.GetType().Name}) in {UIPrefabsList} at index {i}, which is the wrong list. " +
                            $"Widgets are instantiated on demand by GetWidget<T>(), not once at window load, so " +
                            $"move it to the {WidgetsList} list.");
                    }

                    throw new WindowConfigurationException(
                        $"WindowData {Describe(assetName)} has prefab '{prefab.name}' in {UIPrefabsList} at index {i} " +
                        "with no UI component on its root. Every entry must carry a component deriving from " +
                        "ScreenBase, PopupBase or PanelBase, and it must be on the prefab's root GameObject.");
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

        private static void ValidateWidgets(string assetName, IReadOnlyList<WidgetData> widgets)
        {
            if (widgets == null)
            {
                return;
            }

            var seen = new Dictionary<Type, int>();

            for (var i = 0; i < widgets.Count; i++)
            {
                var entry = widgets[i];
                if (entry == null)
                {
                    throw new WindowConfigurationException(
                        $"WindowData {Describe(assetName)} has a null entry in {WidgetsList} at index {i}. " +
                        "Remove the empty slot, or assign a widget prefab to it.");
                }

                var prefab = entry.Prefab;
                if (prefab == null)
                {
                    throw new WindowConfigurationException(
                        $"WindowData {Describe(assetName)} has an entry in {WidgetsList} at index {i} with no " +
                        "prefab assigned. Assign a prefab whose root carries a component deriving from " +
                        "WidgetBase, or remove the entry.");
                }

                var widget = prefab.GetComponent<WidgetBase>();
                if (widget == null)
                {
                    var other = prefab.GetComponent<UIBase>();
                    if (other != null)
                    {
                        var role = other is ScreenBase ? "screen" : other is PopupBase ? "popup" : "panel";
                        throw new WindowConfigurationException(
                            $"WindowData {Describe(assetName)} has prefab '{prefab.name}' " +
                            $"({other.GetType().Name}) in {WidgetsList} at index {i}, but it is a " +
                            $"{role} rather than a widget. Screens, popups " +
                            $"and panels are instantiated once when the window loads, so move it to the " +
                            $"{UIPrefabsList} list.");
                    }

                    throw new WindowConfigurationException(
                        $"WindowData {Describe(assetName)} has prefab '{prefab.name}' in {WidgetsList} at " +
                        $"index {i} with no WidgetBase component on its root. A widget prefab must carry a " +
                        "component deriving from WidgetBase, on the prefab's root GameObject.");
                }

                var type = widget.GetType();
                if (seen.TryGetValue(type, out var first))
                {
                    throw new WindowConfigurationException(
                        $"WindowData {Describe(assetName)} lists two widget prefabs of type {type.Name} in " +
                        $"{WidgetsList}, at indices {first} and {i}. Widgets are spawned by their concrete type, " +
                        "so a window can register only one prefab per type; remove one, or give the second its " +
                        "own type deriving from WidgetBase.");
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
