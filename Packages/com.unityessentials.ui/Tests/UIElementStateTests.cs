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
        // Showing requires a bound element, so every test that drives a transition resolves its
        // element out of a loaded window rather than building a loose one.
        private DeferredTransitionElement LoadDeferredElement()
        {
            var service = CreateLoadedService(BuildUIPrefab<DeferredTransitionElement>());
            return service.GetUI<DeferredTransitionElement>();
        }

        private CountingElement LoadCountingElement()
        {
            var service = CreateLoadedService(BuildUIPrefab<CountingElement>());
            return service.GetUI<CountingElement>();
        }

        // ---- Starting state ------------------------------------------------

        [Test]
        public void FreshlyBuiltElement_StartsHidden()
        {
            var element = BuildUIElement<CountingElement>();

            Assert.AreEqual(UIElementState.Hidden, element.State);
        }

        [Test]
        public void EveryElementLoadedIntoAWindow_StartsHidden()
        {
            var service = CreateLoadedService(
                BuildUIPrefab<TestElementA>(),
                BuildUIPrefab<TestElementB>(),
                BuildUIPrefab<TestElementC>());

            Assert.AreEqual(UIElementState.Hidden, service.GetUI<TestElementA>().State);
            Assert.AreEqual(UIElementState.Hidden, service.GetUI<TestElementB>().State);
            Assert.AreEqual(UIElementState.Hidden, service.GetUI<TestElementC>().State);
        }

        [Test]
        public void ElementLoadedIntoAWindow_StartsDeactivated()
        {
            var service = CreateLoadedService(BuildUIPrefab<TestElementA>());

            Assert.IsFalse(service.GetUI<TestElementA>().gameObject.activeSelf);
        }

        // ---- Synchronous v1 transitions ------------------------------------

        [Test]
        public void Show_WithTheDefaultTransition_LandsInShownImmediately()
        {
            var element = LoadCountingElement();

            element.Show();

            Assert.AreEqual(UIElementState.Shown, element.State);
        }

        [Test]
        public void Show_ActivatesTheGameObject()
        {
            var service = CreateLoadedService(BuildUIPrefab<TestElementA>());
            var element = service.GetUI<TestElementA>();

            element.Show();

            Assert.IsTrue(element.gameObject.activeSelf);
        }

        // With one element kind there is no refresh fast-path: re-showing something already Shown
        // runs the whole ExecuteShow again — token bump, SetActive, OnShow, transition — so the new
        // payload really does reach the element.
        [Test]
        public void Show_OnAnAlreadyShownElement_RunsOnShowAgain()
        {
            var element = LoadCountingElement();
            var uiData = new TestUIData("refresh", 7);
            element.Show();

            element.Show(uiData);

            Assert.AreEqual(2, element.ShowCount);
            Assert.AreSame(uiData, element.LastData);
            Assert.AreEqual(UIElementState.Shown, element.State);
        }

        [Test]
        public void Hide_WithTheDefaultTransition_LandsInHiddenImmediately()
        {
            var element = LoadCountingElement();
            element.Show();

            element.Hide();

            Assert.AreEqual(UIElementState.Hidden, element.State);
        }

        [Test]
        public void Hide_WithTheDefaultTransition_DeactivatesTheGameObject()
        {
            var element = LoadCountingElement();
            element.Show();

            element.Hide();

            Assert.IsFalse(element.gameObject.activeSelf);
        }

        // Double-close is normal with overlapping input; re-running OnHide is how events get
        // double-unsubscribed.
        [Test]
        public void Hide_OnAnAlreadyHiddenElement_DoesNotRunOnHideAgain()
        {
            var element = LoadCountingElement();

            element.Hide();

            Assert.AreEqual(0, element.HideCount);
        }

        // ---- Deferred transitions ------------------------------------------

        [Test]
        public void DeferredShow_StaysShowingUntilCompleteIsInvoked()
        {
            var element = LoadDeferredElement();

            element.Show();

            Assert.AreEqual(UIElementState.Showing, element.State);
        }

        [Test]
        public void DeferredShow_KeepsTheGameObjectActiveWhileShowing()
        {
            var element = LoadDeferredElement();

            element.Show();

            Assert.IsTrue(element.gameObject.activeSelf);
        }

        [Test]
        public void DeferredShow_AfterCompleteIsInvoked_LandsInShown()
        {
            var element = LoadDeferredElement();
            element.Show();

            element.InvokePendingShow();

            Assert.AreEqual(UIElementState.Shown, element.State);
        }

        [Test]
        public void DeferredHide_StaysHidingUntilCompleteIsInvoked()
        {
            var element = LoadDeferredElement();
            element.Show();
            element.InvokePendingShow();

            element.Hide();

            Assert.AreEqual(UIElementState.Hiding, element.State);
        }

        // Deactivating on intent instead of on completion would make every exit tween play on an
        // object that is already gone.
        [Test]
        public void DeferredHide_KeepsTheGameObjectActiveWhileHiding()
        {
            var element = LoadDeferredElement();
            element.Show();
            element.InvokePendingShow();

            element.Hide();

            Assert.IsTrue(element.gameObject.activeSelf);
        }

        [Test]
        public void DeferredHide_AfterCompleteIsInvoked_LandsInHidden()
        {
            var element = LoadDeferredElement();
            element.Show();
            element.InvokePendingShow();
            element.Hide();

            element.InvokePendingHide();

            Assert.AreEqual(UIElementState.Hidden, element.State);
        }

        [Test]
        public void DeferredHide_AfterCompleteIsInvoked_DeactivatesTheGameObject()
        {
            var element = LoadDeferredElement();
            element.Show();
            element.InvokePendingShow();
            element.Hide();

            element.InvokePendingHide();

            Assert.IsFalse(element.gameObject.activeSelf);
        }

        // ---- The transition token ------------------------------------------

        [Test]
        public void DoubleCompleteOnShow_LeavesTheElementShown()
        {
            var element = LoadDeferredElement();
            element.Show();
            element.InvokePendingShow();

            element.InvokePendingShow();

            Assert.AreEqual(UIElementState.Shown, element.State);
        }

        [Test]
        public void DoubleCompleteOnHide_LeavesTheElementHidden()
        {
            var element = LoadDeferredElement();
            element.Show();
            element.InvokePendingShow();
            element.Hide();
            element.InvokePendingHide();

            element.InvokePendingHide();

            Assert.AreEqual(UIElementState.Hidden, element.State);
        }

        [Test]
        public void DoubleCompleteOnHide_LeavesTheGameObjectDeactivated()
        {
            var element = LoadDeferredElement();
            element.Show();
            element.InvokePendingShow();
            element.Hide();
            element.InvokePendingHide();

            element.InvokePendingHide();

            Assert.IsFalse(element.gameObject.activeSelf);
        }

        // The stale-hide case: a Show issued during a pending hide bumps the token, so completing
        // the old hide must not deactivate the object the user is now looking at.
        [Test]
        public void ShowDuringAPendingHide_ThenCompletingTheStaleHide_LeavesTheObjectActive()
        {
            var element = LoadDeferredElement();
            element.DeferShow = false;   // the incoming show completes normally; only the hide hangs
            element.Show();
            element.Hide();

            element.Show();
            element.InvokePendingHide();   // the callback captured by the superseded hide

            Assert.IsTrue(
                element.gameObject.activeSelf,
                "the superseded hide must not deactivate the element underneath a fresh show");
        }

        [Test]
        public void ShowDuringAPendingHide_ThenCompletingTheStaleHide_LeavesTheElementVisible()
        {
            var element = LoadDeferredElement();
            element.DeferShow = false;
            element.Show();
            element.Hide();

            element.Show();
            element.InvokePendingHide();

            Assert.IsTrue(element.IsVisible, "expected Showing or Shown, but the state was " + element.State);
        }

        // ---- IsVisible across all four states ------------------------------

        [Test]
        public void IsVisible_WhenHidden_IsFalse()
        {
            var element = BuildUIElement<DeferredTransitionElement>();

            Assert.IsFalse(element.IsVisible);
        }

        [Test]
        public void IsVisible_WhileShowing_IsTrue()
        {
            var element = LoadDeferredElement();

            element.Show();

            Assert.AreEqual(UIElementState.Showing, element.State);
            Assert.IsTrue(element.IsVisible);
        }

        [Test]
        public void IsVisible_WhenShown_IsTrue()
        {
            var element = LoadDeferredElement();
            element.Show();

            element.InvokePendingShow();

            Assert.AreEqual(UIElementState.Shown, element.State);
            Assert.IsTrue(element.IsVisible);
        }

        // Hiding is not visible even though the GameObject is still active: the element has already
        // declared its intent to leave, and calling it visible would let a query resurrect it.
        [Test]
        public void IsVisible_WhileHiding_IsFalse()
        {
            var element = LoadDeferredElement();
            element.Show();
            element.InvokePendingShow();

            element.Hide();

            Assert.AreEqual(UIElementState.Hiding, element.State);
            Assert.IsFalse(element.IsVisible);
        }
    }
}
