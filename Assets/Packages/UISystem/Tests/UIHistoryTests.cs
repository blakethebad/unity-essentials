using NUnit.Framework;

namespace UnityEssentials.UI.Tests
{
    /// <summary>
    /// The navigation model in isolation: every operation on <see cref="UIHistory{TEntry}"/> driven
    /// by plain objects, with no GameObject, window or service. Deliberately does not derive from
    /// <see cref="UITestFixture"/> so it can run through a plain reflection runner outside Unity.
    /// </summary>
    [TestFixture]
    public sealed class UIHistoryTests
    {
        // One back step exactly as WindowService.ShowPreviousScreen does it: find the previous
        // screen and its data, truncate the current one, re-push the previous with its data.
        private static bool StepBack(UIHistory<object> history, object current, out object restored)
        {
            var previous = history.PreviousScreen(current, out var data);
            if (previous == null)
            {
                restored = null;
                return false;
            }

            history.TruncateTopScreen(current);
            history.PushScreen(previous, data);
            restored = previous;
            return true;
        }

        // ---- Empty history -------------------------------------------------

        [Test]
        public void NewHistory_HasNoVisibleEntries()
        {
            var history = new UIHistory<object>();

            Assert.AreEqual(0, history.VisibleEntries.Count);
        }

        [Test]
        public void NewHistory_HasNoScreenTrail()
        {
            var history = new UIHistory<object>();

            Assert.AreEqual(0, history.ScreenTrail.Count);
        }

        [Test]
        public void Top_OnEmptyHistory_IsNull()
        {
            var history = new UIHistory<object>();

            Assert.IsNull(history.Top);
        }

        [Test]
        public void TopScreen_OnEmptyHistory_IsNull()
        {
            var history = new UIHistory<object>();

            Assert.IsNull(history.TopScreen);
        }

        [Test]
        public void PreviousScreen_OnEmptyHistory_IsNull()
        {
            var history = new UIHistory<object>();

            Assert.IsNull(history.PreviousScreen(null));
        }

        [Test]
        public void TruncateTopScreen_OnEmptyHistory_ReturnsFalse()
        {
            var history = new UIHistory<object>();
            var screenA = new object();

            Assert.IsFalse(history.TruncateTopScreen(screenA));
        }

        // ---- PushScreen ----------------------------------------------------

        [Test]
        public void PushScreen_AppendsToVisibleEntries()
        {
            var history = new UIHistory<object>();
            var screenA = new object();

            history.PushScreen(screenA);

            CollectionAssert.AreEqual(new[] { screenA }, history.VisibleEntries);
        }

        [Test]
        public void PushScreen_AppendsToScreenTrail()
        {
            var history = new UIHistory<object>();
            var screenA = new object();
            var screenB = new object();

            history.PushScreen(screenA);
            history.PushScreen(screenB);

            CollectionAssert.AreEqual(new[] { screenA, screenB }, history.ScreenTrail);
        }

        [Test]
        public void PushScreen_MakesTheNewScreenTheVisibleTop()
        {
            var history = new UIHistory<object>();
            var screenA = new object();
            var screenB = new object();

            history.PushScreen(screenA);
            history.PushScreen(screenB);

            Assert.AreSame(screenB, history.Top);
        }

        [Test]
        public void PushScreen_MakesTheNewScreenTheTrailTop()
        {
            var history = new UIHistory<object>();
            var screenA = new object();
            var screenB = new object();

            history.PushScreen(screenA);
            history.PushScreen(screenB);

            Assert.AreSame(screenB, history.TopScreen);
        }

        // Re-showing a screen already on the trail moves it to the top instead of duplicating, so
        // menu ping-pong stays bounded.
        [Test]
        public void PushScreen_AlreadyOnTheTrail_MovesItToTheTopWithoutDuplicating()
        {
            var history = new UIHistory<object>();
            var screenA = new object();
            var screenB = new object();

            history.PushScreen(screenA);
            history.PushScreen(screenB);
            history.PushScreen(screenA);

            CollectionAssert.AreEqual(new[] { screenB, screenA }, history.ScreenTrail);
        }

        [Test]
        public void PushScreen_AlreadyVisible_MovesItToTheTopOfVisibleEntries()
        {
            var history = new UIHistory<object>();
            var screenA = new object();
            var screenB = new object();

            history.PushScreen(screenA);
            history.PushScreen(screenB);
            history.PushScreen(screenA);

            CollectionAssert.AreEqual(new[] { screenB, screenA }, history.VisibleEntries);
        }

        [Test]
        public void PushScreen_Null_IsIgnored()
        {
            var history = new UIHistory<object>();

            history.PushScreen(null);

            Assert.AreEqual(0, history.ScreenTrail.Count);
        }

        // ---- PushOverlay ---------------------------------------------------

        [Test]
        public void PushOverlay_AppendsToVisibleEntries()
        {
            var history = new UIHistory<object>();
            var screenA = new object();
            var popupA = new object();

            history.PushScreen(screenA);
            history.PushOverlay(popupA);

            CollectionAssert.AreEqual(new[] { screenA, popupA }, history.VisibleEntries);
        }

        // Popups are not navigation destinations: an overlay never touches the back-trail.
        [Test]
        public void PushOverlay_LeavesTheScreenTrailUntouched()
        {
            var history = new UIHistory<object>();
            var screenA = new object();
            var popupA = new object();
            var popupB = new object();

            history.PushScreen(screenA);
            history.PushOverlay(popupA);
            history.PushOverlay(popupB);

            CollectionAssert.AreEqual(new[] { screenA }, history.ScreenTrail);
        }

        [Test]
        public void PushOverlay_MakesThePopupTheVisibleTop()
        {
            var history = new UIHistory<object>();
            var screenA = new object();
            var popupA = new object();

            history.PushScreen(screenA);
            history.PushOverlay(popupA);

            Assert.AreSame(popupA, history.Top);
        }

        [Test]
        public void PushOverlay_AlreadyVisible_MovesItToTheTop()
        {
            var history = new UIHistory<object>();
            var popupA = new object();
            var popupB = new object();

            history.PushOverlay(popupA);
            history.PushOverlay(popupB);
            history.PushOverlay(popupA);

            CollectionAssert.AreEqual(new[] { popupB, popupA }, history.VisibleEntries);
        }

        [Test]
        public void PushOverlay_Null_IsIgnored()
        {
            var history = new UIHistory<object>();

            history.PushOverlay(null);

            Assert.AreEqual(0, history.VisibleEntries.Count);
        }

        // ---- Hide: overlays ------------------------------------------------

        [Test]
        public void Hide_Popup_RemovesItFromVisibleEntries()
        {
            var history = new UIHistory<object>();
            var screenA = new object();
            var popupA = new object();

            history.PushScreen(screenA);
            history.PushOverlay(popupA);
            history.Hide(popupA, false);

            CollectionAssert.AreEqual(new[] { screenA }, history.VisibleEntries);
        }

        [Test]
        public void Hide_Popup_LeavesTheScreenTrailUntouched()
        {
            var history = new UIHistory<object>();
            var screenA = new object();
            var screenB = new object();
            var popupA = new object();

            history.PushScreen(screenA);
            history.PushScreen(screenB);
            history.PushOverlay(popupA);
            history.Hide(popupA, false);

            CollectionAssert.AreEqual(new[] { screenA, screenB }, history.ScreenTrail);
        }

        [Test]
        public void Hide_PopupClosedOutOfOrder_RemovesOnlyThatEntry()
        {
            var history = new UIHistory<object>();
            var screenA = new object();
            var popupA = new object();
            var popupB = new object();

            history.PushScreen(screenA);
            history.PushOverlay(popupA);
            history.PushOverlay(popupB);
            history.Hide(popupA, false);

            CollectionAssert.AreEqual(new[] { screenA, popupB }, history.VisibleEntries);
        }

        // ---- Hide: screens -------------------------------------------------

        // Close-prunes: a screen hidden on its own is the trail top and retreats the trail.
        [Test]
        public void Hide_ScreenThatIsTheTrailTop_PrunesItFromTheTrail()
        {
            var history = new UIHistory<object>();
            var screenA = new object();
            var screenB = new object();

            history.PushScreen(screenA);
            history.PushScreen(screenB);
            history.Hide(screenB, true);

            CollectionAssert.AreEqual(new[] { screenA }, history.ScreenTrail);
        }

        // Supersede-retains: pushed-over screens stay on the trail as the previous screen.
        [Test]
        public void Hide_SupersededScreen_RetainsItOnTheTrail()
        {
            var history = new UIHistory<object>();
            var screenA = new object();
            var screenB = new object();

            history.PushScreen(screenA);
            history.PushScreen(screenB);   // pushed BEFORE A is hidden, exactly as OnScreenShowing does
            history.Hide(screenA, true);

            CollectionAssert.AreEqual(new[] { screenA, screenB }, history.ScreenTrail);
        }

        [Test]
        public void Hide_SupersededScreen_StillRemovesItFromVisibleEntries()
        {
            var history = new UIHistory<object>();
            var screenA = new object();
            var screenB = new object();

            history.PushScreen(screenA);
            history.PushScreen(screenB);
            history.Hide(screenA, true);

            CollectionAssert.AreEqual(new[] { screenB }, history.VisibleEntries);
        }

        [Test]
        public void Hide_UntrackedEntry_IsANoOp()
        {
            var history = new UIHistory<object>();
            var screenA = new object();
            var screenB = new object();

            history.PushScreen(screenA);
            history.Hide(screenB, true);

            CollectionAssert.AreEqual(new[] { screenA }, history.ScreenTrail);
        }

        [Test]
        public void Hide_Null_IsIgnored()
        {
            var history = new UIHistory<object>();
            var screenA = new object();

            history.PushScreen(screenA);
            history.Hide(null, true);

            CollectionAssert.AreEqual(new[] { screenA }, history.ScreenTrail);
        }

        // ---- PreviousScreen ------------------------------------------------

        [Test]
        public void PreviousScreen_AfterAToB_IsA()
        {
            var history = new UIHistory<object>();
            var screenA = new object();
            var screenB = new object();

            history.PushScreen(screenA);
            history.PushScreen(screenB);
            history.Hide(screenA, true);

            Assert.AreSame(screenA, history.PreviousScreen(screenB));
        }

        [Test]
        public void PreviousScreen_AfterAToBToC_IsB()
        {
            var history = new UIHistory<object>();
            var screenA = new object();
            var screenB = new object();
            var screenC = new object();

            history.PushScreen(screenA);
            history.PushScreen(screenB);
            history.Hide(screenA, true);
            history.PushScreen(screenC);
            history.Hide(screenB, true);

            Assert.AreSame(screenB, history.PreviousScreen(screenC));
        }

        [Test]
        public void PreviousScreen_AfterAToBToA_IsB()
        {
            var history = new UIHistory<object>();
            var screenA = new object();
            var screenB = new object();

            history.PushScreen(screenA);
            history.PushScreen(screenB);
            history.Hide(screenA, true);
            history.PushScreen(screenA);
            history.Hide(screenB, true);

            Assert.AreSame(screenB, history.PreviousScreen(screenA));
        }

        // After A → B then B.Hide(): nothing visible, previous is still A — what lets
        // ShowPreviousScreen restore the UI from an empty screen state.
        [Test]
        public void PreviousScreen_AfterClosingTheTopScreen_IsTheScreenBeneathIt()
        {
            var history = new UIHistory<object>();
            var screenA = new object();
            var screenB = new object();

            history.PushScreen(screenA);
            history.PushScreen(screenB);
            history.Hide(screenA, true);
            history.Hide(screenB, true);

            Assert.AreSame(screenA, history.PreviousScreen(null));
        }

        [Test]
        public void PreviousScreen_OfTheOnlyScreenOnTheTrail_IsNull()
        {
            var history = new UIHistory<object>();
            var screenA = new object();

            history.PushScreen(screenA);

            Assert.IsNull(history.PreviousScreen(screenA));
        }

        [Test]
        public void PreviousScreen_AfterClosingTheOnlyScreen_IsNull()
        {
            var history = new UIHistory<object>();
            var screenA = new object();

            history.PushScreen(screenA);
            history.Hide(screenA, true);

            Assert.IsNull(history.PreviousScreen(null));
        }

        [Test]
        public void PreviousScreen_OfAScreenThatIsNotTheTrailTop_SkipsItself()
        {
            var history = new UIHistory<object>();
            var screenA = new object();
            var screenB = new object();

            history.PushScreen(screenA);
            history.PushScreen(screenB);

            Assert.AreSame(screenB, history.PreviousScreen(screenA));
        }

        // ---- Stored data ---------------------------------------------------

        [Test]
        public void PreviousScreen_ReturnsTheDataTheScreenWasPushedWith()
        {
            var history = new UIHistory<object>();
            var screenA = new object();
            var screenB = new object();
            var dataA = new TestUIData("a", 1);

            history.PushScreen(screenA, dataA);
            history.PushScreen(screenB, new TestUIData("b", 2));

            Assert.AreSame(screenA, history.PreviousScreen(screenB, out var data));
            Assert.AreSame(dataA, data);
        }

        [Test]
        public void PreviousScreen_ForAScreenPushedWithNull_ReturnsNullData()
        {
            var history = new UIHistory<object>();
            var screenA = new object();
            var screenB = new object();

            history.PushScreen(screenA);
            history.PushScreen(screenB, new TestUIData("b"));

            history.PreviousScreen(screenB, out var data);

            Assert.IsNull(data);
        }

        [Test]
        public void PreviousScreen_AfterClosingTheTopScreen_ReturnsTheRetainedScreensData()
        {
            var history = new UIHistory<object>();
            var screenA = new object();
            var screenB = new object();
            var dataA = new TestUIData("a");

            history.PushScreen(screenA, dataA);
            history.PushScreen(screenB);
            history.Hide(screenA, true);
            history.Hide(screenB, true);

            history.PreviousScreen(null, out var data);

            Assert.AreSame(dataA, data);
        }

        [Test]
        public void UpdateData_ReplacesTheStoredDataWithoutMovingTheEntry()
        {
            var history = new UIHistory<object>();
            var screenA = new object();
            var screenB = new object();
            var refreshed = new TestUIData("refreshed");

            history.PushScreen(screenA, new TestUIData("original"));
            history.PushScreen(screenB);
            history.UpdateData(screenA, refreshed);

            CollectionAssert.AreEqual(new[] { screenA, screenB }, history.ScreenTrail);
            Assert.AreSame(screenA, history.PreviousScreen(screenB, out var data));
            Assert.AreSame(refreshed, data);
        }

        // ---- TruncateTopScreen ---------------------------------------------

        [Test]
        public void TruncateTopScreen_OnTheTrailTop_RemovesItAndReturnsTrue()
        {
            var history = new UIHistory<object>();
            var screenA = new object();
            var screenB = new object();

            history.PushScreen(screenA);
            history.PushScreen(screenB);

            Assert.IsTrue(history.TruncateTopScreen(screenB));
            CollectionAssert.AreEqual(new[] { screenA }, history.ScreenTrail);
        }

        [Test]
        public void TruncateTopScreen_OnAScreenThatIsNotTheTop_ReturnsFalseAndKeepsTheTrail()
        {
            var history = new UIHistory<object>();
            var screenA = new object();
            var screenB = new object();

            history.PushScreen(screenA);
            history.PushScreen(screenB);

            Assert.IsFalse(history.TruncateTopScreen(screenA));
            CollectionAssert.AreEqual(new[] { screenA, screenB }, history.ScreenTrail);
        }

        [Test]
        public void TruncateTopScreen_LeavesVisibleEntriesUntouched()
        {
            var history = new UIHistory<object>();
            var screenA = new object();
            var screenB = new object();

            history.PushScreen(screenA);
            history.PushScreen(screenB);
            history.TruncateTopScreen(screenB);

            CollectionAssert.AreEqual(new[] { screenA, screenB }, history.VisibleEntries);
        }

        [Test]
        public void TruncateTopScreen_Null_ReturnsFalse()
        {
            var history = new UIHistory<object>();
            var screenA = new object();

            history.PushScreen(screenA);

            Assert.IsFalse(history.TruncateTopScreen(null));
        }

        // Each step truncates and re-pushes, so [A, B, C] retreats one screen per press and the
        // third press answers false — bounded, monotonic, never ping-pongs.
        [Test]
        public void BackSequence_FromThreeScreens_RetreatsOneStepAtATimeAndTerminates()
        {
            var history = new UIHistory<object>();
            var screenA = new object();
            var screenB = new object();
            var screenC = new object();
            history.PushScreen(screenA);
            history.PushScreen(screenB);
            history.PushScreen(screenC);

            var firstStep = StepBack(history, screenC, out var afterFirst);

            Assert.IsTrue(firstStep, "the first back press must find screen B");
            Assert.AreSame(screenB, afterFirst);
            CollectionAssert.AreEqual(new[] { screenA, screenB }, history.ScreenTrail);

            var secondStep = StepBack(history, afterFirst, out var afterSecond);

            Assert.IsTrue(secondStep, "the second back press must find screen A");
            Assert.AreSame(screenA, afterSecond);
            CollectionAssert.AreEqual(new[] { screenA }, history.ScreenTrail);

            var thirdStep = StepBack(history, afterSecond, out _);

            Assert.IsFalse(thirdStep, "the sequence must terminate at the root rather than repeat");
        }

        // ---- Clear ---------------------------------------------------------

        [Test]
        public void Clear_EmptiesVisibleEntries()
        {
            var history = new UIHistory<object>();
            var screenA = new object();
            var popupA = new object();

            history.PushScreen(screenA);
            history.PushOverlay(popupA);
            history.Clear();

            Assert.AreEqual(0, history.VisibleEntries.Count);
        }

        [Test]
        public void Clear_EmptiesTheScreenTrail()
        {
            var history = new UIHistory<object>();
            var screenA = new object();
            var screenB = new object();

            history.PushScreen(screenA);
            history.PushScreen(screenB);
            history.Clear();

            Assert.AreEqual(0, history.ScreenTrail.Count);
        }

        [Test]
        public void Clear_LeavesEveryQueryAnsweringEmpty()
        {
            var history = new UIHistory<object>();
            var screenA = new object();
            var popupA = new object();

            history.PushScreen(screenA);
            history.PushOverlay(popupA);
            history.Clear();

            Assert.IsNull(history.Top);
            Assert.IsNull(history.TopScreen);
            Assert.IsNull(history.PreviousScreen(null));
        }
    }
}
