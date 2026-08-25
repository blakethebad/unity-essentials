using NUnit.Framework;
using UnityEngine.TestTools.Constraints;
using Is = UnityEngine.TestTools.Constraints.Is;

namespace UnityEssentials.Services.Tests
{
    [TestFixture]
    public class PerformanceTests
    {
        // Sink keeps the resolved reference alive so the call cannot be optimised away,
        // and keeps the measured lambdas void-returning (the form Unity documents for
        // Is.Not.AllocatingGCMemory(); a value-returning lambda binds to a different
        // NUnit overload that the Not-prefix does not forward to the constraint).
        private static object _sink;

        [SetUp]
        public void SetUp()
        {
            ConstructionCounter.Reset();
            OtherConstructionCounter.Reset();
            DisposalLog.Clear();
            _sink = null;
        }

        [TearDown]
        public void TearDown() => ServiceLocator.ReleaseAll();

        [Test]
        public void WarmGet_DoesNotAllocate()
        {
            ServiceLocator.Register<Sword>();

            // Warm-up: lazy construction, dictionary internals and JIT all happen here.
            _sink = ServiceLocator.Get<Sword>();

            Assert.That(() => { _sink = ServiceLocator.Get<Sword>(); }, Is.Not.AllocatingGCMemory());
        }

        [Test]
        public void WarmInterfaceGet_DoesNotAllocate()
        {
            ServiceLocator.Register<Sword>().As<IWeaponA>();

            _sink = ServiceLocator.Get<IWeaponA>();

            Assert.That(() => { _sink = ServiceLocator.Get<IWeaponA>(); }, Is.Not.AllocatingGCMemory());
        }

        [Test]
        public void WarmTryGet_DoesNotAllocate()
        {
            ServiceLocator.Register<Sword>();

            ServiceLocator.TryGet<Sword>(out var warm);
            _sink = warm;

            Assert.That(() =>
            {
                ServiceLocator.TryGet<Sword>(out var sword);
                _sink = sword;
            }, Is.Not.AllocatingGCMemory());
        }
    }
}
