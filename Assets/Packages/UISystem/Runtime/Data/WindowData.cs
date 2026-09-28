using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace UnityEssentials.UI
{
    /// <summary>
    /// The asset that defines one window: the <see cref="UIBase"/> prefabs it owns and how its canvas
    /// is configured. Create one through <c>Assets &gt; Create &gt; UnityEssentials &gt; UI &gt; Window
    /// Data</c> and hand it to <see cref="UIService.SwitchWindow"/>.
    /// </summary>
    [CreateAssetMenu(menuName = "UnityEssentials/UI/Window Data", fileName = "WindowData")]
    public sealed class WindowData : ScriptableObject
    {
        private static readonly GameObject[] NoPrefabs = new GameObject[0];

        [Tooltip("The UIBase prefabs owned by this window. Each is instantiated once when the window " +
                 "loads and resolved by its concrete type, so the list holds at most one prefab per type.")]
        [SerializeField] private GameObject[] uiPrefabs = new GameObject[0];

        [Tooltip("How this window's Canvas, CanvasScaler and GraphicRaycaster are configured.")]
        [SerializeField] private CanvasSettings canvas = new CanvasSettings();

        public IReadOnlyList<GameObject> UIPrefabs => uiPrefabs ?? NoPrefabs;

        public CanvasSettings Canvas => EnsureCanvas();

        /// <summary>
        /// Builds a <see cref="WindowData"/> in memory, for windows assembled at runtime rather than
        /// authored as an asset. Does not validate — validation is dispatch-time, by design.
        /// </summary>
        public static WindowData Create(GameObject[] uiPrefabs, CanvasSettings canvas)
        {
            var data = CreateInstance<WindowData>();
            data.uiPrefabs = Copy(uiPrefabs, NoPrefabs);
            data.canvas = canvas ?? new CanvasSettings();
            return data;
        }

        /// <summary>
        /// Creates this asset's window GameObject under <paramref name="parent"/> — canvas trio
        /// configured, <see cref="UIWindow"/> attached. Nothing is instantiated inside it until
        /// <see cref="UIWindow.LoadWindow"/> runs.
        /// </summary>
        public UIWindow GenerateWindow(Transform parent)
        {
            // One constructor call so component order is deterministic on every platform.
            var host = new GameObject(
                $"Window_{name}",
                typeof(RectTransform),
                typeof(Canvas),
                typeof(CanvasScaler),
                typeof(GraphicRaycaster),
                typeof(UIWindow));

            host.transform.SetParent(parent, false);

            var window = host.GetComponent<UIWindow>();
            window.Data = this;
            window.Canvas = host.GetComponent<Canvas>();

            EnsureCanvas().Apply(
                window.Canvas,
                host.GetComponent<CanvasScaler>(),
                host.GetComponent<GraphicRaycaster>());

            return window;
        }

        internal void ThrowIfInvalid()
        {
            // Runs from SwitchWindow before anything is built or unloaded, so a malformed asset
            // leaves the live UI untouched.
            WindowDataValidator.ThrowIfInvalid(name, UIPrefabs);
        }

        private void OnValidate()
        {
            // Unity calls this mid-keystroke and on import; it repairs numbers and must never throw.
            EnsureCanvas().Normalize();
        }

        private CanvasSettings EnsureCanvas()
        {
            // An asset saved before this field existed deserializes with it null.
            if (canvas == null)
            {
                canvas = new CanvasSettings();
            }

            return canvas;
        }

        private static T[] Copy<T>(T[] source, T[] empty)
        {
            if (source == null || source.Length == 0)
            {
                return empty;
            }

            var copy = new T[source.Length];
            Array.Copy(source, copy, source.Length);
            return copy;
        }
    }
}
