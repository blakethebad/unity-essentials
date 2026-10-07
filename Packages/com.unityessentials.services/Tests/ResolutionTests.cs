using NUnit.Framework;

namespace UnityEssentials.Services.Tests
{
    [TestFixture]
    public class ResolutionTests
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

        // ---- Hits and misses ---------------------------------------------

        [Test]
        public void Get_RegisteredService_ReturnsIt()
        {
            var sword = new Sword();
            ServiceLocator.Register(sword);

            Assert.AreSame(sword, ServiceLocator.Get<Sword>());
        }

        [Test]
        public void Get_UnregisteredService_ThrowsServiceNotFound()
        {
            Assert.Throws<ServiceNotFoundException>(() => ServiceLocator.Get<UnregisteredService>());
        }

        [Test]
        public void Get_UnregisteredService_MessageContainsTypeName()
        {
            var exception = Assert.Throws<ServiceNotFoundException>(
                () => ServiceLocator.Get<UnregisteredService>());

            StringAssert.Contains(nameof(UnregisteredService), exception.Message);
        }

        [Test]
        public void TryGet_RegisteredService_ReturnsTrueAndService()
        {
            var sword = new Sword();
            ServiceLocator.Register(sword);

            Assert.IsTrue(ServiceLocator.TryGet<Sword>(out var resolved));
            Assert.AreSame(sword, resolved);
        }

        [Test]
        public void TryGet_UnregisteredService_ReturnsFalseAndNull()
        {
            UnregisteredService resolved = null;

            Assert.DoesNotThrow(() => ServiceLocator.TryGet(out resolved));
            Assert.IsFalse(ServiceLocator.TryGet<UnregisteredService>(out resolved));
            Assert.IsNull(resolved);
        }

        // ---- Lifetimes ----------------------------------------------------

        [Test]
        public void Shared_ReturnsSameReferenceEveryTime()
        {
            ServiceLocator.Register<Sword>();

            Assert.AreSame(ServiceLocator.Get<Sword>(), ServiceLocator.Get<Sword>());
        }

        [Test]
        public void Transient_ReturnsNewInstanceEveryTime()
        {
            ServiceLocator.Register<Sword>(Lifetime.Transient);

            Assert.AreNotSame(ServiceLocator.Get<Sword>(), ServiceLocator.Get<Sword>());
        }

        // ---- Interface keys ------------------------------------------------

        [Test]
        public void InterfaceKey_ReturnsConcreteInstance()
        {
            ServiceLocator.Register<Sword>().As<IWeaponA>();

            Assert.IsInstanceOf<Sword>(ServiceLocator.Get<IWeaponA>());
        }

        [Test]
        public void FourInterfaceKeys_ShareOneSharedInstance()
        {
            ServiceLocator.Register<Sword>()
                .As<IWeaponA>()
                .As<IWeaponB>()
                .As<IWeaponC>()
                .As<IWeaponD>();

            var a = ServiceLocator.Get<IWeaponA>();
            var b = ServiceLocator.Get<IWeaponB>();
            var c = ServiceLocator.Get<IWeaponC>();
            var d = ServiceLocator.Get<IWeaponD>();

            Assert.AreSame(a, b);
            Assert.AreSame(a, c);
            Assert.AreSame(a, d);
        }

        [Test]
        public void InstanceRegistration_ResolvesThroughInterfaceKey()
        {
            var sword = new Sword();
            ServiceLocator.Register(sword).As<IWeaponA>();

            Assert.AreSame(sword, ServiceLocator.Get<IWeaponA>());
        }

        // ---- Failure modes -------------------------------------------------

        [Test]
        public void SharedFactoryReturningNull_ThrowsAtFirstGet()
        {
            ServiceLocator.Register<Sword>(() => null);

            // The plan pins the timing ("throws at creation time"), not the exact type.
            Assert.Catch<ServiceLocatorException>(() => ServiceLocator.Get<Sword>());
        }

        [Test]
        public void CircularResolution_ThrowsServiceLocatorException()
        {
            ServiceLocator.Register<CircularService>(() => new CircularService());

            Assert.Throws<ServiceLocatorException>(() => ServiceLocator.Get<CircularService>());
        }
    }
}
