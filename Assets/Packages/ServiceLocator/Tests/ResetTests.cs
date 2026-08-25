using NUnit.Framework;

namespace UnityEssentials.Services.Tests
{
    /// <summary>
    /// Covers the domain-reload hard reset (<see cref="ServiceRegistry.ResetStatics"/>), reachable
    /// from tests through InternalsVisibleTo. Reset wipes state without disposing anything, because
    /// after a domain reload the previous session's objects are already gone.
    /// </summary>
    [TestFixture]
    public class ResetTests
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
        public void ResetStatics_ClearsAllRegistrations()
        {
            ServiceLocator.Register<Sword>().As<IWeaponA>();
            var scope = ServiceLocator.CreateScope("Combat");
            scope.Register<Axe>();

            ServiceRegistry.ResetStatics();

            Assert.Throws<ServiceNotFoundException>(() => ServiceLocator.Get<IWeaponA>());
            Assert.IsFalse(ServiceLocator.TryGet<Axe>(out _));
        }

        [Test]
        public void ResetStatics_DoesNotDisposeServices()
        {
            var manual = new DisposableA();
            ServiceLocator.Register(manual);
            ServiceLocator.Register<DisposableB>();
            var created = ServiceLocator.Get<DisposableB>();

            var scope = ServiceLocator.CreateScope("Combat");
            var scoped = new DisposableC();
            scope.Register(scoped);

            ServiceRegistry.ResetStatics();

            Assert.IsFalse(manual.WasDisposed);
            Assert.IsFalse(created.WasDisposed);
            Assert.IsFalse(scoped.WasDisposed);
            CollectionAssert.IsEmpty(DisposalLog.Entries);
        }

        [Test]
        public void ResetStatics_LeavesLocatorUsable()
        {
            ServiceLocator.Register<Sword>();
            ServiceRegistry.ResetStatics();

            var sword = new Sword();
            ServiceLocator.Register(sword);

            Assert.AreSame(sword, ServiceLocator.Get<Sword>());
        }

        [Test]
        public void ResetStatics_ReleasesStaleScopeHandles()
        {
            var scope = ServiceLocator.CreateScope("Combat");
            scope.Register(new Sword());

            ServiceRegistry.ResetStatics();

            // A handle from before the reset must throw instead of resolving dead objects
            // (this is the domain-reload-disabled scenario).
            Assert.IsTrue(scope.IsReleased);
            Assert.Throws<ServiceLocatorException>(() => scope.Get<Sword>());
            Assert.Throws<ServiceLocatorException>(() => scope.Register(new Axe()));
        }

        [Test]
        public void ResetStatics_ClearsScopesSoNamesAreFree()
        {
            var scope = ServiceLocator.CreateScope("Combat");
            scope.Register<Sword>();

            ServiceRegistry.ResetStatics();

            ServiceScope rebuilt = null;
            Assert.DoesNotThrow(() => rebuilt = ServiceLocator.CreateScope("Combat"));

            var sword = new Sword();
            rebuilt.Register(sword);

            Assert.AreSame(sword, ServiceLocator.Get<Sword>());
        }
    }
}
