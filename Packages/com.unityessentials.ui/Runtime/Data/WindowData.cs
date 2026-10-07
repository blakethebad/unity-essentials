using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace UnityEssentials.UI
{
    [CreateAssetMenu(menuName = "UnityEssentials/UI/Window Data", fileName = "WindowData")]
    public sealed class WindowData : ScriptableObject
    {
        [Tooltip("The UIBase prefabs owned by this window. Each is instantiated once when the window " +
                 "loads and resolved by its concrete type, so the list holds at most one prefab per type.")]
        [SerializeField] private GameObject[] uiPrefabs = new GameObject[0];

        [Tooltip("How this window's Canvas, CanvasScaler and GraphicRaycaster are configured.")]
        [SerializeField] private CanvasSettings canvas = new CanvasSettings();

        public IReadOnlyList<GameObject> UIPrefabs => uiPrefabs;
        public CanvasSettings Canvas => canvas;

        public UIWindow GenerateWindow(Transform parent)
        {
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

            canvas.Apply(
                window.Canvas,
                host.GetComponent<CanvasScaler>(),
                host.GetComponent<GraphicRaycaster>());

            return window;
        }

        internal void ThrowIfInvalid()
        {
            WindowDataValidator.ThrowIfInvalid(name, UIPrefabs);
        }

        private void OnValidate()
        {
            canvas.Normalize();
        }
    }
}
