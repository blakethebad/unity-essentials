using NUnit.Framework;

namespace UnityEssentials.Services.Tests
{
    [TestFixture]
    public class LifetimeTests
    {
        [SetUp]
        public void SetUp()
        {
            ConstructionCounter.Reset();
            OtherConstructionCounter.Reset();
            DisposalLog.Clear();
        }

        [TearDown]
        public void TearDown() => ServiceLocator.ReleaseAll();

        [Test]
        public void SharedType_ConstructsOnFirstGetOnly()
        {
            ServiceLocator.Register<ConstructionCounter>();
            Assert.AreEqual(0, ConstructionCounter.Count);

            ServiceLocator.Get<ConstructionCounter>();
            ServiceLocator.Get<ConstructionCounter>();
            ServiceLocator.Get<ConstructionCounter>();

            Assert.AreEqual(1, ConstructionCounter.Count);
        }

        [Test]
        public void SharedFactory_IsInvokedOnce()
        {
            var invocations = 0;
            ServiceLocator.Register<Sword>(() =>
            {
                invocations++;
                return new Sword();
            });

            ServiceLocator.Get<Sword>();
            ServiceLocator.Get<Sword>();

            Assert.AreEqual(1, invocations);
        }

        [Test]
        public void TransientType_ConstructsPerGet()
        {
            ServiceLocator.Register<ConstructionCounter>(Lifetime.Transient);

            ServiceLocator.Get<ConstructionCounter>();
            ServiceLocator.Get<ConstructionCounter>();
            ServiceLocator.Get<ConstructionCounter>();

            Assert.AreEqual(3, ConstructionCounter.Count);
        }

        [Test]
        public void TransientFactory_IsInvokedPerGet()
        {
            var invocations = 0;
            ServiceLocator.Register<Sword>(() =>
            {
                invocations++;
                return new Sword();
            }, Lifetime.Transient);

            ServiceLocator.Get<Sword>();
            ServiceLocator.Get<Sword>();

            Assert.AreEqual(2, invocations);
        }

        [Test]
        public void Transient_IsNotSharedAcrossInterfaceKeys()
        {
            ServiceLocator.Register<Sword>(Lifetime.Transient)
                .As<IWeaponA>()
                .As<IWeaponB>();

            Assert.AreNotSame(ServiceLocator.Get<IWeaponA>(), ServiceLocator.Get<IWeaponB>());
        }

        [Test]
        public void NonLazyShared_IsNotReconstructedOnGet()
        {
            ServiceLocator.Register<ConstructionCounter>().NonLazy();
            Assert.AreEqual(1, ConstructionCounter.Count);

            var first = ServiceLocator.Get<ConstructionCounter>();
            var second = ServiceLocator.Get<ConstructionCounter>();

            Assert.AreEqual(1, ConstructionCounter.Count);
            Assert.AreSame(first, second);
        }

        [Test]
        public void LazyService_NeverResolved_IsNeverConstructed()
        {
            ServiceLocator.Register<ConstructionCounter>();
            ServiceLocator.Register<OtherConstructionCounter>();

            ServiceLocator.Get<OtherConstructionCounter>();

            Assert.AreEqual(0, ConstructionCounter.Count);
            Assert.AreEqual(1, OtherConstructionCounter.Count);
        }
    }
}
