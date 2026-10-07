using NUnit.Framework;
using UnityEngine;

namespace UnityEssentials.Haptics.Tests
{
    /// <summary>
    /// Covers the hidden lifecycle driver: how <see cref="HapticLifecycleDriver.Create"/> configures
    /// its host GameObject, and how <see cref="HapticManager.NotifyApplicationPause"/> routes the
    /// application's pause state to the active provider.
    /// </summary>
    /// <remarks>
    /// Everything here is asserted at the manager's level rather than by pausing the application.
    /// EditMode runs without a player loop, so Unity never delivers <c>OnApplicationPause</c> to a
    /// component and the driver's own message body cannot be triggered from a test. That body is one
    /// forwarding call with no logic of its own; what is worth verifying is the manager's end of it,
    /// which is exactly what these tests drive.
    /// <para>
    /// The driver built directly by <c>Create</c> is not the manager's — the manager never learns about
    /// it — so <see cref="HapticManager.ResetStatics"/> will not clean it up and that test destroys its
    /// GameObject itself, in a <c>finally</c> so a failed assertion cannot leak a hidden object into the
    /// rest of the run.
    /// </para>
    /// </remarks>
    [TestFixture]
    public class HapticLifecycleDriverTests
    {
        /// <summary>The double installed for the current test. Fresh per test, so its counters start at zero.</summary>
        private TestHapticProvider _provider;

        /// <summary>Resets the manager to a fresh session, then installs the double it will select.</summary>
        [SetUp]
        public void SetUp()
        {
            HapticManager.ResetStatics();
            _provider = new TestHapticProvider();
            HapticManager.ProviderOverride = _provider;
        }

        /// <summary>Resets the manager, destroying any driver it created during the test.</summary>
        [TearDown]
        public void TearDown() => HapticManager.ResetStatics();

        /// <summary>
        /// The host object stays out of the hierarchy window and out of saved scenes: a library that
        /// serialized its own plumbing into a consumer's scene would be a bug reported as a mystery
        /// GameObject.
        /// </summary>
        [Test]
        public void Create_HidesAndDoesNotSaveItsGameObject()
        {
            var driver = HapticLifecycleDriver.Create();

            try
            {
                Assert.IsNotNull(driver);
                Assert.AreEqual(HideFlags.HideAndDontSave, driver.gameObject.hideFlags);
                Assert.AreEqual(HapticLifecycleDriver.GameObjectName, driver.gameObject.name);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(driver.gameObject);
            }
        }

        /// <summary>
        /// Backgrounding has to reach the provider, because an Android waveform keeps vibrating after
        /// the process loses focus and the iOS engine needs stopping before the system suspends it.
        /// </summary>
        [Test]
        public void NotifyApplicationPause_AfterFirstPlay_ForwardsPauseToProvider()
        {
            HapticService.Play(HapticPresetType.Medium);

            HapticManager.NotifyApplicationPause(true);

            Assert.AreEqual(1, _provider.PauseCalls);
            Assert.IsTrue(_provider.LastPauseValue);
        }

        /// <summary>
        /// Resuming is forwarded as its own notification rather than inferred, since a backend whose
        /// engine the system tore down while suspended restarts it here.
        /// </summary>
        [Test]
        public void NotifyApplicationPause_AfterFirstPlay_ForwardsResumeToProvider()
        {
            HapticService.Play(HapticPresetType.Medium);
            HapticManager.NotifyApplicationPause(true);

            HapticManager.NotifyApplicationPause(false);

            Assert.AreEqual(2, _provider.PauseCalls);
            Assert.IsFalse(_provider.LastPauseValue);
        }

        /// <summary>
        /// A pause that arrives before anything has been played is dropped rather than used as a reason
        /// to initialize: an uninitialized provider has nothing to cancel or restart, and backgrounding
        /// an app that never asked for a haptic must not be what acquires a vibrator.
        /// </summary>
        [Test]
        public void NotifyApplicationPause_BeforeInitialization_DoesNothing()
        {
            Assert.DoesNotThrow(() => HapticManager.NotifyApplicationPause(true));

            Assert.AreEqual(0, _provider.PauseCalls);
            Assert.AreEqual(0, _provider.InitializeCalls);
            Assert.IsNull(HapticManager.Driver);
        }
    }
}
