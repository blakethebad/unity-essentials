using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Text;
using UnityEngine;
using UnityEngine.EventSystems;

namespace UnityEssentials.UI
{
    /// <summary>
    /// One loaded window: an overlay canvas and one instance of every UI element its
    /// <see cref="WindowData"/> listed, all parented directly under the window GameObject. Generated
    /// by <see cref="WindowData.GenerateWindow"/> and filled by <see cref="LoadWindow"/>; do not add
    /// this component by hand.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class UIWindow : MonoBehaviour
    {
        private static bool _eventSystemWarningIssued;

        private readonly List<UIBase> _elements = new List<UIBase>();
        private readonly Dictionary<Type, UIBase> _elementsByType = new Dictionary<Type, UIBase>();

        private ReadOnlyCollection<UIBase> _elementsView;

        /// <summary>The asset this window was built from, assigned by <see cref="WindowData.GenerateWindow"/>.</summary>
        public WindowData Data { get; internal set; }

        /// <summary>The service that loaded this window, or null until <see cref="LoadWindow"/> runs.</summary>
        public UIService Service { get; private set; }

        /// <summary>This window's canvas, assigned by <see cref="WindowData.GenerateWindow"/>.</summary>
        public Canvas Canvas { get; internal set; }

        /// <summary>
        /// Every element this window instantiated, in the order its <see cref="WindowData"/> listed
        /// their prefabs.
        /// </summary>
        /// <remarks>
        /// Wrapped rather than handed out as the backing list: an <see cref="IReadOnlyList{T}"/> that
        /// is really a <see cref="List{T}"/> can be cast back and mutated, and this window is the
        /// sole owner of the element store.
        /// </remarks>
        public IReadOnlyList<UIBase> Elements =>
            _elementsView ?? (_elementsView = new ReadOnlyCollection<UIBase>(_elements));

        /// <summary>
        /// Loads this window for <paramref name="service"/>: instantiates one bound, deactivated
        /// instance of every UI prefab. Runs exactly once.
        /// </summary>
        /// <param name="service">The service taking ownership of this window.</param>
        /// <exception cref="ArgumentNullException"><paramref name="service"/> is null.</exception>
        /// <exception cref="WindowConfigurationException">
        /// This window has no <see cref="Data"/>, or it has already been loaded.
        /// </exception>
        public void LoadWindow(UIService service)
        {
            if (service == null)
            {
                throw new ArgumentNullException(nameof(service));
            }

            if (Data == null)
            {
                throw new WindowConfigurationException(
                    $"Window '{name}' has no WindowData, so it was not created through " +
                    "WindowData.GenerateWindow. Never add the UIWindow component by hand — call " +
                    "UIService.SwitchWindow and let the service generate and load the window.");
            }

            if (Service != null)
            {
                throw new WindowConfigurationException(
                    $"Window '{name}' is already loaded and a window loads exactly once. To rebuild it, " +
                    "unload it and generate a fresh window — which is what UIService.SwitchWindow does.");
            }

            Service = service;
            LoadElements();
            WarnIfNoEventSystem();
        }

        /// <summary>
        /// Hides every element that is still visible, then releases the loaded references and
        /// destroys the window GameObject.
        /// </summary>
        /// <remarks>
        /// The hide pass currently walks the element store in authored order, reversed, because that
        /// is how the store is laid out today — it is the window's current teardown order and not a
        /// guarantee callers may lean on. A later version that reintroduces kind-specific element
        /// bases may hide in true reverse-show order instead.
        /// </remarks>
        public void UnloadWindow()
        {
            // OnHide is the author's teardown hook — unsubscribing events, stopping coroutines,
            // releasing handles — and it must run while the object is still alive, so every visible
            // element is hidden before anything here destroys it.
            for (var i = _elements.Count - 1; i >= 0; i--)
            {
                var element = _elements[i];

                // Unity's overloaded comparison: a destroyed element counts as null.
                if (element != null && element.IsVisible)
                {
                    element.Hide();
                }
            }

            _elements.Clear();
            _elementsByType.Clear();

            SafeDestroy(gameObject);
        }

        /// <summary>Returns this window's instance of <typeparamref name="T"/>, or throws naming what it does carry.</summary>
        /// <typeparam name="T">The element type to resolve.</typeparam>
        /// <returns>This window's instance of <typeparamref name="T"/>.</returns>
        /// <exception cref="UIElementNotFoundException">This window carries no element of that type.</exception>
        public T GetUI<T>() where T : UIBase
        {
            return (T)GetUI(typeof(T));
        }

        /// <summary>Returns this window's instance of <paramref name="uiType"/>, or throws naming what it does carry.</summary>
        /// <param name="uiType">The element type to resolve.</param>
        /// <returns>This window's instance of <paramref name="uiType"/>.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="uiType"/> is null.</exception>
        /// <exception cref="UIElementNotFoundException">This window carries no element of that type.</exception>
        public UIBase GetUI(Type uiType)
        {
            if (uiType == null)
            {
                throw new ArgumentNullException(nameof(uiType));
            }

            if (_elementsByType.TryGetValue(uiType, out var element))
            {
                return element;
            }

            throw new UIElementNotFoundException(
                $"Window '{name}' has no UI element of type '{uiType.Name}'. It carries: {DescribeElements()}. " +
                "Add the prefab to the UI Prefabs list of the WindowData this window was built from, or switch " +
                "to the window that owns it.");
        }

        /// <summary>Tries to resolve <paramref name="uiType"/>, returning false instead of throwing on a miss.</summary>
        /// <param name="uiType">The element type to resolve; a null type is a miss rather than an error.</param>
        /// <param name="ui">The resolved element, or null on a miss.</param>
        /// <returns>True when this window carries an element of that type.</returns>
        public bool TryGetUI(Type uiType, out UIBase ui)
        {
            if (uiType == null)
            {
                ui = null;
                return false;
            }

            return _elementsByType.TryGetValue(uiType, out ui);
        }

        /// <summary>Destroys <paramref name="target"/> with the destroy call that is legal in the current mode.</summary>
        /// <param name="target">The object to destroy; a null or already destroyed target is ignored.</param>
        internal static void SafeDestroy(UnityEngine.Object target)
        {
            if (target == null)
            {
                return;
            }

            if (Application.isPlaying)
            {
                Destroy(target);
            }
            else
            {
                // Outside play mode a queued Destroy never runs (no frame end), and DestroyImmediate
                // is forbidden during play — hence the split.
                DestroyImmediate(target);
            }
        }

        private void LoadElements()
        {
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

                // Instantiated active then deactivated, so Awake/OnEnable run exactly once at load.
                // Everything is parented straight under the window; sibling order is authored order.
                var instance = Instantiate(prototype, transform, false);
                instance.Bind(this);
                instance.gameObject.SetActive(false);

                _elements.Add(instance);
                _elementsByType[instance.GetType()] = instance;
            }
        }

        private string DescribeElements()
        {
            if (_elements.Count == 0)
            {
                return "nothing — its WindowData lists no UI prefabs";
            }

            var builder = new StringBuilder();

            for (var i = 0; i < _elements.Count; i++)
            {
                if (i > 0)
                {
                    builder.Append(", ");
                }

                builder.Append(_elements[i].GetType().Name);
            }

            return builder.ToString();
        }

        private static void WarnIfNoEventSystem()
        {
            if (_eventSystemWarningIssued || !Application.isPlaying || EventSystem.current != null)
            {
                return;
            }

            _eventSystemWarningIssued = true;

            Debug.LogWarning(
                "UI system: a window was loaded but the scene has no EventSystem, so no UI element will receive " +
                "clicks, taps or navigation input. Add one via GameObject > UI > Event System, with the input " +
                "module that matches your project's input backend — InputSystemUIInputModule for the Input System " +
                "package, StandaloneInputModule for the legacy input manager. The UI system never creates one " +
                "itself: it cannot know which module is correct, and picking the wrong one silently kills all UI " +
                "input rather than failing loudly.");
        }

        // Needed because Enter Play Mode Options can skip the domain reload that would reset statics.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetWarningState()
        {
            _eventSystemWarningIssued = false;
        }
    }
}
