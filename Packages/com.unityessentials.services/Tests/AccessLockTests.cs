using NUnit.Framework;

namespace UnityEssentials.Services.Tests
{
    [TestFixture]
    public class AccessLockTests
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
        public void As_LocksTheSelfKey()
        {
            ServiceLocator.Register<Sword>().As<IWeaponA>();

            Assert.Throws<ServiceNotFoundException>(() => ServiceLocator.Get<Sword>());
            Assert.IsInstanceOf<Sword>(ServiceLocator.Get<IWeaponA>());
        }

        [Test]
        public void LockedSelfKey_MissMessageMentionsTheLock()
        {
            ServiceLocator.Register<Sword>().As<IWeaponA>();

            var exception = Assert.Throws<ServiceNotFoundException>(() => ServiceLocator.Get<Sword>());

            Assert.That(exception.Message.ToLowerInvariant(), Does.Contain("locked"));
        }

        [Test]
        public void LockedSelfKey_TryGetReturnsFalse()
        {
            ServiceLocator.Register<Sword>().As<IWeaponA>();

            Assert.IsFalse(ServiceLocator.TryGet<Sword>(out var sword));
            Assert.IsNull(sword);
        }

        [Test]
        public void AsSelf_AfterAs_ReEnablesSelfKey()
        {
            ServiceLocator.Register<Sword>().As<IWeaponA>().AsSelf();

            var self = ServiceLocator.Get<Sword>();

            Assert.IsNotNull(self);
            Assert.AreSame(self, ServiceLocator.Get<IWeaponA>());
        }

        [Test]
        public void AsSelf_BeforeAs_ReEnablesSelfKey()
        {
            ServiceLocator.Register<Sword>().AsSelf().As<IWeaponA>();

            var self = ServiceLocator.Get<Sword>();

            Assert.IsNotNull(self);
            Assert.AreSame(self, ServiceLocator.Get<IWeaponA>());
        }

        [Test]
        public void WithoutAs_SelfKeyIsPublishedImplicitly()
        {
            ServiceLocator.Register<Sword>();

            Assert.IsTrue(ServiceLocator.TryGet<Sword>(out var sword));
            Assert.IsNotNull(sword);
        }

        [Test]
        public void LockedSelfKey_LeavesNoResidue_SoItCanBeRegisteredAgain()
        {
            ServiceLocator.Register<Sword>().As<IWeaponA>();

            var replacement = new Sword();
            Assert.DoesNotThrow(() => ServiceLocator.Register(replacement));
            Assert.AreSame(replacement, ServiceLocator.Get<Sword>());
        }

        [Test]
        public void As_WithConcreteTypeItself_ActsAsAsSelf()
        {
            ServiceLocator.Register<Sword>().As<Sword>().As<IWeaponA>();

            var self = ServiceLocator.Get<Sword>();

            Assert.IsNotNull(self);
            Assert.AreSame(self, ServiceLocator.Get<IWeaponA>());
        }

        [Test]
        public void MultipleAs_LockSelfOnlyOnce_AsSelfStillWins()
        {
            ServiceLocator.Register<Sword>()
                .As<IWeaponA>()
                .As<IWeaponB>()
                .AsSelf()
                .As<IWeaponC>();

            var self = ServiceLocator.Get<Sword>();

            Assert.AreSame(self, ServiceLocator.Get<IWeaponA>());
            Assert.AreSame(self, ServiceLocator.Get<IWeaponB>());
            Assert.AreSame(self, ServiceLocator.Get<IWeaponC>());
        }
    }
}
