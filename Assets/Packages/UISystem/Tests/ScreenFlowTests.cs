using NUnit.Framework;

namespace UnityEssentials.UI.Tests
{
    /// <summary>
    /// The screen half of the navigation model: exclusivity, the supersede sequence and its
    /// push-before-hide ordering, auto-panels (order, payload and failure modes), and the
    /// same-screen refresh fast path.
    /// </summary>
    [TestFixture]
    public class ScreenFlowTests : UITestFixture
    {
        // ---- Screen exclusivity ---------------------------------------------

        [Test]
        public void ShowUI_AfterAScreenChain_LeavesExactlyOneScreenVisible()
        {
            var service = CreateLoadedService(
                BuildUIPrefab<TestScreenA>(),
                BuildUIPrefab<TestScreenB>(),
                BuildUIPrefab<TestScreenC>());

            service.ShowUI<TestScreenA>();
            service.ShowUI<TestScreenB>();
            service.ShowUI<TestScreenC>();

            var visibleScreens = 0;
            var elements = service.ActiveWindow.Elements;
            for (var i = 0; i < elements.Count; i++)
            {
                if (elements[i] is ScreenBase && elements[i].IsVisible)
                {
                    visibleScreens++;
                }
            }

            Assert.AreEqual(1, visibleScreens, "Screens are mutually exclusive: " + UICallLog.Describe());
        }

        [Test]
        public void ShowUI_AfterAScreenChain_MakesTheLastScreenActive()
        {
            var service = CreateLoadedService(
                BuildUIPrefab<TestScreenA>(),
                BuildUIPrefab<TestScreenB>(),
                BuildUIPrefab<TestScreenC>());

            service.ShowUI<TestScreenA>();
            service.ShowUI<TestScreenB>();
            var last = service.ShowUI<TestScreenC>();

            Assert.AreSame(last, service.ActiveScreen);
        }

        [Test]
        public void ShowUI_SupersedingAScreen_RunsItsOnHideExactlyOnce()
        {
            var service = CreateLoadedService(BuildUIPrefab<CountingScreen>(), BuildUIPrefab<TestScreenB>());
            var outgoing = service.ShowUI<CountingScreen>();
            outgoing.ResetCounts();

            service.ShowUI<TestScreenB>();

            Assert.AreEqual(1, outgoing.HideCount, UICallLog.Describe());
        }

        // The push-before-hide invariant: swapping the push and the hide in OnScreenShowing makes
        // this answer null with no exception anywhere — only a back button that skips a screen.
        [Test]
        public void ShowUI_SupersedingAScreen_KeepsItAsThePreviousScreen()
        {
            var service = CreateLoadedService(BuildUIPrefab<TestScreenA>(), BuildUIPrefab<TestScreenB>());
            var first = service.ShowUI<TestScreenA>();

            service.ShowUI<TestScreenB>();

            Assert.AreSame(first, service.PreviousScreen);
        }

        // ---- Auto-panels -----------------------------------------------------

        [Test]
        public void AutoPanels_OpenInDeclarationOrderAfterTheScreensOwnShow()
        {
            var service = CreateLoadedService(
                BuildUIPrefab<AutoPanelScreen>(),
                BuildUIPrefab<TestPanelA>(),
                BuildUIPrefab<TestPanelB>());

            var screen = service.ShowUI<AutoPanelScreen>();

            Assert.AreEqual(3, CallLog.Count, UICallLog.Describe());
            Assert.AreSame(screen, CallLog[0], UICallLog.Describe());
            Assert.AreSame(service.GetUI<TestPanelA>(), CallLog[1], UICallLog.Describe());
            Assert.AreSame(service.GetUI<TestPanelB>(), CallLog[2], UICallLog.Describe());
        }

        [Test]
        public void AutoPanels_ReceiveThePayloadFromGetAutoPanelData()
        {
            var service = CreateLoadedService(
                BuildUIPrefab<AutoPanelScreen>(),
                BuildUIPrefab<TestPanelA>(),
                BuildUIPrefab<TestPanelB>());

            service.ShowUI<AutoPanelScreen>();

            var received = service.GetUI<TestPanelA>().LastData as TestUIData;
            Assert.IsNotNull(received, "The panel was shown with no payload at all.");
            Assert.AreEqual(AutoPanelScreen.ExpectedDataLabel(typeof(TestPanelA)), received.Label);
        }

        [Test]
        public void AutoPanels_NonPanelEntry_ThrowsWindowConfigurationException()
        {
            var service = CreateLoadedService(BuildUIPrefab<BadAutoPanelScreen>());

            var exception = Assert.Throws<WindowConfigurationException>(() => service.ShowUI<BadAutoPanelScreen>());
            StringAssert.Contains("TestPopupA", exception.Message);
        }

        [Test]
        public void AutoPanels_MissingPanel_ThrowsNamingBothTheScreenAndThePanel()
        {
            var service = CreateLoadedService(BuildUIPrefab<MissingAutoPanelScreen>());

            var exception = Assert.Throws<UIElementNotFoundException>(() => service.ShowUI<MissingAutoPanelScreen>());
            StringAssert.Contains("MissingAutoPanelScreen", exception.Message);
            StringAssert.Contains("UnregisteredPanel", exception.Message);
        }

        [Test]
        public void AutoPanels_NullEntry_ThrowsWindowConfigurationException()
        {
            var service = CreateLoadedService(BuildUIPrefab<NullAutoPanelEntryScreen>());

            Assert.Throws<WindowConfigurationException>(() => service.ShowUI<NullAutoPanelEntryScreen>());
        }

        [Test]
        public void AutoPanels_NullArray_OpensNothingAndDoesNotThrow()
        {
            var service = CreateLoadedService(BuildUIPrefab<NullAutoPanelArrayScreen>(), BuildUIPrefab<TestPanelA>());

            Assert.DoesNotThrow(() => service.ShowUI<NullAutoPanelArrayScreen>());
            Assert.AreEqual(0, service.ActivePanels.Count);
        }

        // ---- Panels never outlive their screen -------------------------------

        [Test]
        public void ScreenChange_ClosesEveryPanelThePreviousScreenOpened()
        {
            var service = CreateLoadedService(
                BuildUIPrefab<AutoPanelScreen>(),
                BuildUIPrefab<TestScreenB>(),
                BuildUIPrefab<TestPanelA>(),
                BuildUIPrefab<TestPanelB>());

            service.ShowUI<AutoPanelScreen>();
            service.ShowUI<TestScreenB>();

            Assert.AreEqual(0, service.ActivePanels.Count, UICallLog.Describe());
            Assert.IsFalse(service.GetUI<TestPanelA>().IsVisible);
            Assert.IsFalse(service.GetUI<TestPanelB>().IsVisible);
        }

        // ---- Same-screen re-show ---------------------------------------------

        [Test]
        public void ReShowingTheActiveScreen_RunsOnShowAgainWithTheNewData()
        {
            var service = CreateLoadedService(BuildUIPrefab<CountingScreen>());
            var screen = service.ShowUI<CountingScreen>();
            var refreshed = new TestUIData("refresh", 7);

            service.ShowUI<CountingScreen>(refreshed);

            Assert.AreEqual(2, screen.ShowCount);
            Assert.AreSame(refreshed, screen.LastData);
        }

        [Test]
        public void ReShowingTheActiveScreen_DoesNotReopenItsPanels()
        {
            var service = CreateLoadedService(
                BuildUIPrefab<AutoPanelScreen>(),
                BuildUIPrefab<TestPanelA>(),
                BuildUIPrefab<TestPanelB>());

            service.ShowUI<AutoPanelScreen>();
            service.ShowUI<AutoPanelScreen>();

            var panel = service.GetUI<TestPanelA>();
            Assert.AreEqual(1, panel.ShowCount, UICallLog.Describe());
            Assert.AreEqual(0, panel.HideCount, UICallLog.Describe());
        }

        [Test]
        public void ReShowingTheActiveScreen_DoesNotPushHistoryAgain()
        {
            var service = CreateLoadedService(BuildUIPrefab<TestScreenA>(), BuildUIPrefab<TestScreenB>());
            var first = service.ShowUI<TestScreenA>();
            service.ShowUI<TestScreenB>();
            service.ShowUI<TestScreenB>();

            Assert.IsTrue(service.Back(), "The trail should still hold the screen shown before the refresh.");
            Assert.AreSame(first, service.ActiveScreen);
            Assert.IsFalse(service.Back(), "The refresh must not have left an extra step on the trail.");
        }

        // The refresh updates the stored history data, so a later Back restores the screen with the
        // payload it last displayed rather than the one it was first opened with.
        [Test]
        public void ReShowingTheActiveScreen_UpdatesTheDataBackWillRestoreItWith()
        {
            var service = CreateLoadedService(BuildUIPrefab<CountingScreen>(), BuildUIPrefab<TestScreenB>());
            var screen = service.ShowUI<CountingScreen>(new TestUIData("original"));
            var refreshed = new TestUIData("refreshed");
            service.ShowUI<CountingScreen>(refreshed);

            service.ShowUI<TestScreenB>();
            service.Back();

            Assert.AreSame(refreshed, screen.LastData);
        }
    }
}
