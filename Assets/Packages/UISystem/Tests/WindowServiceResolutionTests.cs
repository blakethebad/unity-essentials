using NUnit.Framework;

namespace UnityEssentials.UI.Tests
{
    /// <summary>
    /// How <see cref="WindowService"/> answers "give me this element": the GetUI/TryGetUI split,
    /// the miss messages written to be read from a build log, and which members treat "no window
    /// loaded" as an error versus an answer.
    /// </summary>
    [TestFixture]
    public class WindowServiceResolutionTests : UITestFixture
    {
        // ---- Hits -------------------------------------------------------------

        [Test]
        public void GetUI_ForALoadedType_ReturnsTheWindowsInstance()
        {
            var service = CreatePopulatedService();

            var screen = service.GetUI<TestScreenA>();

            Assert.IsNotNull(screen);
            Assert.AreSame(service.ActiveWindow.GetUI(typeof(TestScreenA)), screen);
        }

        [Test]
        public void GetUI_CalledTwice_ReturnsTheSameInstance()
        {
            var service = CreatePopulatedService();

            Assert.AreSame(service.GetUI<TestScreenA>(), service.GetUI<TestScreenA>());
        }

        [Test]
        public void GetUI_ForAHiddenElement_StillResolves()
        {
            var service = CreatePopulatedService();

            var popup = service.GetUI<TestPopupA>();

            Assert.AreEqual(UIElementState.Hidden, popup.State);
        }

        [Test]
        public void TryGetUI_ForALoadedType_ReturnsTrueAndTheInstance()
        {
            var service = CreatePopulatedService();

            var found = service.TryGetUI<TestScreenA>(out var screen);

            Assert.IsTrue(found);
            Assert.AreSame(service.GetUI<TestScreenA>(), screen);
        }

        // ---- Misses ------------------------------------------------------------

        [Test]
        public void GetUI_ForATypeTheWindowDoesNotCarry_Throws()
        {
            var service = CreatePopulatedService();

            Assert.Throws<UIElementNotFoundException>(() => service.GetUI<UnregisteredScreen>());
        }

        // The inventory in the message is the contract: it tells the reader whether the prefab is
        // missing from the asset or the wrong window is active.
        [Test]
        public void GetUI_MissMessage_ListsTheTypesTheWindowCarries()
        {
            var service = CreatePopulatedService();

            var error = Assert.Throws<UIElementNotFoundException>(() => service.GetUI<UnregisteredScreen>());

            StringAssert.Contains("UnregisteredScreen", error.Message);
            StringAssert.Contains("TestScreenA", error.Message);
            StringAssert.Contains("TestPopupA", error.Message);
            StringAssert.Contains("TestPanelA", error.Message);
        }

        [Test]
        public void ShowUI_ForATypeTheWindowDoesNotCarry_Throws()
        {
            var service = CreatePopulatedService();

            Assert.Throws<UIElementNotFoundException>(() => service.ShowUI<UnregisteredScreen>());
        }

        [Test]
        public void TryGetUI_ForATypeTheWindowDoesNotCarry_ReturnsFalseAndNull()
        {
            var service = CreatePopulatedService();

            var found = service.TryGetUI<UnregisteredScreen>(out var screen);

            Assert.IsFalse(found);
            Assert.IsNull(screen);
        }

        // ---- Widget types ---------------------------------------------------------

        // The generic surface rejects widget types at compile time now that widgets root at
        // UIWidget; only the Type-taking window overload can still be handed one.
        [Test]
        public void WindowGetUI_ForAWidgetType_PointsAtGetWidget()
        {
            var service = CreatePopulatedService();

            var error = Assert.Throws<UIElementNotFoundException>(
                () => service.ActiveWindow.GetUI(typeof(TestWidget)));

            StringAssert.Contains("GetWidget<TestWidget>()", error.Message);
        }

        // ---- Before any window is loaded — the calls that throw -----------------------

        [Test]
        public void ShowUI_BeforeSwitchWindow_ThrowsNoActiveWindow()
        {
            var service = CreateService();

            var error = Assert.Throws<NoActiveWindowException>(() => service.ShowUI<TestScreenA>());

            StringAssert.Contains("SwitchWindow", error.Message);
        }

        [Test]
        public void HideUI_BeforeSwitchWindow_ThrowsNoActiveWindow()
        {
            var service = CreateService();

            Assert.Throws<NoActiveWindowException>(() => service.HideUI<TestScreenA>());
        }

        [Test]
        public void GetUI_BeforeSwitchWindow_ThrowsNoActiveWindow()
        {
            var service = CreateService();

            Assert.Throws<NoActiveWindowException>(() => service.GetUI<TestScreenA>());
        }

        [Test]
        public void GetWidget_BeforeSwitchWindow_ThrowsNoActiveWindow()
        {
            var service = CreateService();

            Assert.Throws<NoActiveWindowException>(() => service.GetWidget<TestWidget>());
        }

        [Test]
        public void ReturnWidget_BeforeSwitchWindow_ThrowsNoActiveWindow()
        {
            var service = CreateService();
            var widget = BuildWidget<CountingWidget>();

            Assert.Throws<NoActiveWindowException>(() => service.ReturnWidget(widget));
        }

        [Test]
        public void GetUI_AfterCloseWindow_ThrowsNoActiveWindow()
        {
            var service = CreatePopulatedService();
            service.CloseWindow();

            Assert.Throws<NoActiveWindowException>(() => service.GetUI<TestScreenA>());
        }

        // ---- Before any window is loaded — the calls that answer instead ----------------

        [Test]
        public void TryGetUI_BeforeSwitchWindow_ReturnsFalseAndNull()
        {
            var service = CreateService();

            var found = service.TryGetUI<TestScreenA>(out var screen);

            Assert.IsFalse(found);
            Assert.IsNull(screen);
        }

        [Test]
        public void Back_BeforeSwitchWindow_ReturnsFalse()
        {
            var service = CreateService();

            Assert.IsFalse(service.Back());
        }

        [Test]
        public void ShowPreviousScreen_BeforeSwitchWindow_ReturnsFalse()
        {
            var service = CreateService();

            Assert.IsFalse(service.ShowPreviousScreen());
        }

        [Test]
        public void HideAllPopups_BeforeSwitchWindow_DoesNothing()
        {
            var service = CreateService();

            Assert.DoesNotThrow(() => service.HideAllPopups());
        }

        [Test]
        public void HideAllPanels_BeforeSwitchWindow_DoesNothing()
        {
            var service = CreateService();

            Assert.DoesNotThrow(() => service.HideAllPanels());
        }

        [Test]
        public void CloseWindow_BeforeSwitchWindow_DoesNothing()
        {
            var service = CreateService();

            Assert.DoesNotThrow(() => service.CloseWindow());
        }

        [Test]
        public void Queries_BeforeSwitchWindow_ReportTheEmptyState()
        {
            var service = CreateService();

            Assert.IsNull(service.ActiveWindow);
            Assert.IsNull(service.ActiveWindowData);
            Assert.IsFalse(service.IsWindowLoaded);
            Assert.IsNull(service.ActiveScreen);
            Assert.IsNull(service.PreviousScreen);
            Assert.AreEqual(0, service.ActivePopups.Count);
            Assert.AreEqual(0, service.ActivePanels.Count);
        }

        // ---- Helpers ---------------------------------------------------------------

        private WindowService CreatePopulatedService()
        {
            return CreateLoadedService(
                BuildUIPrefab<TestScreenA>(),
                BuildUIPrefab<TestPopupA>(),
                BuildUIPrefab<TestPanelA>());
        }
    }
}
