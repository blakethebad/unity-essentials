using System;
using NUnit.Framework;

namespace UnityEssentials.Services.Tests
{
    [TestFixture]
    public class RegistrationTests
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

        // ---- The three overloads round-trip ------------------------------

        [Test]
        public void RegisterType_RoundTrips()
        {
            ServiceLocator.Register<Sword>();

            Assert.IsInstanceOf<Sword>(ServiceLocator.Get<Sword>());
        }

        [Test]
        public void RegisterInstance_RoundTrips()
        {
            var sword = new Sword();
            ServiceLocator.Register(sword);

            Assert.AreSame(sword, ServiceLocator.Get<Sword>());
        }

        [Test]
        public void RegisterFactory_RoundTrips()
        {
            var sword = new Sword();
            ServiceLocator.Register<Sword>(() => sword);

            Assert.AreSame(sword, ServiceLocator.Get<Sword>());
        }

        [Test]
        public void RegisterType_WithExplicitSharedLifetime_RoundTrips()
        {
            ServiceLocator.Register<Sword>(Lifetime.Shared);

            Assert.IsNotNull(ServiceLocator.Get<Sword>());
        }

        // ---- Lazy vs NonLazy ---------------------------------------------

        [Test]
        public void Register_IsLazyByDefault_DoesNotConstruct()
        {
            ServiceLocator.Register<ConstructionCounter>();

            Assert.AreEqual(0, ConstructionCounter.Count);
        }

        [Test]
        public void Get_ConstructsLazyServiceExactlyOnce()
        {
            ServiceLocator.Register<ConstructionCounter>();

            ServiceLocator.Get<ConstructionCounter>();
            ServiceLocator.Get<ConstructionCounter>();

            Assert.AreEqual(1, ConstructionCounter.Count);
        }

        [Test]
        public void Register_FactoryIsLazyByDefault_DoesNotConstruct()
        {
            ServiceLocator.Register<ConstructionCounter>(() => new ConstructionCounter());

            Assert.AreEqual(0, ConstructionCounter.Count);
        }

        [Test]
        public void NonLazy_ConstructsDuringRegistration()
        {
            ServiceLocator.Register<ConstructionCounter>().NonLazy();

            Assert.AreEqual(1, ConstructionCounter.Count);
        }

        [Test]
        public void NonLazy_Factory_ConstructsDuringRegistration()
        {
            ServiceLocator.Register<ConstructionCounter>(() => new ConstructionCounter()).NonLazy();

            Assert.AreEqual(1, ConstructionCounter.Count);
        }

        [Test]
        public void NonLazy_OnTransient_Throws()
        {
            Assert.Throws<ServiceRegistrationException>(
                () => ServiceLocator.Register<Sword>(Lifetime.Transient).NonLazy());
        }

        [Test]
        public void NonLazy_OnInstanceRegistration_IsNoOp()
        {
            var sword = new Sword();

            Assert.DoesNotThrow(() => ServiceLocator.Register(sword).NonLazy());
            Assert.AreSame(sword, ServiceLocator.Get<Sword>());
        }

        // ---- Duplicate keys ----------------------------------------------

        [Test]
        public void DuplicateSelfKey_InSameScope_Throws()
        {
            ServiceLocator.Register<Sword>();

            Assert.Throws<ServiceRegistrationException>(() => ServiceLocator.Register<Sword>());
        }

        [Test]
        public void DuplicateInterfaceKey_InSameScope_Throws()
        {
            ServiceLocator.Register<Sword>().As<IWeaponA>();

            Assert.Throws<ServiceRegistrationException>(
                () => ServiceLocator.Register<Axe>().As<IWeaponA>());
        }

        // ---- .As<>() validation ------------------------------------------

        [Test]
        public void As_UnimplementedInterface_Throws()
        {
            Assert.Throws<ServiceRegistrationException>(
                () => ServiceLocator.Register<Shield>().As<IWeaponA>());
        }

        [Test]
        public void As_FourInterfaces_IsAllowed()
        {
            Assert.DoesNotThrow(() => ServiceLocator.Register<Sword>()
                .As<IWeaponA>()
                .As<IWeaponB>()
                .As<IWeaponC>()
                .As<IWeaponD>());
        }

        [Test]
        public void As_FifthInterface_Throws()
        {
            Assert.Throws<ServiceRegistrationException>(() => ServiceLocator.Register<Sword>()
                .As<IWeaponA>()
                .As<IWeaponB>()
                .As<IWeaponC>()
                .As<IWeaponD>()
                .As<IWeaponE>());
        }

        // ---- Null arguments ----------------------------------------------

        [Test]
        public void RegisterNullInstance_ThrowsArgumentNullException()
        {
            Assert.Throws<ArgumentNullException>(() => ServiceLocator.Register((Sword)null));
        }

        [Test]
        public void RegisterNullFactory_ThrowsArgumentNullException()
        {
            // Explicit type argument keeps this on the factory overload (without it, the instance
            // overload would bind with TConcrete = Func<Sword>).
            Assert.Throws<ArgumentNullException>(() => ServiceLocator.Register<Sword>((Func<Sword>)null));
        }

        // ---- Uninitialized builder ---------------------------------------

        [Test]
        public void DefaultBuilder_ThrowsWhenUsed()
        {
            var builder = default(RegistrationBuilder<Sword>);

            Assert.Throws<ServiceRegistrationException>(() => builder.AsSelf());
        }

        [Test]
        public void FailedAs_LeavesTheSelfKeyPublished()
        {
            ServiceLocator.Register<Sword>().As<IWeaponA>();
            var builder = ServiceLocator.Register<Axe>();

            // IWeaponA is taken in this scope, so the chain fails — but the failure must not
            // have locked Axe's self key as a side effect.
            Assert.Throws<ServiceRegistrationException>(() => builder.As<IWeaponA>());
            Assert.IsInstanceOf<Axe>(ServiceLocator.Get<Axe>());
        }
    }
}
