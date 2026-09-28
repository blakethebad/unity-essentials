using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

namespace UnityEssentials.UI.Tests
{
    /// <summary>
    /// The base every UI fixture derives from: tracks the objects and services a test creates, tears
    /// them down deterministically, resets the package's static state, and carries the builders that
    /// turn plain GameObjects into the "prefabs" a <see cref="WindowData"/> expects.
    /// </summary>
    public abstract class UITestFixture
    {
        private readonly List<UnityEngine.Object> _trackedObjects = new List<UnityEngine.Object>();
        private readonly List<UIService> _trackedServices = new List<UIService>();

        protected static IReadOnlyList<UIBase> CallLog => UICallLog.Entries;

        protected static IReadOnlyList<UIBase> ShowLog => UICallLog.Shows;

        protected static IReadOnlyList<UIBase> HideLog => UICallLog.Hides;

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
            // Windows are closed first so every element's OnHide runs against a live window — closing
            // also destroys each service's window parent — then tracked objects are destroyed
            // newest-first. Dispose rather than CloseWindow so every test exercises the IDisposable
            // implementation, which is specified to be an idempotent CloseWindow.
            try
            {
                for (var i = _trackedServices.Count - 1; i >= 0; i--)
                {
                    _trackedServices[i].Dispose();
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
        protected UIService TrackService(UIService service)
        {
            if (service != null)
            {
                _trackedServices.Add(service);
            }

            return service;
        }

        /// <summary>Creates a tracked <see cref="UIService"/> with no window loaded.</summary>
        protected UIService CreateService()
        {
            return TrackService(new UIService());
        }

        /// <summary>
        /// Creates a tracked service and immediately loads a window built from
        /// <paramref name="uiPrefabs"/>. The route to a bound element: resolve one with
        /// <see cref="UIService.GetUI{T}"/> and it is legal to show.
        /// </summary>
        protected UIService CreateLoadedService(params GameObject[] uiPrefabs)
        {
            var service = CreateService();
            service.SwitchWindow(BuildWindowData(uiPrefabs));
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

        /// <summary>
        /// Builds a runtime prefab and hands back its component. The element is <b>unbound</b> — no
        /// window ever instantiated it — so calling <see cref="UIBase.Show(IUIData)"/> on it throws
        /// <see cref="UIBindingException"/>.
        /// </summary>
        /// <remarks>
        /// Use this to assert that throw, and to inspect an element's initial <see cref="UIBase.State"/>
        /// before anything has happened to it. For an element you intend to show, go through
        /// <see cref="CreateLoadedService(GameObject[])"/> and <see cref="UIService.GetUI{T}"/>
        /// instead: only a window-instantiated instance is bound.
        /// </remarks>
        protected T BuildUIElement<T>() where T : UIBase
        {
            return BuildUIPrefab<T>().GetComponent<T>();
        }

        /// <summary>Builds a tracked, active GameObject carrying no UI component.</summary>
        protected GameObject BuildEmptyPrefab(string objectName = "EmptyPrefab")
        {
            return Track(new GameObject(objectName));
        }

        /// <summary>Builds a tracked <see cref="WindowData"/> from UI prefabs alone. Not validated.</summary>
        protected WindowData BuildWindowData(params GameObject[] uiPrefabs)
        {
            return BuildWindowData(uiPrefabs, null);
        }

        /// <summary>
        /// Builds a tracked <see cref="WindowData"/> with explicit canvas settings. Nothing is
        /// filtered or checked — malformed shapes must reach validation intact.
        /// </summary>
        protected WindowData BuildWindowData(GameObject[] uiPrefabs, CanvasSettings canvas)
        {
            return Track(WindowData.Create(uiPrefabs, canvas));
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
