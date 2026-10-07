using System;
using NUnit.Framework;

namespace UnityEssentials.UI.Tests
{
    /// <summary>
    /// The binding contract: an element belongs to exactly one window for its whole lifetime, and
    /// must be bound before it can be shown. Covers the throws an unbound or re-bound element
    /// raises, and that Show/Hide called straight on a bound element work as well as through the
    /// service.
    /// </summary>
    [TestFixture]
    public sealed class BindingTests : UITestFixture
    {
        // ---- Unbound elements ----------------------------------------------

        // An unbound element would show perfectly while belonging to no window, so nothing could
        // ever tear it down — hence the throw.
        [Test]
        public void UnboundElement_Show_ThrowsUIBindingException()
        {
            var element = BuildUIElement<UnregisteredElement>();

            Assert.Throws<UIBindingException>(() => element.Show());
        }

        // The idempotent no-op returns before the binding check, so a defensive double-close on a
        // teardown path never throws.
        [Test]
        public void UnboundElement_HideWhileAlreadyHidden_DoesNotThrow()
        {
            var element = BuildUIElement<UnregisteredElement>();

            Assert.DoesNotThrow(() => element.Hide());
        }

        // ---- Binding --------------------------------------------------------

        [Test]
        public void Bind_ToASecondWindow_ThrowsUIBindingException()
        {
            var firstService = CreateLoadedService(BuildUIPrefab<TestElementA>());
            var element = firstService.GetUI<TestElementA>();
            var secondService = CreateLoadedService(BuildUIPrefab<TestElementB>());
            var secondWindow = secondService.ActiveWindow;

            Assert.Throws<UIBindingException>(() => element.Bind(secondWindow));
        }

        [Test]
        public void Bind_ToTheSameWindow_IsANoOp()
        {
            var service = CreateLoadedService(BuildUIPrefab<TestElementA>());
            var element = service.GetUI<TestElementA>();
            var window = service.ActiveWindow;

            Assert.DoesNotThrow(() => element.Bind(window));
            Assert.AreSame(window, element.Window);
        }

        [Test]
        public void Bind_Null_ThrowsArgumentNullException()
        {
            var element = BuildUIElement<CountingElement>();

            Assert.Throws<ArgumentNullException>(() => element.Bind(null));
        }

        // ---- Bound elements driven directly ---------------------------------

        // Show and Hide are the same code path whether the call came from ShowUI/HideUI or straight
        // from a button handler holding the element: the service keeps no lists to fall out of sync.
        [Test]
        public void ElementShownDirectly_IsVisible()
        {
            var service = CreateLoadedService(BuildUIPrefab<CountingElement>());
            var element = service.GetUI<CountingElement>();

            element.Show();

            Assert.IsTrue(element.IsVisible);
            Assert.AreEqual(1, element.ShowCount);
        }

        [Test]
        public void ElementHiddenDirectly_IsHidden()
        {
            var service = CreateLoadedService(BuildUIPrefab<CountingElement>());
            var element = service.GetUI<CountingElement>();
            element.Show();

            element.Hide();

            Assert.IsFalse(element.IsVisible);
            Assert.AreEqual(UIElementState.Hidden, element.State);
            Assert.AreEqual(1, element.HideCount);
        }
    }
}
