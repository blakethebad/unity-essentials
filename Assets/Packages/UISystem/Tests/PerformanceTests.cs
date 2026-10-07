using NUnit.Framework;
using UnityEngine.TestTools.Constraints;
using Is = UnityEngine.TestTools.Constraints.Is;

namespace UnityEssentials.UI.Tests
{
    /// <summary>
    /// The allocation floor of the read paths a game touches every frame: resolving an element
    /// through the service or through the window, and asking an element whether it is visible.
    /// Show and Hide are deliberately absent — their per-call closure is the stale-transition guard.
    /// </summary>
    [TestFixture]
    public class PerformanceTests : UITestFixture
    {
        // Sink keeps the resolved reference alive so the call cannot be optimised away,
        // and keeps the measured lambdas void-returning (the form Unity documents for
        // Is.Not.AllocatingGCMemory(); a value-returning lambda binds to a different
        // NUnit overload that the Not-prefix does not forward to the constraint).
        private static object _sink;

        [SetUp]
        public void SetUp()
        {
            _sink = null;
        }

        [Test]
        public void WarmGetUI_DoesNotAllocate()
        {
            var service = CreateLoadedService(BuildUIPrefab<TestElementA>());

            // Warm-up: window generation, dictionary internals and JIT all happen here.
            _sink = service.GetUI<TestElementA>();

            Assert.That(() => { _sink = service.GetUI<TestElementA>(); }, Is.Not.AllocatingGCMemory());
        }

        [Test]
        public void WarmTryGetUI_DoesNotAllocate()
        {
            var service = CreateLoadedService(BuildUIPrefab<TestElementA>());

            service.TryGetUI<TestElementA>(out var warm);
            _sink = warm;

            Assert.That(() =>
            {
                service.TryGetUI<TestElementA>(out var element);
                _sink = element;
            }, Is.Not.AllocatingGCMemory());
        }

        // The Type-taking window overload is the one a caller reaches for when the element type is
        // only known at runtime, so it has to stay as cheap as the generic façade over it.
        [Test]
        public void WarmWindowGetUIByType_DoesNotAllocate()
        {
            var service = CreateLoadedService(BuildUIPrefab<TestElementA>());
            var window = service.ActiveWindow;

            _sink = window.GetUI(typeof(TestElementA));

            Assert.That(() => { _sink = window.GetUI(typeof(TestElementA)); }, Is.Not.AllocatingGCMemory());
        }

        // Read through the sink rather than assigned to it: a bool stored in an object field would
        // box, and the box would be the only allocation the measurement ever saw.
        [Test]
        public void IsVisible_DoesNotAllocate()
        {
            var service = CreateLoadedService(BuildUIPrefab<TestElementA>());
            var element = service.ShowUI<TestElementA>();

            if (element.IsVisible)
            {
                _sink = element;
            }

            Assert.That(() =>
            {
                if (element.IsVisible)
                {
                    _sink = element;
                }
            }, Is.Not.AllocatingGCMemory());
        }
    }
}
