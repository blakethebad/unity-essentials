using NUnit.Framework;

namespace UnityEssentials.UI.Tests
{
    /// <summary>
    /// UIBase's four-state show/hide machine and the transition token that guards it: starting
    /// state, the synchronous v1 transitions, deferred transitions, misbehaving completion
    /// callbacks, and IsVisible across all four states.
    /// </summary>
    [TestFixture]
    public sealed class UIElementStateTests : UITestFixture
    {
        private DeferredTransitionScreen LoadDeferredScreen()
        {
            var service = CreateLoadedService(BuildUIPrefab<DeferredTransitionScreen>());
            return service.GetUI<DeferredTransitionScreen>();
        }

        // ---- Starting state ------------------------------------------------

        [Test]
        public void FreshlyBuiltElement_StartsHidden()
        {
            var widget = BuildWidget<CountingWidget>();

            Assert.AreEqual(UIElementState.Hidden, widget.State);
        }

        [Test]
        public void EveryElementLoadedIntoAWindow_StartsHidden()
        {
            var service = CreateLoadedService(
                BuildUIPrefab<TestScreenA>(),
                BuildUIPrefab<TestPopupA>(),
                BuildUIPrefab<TestPanelA>());

            Assert.AreEqual(UIElementState.Hidden, service.GetUI<TestScreenA>().State);
            Assert.AreEqual(UIElementState.Hidden, service.GetUI<TestPopupA>().State);
            Assert.AreEqual(UIElementState.Hidden, service.GetUI<TestPanelA>().State);
        }

        [Test]
        public void ElementLoadedIntoAWindow_StartsDeactivated()
        {
            var service = CreateLoadedService(BuildUIPrefab<TestScreenA>());

            Assert.IsFalse(service.GetUI<TestScreenA>().gameObject.activeSelf);
        }

        // ---- Synchronous v1 transitions ------------------------------------

        [Test]
        public void Show_WithTheDefaultTransition_LandsInShownImmediately()
        {
            var widget = BuildWidget<CountingWidget>();

            widget.Show();

            Assert.AreEqual(UIElementState.Shown, widget.State);
        }

        [Test]
        public void Show_ActivatesTheGameObject()
        {
            var service = CreateLoadedService(BuildUIPrefab<TestScreenA>());
            var screen = service.GetUI<TestScreenA>();

            screen.Show();

            Assert.IsTrue(screen.gameObject.activeSelf);
        }

        [Test]
        public void Hide_WithTheDefaultTransition_LandsInHiddenImmediately()
        {
            var widget = BuildWidget<CountingWidget>();
            widget.Show();

            widget.Hide();

            Assert.AreEqual(UIElementState.Hidden, widget.State);
        }

        [Test]
        public void Hide_WithTheDefaultTransition_DeactivatesTheGameObject()
        {
            var widget = BuildWidget<CountingWidget>();
            widget.Show();

            widget.Hide();

            Assert.IsFalse(widget.gameObject.activeSelf);
        }

        // Double-close is normal with overlapping input; re-running OnHide is how events get
        // double-unsubscribed.
        [Test]
        public void Hide_OnAnAlreadyHiddenElement_DoesNotRunOnHideAgain()
        {
            var widget = BuildWidget<CountingWidget>();

            widget.Hide();

            Assert.AreEqual(0, widget.HideCount);
        }

        // ---- Deferred transitions ------------------------------------------

        [Test]
        public void DeferredShow_StaysShowingUntilCompleteIsInvoked()
        {
            var screen = LoadDeferredScreen();

            screen.Show();

            Assert.AreEqual(UIElementState.Showing, screen.State);
        }

        [Test]
        public void DeferredShow_KeepsTheGameObjectActiveWhileShowing()
        {
            var screen = LoadDeferredScreen();

            screen.Show();

            Assert.IsTrue(screen.gameObject.activeSelf);
        }

        [Test]
        public void DeferredShow_AfterCompleteIsInvoked_LandsInShown()
        {
            var screen = LoadDeferredScreen();
            screen.Show();

            screen.InvokePendingShow();

            Assert.AreEqual(UIElementState.Shown, screen.State);
        }

        [Test]
        public void DeferredHide_StaysHidingUntilCompleteIsInvoked()
        {
            var screen = LoadDeferredScreen();
            screen.Show();
            screen.InvokePendingShow();

            screen.Hide();

            Assert.AreEqual(UIElementState.Hiding, screen.State);
        }

        // Deactivating on intent instead of on completion would make every exit tween play on an
        // object that is already gone.
        [Test]
        public void DeferredHide_KeepsTheGameObjectActiveWhileHiding()
        {
            var screen = LoadDeferredScreen();
            screen.Show();
            screen.InvokePendingShow();

            screen.Hide();

            Assert.IsTrue(screen.gameObject.activeSelf);
        }

        [Test]
        public void DeferredHide_AfterCompleteIsInvoked_LandsInHidden()
        {
            var screen = LoadDeferredScreen();
            screen.Show();
            screen.InvokePendingShow();
            screen.Hide();

            screen.InvokePendingHide();

            Assert.AreEqual(UIElementState.Hidden, screen.State);
        }

        [Test]
        public void DeferredHide_AfterCompleteIsInvoked_DeactivatesTheGameObject()
        {
            var screen = LoadDeferredScreen();
            screen.Show();
            screen.InvokePendingShow();
            screen.Hide();

            screen.InvokePendingHide();

            Assert.IsFalse(screen.gameObject.activeSelf);
        }

        // ---- The transition token ------------------------------------------

        [Test]
        public void DoubleCompleteOnShow_LeavesTheElementShown()
        {
            var screen = LoadDeferredScreen();
            screen.Show();
            screen.InvokePendingShow();

            screen.InvokePendingShow();

            Assert.AreEqual(UIElementState.Shown, screen.State);
        }

        [Test]
        public void DoubleCompleteOnHide_LeavesTheElementHidden()
        {
            var screen = LoadDeferredScreen();
            screen.Show();
            screen.InvokePendingShow();
            screen.Hide();
            screen.InvokePendingHide();

            screen.InvokePendingHide();

            Assert.AreEqual(UIElementState.Hidden, screen.State);
        }

        [Test]
        public void DoubleCompleteOnHide_LeavesTheGameObjectDeactivated()
        {
            var screen = LoadDeferredScreen();
            screen.Show();
            screen.InvokePendingShow();
            screen.Hide();
            screen.InvokePendingHide();

            screen.InvokePendingHide();

            Assert.IsFalse(screen.gameObject.activeSelf);
        }

        // The stale-hide case: a Show issued during a pending hide bumps the token, so completing
        // the old hide must not deactivate the object the user is now looking at.
        [Test]
        public void ShowDuringAPendingHide_ThenCompletingTheStaleHide_LeavesTheObjectActive()
        {
            var screen = LoadDeferredScreen();
            screen.DeferShow = false;   // the incoming show completes normally; only the hide hangs
            screen.Show();
            screen.Hide();

            screen.Show();
            screen.InvokePendingHide();   // the callback captured by the superseded hide

            Assert.IsTrue(
                screen.gameObject.activeSelf,
                "the superseded hide must not deactivate the element underneath a fresh show");
        }

        [Test]
        public void ShowDuringAPendingHide_ThenCompletingTheStaleHide_LeavesTheElementVisible()
        {
            var screen = LoadDeferredScreen();
            screen.DeferShow = false;
            screen.Show();
            screen.Hide();

            screen.Show();
            screen.InvokePendingHide();

            Assert.IsTrue(screen.IsVisible, "expected Showing or Shown, but the state was " + screen.State);
        }

        // ---- IsVisible across all four states ------------------------------

        [Test]
        public void IsVisible_WhenHidden_IsFalse()
        {
            var widget = BuildWidget<DeferredTransitionWidget>();

            Assert.IsFalse(widget.IsVisible);
        }

        [Test]
        public void IsVisible_WhileShowing_IsTrue()
        {
            var widget = BuildWidget<DeferredTransitionWidget>();

            widget.Show();

            Assert.AreEqual(UIElementState.Showing, widget.State);
            Assert.IsTrue(widget.IsVisible);
        }

        [Test]
        public void IsVisible_WhenShown_IsTrue()
        {
            var widget = BuildWidget<DeferredTransitionWidget>();
            widget.Show();

            widget.InvokePendingShow();

            Assert.AreEqual(UIElementState.Shown, widget.State);
            Assert.IsTrue(widget.IsVisible);
        }

        // Hiding is not visible even though the GameObject is still active: the element already
        // left the active lists, and calling it visible would let a query resurrect it.
        [Test]
        public void IsVisible_WhileHiding_IsFalse()
        {
            var widget = BuildWidget<DeferredTransitionWidget>();
            widget.Show();
            widget.InvokePendingShow();

            widget.Hide();

            Assert.AreEqual(UIElementState.Hiding, widget.State);
            Assert.IsFalse(widget.IsVisible);
        }
    }
}
