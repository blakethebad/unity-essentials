using NUnit.Framework;

namespace UnityEssentials.UI.Tests
{
    /// <summary>
    /// The two non-exclusive element kinds: popups, which stack, sort themselves to the front and
    /// take part in back navigation, and panels, which stack, do not sort and take part in nothing.
    /// The reverse-order HideAll* contract is asserted against the OnHide call order.
    /// </summary>
    [TestFixture]
    public class PopupPanelTests : UITestFixture
    {
        // ---- Popup stacking --------------------------------------------------

        [Test]
        public void ShowUI_TwoPopups_LeavesBothVisible()
        {
            var service = CreateLoadedService(BuildUIPrefab<TestPopupA>(), BuildUIPrefab<TestPopupB>());

            var first = service.ShowUI<TestPopupA>();
            var second = service.ShowUI<TestPopupB>();

            Assert.IsTrue(first.IsVisible && second.IsVisible, UICallLog.Describe());
        }

        [Test]
        public void ActivePopups_HoldsOpenPopupsInShowOrder()
        {
            var service = CreateLoadedService(BuildUIPrefab<TestPopupA>(), BuildUIPrefab<TestPopupB>());

            var first = service.ShowUI<TestPopupA>();
            var second = service.ShowUI<TestPopupB>();

            Assert.AreEqual(2, service.ActivePopups.Count);
            Assert.AreSame(first, service.ActivePopups[0]);
            Assert.AreSame(second, service.ActivePopups[1]);
        }

        // SetAsLastSibling on every show is the whole stacking mechanism: the newest popup is the
        // last child of the window and draws on top.
        [Test]
        public void ShowUI_ReShowingAPopup_MovesItInFrontOfItsSiblings()
        {
            var service = CreateLoadedService(BuildUIPrefab<TestPopupA>(), BuildUIPrefab<TestPopupB>());
            var first = service.ShowUI<TestPopupA>();
            var second = service.ShowUI<TestPopupB>();

            service.ShowUI<TestPopupA>();

            var lastIndex = service.ActiveWindow.transform.childCount - 1;
            Assert.AreEqual(lastIndex, first.transform.GetSiblingIndex(), "A re-show must sort the popup to the front.");
            Assert.Less(second.transform.GetSiblingIndex(), first.transform.GetSiblingIndex());
        }

        [Test]
        public void HideUI_OnAPopupBeneathAnother_LeavesTheRestOfTheStackIntact()
        {
            var service = CreateLoadedService(BuildUIPrefab<TestPopupA>(), BuildUIPrefab<TestPopupB>());
            service.ShowUI<TestPopupA>();
            var second = service.ShowUI<TestPopupB>();

            service.HideUI<TestPopupA>();

            Assert.AreEqual(1, service.ActivePopups.Count);
            Assert.AreSame(second, service.ActivePopups[0]);
            Assert.IsTrue(second.IsVisible, "Closing the popup beneath it must not disturb this one.");
        }

        [Test]
        public void HideAllPopups_ClosesThemInReverseShowOrder()
        {
            var service = CreateLoadedService(BuildUIPrefab<TestPopupA>(), BuildUIPrefab<TestPopupB>());
            var first = service.ShowUI<TestPopupA>();
            var second = service.ShowUI<TestPopupB>();

            service.HideAllPopups();

            Assert.AreEqual(2, HideLog.Count, UICallLog.Describe());
            Assert.AreSame(second, HideLog[0], UICallLog.Describe());
            Assert.AreSame(first, HideLog[1], UICallLog.Describe());
        }

        // ---- Panels ----------------------------------------------------------

        [Test]
        public void ActivePanels_HoldsOpenPanelsInShowOrder()
        {
            var service = CreateLoadedService(BuildUIPrefab<TestPanelA>(), BuildUIPrefab<TestPanelB>());

            var first = service.ShowUI<TestPanelA>();
            var second = service.ShowUI<TestPanelB>();

            Assert.AreEqual(2, service.ActivePanels.Count);
            Assert.AreSame(first, service.ActivePanels[0]);
            Assert.AreSame(second, service.ActivePanels[1]);
        }

        [Test]
        public void HideAllPanels_ClosesThemInReverseShowOrder()
        {
            var service = CreateLoadedService(BuildUIPrefab<TestPanelA>(), BuildUIPrefab<TestPanelB>());
            var first = service.ShowUI<TestPanelA>();
            var second = service.ShowUI<TestPanelB>();

            service.HideAllPanels();

            Assert.AreEqual(2, HideLog.Count, UICallLog.Describe());
            Assert.AreSame(second, HideLog[0], UICallLog.Describe());
            Assert.AreSame(first, HideLog[1], UICallLog.Describe());
        }

        // ---- Panels are invisible to navigation ------------------------------

        [Test]
        public void Back_WithPanelsOpenOverTheOnlyScreen_ReturnsFalse()
        {
            var service = CreateLoadedService(
                BuildUIPrefab<TestScreenA>(),
                BuildUIPrefab<TestPanelA>(),
                BuildUIPrefab<TestPanelB>());

            service.ShowUI<TestScreenA>();
            service.ShowUI<TestPanelA>();
            service.ShowUI<TestPanelB>();

            Assert.IsFalse(service.Back(), UICallLog.Describe());
        }

        [Test]
        public void Back_WithPanelsOpenOverTheOnlyScreen_LeavesThePanelsOpen()
        {
            var service = CreateLoadedService(
                BuildUIPrefab<TestScreenA>(),
                BuildUIPrefab<TestPanelA>(),
                BuildUIPrefab<TestPanelB>());

            service.ShowUI<TestScreenA>();
            service.ShowUI<TestPanelA>();
            service.ShowUI<TestPanelB>();
            service.Back();

            Assert.AreEqual(2, service.ActivePanels.Count, UICallLog.Describe());
        }
    }
}
