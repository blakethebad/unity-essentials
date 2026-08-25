using System;
using NUnit.Framework;

namespace UnityEssentials.Services.Tests
{
    [TestFixture]
    public class ScopeTests
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

        // ---- Unified resolution (the headline requirement) ----------------

        [Test]
        public void ScopedRegistration_ResolvesThroughGlobalGet()
        {
            var scope = ServiceLocator.CreateScope("Combat");
            var sword = new Sword();
            scope.Register(sword);

            Assert.AreSame(sword, ServiceLocator.Get<Sword>());
        }

        [Test]
        public void ScopedRegistration_ResolvesThroughGlobalTryGet()
        {
            var scope = ServiceLocator.CreateScope("Combat");
            scope.Register<Sword>().As<IWeaponA>();

            Assert.IsTrue(ServiceLocator.TryGet<IWeaponA>(out var weapon));
            Assert.IsInstanceOf<Sword>(weapon);
        }

        // ---- Handle surface -----------------------------------------------

        [Test]
        public void CreateScope_ExposesNameAndIsNotReleased()
        {
            var scope = ServiceLocator.CreateScope("Combat");

            Assert.AreEqual("Combat", scope.Name);
            Assert.IsFalse(scope.IsReleased);
        }

        [Test]
        public void Release_RemovesScopedBindings()
        {
            var scope = ServiceLocator.CreateScope("Combat");
            scope.Register<Sword>();

            scope.Release();

            Assert.IsTrue(scope.IsReleased);
            Assert.Throws<ServiceNotFoundException>(() => ServiceLocator.Get<Sword>());
        }

        [Test]
        public void UsingBlock_ReleasesScope()
        {
            ServiceScope captured;
            using (var scope = ServiceLocator.CreateScope("Combat"))
            {
                captured = scope;
                scope.Register<Sword>();
                Assert.IsNotNull(ServiceLocator.Get<Sword>());
            }

            Assert.IsTrue(captured.IsReleased);
            Assert.Throws<ServiceNotFoundException>(() => ServiceLocator.Get<Sword>());
        }

        [Test]
        public void DoubleRelease_IsIdempotent()
        {
            var scope = ServiceLocator.CreateScope("Combat");
            scope.Register<Sword>();

            scope.Release();

            Assert.DoesNotThrow(() => scope.Release());
            Assert.DoesNotThrow(() => scope.Dispose());
            Assert.IsTrue(scope.IsReleased);
        }

        // ---- Use after release ---------------------------------------------

        [Test]
        public void Register_AfterRelease_Throws()
        {
            var scope = ServiceLocator.CreateScope("Combat");
            scope.Release();

            Assert.Throws<ServiceLocatorException>(() => scope.Register<Sword>());
        }

        [Test]
        public void Get_AfterRelease_Throws()
        {
            ServiceLocator.Register<Sword>();
            var scope = ServiceLocator.CreateScope("Combat");
            scope.Release();

            // Global registration exists, so the throw must come from the released-scope guard.
            Assert.Throws<ServiceLocatorException>(() => scope.Get<Sword>());
        }

        // ---- Scope names -----------------------------------------------------

        [Test]
        public void DuplicateScopeName_Throws()
        {
            ServiceLocator.CreateScope("Combat");

            Assert.Throws<ServiceRegistrationException>(() => ServiceLocator.CreateScope("Combat"));
        }

        [Test]
        public void NullScopeName_Throws()
        {
            Assert.Throws<ArgumentNullException>(() => ServiceLocator.CreateScope(null));
        }

        [Test]
        public void EmptyScopeName_Throws()
        {
            Assert.Throws<ArgumentException>(() => ServiceLocator.CreateScope(string.Empty));
        }

        [Test]
        public void ReservedRootScopeName_Throws()
        {
            Assert.Throws<ServiceRegistrationException>(() => ServiceLocator.CreateScope("<global>"));
        }

        [Test]
        public void ScopeName_IsReusableAfterRelease()
        {
            var first = ServiceLocator.CreateScope("Combat");
            first.Release();

            Assert.DoesNotThrow(() => ServiceLocator.CreateScope("Combat"));
        }

        // ---- Scope-first resolution -------------------------------------------

        [Test]
        public void ScopeGet_PrefersItsOwnBinding()
        {
            var scope = ServiceLocator.CreateScope("Combat");
            var scoped = new Sword();
            scope.Register(scoped);

            var global = new Sword();
            ServiceLocator.Register(global);

            Assert.AreSame(scoped, scope.Get<Sword>());
            Assert.AreSame(global, ServiceLocator.Get<Sword>());
        }

        [Test]
        public void ScopeGet_FallsBackToOuterRegistrations()
        {
            var axe = new Axe();
            ServiceLocator.Register(axe);
            var scope = ServiceLocator.CreateScope("Combat");
            scope.Register<Sword>();

            Assert.AreSame(axe, scope.Get<Axe>());
        }

        [Test]
        public void ScopeTryGet_Miss_ReturnsFalseAndNull()
        {
            var scope = ServiceLocator.CreateScope("Combat");

            Assert.IsFalse(scope.TryGet<UnregisteredService>(out var service));
            Assert.IsNull(service);
        }
    }
}
