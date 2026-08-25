using System;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

namespace UnityEssentials.UI.Tests
{
    /// <summary>
    /// The window-boundary traces: switching between assets, closing a window outright, the
    /// hide-before-destroy teardown order, what survives a failed switch, and what a boundary does
    /// to the navigation history.
    /// </summary>
    [TestFixture]
    public class WindowSwitchTests : UITestFixture
    {
        // ---- Teardown order ----------------------------------------------------

        // Popups newest-first, then panels newest-first, then the screen — all before anything is
        // destroyed, so every OnHide teardown hook sees a live world.
        [Test]
        public void SwitchWindow_HidesPopupsThenPanelsThenTheScreen()
        {
            var service = CreatePopulatedService();
            service.ShowUI<TestScreenA>();
            service.ShowUI<TestPanelA>();
            service.ShowUI<TestPanelB>();
            service.ShowUI<TestPopupA>();
            service.ShowUI<TestPopupB>();
            UICallLog.Clear();

            service.SwitchWindow(BuildWindowData(BuildUIPrefab<TestScreenC>()));

            CollectionAssert.AreEqual(
                new[]
                {
                    "Hide:TestPopupB",
                    "Hide:TestPopupA",
                    "Hide:TestPanelB",
                    "Hide:TestPanelA",
                    "Hide:TestScreenA"
                },
                UICallLog.Markers,
                UICallLog.Describe());
        }

        [Test]
        public void SwitchWindow_RunsEachElementsHideExactlyOnce()
        {
            var service = CreatePopulatedService();
            service.ShowUI<TestScreenA>();
            service.ShowUI<TestPanelA>();
            var screen = service.GetUI<TestScreenA>();
            var panel = service.GetUI<TestPanelA>();

            service.SwitchWindow(BuildWindowData(BuildUIPrefab<TestScreenC>()));

            Assert.AreEqual(1, screen.HideCount);
            Assert.AreEqual(1, panel.HideCount);
        }

        // ---- What a switch destroys and what it reuses ---------------------------

        [Test]
        public void SwitchWindow_DestroysThePreviousWindow()
        {
            var service = CreateLoadedService(BuildUIPrefab<TestScreenA>());
            var previous = service.ActiveWindow;

            service.SwitchWindow(BuildWindowData(BuildUIPrefab<TestScreenB>()));

            Assert.IsTrue(previous == null, "the previous window's GameObject should have been destroyed");
        }

        [Test]
        public void SwitchWindow_DestroysThePreviousWindowsElements()
        {
            var service = CreateLoadedService(BuildUIPrefab<TestScreenA>());
            var element = service.GetUI<TestScreenA>();

            service.SwitchWindow(BuildWindowData(BuildUIPrefab<TestScreenB>()));

            Assert.IsTrue(element == null, "the previous window's elements should have been destroyed with it");
        }

        [Test]
        public void SwitchWindow_ReusesTheWindowParent()
        {
            var service = CreateLoadedService(BuildUIPrefab<TestScreenA>());
            var parent = service.ActiveWindow.transform.parent;

            service.SwitchWindow(BuildWindowData(BuildUIPrefab<TestScreenB>()));

            Assert.AreEqual(parent, service.ActiveWindow.transform.parent);
        }

        [Test]
        public void SwitchWindow_LoadsTheIncomingDefinition()
        {
            var service = CreateLoadedService(BuildUIPrefab<TestScreenA>());
            var data = BuildWindowData(BuildUIPrefab<TestScreenB>());

            var window = service.SwitchWindow(data);

            Assert.AreSame(data, service.ActiveWindowData);
            Assert.AreSame(window, service.ActiveWindow);
            Assert.IsNotNull(window.GetUI<TestScreenB>());
        }

        // ---- History ------------------------------------------------------------

        [Test]
        public void SwitchWindow_ClearsTheNavigationHistory()
        {
            var service = CreatePopulatedService();
            service.ShowUI<TestScreenA>();

            service.SwitchWindow(BuildWindowData(BuildUIPrefab<TestScreenC>()));

            Assert.IsFalse(service.Back(), "Back must not step into the window that was just unloaded");
        }

        [Test]
        public void SwitchWindow_ClearsTheActiveScreenAndLists()
        {
            var service = CreatePopulatedService();
            service.ShowUI<TestScreenA>();
            service.ShowUI<TestPanelA>();
            service.ShowUI<TestPopupA>();

            service.SwitchWindow(BuildWindowData(BuildUIPrefab<TestScreenC>()));

            Assert.IsNull(service.ActiveScreen);
            Assert.AreEqual(0, service.ActivePopups.Count);
            Assert.AreEqual(0, service.ActivePanels.Count);
        }

        // ---- The same-asset no-op -------------------------------------------------

        [Test]
        public void SwitchWindow_WithTheAlreadyActiveAsset_ReturnsTheSameWindow()
        {
            var service = CreateLoadedService(BuildUIPrefab<TestScreenA>());
            var window = service.ActiveWindow;

            var again = service.SwitchWindow(service.ActiveWindowData);

            Assert.AreSame(window, again);
        }

        [Test]
        public void SwitchWindow_WithTheAlreadyActiveAsset_KeepsTheExistingElements()
        {
            var service = CreateLoadedService(BuildUIPrefab<TestScreenA>());
            var element = service.GetUI<TestScreenA>();

            service.SwitchWindow(service.ActiveWindowData);

            Assert.AreSame(element, service.GetUI<TestScreenA>());
        }

        [Test]
        public void SwitchWindow_WithTheAlreadyActiveAsset_HidesNothing()
        {
            var service = CreateLoadedService(BuildUIPrefab<TestScreenA>());
            service.ShowUI<TestScreenA>();
            UICallLog.Clear();

            service.SwitchWindow(service.ActiveWindowData);

            Assert.AreEqual(0, HideLog.Count, UICallLog.Describe());
        }

        [Test]
        public void SwitchWindow_WithNull_ThrowsArgumentNullException()
        {
            var service = CreateLoadedService(BuildUIPrefab<TestScreenA>());

            Assert.Throws<ArgumentNullException>(() => service.SwitchWindow(null));
        }

        // ---- A rejected switch leaves the live window untouched ----------------------

        [Test]
        public void SwitchWindow_WithAMalformedDefinition_Throws()
        {
            var service = CreateLoadedService(BuildUIPrefab<TestScreenA>());

            Assert.Throws<WindowConfigurationException>(
                () => service.SwitchWindow(BuildWindowData(new GameObject[] { null })));
        }

        // Validation runs before anything is unloaded, so a malformed asset costs nothing.
        [Test]
        public void SwitchWindow_WithAMalformedDefinition_LeavesTheLiveWindowLoaded()
        {
            var service = CreateLoadedService(BuildUIPrefab<TestScreenA>(), BuildUIPrefab<TestScreenB>());
            var window = service.ActiveWindow;

            Assert.Throws<WindowConfigurationException>(
                () => service.SwitchWindow(BuildWindowData(new GameObject[] { null })));

            Assert.AreSame(window, service.ActiveWindow);
        }

        [Test]
        public void SwitchWindow_WithAMalformedDefinition_LeavesTheVisibleScreenVisible()
        {
            var service = CreateLoadedService(BuildUIPrefab<TestScreenA>(), BuildUIPrefab<TestScreenB>());
            service.ShowUI<TestScreenA>();

            Assert.Throws<WindowConfigurationException>(
                () => service.SwitchWindow(BuildWindowData(new GameObject[] { null })));

            Assert.AreSame(service.GetUI<TestScreenA>(), service.ActiveScreen);
            Assert.IsTrue(service.ActiveScreen.IsVisible);
        }

        [Test]
        public void SwitchWindow_WithAMalformedDefinition_LeavesTheLiveWindowUsable()
        {
            var service = CreateLoadedService(BuildUIPrefab<TestScreenA>(), BuildUIPrefab<TestScreenB>());
            service.ShowUI<TestScreenA>();

            Assert.Throws<WindowConfigurationException>(
                () => service.SwitchWindow(BuildWindowData(new GameObject[] { null })));

            Assert.DoesNotThrow(() => service.ShowUI<TestScreenB>());
            Assert.AreSame(service.GetUI<TestScreenB>(), service.ActiveScreen);
        }

        [Test]
        public void SwitchWindow_WithAMalformedDefinition_RaisesNoWindowSwitched()
        {
            var service = CreateLoadedService(BuildUIPrefab<TestScreenA>());
            var switches = 0;
            service.WindowSwitched += (from, to) => switches++;

            Assert.Throws<WindowConfigurationException>(
                () => service.SwitchWindow(BuildWindowData(new GameObject[] { null })));

            Assert.AreEqual(0, switches);
        }

        // ---- WindowSwitched ---------------------------------------------------------

        [Test]
        public void WindowSwitched_ReportsTheOutgoingAsset()
        {
            var service = CreateService();
            var froms = new List<WindowData>();
            service.WindowSwitched += (from, to) => froms.Add(from);
            var dataA = BuildWindowData(BuildUIPrefab<TestScreenA>());
            var dataB = BuildWindowData(BuildUIPrefab<TestScreenB>());

            service.SwitchWindow(dataA);
            service.SwitchWindow(dataB);

            Assert.AreEqual(2, froms.Count);
            Assert.IsNull(froms[0]);
            Assert.AreSame(dataA, froms[1]);
        }

        [Test]
        public void WindowSwitched_ReportsTheIncomingAssetOncePerSwitch()
        {
            var service = CreateService();
            var tos = new List<WindowData>();
            service.WindowSwitched += (from, to) => tos.Add(to);
            var dataA = BuildWindowData(BuildUIPrefab<TestScreenA>());
            var dataB = BuildWindowData(BuildUIPrefab<TestScreenB>());

            service.SwitchWindow(dataA);
            service.SwitchWindow(dataB);

            Assert.AreEqual(2, tos.Count);
            Assert.AreSame(dataA, tos[0]);
            Assert.AreSame(dataB, tos[1]);
        }

        [Test]
        public void WindowSwitched_IsRaisedAfterTheNewWindowIsBuilt()
        {
            var service = CreateService();
            UIWindow observed = null;
            service.WindowSwitched += (from, to) => observed = service.ActiveWindow;

            service.SwitchWindow(BuildWindowData(BuildUIPrefab<TestScreenA>()));

            Assert.IsNotNull(observed);
            Assert.IsNotNull(observed.GetUI<TestScreenA>());
        }

        // ---- CloseWindow ---------------------------------------------------------------

        [Test]
        public void CloseWindow_UnloadsTheActiveWindow()
        {
            var service = CreateLoadedService(BuildUIPrefab<TestScreenA>());
            var window = service.ActiveWindow;

            service.CloseWindow();

            Assert.IsNull(service.ActiveWindow);
            Assert.IsFalse(service.IsWindowLoaded);
            Assert.IsTrue(window == null, "the closed window's GameObject should have been destroyed");
        }

        [Test]
        public void CloseWindow_HidesPopupsThenPanelsThenTheScreenBeforeDestroying()
        {
            var service = CreatePopulatedService();
            service.ShowUI<TestScreenA>();
            service.ShowUI<TestPanelA>();
            service.ShowUI<TestPopupA>();
            UICallLog.Clear();

            service.CloseWindow();

            CollectionAssert.AreEqual(
                new[] { "Hide:TestPopupA", "Hide:TestPanelA", "Hide:TestScreenA" },
                UICallLog.Markers,
                UICallLog.Describe());
        }

        [Test]
        public void CloseWindow_RaisesWindowSwitchedWithANullDestination()
        {
            var service = CreateService();
            var data = BuildWindowData(BuildUIPrefab<TestScreenA>());
            service.SwitchWindow(data);
            var froms = new List<WindowData>();
            var tos = new List<WindowData>();
            service.WindowSwitched += (from, to) => { froms.Add(from); tos.Add(to); };

            service.CloseWindow();

            Assert.AreEqual(1, tos.Count);
            Assert.AreSame(data, froms[0]);
            Assert.IsNull(tos[0]);
        }

        [Test]
        public void CloseWindow_WithNothingLoaded_DoesNothing()
        {
            var service = CreateService();
            var switches = 0;
            service.WindowSwitched += (from, to) => switches++;

            Assert.DoesNotThrow(() => service.CloseWindow());

            Assert.AreEqual(0, switches);
        }

        [Test]
        public void CloseWindow_ThenShowUI_ThrowsNoActiveWindow()
        {
            var service = CreateLoadedService(BuildUIPrefab<TestScreenA>());

            service.CloseWindow();

            Assert.Throws<NoActiveWindowException>(() => service.ShowUI<TestScreenA>());
        }

        [Test]
        public void CloseWindow_ThenSwitchWindow_LoadsANewWindow()
        {
            var service = CreateLoadedService(BuildUIPrefab<TestScreenA>());
            service.CloseWindow();

            var window = service.SwitchWindow(BuildWindowData(BuildUIPrefab<TestScreenB>()));

            Assert.IsNotNull(window);
            Assert.IsNotNull(window.GetUI<TestScreenB>());
        }

        [Test]
        public void CloseWindow_CalledTwice_IsANoOp()
        {
            var service = CreateLoadedService(BuildUIPrefab<TestScreenA>());
            service.CloseWindow();

            Assert.DoesNotThrow(() => service.CloseWindow());
        }

        [Test]
        public void CloseWindow_LeavesTheQueriesAnsweringTheEmptyState()
        {
            var service = CreateLoadedService(BuildUIPrefab<TestScreenA>());

            service.CloseWindow();

            Assert.IsNull(service.ActiveWindow);
            Assert.IsNull(service.ActiveWindowData);
            Assert.IsFalse(service.IsWindowLoaded);
            Assert.IsNull(service.ActiveScreen);
            Assert.AreEqual(0, service.ActivePopups.Count);
            Assert.AreEqual(0, service.ActivePanels.Count);
        }

        // ---- Helpers ----------------------------------------------------------------

        // Two of each overlay kind: reverse-order closing is only observable with a pair.
        private WindowService CreatePopulatedService()
        {
            return CreateLoadedService(
                BuildUIPrefab<TestScreenA>(),
                BuildUIPrefab<TestPanelA>(),
                BuildUIPrefab<TestPanelB>(),
                BuildUIPrefab<TestPopupA>(),
                BuildUIPrefab<TestPopupB>());
        }
    }
}
