using NUnit.Framework;
using UnityEngine;
using UnityEngine.UI;

namespace UnityEssentials.UI.Tests
{
    /// <summary>
    /// What <c>SwitchWindow</c> builds: the service's window parent, the window GameObject with its
    /// canvas trio, and one bound, deactivated instance of every UI prefab parented directly under
    /// the window — a flat hierarchy with nothing else created alongside it.
    /// </summary>
    [TestFixture]
    public class WindowLoadTests : UITestFixture
    {
        // ---- The window parent ------------------------------------------------

        [Test]
        public void NewService_BeforeSwitchWindow_CreatesNoWindowParent()
        {
            CreateService();

            Assert.IsNull(GameObject.Find(UIService.WindowParentName));
        }

        [Test]
        public void SwitchWindow_CreatesTheWindowParent()
        {
            CreateLoadedService(BuildUIPrefab<TestElementA>());

            Assert.IsNotNull(GameObject.Find(UIService.WindowParentName));
        }

        [Test]
        public void WindowParent_ReadTwice_ReturnsTheSameParent()
        {
            var service = CreateService();

            var first = service.WindowParent;
            var second = service.WindowParent;

            Assert.AreSame(first, second);
        }

        [Test]
        public void SwitchWindow_OnTwoServices_ParentsEachWindowUnderItsOwnParent()
        {
            var first = CreateLoadedService(BuildUIPrefab<TestElementA>());
            var second = CreateLoadedService(BuildUIPrefab<TestElementB>());

            // Not AreNotEqual: NUnit walks a Transform as a collection of children, and two
            // structurally identical hierarchies compare equal — identity is what matters here.
            Assert.AreNotSame(first.ActiveWindow.transform.parent, second.ActiveWindow.transform.parent);
        }

        [Test]
        public void WindowParent_OutsidePlayMode_CreatesTheParent()
        {
            var parent = CreateService().WindowParent;

            Assert.IsFalse(Application.isPlaying, "this suite is EditMode by design; see UIWindow.SafeDestroy.");
            Assert.IsNotNull(parent);
            Assert.AreEqual(UIService.WindowParentName, parent.gameObject.name);
        }

        [Test]
        public void WindowParent_LeavesTheParentVisibleInTheHierarchy()
        {
            var parent = CreateService().WindowParent;

            Assert.AreEqual(HideFlags.None, parent.gameObject.hideFlags);
        }

        [Test]
        public void CloseWindow_DestroysTheWindowParent()
        {
            var service = CreateLoadedService(BuildUIPrefab<TestElementA>());
            var host = service.WindowParent.gameObject;

            service.CloseWindow();

            Assert.IsTrue(host == null, "the window parent should have been destroyed, not merely forgotten");
        }

        [Test]
        public void SwitchWindow_AfterCloseWindow_CreatesANewParent()
        {
            var service = CreateLoadedService(BuildUIPrefab<TestElementA>());
            var first = service.WindowParent;
            service.CloseWindow();

            service.SwitchWindow(BuildWindowData(BuildUIPrefab<TestElementB>()));

            Assert.IsNotNull(service.WindowParent);
            Assert.AreNotSame(first, service.WindowParent);
        }

        [Test]
        public void SwitchWindow_ParentsTheWindowUnderTheWindowParent()
        {
            var service = CreateLoadedService(BuildUIPrefab<TestElementA>());

            Assert.AreSame(service.WindowParent, service.ActiveWindow.transform.parent);
        }

        // ---- The window GameObject -------------------------------------------

        [Test]
        public void SwitchWindow_BuildsTheCanvasTrioOnTheWindow()
        {
            var window = CreateLoadedService(BuildUIPrefab<TestElementA>()).ActiveWindow;

            Assert.IsNotNull(window.GetComponent<Canvas>());
            Assert.IsNotNull(window.GetComponent<CanvasScaler>());
            Assert.IsNotNull(window.GetComponent<GraphicRaycaster>());
            Assert.AreSame(window.GetComponent<Canvas>(), window.Canvas);
        }

        [Test]
        public void SwitchWindow_ConfiguresTheCanvasFromTheAssetsSettings()
        {
            var service = CreateService();
            var data = BuildWindowData(
                new[] { BuildUIPrefab<TestElementA>() },
                BuildCanvasSettings("{\"sortingOrder\":9,\"referenceResolution\":{\"x\":800.0,\"y\":600.0}}"));

            var window = service.SwitchWindow(data);

            Assert.AreEqual(9, window.Canvas.sortingOrder);
            Assert.AreEqual(new Vector2(800f, 600f), window.GetComponent<CanvasScaler>().referenceResolution);
        }

        [Test]
        public void SwitchWindow_AlwaysBuildsAScreenSpaceOverlayCanvas()
        {
            var window = CreateLoadedService(BuildUIPrefab<TestElementA>()).ActiveWindow;

            Assert.AreEqual(RenderMode.ScreenSpaceOverlay, window.Canvas.renderMode);
            Assert.IsTrue(window.Canvas.worldCamera == null);
        }

        // ---- Elements ----------------------------------------------------------

        [Test]
        public void SwitchWindow_InstantiatesOneElementPerPrefabInOrder()
        {
            var window = CreateLoadedService(
                BuildUIPrefab<TestElementA>(),
                BuildUIPrefab<TestElementB>(),
                BuildUIPrefab<TestElementC>()).ActiveWindow;

            Assert.AreEqual(3, window.Elements.Count);
            Assert.IsInstanceOf<TestElementA>(window.Elements[0]);
            Assert.IsInstanceOf<TestElementB>(window.Elements[1]);
            Assert.IsInstanceOf<TestElementC>(window.Elements[2]);
        }

        [Test]
        public void SwitchWindow_ParentsEveryElementDirectlyUnderTheWindow()
        {
            var window = CreateLoadedService(
                BuildUIPrefab<TestElementA>(),
                BuildUIPrefab<TestElementB>(),
                BuildUIPrefab<TestElementC>()).ActiveWindow;

            // AreSame, not AreEqual: NUnit deep-compares a Transform as a collection of children.
            Assert.AreSame(window.transform, window.GetUI<TestElementA>().transform.parent);
            Assert.AreSame(window.transform, window.GetUI<TestElementB>().transform.parent);
            Assert.AreSame(window.transform, window.GetUI<TestElementC>().transform.parent);
        }

        // Sibling order is authored order: no containers, no re-sorting at load.
        [Test]
        public void SwitchWindow_OrdersElementsByTheirAuthoredOrder()
        {
            var window = CreateLoadedService(
                BuildUIPrefab<TestElementA>(),
                BuildUIPrefab<TestElementB>(),
                BuildUIPrefab<TestElementC>()).ActiveWindow;

            Assert.AreEqual(0, window.GetUI<TestElementA>().transform.GetSiblingIndex());
            Assert.AreEqual(1, window.GetUI<TestElementB>().transform.GetSiblingIndex());
            Assert.AreEqual(2, window.GetUI<TestElementC>().transform.GetSiblingIndex());
        }

        [Test]
        public void SwitchWindow_InstantiatesClonesRatherThanTheSourcePrefabs()
        {
            var prefab = BuildUIPrefab<TestElementA>();
            var window = CreateLoadedService(prefab).ActiveWindow;

            Assert.AreNotSame(prefab.GetComponent<TestElementA>(), window.GetUI<TestElementA>());
        }

        [Test]
        public void SwitchWindow_LeavesEveryElementDeactivated()
        {
            var window = CreateLoadedService(
                BuildUIPrefab<TestElementA>(),
                BuildUIPrefab<TestElementB>(),
                BuildUIPrefab<TestElementC>()).ActiveWindow;

            for (var i = 0; i < window.Elements.Count; i++)
            {
                Assert.IsFalse(
                    window.Elements[i].gameObject.activeSelf,
                    $"{window.Elements[i].GetType().Name} was left active by window load");
            }
        }

        [Test]
        public void SwitchWindow_LeavesEveryElementHidden()
        {
            var window = CreateLoadedService(
                BuildUIPrefab<TestElementA>(),
                BuildUIPrefab<TestElementB>(),
                BuildUIPrefab<TestElementC>()).ActiveWindow;

            for (var i = 0; i < window.Elements.Count; i++)
            {
                Assert.AreEqual(
                    UIElementState.Hidden,
                    window.Elements[i].State,
                    $"{window.Elements[i].GetType().Name} did not start hidden");
            }
        }

        [Test]
        public void SwitchWindow_ShowsNothing()
        {
            var window = CreateLoadedService(
                BuildUIPrefab<TestElementA>(),
                BuildUIPrefab<TestElementB>(),
                BuildUIPrefab<TestElementC>()).ActiveWindow;

            for (var i = 0; i < window.Elements.Count; i++)
            {
                Assert.IsFalse(
                    window.Elements[i].IsVisible,
                    $"{window.Elements[i].GetType().Name} was shown by window load");
            }

            Assert.AreEqual(0, ShowLog.Count, UICallLog.Describe());
        }

        // ---- Type-keyed resolution ---------------------------------------------

        [Test]
        public void GetUI_ResolvesByConcreteType()
        {
            var window = CreateLoadedService(
                BuildUIPrefab<TestElementA>(),
                BuildUIPrefab<TestElementB>()).ActiveWindow;

            var resolved = window.GetUI<TestElementA>();

            Assert.IsInstanceOf<TestElementA>(resolved);
            Assert.AreSame(resolved, window.GetUI(typeof(TestElementA)));
        }

        [Test]
        public void GetUI_DistinguishesElementsOfDifferentTypes()
        {
            var window = CreateLoadedService(
                BuildUIPrefab<TestElementA>(),
                BuildUIPrefab<TestElementB>()).ActiveWindow;

            Assert.AreNotSame(window.GetUI<TestElementA>(), window.GetUI<TestElementB>());
        }

        [Test]
        public void SwitchWindow_BindsEveryElementToTheWindow()
        {
            var service = CreateLoadedService(BuildUIPrefab<TestElementA>());
            var window = service.ActiveWindow;

            Assert.AreSame(window, window.GetUI<TestElementA>().Window);
        }

        // ---- The flat hierarchy ---------------------------------------------------

        [Test]
        public void SwitchWindow_CreatesNoChildrenBeyondTheListedElements()
        {
            var window = CreateLoadedService(
                BuildUIPrefab<TestElementA>(),
                BuildUIPrefab<TestElementB>()).ActiveWindow;

            Assert.AreEqual(window.Elements.Count, window.transform.childCount);
        }
    }
}
