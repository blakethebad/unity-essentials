using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Text;
using UnityEngine;
using UnityEngine.EventSystems;

namespace UnityEssentials.UI
{
    [DisallowMultipleComponent]
    public sealed class UIWindow : MonoBehaviour
    {
        private readonly List<UIBase> _elements = new List<UIBase>();
        private readonly Dictionary<Type, UIBase> _elementsByType = new Dictionary<Type, UIBase>();

        private ReadOnlyCollection<UIBase> _elementsView;

        public WindowData Data { get; internal set; }

        public UIService Service { get; private set; }

        public Canvas Canvas { get; internal set; }

        public IReadOnlyList<UIBase> Elements =>
            _elementsView ?? (_elementsView = new ReadOnlyCollection<UIBase>(_elements));

        public void LoadWindow(UIService service)
        {
            if (service == null)
                throw new ArgumentNullException(nameof(service));

            Service = service;
            var prefabs = Data.UIPrefabs;

            for (var i = 0; i < prefabs.Count; i++)
            {
                var prefab = prefabs[i];
                if (prefab == null)
                {
                    throw new WindowConfigurationException(
                        $"WindowData '{Data.name}' has a null entry in UI Prefabs at index {i}. This should have " +
                        "been caught by WindowData.ThrowIfInvalid before the window was built; call SwitchWindow " +
                        "rather than generating and loading a window directly.");
                }

                var prototype = prefab.GetComponent<UIBase>();
                if (prototype == null)
                {
                    throw new WindowConfigurationException(
                        $"WindowData '{Data.name}' has prefab '{prefab.name}' in UI Prefabs at index {i} with no " +
                        "UI component on its root. This should have been caught by WindowData.ThrowIfInvalid " +
                        "before the window was built; call SwitchWindow rather than generating and loading a " +
                        "window directly.");
                }

                var instance = Instantiate(prototype, transform, false);
                instance.Bind(this);
                instance.gameObject.SetActive(false);

                _elements.Add(instance);
                _elementsByType[instance.GetType()] = instance;
            }
        }

        public void UnloadWindow()
        {
            for (var i = _elements.Count - 1; i >= 0; i--)
            {
                var element = _elements[i];

                if (element != null && element.IsVisible)
                    element.Hide();
            }

            _elements.Clear();
            _elementsByType.Clear();

            SafeDestroy(gameObject);
        }

        public T GetUI<T>() where T : UIBase => (T)GetUI(typeof(T));

        public UIBase GetUI(Type uiType)
        {
            if (uiType == null)
                throw new ArgumentNullException(nameof(uiType));

            if (_elementsByType.TryGetValue(uiType, out var element))
                return element;

            throw new UIElementNotFoundException(
                $"Window '{name}' has no UI element of type '{uiType.Name}'." +
                "Add the prefab to the UI Prefabs list of the WindowData this window was built from, or switch " +
                "to the window that owns it.");
        }

        public bool TryGetUI(Type uiType, out UIBase ui)
        {
            if (uiType == null)
            {
                ui = null;
                return false;
            }

            return _elementsByType.TryGetValue(uiType, out ui);
        }

        internal static void SafeDestroy(UnityEngine.Object target)
        {
            if (target == null)
                return;

            if (Application.isPlaying)
                Destroy(target);
            else
                DestroyImmediate(target);
        }
    }
}
