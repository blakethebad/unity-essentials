using NUnit.Framework;

namespace UnityEssentials.UI.Tests
{
    /// <summary>
    /// How <see cref="UIService"/> answers "give me this element": the GetUI/TryGetUI split, the miss
    /// messages written to be read from a build log, and which members treat "no window loaded" as an
    /// error versus an answer.
    /// </summary>
    [TestFixture]
    public class UIServiceResolutionTests : UITestFixture
    {
        // ---- Hits -------------------------------------------------------------

        [Test]
        public void GetUI_ForALoadedType_ReturnsTheWindowsInstance()
        {
            var service = CreatePopulatedService();

            var element = service.GetUI<TestElementA>();

            Assert.IsNotNull(element);
            Assert.AreSame(service.ActiveWindow.GetUI(typeof(TestElementA)), element);
        }

        [Test]
        public void GetUI_CalledTwice_ReturnsTheSameInstance()
        {
            var service = CreatePopulatedService();

            Assert.AreSame(service.GetUI<TestElementA>(), service.GetUI<TestElementA>());
        }

        [Test]
        public void GetUI_ForAHiddenElement_StillResolves()
        {
            var service = CreatePopulatedService();

            var element = service.GetUI<TestElementB>();

            Assert.AreEqual(UIElementState.Hidden, element.State);
        }

        [Test]
        public void TryGetUI_ForALoadedType_ReturnsTrueAndTheInstance()
        {
            var service = CreatePopulatedService();

            var found = service.TryGetUI<TestElementA>(out var element);

            Assert.IsTrue(found);
            Assert.AreSame(service.GetUI<TestElementA>(), element);
        }

        // ---- Misses ------------------------------------------------------------

        [Test]
        public void GetUI_ForATypeTheWindowDoesNotCarry_Throws()
        {
            var service = CreatePopulatedService();

            Assert.Throws<UIElementNotFoundException>(() => service.GetUI<UnregisteredElement>());
        }

        // The inventory in the message is the contract: it tells the reader whether the prefab is
        // missing from the asset or the wrong window is active.
        [Test]
        public void GetUI_MissMessage_ListsTheTypesTheWindowCarries()
        {
            var service = CreatePopulatedService();

            var error = Assert.Throws<UIElementNotFoundException>(() => service.GetUI<UnregisteredElement>());

            StringAssert.Contains("UnregisteredElement", error.Message);
            StringAssert.Contains("TestElementA", error.Message);
            StringAssert.Contains("TestElementB", error.Message);
            StringAssert.Contains("TestElementC", error.Message);
        }

        [Test]
        public void ShowUI_ForATypeTheWindowDoesNotCarry_Throws()
        {
            var service = CreatePopulatedService();

            Assert.Throws<UIElementNotFoundException>(() => service.ShowUI<UnregisteredElement>());
        }

        [Test]
        public void TryGetUI_ForATypeTheWindowDoesNotCarry_ReturnsFalseAndNull()
        {
            var service = CreatePopulatedService();

            var found = service.TryGetUI<UnregisteredElement>(out var element);

            Assert.IsFalse(found);
            Assert.IsNull(element);
        }

        // ---- Before any window is loaded — the calls that throw -----------------------

        [Test]
        public void ShowUI_BeforeSwitchWindow_ThrowsNoActiveWindow()
        {
            var service = CreateService();

            var error = Assert.Throws<NoActiveWindowException>(() => service.ShowUI<TestElementA>());

            StringAssert.Contains("SwitchWindow", error.Message);
        }

        [Test]
        public void HideUI_BeforeSwitchWindow_ThrowsNoActiveWindow()
        {
            var service = CreateService();

            Assert.Throws<NoActiveWindowException>(() => service.HideUI<TestElementA>());
        }

        [Test]
        public void GetUI_BeforeSwitchWindow_ThrowsNoActiveWindow()
        {
            var service = CreateService();

            Assert.Throws<NoActiveWindowException>(() => service.GetUI<TestElementA>());
        }

        [Test]
        public void GetUI_AfterCloseWindow_ThrowsNoActiveWindow()
        {
            var service = CreatePopulatedService();
            service.CloseWindow();

            Assert.Throws<NoActiveWindowException>(() => service.GetUI<TestElementA>());
        }

        // ---- Before any window is loaded — the calls that answer instead ----------------

        [Test]
        public void TryGetUI_BeforeSwitchWindow_ReturnsFalseAndNull()
        {
            var service = CreateService();

            var found = service.TryGetUI<TestElementA>(out var element);

            Assert.IsFalse(found);
            Assert.IsNull(element);
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
        }

        // ---- Helpers ---------------------------------------------------------------

        private UIService CreatePopulatedService()
        {
            return CreateLoadedService(
                BuildUIPrefab<TestElementA>(),
                BuildUIPrefab<TestElementB>(),
                BuildUIPrefab<TestElementC>());
        }
    }
}
