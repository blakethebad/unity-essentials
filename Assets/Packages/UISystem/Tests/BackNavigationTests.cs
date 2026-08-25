using NUnit.Framework;

namespace UnityEssentials.UI.Tests
{
    /// <summary>
    /// <c>Back()</c> and <c>ShowPreviousScreen()</c>: popup precedence, the screen back-step and its
    /// stored-data restore, termination of repeated presses, and every case that answers
    /// <c>false</c> rather than throwing.
    /// </summary>
    [TestFixture]
    public class BackNavigationTests : UITestFixture
    {
        // ---- Popup precedence ------------------------------------------------

        [Test]
        public void Back_WithPopupsOpen_HidesTheNewestPopup()
        {
            var service = CreateLoadedService(
                BuildUIPrefab<TestScreenA>(),
                BuildUIPrefab<TestPopupA>(),
                BuildUIPrefab<TestPopupB>());

            service.ShowUI<TestScreenA>();
            var first = service.ShowUI<TestPopupA>();
            var newest = service.ShowUI<TestPopupB>();

            Assert.IsTrue(service.Back());
            Assert.IsFalse(newest.IsVisible, "Back must close the topmost popup.");
            Assert.IsTrue(first.IsVisible, "Back must close exactly one popup.");
        }

        [Test]
        public void Back_WithAPopupOpen_LeavesTheActiveScreenUnchanged()
        {
            var service = CreateLoadedService(BuildUIPrefab<TestScreenA>(), BuildUIPrefab<TestPopupA>());
            var screen = service.ShowUI<TestScreenA>();
            service.ShowUI<TestPopupA>();

            service.Back();

            Assert.AreSame(screen, service.ActiveScreen);
        }

        // ---- The screen back-step --------------------------------------------

        [Test]
        public void Back_AfterASecondScreen_ReturnsToTheFirst()
        {
            var service = CreateLoadedService(BuildUIPrefab<TestScreenA>(), BuildUIPrefab<TestScreenB>());
            var first = service.ShowUI<TestScreenA>();
            service.ShowUI<TestScreenB>();

            Assert.IsTrue(service.Back());
            Assert.AreSame(first, service.ActiveScreen);
        }

        // The screen is restored with the data it was opened with; null stays null.
        [Test]
        public void Back_RestoresThePreviousScreenWithItsOriginalData()
        {
            var service = CreateLoadedService(BuildUIPrefab<CountingScreen>(), BuildUIPrefab<TestScreenB>());
            var data = new TestUIData("original", 3);
            var first = service.ShowUI<CountingScreen>(data);
            service.ShowUI<TestScreenB>();

            service.Back();

            Assert.AreSame(first, service.ActiveScreen);
            Assert.AreSame(data, first.LastData);
        }

        [Test]
        public void Back_ForAScreenOpenedWithNoData_RestoresItWithNull()
        {
            var service = CreateLoadedService(BuildUIPrefab<CountingScreen>(), BuildUIPrefab<TestScreenB>());
            var first = service.ShowUI<CountingScreen>();
            service.ShowUI<TestScreenB>();

            service.Back();

            Assert.AreSame(first, service.ActiveScreen);
            Assert.IsNull(first.LastData);
        }

        // Each step truncates the trail, so [A, B, C] retreats one press at a time and terminates.
        [Test]
        public void Back_RepeatedAcrossThreeScreens_WalksBackAndThenTerminates()
        {
            var service = CreateLoadedService(
                BuildUIPrefab<TestScreenA>(),
                BuildUIPrefab<TestScreenB>(),
                BuildUIPrefab<TestScreenC>());

            var first = service.ShowUI<TestScreenA>();
            var second = service.ShowUI<TestScreenB>();
            service.ShowUI<TestScreenC>();

            Assert.IsTrue(service.Back(), "First press: C to B.");
            Assert.AreSame(second, service.ActiveScreen);

            Assert.IsTrue(service.Back(), "Second press: B to A.");
            Assert.AreSame(first, service.ActiveScreen);

            Assert.IsFalse(service.Back(), "Third press: nothing behind A.");
        }

        // ---- Nothing to go back to -------------------------------------------

        [Test]
        public void Back_WithAnEmptyHistory_ReturnsFalse()
        {
            var service = CreateLoadedService(BuildUIPrefab<TestScreenA>());

            Assert.IsFalse(service.Back());
        }

        [Test]
        public void Back_WithASingleScreen_ReturnsFalse()
        {
            var service = CreateLoadedService(BuildUIPrefab<TestScreenA>());
            service.ShowUI<TestScreenA>();

            Assert.IsFalse(service.Back());
        }

        [Test]
        public void Back_ImmediatelyAfterAWindowSwitch_ReturnsFalse()
        {
            var service = CreateLoadedService(BuildUIPrefab<TestScreenA>(), BuildUIPrefab<TestScreenB>());
            service.ShowUI<TestScreenA>();
            service.ShowUI<TestScreenB>();

            service.SwitchWindow(BuildWindowData(BuildUIPrefab<TestScreenC>()));

            Assert.IsFalse(service.Back());
        }

        [Test]
        public void Back_AfterTheLastScreenWasClosed_ReturnsFalse()
        {
            var service = CreateLoadedService(BuildUIPrefab<TestScreenA>());
            service.ShowUI<TestScreenA>();

            service.HideUI<TestScreenA>();

            Assert.IsFalse(service.Back());
        }

        // ---- ShowPreviousScreen ----------------------------------------------

        // The case that distinguishes it from Back(): restoring works even with nothing visible.
        [Test]
        public void ShowPreviousScreen_AfterTheActiveScreenWasHidden_RestoresThePreviousOne()
        {
            var service = CreateLoadedService(BuildUIPrefab<TestScreenA>(), BuildUIPrefab<TestScreenB>());
            var first = service.ShowUI<TestScreenA>();
            service.ShowUI<TestScreenB>();
            service.HideUI<TestScreenB>();
            Assert.IsNull(service.ActiveScreen, "Arrange failed: hiding the active screen should leave none.");

            Assert.IsTrue(service.ShowPreviousScreen());
            Assert.AreSame(first, service.ActiveScreen);
        }

        [Test]
        public void ShowPreviousScreen_RestoresTheScreenWithItsOriginalData()
        {
            var service = CreateLoadedService(BuildUIPrefab<CountingScreen>(), BuildUIPrefab<TestScreenB>());
            var data = new TestUIData("kept");
            var first = service.ShowUI<CountingScreen>(data);
            service.ShowUI<TestScreenB>();
            service.HideUI<TestScreenB>();

            service.ShowPreviousScreen();

            Assert.AreSame(first, service.ActiveScreen);
            Assert.AreSame(data, first.LastData);
        }
    }
}
