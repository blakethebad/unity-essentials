using System;
using NUnit.Framework;

namespace UnityEssentials.UI.Tests
{
    /// <summary>
    /// Registration lives in the element bases, not in the service: Show/Hide called directly on an
    /// element — never through ShowUI/HideUI — must leave ActiveScreen, the active lists and the
    /// history exactly as correct as if the service had been asked. Plus the binding contract that
    /// makes that trustworthy.
    /// </summary>
    [TestFixture]
    public sealed class RegistrationTests : UITestFixture
    {
        // ---- Screens driven directly ---------------------------------------

        [Test]
        public void ScreenShownDirectly_BecomesTheActiveScreen()
        {
            var service = CreateLoadedService(BuildUIPrefab<CountingScreen>());
            var screen = service.GetUI<CountingScreen>();

            screen.Show();

            Assert.AreSame(screen, service.ActiveScreen);
        }

        [Test]
        public void ScreenHiddenDirectly_ClearsTheActiveScreen()
        {
            var service = CreateLoadedService(BuildUIPrefab<CountingScreen>());
            var screen = service.GetUI<CountingScreen>();
            screen.Show();

            screen.Hide();

            Assert.IsNull(service.ActiveScreen);
        }

        [Test]
        public void ScreenShownDirectly_HidesThePreviousScreenExactlyOnce()
        {
            var service = CreateLoadedService(BuildUIPrefab<CountingScreen>(), BuildUIPrefab<TestScreenB>());
            var first = service.GetUI<CountingScreen>();
            var second = service.GetUI<TestScreenB>();
            first.Show();

            second.Show();

            Assert.AreEqual(1, first.HideCount);
            Assert.AreEqual(UIElementState.Hidden, first.State);
        }

        [Test]
        public void ScreenShownDirectly_RecordsTheSupersededScreenAsPrevious()
        {
            var service = CreateLoadedService(BuildUIPrefab<TestScreenA>(), BuildUIPrefab<TestScreenB>());
            var first = service.GetUI<TestScreenA>();
            var second = service.GetUI<TestScreenB>();
            first.Show();

            second.Show();

            Assert.AreSame(first, service.PreviousScreen);
        }

        [Test]
        public void ScreenShownDirectly_IsReachableByBack()
        {
            var service = CreateLoadedService(BuildUIPrefab<TestScreenA>(), BuildUIPrefab<TestScreenB>());
            var first = service.GetUI<TestScreenA>();
            var second = service.GetUI<TestScreenB>();
            first.Show();
            second.Show();

            var handled = service.Back();

            Assert.IsTrue(handled);
            Assert.AreSame(first, service.ActiveScreen);
        }

        // A direct show records its data in the history too, so Back restores it.
        [Test]
        public void ScreenShownDirectlyWithData_IsRestoredWithThatDataByBack()
        {
            var service = CreateLoadedService(BuildUIPrefab<CountingScreen>(), BuildUIPrefab<TestScreenB>());
            var first = service.GetUI<CountingScreen>();
            var data = new TestUIData("direct");
            first.Show(data);
            service.GetUI<TestScreenB>().Show();

            service.Back();

            Assert.AreSame(first, service.ActiveScreen);
            Assert.AreSame(data, first.LastData);
        }

        [Test]
        public void ScreenHiddenDirectly_LeavesTheScreenBeneathAsPrevious()
        {
            var service = CreateLoadedService(BuildUIPrefab<TestScreenA>(), BuildUIPrefab<TestScreenB>());
            var first = service.GetUI<TestScreenA>();
            var second = service.GetUI<TestScreenB>();
            first.Show();
            second.Show();

            second.Hide();

            Assert.AreSame(first, service.PreviousScreen);
        }

        [Test]
        public void BackAfterClosingTheActiveScreenDirectly_ReturnsFalse()
        {
            var service = CreateLoadedService(BuildUIPrefab<TestScreenA>(), BuildUIPrefab<TestScreenB>());
            var first = service.GetUI<TestScreenA>();
            var second = service.GetUI<TestScreenB>();
            first.Show();
            second.Show();
            second.Hide();

            Assert.IsFalse(service.Back());
        }

        [Test]
        public void ShowPreviousScreenAfterClosingTheActiveScreenDirectly_RestoresIt()
        {
            var service = CreateLoadedService(BuildUIPrefab<TestScreenA>(), BuildUIPrefab<TestScreenB>());
            var first = service.GetUI<TestScreenA>();
            var second = service.GetUI<TestScreenB>();
            first.Show();
            second.Show();
            second.Hide();

            var restored = service.ShowPreviousScreen();

            Assert.IsTrue(restored);
            Assert.AreSame(first, service.ActiveScreen);
        }

        // ---- Popups driven directly ----------------------------------------

        [Test]
        public void PopupShownDirectly_EntersActivePopups()
        {
            var service = CreateLoadedService(BuildUIPrefab<CountingPopup>());
            var popup = service.GetUI<CountingPopup>();

            popup.Show();

            CollectionAssert.AreEqual(new[] { popup }, service.ActivePopups);
        }

        [Test]
        public void PopupHiddenDirectly_LeavesActivePopups()
        {
            var service = CreateLoadedService(BuildUIPrefab<CountingPopup>());
            var popup = service.GetUI<CountingPopup>();
            popup.Show();

            popup.Hide();

            Assert.AreEqual(0, service.ActivePopups.Count);
        }

        [Test]
        public void PopupShownDirectly_IsWhatBackClosesFirst()
        {
            var service = CreateLoadedService(BuildUIPrefab<TestScreenA>(), BuildUIPrefab<CountingPopup>());
            var screen = service.GetUI<TestScreenA>();
            var popup = service.GetUI<CountingPopup>();
            screen.Show();
            popup.Show();

            var handled = service.Back();

            Assert.IsTrue(handled);
            Assert.AreEqual(UIElementState.Hidden, popup.State);
            Assert.AreSame(screen, service.ActiveScreen);
        }

        // ---- Panels driven directly ----------------------------------------

        [Test]
        public void PanelShownDirectly_EntersActivePanels()
        {
            var service = CreateLoadedService(BuildUIPrefab<CountingPanel>());
            var panel = service.GetUI<CountingPanel>();

            panel.Show();

            CollectionAssert.AreEqual(new[] { panel }, service.ActivePanels);
        }

        [Test]
        public void PanelHiddenDirectly_LeavesActivePanels()
        {
            var service = CreateLoadedService(BuildUIPrefab<CountingPanel>());
            var panel = service.GetUI<CountingPanel>();
            panel.Show();

            panel.Hide();

            Assert.AreEqual(0, service.ActivePanels.Count);
        }

        // A panel never enters history, however it was opened — back must not strand the player by
        // dismissing a nav bar.
        [Test]
        public void PanelShownDirectly_IsNeverTargetedByBack()
        {
            var service = CreateLoadedService(BuildUIPrefab<TestScreenA>(), BuildUIPrefab<CountingPanel>());
            var screen = service.GetUI<TestScreenA>();
            var panel = service.GetUI<CountingPanel>();
            screen.Show();
            panel.Show();

            var handled = service.Back();

            Assert.IsFalse(handled);
            Assert.IsTrue(panel.IsVisible);
        }

        // ---- A direct screen show still closes the overlays -----------------

        [Test]
        public void ScreenShownDirectly_ClosesPopupsOpenedOverThePreviousScreen()
        {
            var service = CreateLoadedService(
                BuildUIPrefab<TestScreenA>(),
                BuildUIPrefab<TestScreenB>(),
                BuildUIPrefab<CountingPopup>());
            service.GetUI<TestScreenA>().Show();
            service.GetUI<CountingPopup>().Show();

            service.GetUI<TestScreenB>().Show();

            Assert.AreEqual(0, service.ActivePopups.Count);
        }

        [Test]
        public void ScreenShownDirectly_ClosesPanelsOpenedOverThePreviousScreen()
        {
            var service = CreateLoadedService(
                BuildUIPrefab<TestScreenA>(),
                BuildUIPrefab<TestScreenB>(),
                BuildUIPrefab<CountingPanel>());
            service.GetUI<TestScreenA>().Show();
            service.GetUI<CountingPanel>().Show();

            service.GetUI<TestScreenB>().Show();

            Assert.AreEqual(0, service.ActivePanels.Count);
        }

        // ---- Unbound elements ----------------------------------------------

        // An unbound screen would show perfectly and register nowhere, corrupting the lists
        // silently — hence the throw.
        [Test]
        public void UnboundScreen_Show_ThrowsUIBindingException()
        {
            var screen = BuildUIElement<UnregisteredScreen>();

            Assert.Throws<UIBindingException>(() => screen.Show());
        }

        [Test]
        public void UnboundPopup_Show_ThrowsUIBindingException()
        {
            var popup = BuildUIElement<UnregisteredPopup>();

            Assert.Throws<UIBindingException>(() => popup.Show());
        }

        [Test]
        public void UnboundPanel_Show_ThrowsUIBindingException()
        {
            var panel = BuildUIElement<UnregisteredPanel>();

            Assert.Throws<UIBindingException>(() => panel.Show());
        }

        // The idempotent no-op returns before the binding check, so a defensive double-close on a
        // teardown path never throws.
        [Test]
        public void UnboundScreen_HideWhileAlreadyHidden_DoesNotThrow()
        {
            var screen = BuildUIElement<UnregisteredScreen>();

            Assert.DoesNotThrow(() => screen.Hide());
        }

        // Widgets register with nothing, so an unbound widget is fully functional.
        [Test]
        public void UnboundWidget_Show_Works()
        {
            var widget = BuildWidget<CountingWidget>();

            widget.Show();

            Assert.AreEqual(UIElementState.Shown, widget.State);
            Assert.AreEqual(1, widget.ShowCount);
        }

        [Test]
        public void UnboundWidget_Hide_Works()
        {
            var widget = BuildWidget<CountingWidget>();
            widget.Show();

            widget.Hide();

            Assert.AreEqual(UIElementState.Hidden, widget.State);
            Assert.AreEqual(1, widget.HideCount);
        }

        [Test]
        public void UnboundWidget_Show_EntersNoServiceList()
        {
            var service = CreateLoadedService(BuildUIPrefab<TestScreenA>());
            service.GetUI<TestScreenA>().Show();
            var widget = BuildWidget<CountingWidget>();

            widget.Show();

            Assert.AreEqual(0, service.ActivePopups.Count);
            Assert.AreEqual(0, service.ActivePanels.Count);
            Assert.AreSame(service.GetUI<TestScreenA>(), service.ActiveScreen);
        }

        // ---- Binding --------------------------------------------------------

        [Test]
        public void Bind_ToASecondWindow_ThrowsUIBindingException()
        {
            var firstService = CreateLoadedService(BuildUIPrefab<TestScreenA>());
            var screen = firstService.GetUI<TestScreenA>();
            var secondService = CreateLoadedService(BuildUIPrefab<TestScreenB>());
            var secondWindow = secondService.ActiveWindow;

            Assert.Throws<UIBindingException>(() => screen.Bind(secondWindow));
        }

        [Test]
        public void Bind_ToTheSameWindow_IsANoOp()
        {
            var service = CreateLoadedService(BuildUIPrefab<TestScreenA>());
            var screen = service.GetUI<TestScreenA>();
            var window = service.ActiveWindow;

            Assert.DoesNotThrow(() => screen.Bind(window));
            Assert.AreSame(window, screen.Window);
        }

        [Test]
        public void Bind_Null_ThrowsArgumentNullException()
        {
            var widget = BuildWidget<CountingWidget>();

            Assert.Throws<ArgumentNullException>(() => widget.Bind(null));
        }
    }
}
