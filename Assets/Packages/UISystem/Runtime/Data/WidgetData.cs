using System;
using UnityEngine;

namespace UnityEssentials.UI
{
    /// <summary>
    /// One entry in <see cref="WindowData.Widgets"/>: the prefab for a spawnable
    /// <see cref="WidgetBase"/>. Widgets listed here are not instantiated at load — the window
    /// stores the prototype and <c>GetWidget&lt;T&gt;()</c> creates instances on demand. A class
    /// rather than a bare prefab reference so pooling settings can be added later without migration.
    /// </summary>
    [Serializable]
    public sealed class WidgetData
    {
        [Tooltip("Prefab for a spawnable widget. Must have a component deriving from WidgetBase. " +
                 "Widgets are instantiated on demand by GetWidget<T>(), not when the window loads.")]
        [SerializeField] private GameObject prefab;

        public WidgetData()
        {
        }

        public WidgetData(GameObject prefab)
        {
            this.prefab = prefab;
        }

        public GameObject Prefab => prefab;
    }
}
