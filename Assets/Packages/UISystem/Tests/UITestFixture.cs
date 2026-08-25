using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

namespace UnityEssentials.UI.Tests
{
    /// <summary>
    /// The base every UI fixture derives from: tracks the objects and services a test creates,
    /// tears them down deterministically, resets the package's static state, and carries the
    /// builders that turn plain GameObjects into the "prefabs" a <see cref="WindowData"/> expects.
    /// </summary>
    public abstract class UITestFixture
    {
        private readonly List<UnityEngine.Object> _trackedObjects = new List<UnityEngine.Object>();
        private readonly List<WindowService> _trackedServices = new List<WindowService>();

        protected static IReadOnlyList<MonoBehaviour> CallLog => UICallLog.Entries;

        protected static IReadOnlyList<MonoBehaviour> ShowLog => UICallLog.Shows;

        protected static IReadOnlyList<MonoBehaviour> HideLog => UICallLog.Hides;

        // Not named SetUp/TearDown: a derived fixture declaring its own would hide these and NUnit
        // would run only one of the two.
        [SetUp]
        public void UITestFixtureSetUp()
        {
            UICallLog.Clear();
        }

        [TearDown]
        public void UITestFixtureTearDown()
        {
            // Windows are closed first so every element's OnHide runs against a live window —
            // CloseWindow also destroys each service's window parent — then tracked objects are
            // destroyed newest-first.
            try
            {
                for (var i = _trackedServices.Count - 1; i >= 0; i--)
                {
                    _trackedServices[i].CloseWindow();
                }
            }
            finally
            {
                _trackedServices.Clear();

                for (var i = _trackedObjects.Count - 1; i >= 0; i--)
                {
                    var tracked = _trackedObjects[i];
                    if (tracked != null)
                    {
                        UnityEngine.Object.DestroyImmediate(tracked);
                    }
                }

                _trackedObjects.Clear();
                UICallLog.Clear();
            }
        }

        /// <summary>Registers a Unity object for destruction at the end of the test and hands it back.</summary>
        protected T Track<T>(T obj) where T : UnityEngine.Object
        {
            // Compared through a non-generic local so Unity's overloaded == applies.
            UnityEngine.Object candidate = obj;
            if (candidate != null)
            {
                _trackedObjects.Add(obj);
            }

            return obj;
        }

        /// <summary>Registers a service so its window is closed at the end of the test, and hands it back.</summary>
        protected WindowService TrackService(WindowService service)
        {
            if (service != null)
            {
                _trackedServices.Add(service);
            }

            return service;
        }

        /// <summary>Creates a tracked <see cref="WindowService"/> with no window loaded.</summary>
        protected WindowService CreateService()
        {
            return TrackService(new WindowService());
        }

        /// <summary>Creates a service and immediately loads a window built from <paramref name="uiPrefabs"/>.</summary>
        protected WindowService CreateLoadedService(params GameObject[] uiPrefabs)
        {
            var service = CreateService();
            service.SwitchWindow(BuildWindowData(uiPrefabs));
            return service;
        }

        /// <summary>Creates a service and loads a window carrying both UI prefabs and widget prototypes.</summary>
        protected WindowService CreateLoadedService(GameObject[] uiPrefabs, WidgetData[] widgets)
        {
            var service = CreateService();
            service.SwitchWindow(BuildWindowData(uiPrefabs, widgets));
            return service;
        }

        /// <summary>
        /// Builds a runtime stand-in for a UI prefab: a tracked, active GameObject carrying a
        /// <typeparamref name="T"/> component. Instantiate treats a scene object exactly like a prefab.
        /// </summary>
        protected GameObject BuildUIPrefab<T>() where T : UIBase
        {
            var host = Track(new GameObject(typeof(T).Name));
            host.AddComponent<T>();
            return host;
        }

        /// <summary>Builds a runtime prefab and hands back its component. Never bound to a window.</summary>
        protected T BuildUIElement<T>() where T : UIBase
        {
            return BuildUIPrefab<T>().GetComponent<T>();
        }

        /// <summary>
        /// Builds a runtime stand-in for a widget prefab. Separate from <see cref="BuildUIPrefab{T}"/>
        /// because widgets live in their own hierarchy rooted at <see cref="UIWidget"/>.
        /// </summary>
        protected GameObject BuildWidgetPrefab<T>() where T : UIWidget
        {
            var host = Track(new GameObject(typeof(T).Name));
            host.AddComponent<T>();
            return host;
        }

        /// <summary>Builds a widget prefab and hands back its component. Never bound to a window.</summary>
        protected T BuildWidget<T>() where T : UIWidget
        {
            return BuildWidgetPrefab<T>().GetComponent<T>();
        }

        /// <summary>Builds a tracked, active GameObject carrying no UI component.</summary>
        protected GameObject BuildEmptyPrefab(string objectName = "EmptyPrefab")
        {
            return Track(new GameObject(objectName));
        }

        /// <summary>Builds a tracked <see cref="WindowData"/> from UI prefabs alone.</summary>
        protected WindowData BuildWindowData(params GameObject[] uiPrefabs)
        {
            return BuildWindowData(uiPrefabs, null, null);
        }

        /// <summary>Builds a tracked <see cref="WindowData"/> from UI prefabs and widget entries.</summary>
        protected WindowData BuildWindowData(GameObject[] uiPrefabs, WidgetData[] widgets)
        {
            return BuildWindowData(uiPrefabs, widgets, null);
        }

        /// <summary>Builds a tracked <see cref="WindowData"/> with explicit canvas settings. Not validated.</summary>
        protected WindowData BuildWindowData(GameObject[] uiPrefabs, WidgetData[] widgets, CanvasSettings canvas)
        {
            return Track(WindowData.Create(uiPrefabs, widgets, canvas));
        }

        /// <summary>Wraps each prefab in a <see cref="WidgetData"/>. Nothing is filtered — malformed shapes must reach validation intact.</summary>
        protected static WidgetData[] BuildWidgets(params GameObject[] widgetPrefabs)
        {
            if (widgetPrefabs == null)
            {
                return new WidgetData[0];
            }

            var entries = new WidgetData[widgetPrefabs.Length];
            for (var i = 0; i < widgetPrefabs.Length; i++)
            {
                entries[i] = new WidgetData(widgetPrefabs[i]);
            }

            return entries;
        }

        /// <summary>
        /// Builds a <see cref="CanvasSettings"/> with non-default values by overwriting a
        /// default-constructed instance from JSON keyed by the serialized field names.
        /// </summary>
        protected static CanvasSettings BuildCanvasSettings(string json)
        {
            var settings = new CanvasSettings();
            JsonUtility.FromJsonOverwrite(json, settings);
            return settings;
        }
    }
}
