using System;
using NUnit.Framework;
using UnityEngine;

namespace UnityEssentials.UI.Tests
{
    /// <summary>
    /// The window-boundary traces: switching between assets, closing a window outright, disposing the
    /// service, the hide-before-destroy teardown, and what survives a switch that was rejected as
    /// malformed.
    /// </summary>
    [TestFixture]
    public class WindowSwitchTests : UITestFixture
    {
        // ---- Teardown ----------------------------------------------------------

        // A set, not a sequence: the hide pass lives in UIWindow.UnloadWindow, which walks its
        // element store in reverse index order because that is how the store happens to be laid out
        // today. The window's own documentation calls that "current layout, not a guarantee", so the
        // contract worth pinning is that every visible element was hidden — and that an element left
        // hidden was not.
        [Test]
        public void SwitchWindow_RunsOnHideForEveryVisibleElement()
        {
            var service = CreatePopulatedService();
            service.ShowUI<TestElementA>();
            service.ShowUI<TestElementB>();
            service.ShowUI<TestElementC>();
            service.ShowUI<TestElementD>();
            UICallLog.Clear();

            service.SwitchWindow(BuildWindowData(BuildUIPrefab<CountingElement>()));

            CollectionAssert.AreEquivalent(
                new[]
                {
                    "Hide:TestElementA",
                    "Hide:TestElementB",
                    "Hide:TestElementC",
                    "Hide:TestElementD"
                },
                UICallLog.Markers,
                UICallLog.Describe());
        }

        [Test]
        public void SwitchWindow_RunsEachElementsHideExactlyOnce()
        {
            var service = CreatePopulatedService();
            service.ShowUI<TestElementA>();
            service.ShowUI<TestElementC>();
            var first = service.GetUI<TestElementA>();
            var second = service.GetUI<TestElementC>();

            service.SwitchWindow(BuildWindowData(BuildUIPrefab<CountingElement>()));

            Assert.AreEqual(1, first.HideCount);
            Assert.AreEqual(1, second.HideCount);
        }

        // ---- What a switch destroys and what it reuses ---------------------------

        [Test]
        public void SwitchWindow_DestroysThePreviousWindow()
        {
            var service = CreateLoadedService(BuildUIPrefab<TestElementA>());
            var previous = service.ActiveWindow;

            service.SwitchWindow(BuildWindowData(BuildUIPrefab<TestElementB>()));

            Assert.IsTrue(previous == null, "the previous window's GameObject should have been destroyed");
        }

        [Test]
        public void SwitchWindow_DestroysThePreviousWindowsElements()
        {
            var service = CreateLoadedService(BuildUIPrefab<TestElementA>());
            var element = service.GetUI<TestElementA>();

            service.SwitchWindow(BuildWindowData(BuildUIPrefab<TestElementB>()));

            Assert.IsTrue(element == null, "the previous window's elements should have been destroyed with it");
        }

        [Test]
        public void SwitchWindow_ReusesTheWindowParent()
        {
            var service = CreateLoadedService(BuildUIPrefab<TestElementA>());
            var parent = service.ActiveWindow.transform.parent;

            service.SwitchWindow(BuildWindowData(BuildUIPrefab<TestElementB>()));

            Assert.AreSame(parent, service.ActiveWindow.transform.parent);
        }

        [Test]
        public void SwitchWindow_LoadsTheIncomingDefinition()
        {
            var service = CreateLoadedService(BuildUIPrefab<TestElementA>());
            var data = BuildWindowData(BuildUIPrefab<TestElementB>());

            var window = service.SwitchWindow(data);

            Assert.AreSame(data, service.ActiveWindowData);
            Assert.AreSame(window, service.ActiveWindow);
            Assert.IsNotNull(window.GetUI<TestElementB>());
        }

        // ---- The same-asset no-op -------------------------------------------------

        [Test]
        public void SwitchWindow_WithTheAlreadyActiveAsset_ReturnsTheSameWindow()
        {
            var service = CreateLoadedService(BuildUIPrefab<TestElementA>());
            var window = service.ActiveWindow;

            var again = service.SwitchWindow(service.ActiveWindowData);

            Assert.AreSame(window, again);
        }

        [Test]
        public void SwitchWindow_WithTheAlreadyActiveAsset_KeepsTheExistingElements()
        {
            var service = CreateLoadedService(BuildUIPrefab<TestElementA>());
            var element = service.GetUI<TestElementA>();

            service.SwitchWindow(service.ActiveWindowData);

            Assert.AreSame(element, service.GetUI<TestElementA>());
        }

        [Test]
        public void SwitchWindow_WithTheAlreadyActiveAsset_HidesNothing()
        {
            var service = CreateLoadedService(BuildUIPrefab<TestElementA>());
            service.ShowUI<TestElementA>();
            UICallLog.Clear();

            service.SwitchWindow(service.ActiveWindowData);

            Assert.AreEqual(0, HideLog.Count, UICallLog.Describe());
        }

        [Test]
        public void SwitchWindow_WithNull_ThrowsArgumentNullException()
        {
            var service = CreateLoadedService(BuildUIPrefab<TestElementA>());

            Assert.Throws<ArgumentNullException>(() => service.SwitchWindow(null));
        }

        // ---- A rejected switch leaves the live window untouched ----------------------

        [Test]
        public void SwitchWindow_WithAMalformedDefinition_Throws()
        {
            var service = CreateLoadedService(BuildUIPrefab<TestElementA>());

            Assert.Throws<WindowConfigurationException>(
                () => service.SwitchWindow(BuildWindowData(new GameObject[] { null })));
        }

        // Validation runs before anything is unloaded, so a malformed asset costs nothing.
        [Test]
        public void SwitchWindow_WithAMalformedDefinition_LeavesTheLiveWindowLoaded()
        {
            var service = CreateLoadedService(BuildUIPrefab<TestElementA>(), BuildUIPrefab<TestElementB>());
            var window = service.ActiveWindow;

            Assert.Throws<WindowConfigurationException>(
                () => service.SwitchWindow(BuildWindowData(new GameObject[] { null })));

            Assert.AreSame(window, service.ActiveWindow);
        }

        [Test]
        public void SwitchWindow_WithAMalformedDefinition_LeavesTheVisibleElementVisible()
        {
            var service = CreateLoadedService(BuildUIPrefab<TestElementA>(), BuildUIPrefab<TestElementB>());
            var element = service.ShowUI<TestElementA>();

            Assert.Throws<WindowConfigurationException>(
                () => service.SwitchWindow(BuildWindowData(new GameObject[] { null })));

            Assert.AreSame(element, service.GetUI<TestElementA>());
            Assert.IsTrue(element.IsVisible);
        }

        [Test]
        public void SwitchWindow_WithAMalformedDefinition_LeavesTheLiveWindowUsable()
        {
            var service = CreateLoadedService(BuildUIPrefab<TestElementA>(), BuildUIPrefab<TestElementB>());
            service.ShowUI<TestElementA>();

            Assert.Throws<WindowConfigurationException>(
                () => service.SwitchWindow(BuildWindowData(new GameObject[] { null })));

            Assert.DoesNotThrow(() => service.ShowUI<TestElementB>());
            Assert.IsTrue(service.GetUI<TestElementB>().IsVisible);
        }

        // ---- CloseWindow ---------------------------------------------------------------

        [Test]
        public void CloseWindow_UnloadsTheActiveWindow()
        {
            var service = CreateLoadedService(BuildUIPrefab<TestElementA>());
            var window = service.ActiveWindow;

            service.CloseWindow();

            Assert.IsNull(service.ActiveWindow);
            Assert.IsFalse(service.IsWindowLoaded);
            Assert.IsTrue(window == null, "the closed window's GameObject should have been destroyed");
        }

        // A set again, for the reason given on SwitchWindow_RunsOnHideForEveryVisibleElement. The
        // "before destroying" half is what the recorded hides prove: a destroyed element runs no
        // OnHide at all, so a marker for an element that reads as null afterwards can only have been
        // written while it was still alive.
        [Test]
        public void CloseWindow_RunsOnHideBeforeDestroying()
        {
            var service = CreatePopulatedService();
            service.ShowUI<TestElementA>();
            service.ShowUI<TestElementB>();
            service.ShowUI<TestElementC>();
            var element = service.GetUI<TestElementA>();
            UICallLog.Clear();

            service.CloseWindow();

            CollectionAssert.AreEquivalent(
                new[] { "Hide:TestElementA", "Hide:TestElementB", "Hide:TestElementC" },
                UICallLog.Markers,
                UICallLog.Describe());
            Assert.IsTrue(element == null, "the window's elements should have been destroyed after their OnHide ran");
        }

        [Test]
        public void CloseWindow_WithNothingLoaded_DoesNothing()
        {
            var service = CreateService();

            Assert.DoesNotThrow(() => service.CloseWindow());

            Assert.IsNull(service.ActiveWindow);
            Assert.IsNull(service.ActiveWindowData);
            Assert.IsFalse(service.IsWindowLoaded);
        }

        [Test]
        public void CloseWindow_ThenShowUI_ThrowsNoActiveWindow()
        {
            var service = CreateLoadedService(BuildUIPrefab<TestElementA>());

            service.CloseWindow();

            Assert.Throws<NoActiveWindowException>(() => service.ShowUI<TestElementA>());
        }

        [Test]
        public void CloseWindow_ThenSwitchWindow_LoadsANewWindow()
        {
            var service = CreateLoadedService(BuildUIPrefab<TestElementA>());
            service.CloseWindow();

            var window = service.SwitchWindow(BuildWindowData(BuildUIPrefab<TestElementB>()));

            Assert.IsNotNull(window);
            Assert.IsNotNull(window.GetUI<TestElementB>());
        }

        [Test]
        public void CloseWindow_CalledTwice_IsANoOp()
        {
            var service = CreateLoadedService(BuildUIPrefab<TestElementA>());
            service.CloseWindow();

            Assert.DoesNotThrow(() => service.CloseWindow());
        }

        [Test]
        public void CloseWindow_LeavesTheQueriesAnsweringTheEmptyState()
        {
            var service = CreateLoadedService(BuildUIPrefab<TestElementA>());

            service.CloseWindow();

            Assert.IsNull(service.ActiveWindow);
            Assert.IsNull(service.ActiveWindowData);
            Assert.IsFalse(service.IsWindowLoaded);
        }

        // ---- Dispose ---------------------------------------------------------------------

        // Dispose is specified as an idempotent CloseWindow, not a one-way door: it raises no
        // ObjectDisposedException and leaves the service reusable, which is what lets a fixture
        // teardown dispose a service the test already closed.
        [Test]
        public void Dispose_ClosesTheActiveWindow()
        {
            var service = CreateLoadedService(BuildUIPrefab<TestElementA>());
            var window = service.ActiveWindow;

            service.Dispose();

            Assert.IsNull(service.ActiveWindow);
            Assert.IsFalse(service.IsWindowLoaded);
            Assert.IsTrue(window == null, "the disposed service's window should have been destroyed");
        }

        [Test]
        public void Dispose_CalledTwice_IsANoOp()
        {
            var service = CreateLoadedService(BuildUIPrefab<TestElementA>());
            service.Dispose();

            Assert.DoesNotThrow(() => service.Dispose());

            Assert.IsFalse(service.IsWindowLoaded);
        }

        // ---- Helpers ----------------------------------------------------------------

        // Five distinct types, one of which the teardown tests leave hidden, so "hid everything
        // visible" stays distinguishable from "hid everything".
        private UIService CreatePopulatedService()
        {
            return CreateLoadedService(
                BuildUIPrefab<TestElementA>(),
                BuildUIPrefab<TestElementB>(),
                BuildUIPrefab<TestElementC>(),
                BuildUIPrefab<TestElementD>(),
                BuildUIPrefab<TestElementE>());
        }
    }
}
