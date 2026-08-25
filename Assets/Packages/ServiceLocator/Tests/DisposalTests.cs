using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace UnityEssentials.Services.Tests
{
    [TestFixture]
    public class DisposalTests
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
        public void CreatedSharedDisposables_AreDisposedInReverseRegistrationOrder()
        {
            ServiceLocator.Register<DisposableA>();
            ServiceLocator.Register<DisposableB>();
            ServiceLocator.Register<DisposableC>();

            ServiceLocator.Get<DisposableA>();
            ServiceLocator.Get<DisposableB>();
            ServiceLocator.Get<DisposableC>();

            ServiceLocator.ReleaseAll();

            CollectionAssert.AreEqual(new[] { "C", "B", "A" }, DisposalLog.Entries);
        }

        [Test]
        public void NeverCreatedLazyService_IsNotDisposed()
        {
            ServiceLocator.Register<DisposableA>();
            ServiceLocator.Register<DisposableB>();

            ServiceLocator.Get<DisposableB>();

            ServiceLocator.ReleaseAll();

            CollectionAssert.AreEqual(new[] { "B" }, DisposalLog.Entries);
        }

        [Test]
        public void ManualInstance_IsDisposedByDefault()
        {
            var instance = new DisposableA();
            ServiceLocator.Register(instance);

            ServiceLocator.ReleaseAll();

            Assert.IsTrue(instance.WasDisposed);
        }

        [Test]
        public void ExternallyOwnedInstance_IsNotDisposed()
        {
            var instance = new DisposableA();
            ServiceLocator.Register(instance).ExternallyOwned();

            ServiceLocator.ReleaseAll();

            Assert.IsFalse(instance.WasDisposed);
            CollectionAssert.IsEmpty(DisposalLog.Entries);
        }

        [Test]
        public void ExternallyOwnedCreatedService_IsNotDisposed()
        {
            ServiceLocator.Register<DisposableA>().ExternallyOwned();
            ServiceLocator.Get<DisposableA>();

            ServiceLocator.ReleaseAll();

            CollectionAssert.IsEmpty(DisposalLog.Entries);
        }

        [Test]
        public void Transients_AreNeverDisposed()
        {
            ServiceLocator.Register<DisposableA>(Lifetime.Transient);

            ServiceLocator.Get<DisposableA>();
            ServiceLocator.Get<DisposableA>();

            ServiceLocator.ReleaseAll();

            CollectionAssert.IsEmpty(DisposalLog.Entries);
        }

        [Test]
        public void DisposableBehindInterfaceKey_IsDisposed()
        {
            ServiceLocator.Register<DisposableWeapon>().As<IWeaponA>();
            var weapon = (DisposableWeapon)ServiceLocator.Get<IWeaponA>();

            ServiceLocator.ReleaseAll();

            Assert.IsTrue(weapon.WasDisposed);
        }

        [Test]
        public void ThrowingDispose_IsLoggedAndDisposalContinues()
        {
            var first = new DisposableA();
            ServiceLocator.Register(first);
            ServiceLocator.Register(new ThrowingDisposable());

            LogAssert.Expect(LogType.Exception, new Regex(ThrowingDisposable.FailureMessage));

            ServiceLocator.ReleaseAll();

            // Reverse registration order: the thrower runs first, the earlier one must still run.
            CollectionAssert.Contains(DisposalLog.Entries, "Throwing");
            Assert.IsTrue(first.WasDisposed, "Disposables registered before the thrower must still be disposed.");
        }

        [Test]
        public void ScopeRelease_DisposesOnlyItsOwnServices()
        {
            var global = new DisposableA();
            ServiceLocator.Register(global);

            var scope = ServiceLocator.CreateScope("Combat");
            var scoped = new DisposableB();
            scope.Register(scoped);

            scope.Release();

            Assert.IsTrue(scoped.WasDisposed);
            Assert.IsFalse(global.WasDisposed);
            CollectionAssert.AreEqual(new[] { "B" }, DisposalLog.Entries);
        }

        [Test]
        public void ReleaseAll_WipesGlobalAndScopedServices()
        {
            var global = new DisposableA();
            ServiceLocator.Register(global);

            var scope = ServiceLocator.CreateScope("Combat");
            var scoped = new DisposableB();
            scope.Register(scoped);

            ServiceLocator.ReleaseAll();

            Assert.IsTrue(global.WasDisposed);
            Assert.IsTrue(scoped.WasDisposed);
            Assert.IsTrue(scope.IsReleased);
            Assert.Throws<ServiceNotFoundException>(() => ServiceLocator.Get<DisposableA>());
            Assert.Throws<ServiceNotFoundException>(() => ServiceLocator.Get<DisposableB>());
        }
    }
}
