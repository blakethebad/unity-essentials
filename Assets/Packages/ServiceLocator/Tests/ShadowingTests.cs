using NUnit.Framework;

namespace UnityEssentials.Services.Tests
{
    [TestFixture]
    public class ShadowingTests
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
        public void SameKeyInDifferentScopes_DoesNotThrow()
        {
            ServiceLocator.Register(new Sword());
            var scope = ServiceLocator.CreateScope("Combat");

            Assert.DoesNotThrow(() => scope.Register(new Sword()));
        }

        [Test]
        public void ScopeRegistration_ShadowsGlobalRegistration()
        {
            var global = new Sword();
            ServiceLocator.Register(global);

            var scope = ServiceLocator.CreateScope("Combat");
            var scoped = new Sword();
            scope.Register(scoped);

            Assert.AreSame(scoped, ServiceLocator.Get<Sword>());
        }

        [Test]
        public void ScopeRelease_RestoresShadowedRegistration()
        {
            var global = new Sword();
            ServiceLocator.Register(global);

            var scope = ServiceLocator.CreateScope("Combat");
            scope.Register(new Sword());
            scope.Release();

            Assert.AreSame(global, ServiceLocator.Get<Sword>());
        }

        [Test]
        public void MidStackRelease_LeavesMostRecentRegistrationWinning()
        {
            var global = new Sword();
            ServiceLocator.Register(global);

            var scopeA = ServiceLocator.CreateScope("A");
            var a = new Sword();
            scopeA.Register(a);

            var scopeB = ServiceLocator.CreateScope("B");
            var b = new Sword();
            scopeB.Register(b);

            Assert.AreSame(b, ServiceLocator.Get<Sword>());

            scopeA.Release();
            Assert.AreSame(b, ServiceLocator.Get<Sword>());

            scopeB.Release();
            Assert.AreSame(global, ServiceLocator.Get<Sword>());
        }

        [Test]
        public void Shadowing_IsPerKey_NotPerRegistration()
        {
            var sword = new Sword();
            ServiceLocator.Register(sword).As<IWeaponA>().AsSelf();

            var scope = ServiceLocator.CreateScope("Combat");
            var axe = new Axe();
            scope.Register(axe).As<IWeaponA>();

            // Only the IWeaponA key is shadowed; the concrete Sword key is untouched.
            Assert.AreSame(axe, ServiceLocator.Get<IWeaponA>());
            Assert.AreSame(sword, ServiceLocator.Get<Sword>());

            scope.Release();

            Assert.AreSame(sword, ServiceLocator.Get<IWeaponA>());
        }

        [Test]
        public void ReleaseAll_AllowsFullRebuild()
        {
            var first = new Sword();
            ServiceLocator.Register(first);
            var scope = ServiceLocator.CreateScope("Combat");
            scope.Register(new Axe());

            ServiceLocator.ReleaseAll();

            Assert.Throws<ServiceNotFoundException>(() => ServiceLocator.Get<Sword>());

            var second = new Sword();
            ServiceLocator.Register(second);
            var rebuiltScope = ServiceLocator.CreateScope("Combat");
            var axe = new Axe();
            rebuiltScope.Register(axe);

            Assert.AreSame(second, ServiceLocator.Get<Sword>());
            Assert.AreSame(axe, ServiceLocator.Get<Axe>());
        }
    }
}
