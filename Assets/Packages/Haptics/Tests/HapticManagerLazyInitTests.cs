using System;
using NUnit.Framework;
using UnityEngine;

namespace UnityEssentials.Haptics.Tests
{
    /// <summary>
    /// Covers the two-stage lazy initialization, the mute gate and the pattern validation order that
    /// <see cref="HapticManager"/> sits between <see cref="HapticService"/> and the active provider,
    /// observed through a <see cref="TestHapticProvider"/> installed with
    /// <see cref="HapticManager.ProviderOverride"/>.
    /// </summary>
    /// <remarks>
    /// The calls go through <see cref="HapticService"/> rather than the manager directly, so the facade
    /// is covered by the same run: it is a pass-through plus a main-thread assert, and a test that
    /// bypassed it would leave that pass-through unverified.
    /// <para>
    /// Order matters in <c>SetUp</c>. <see cref="HapticManager.ResetStatics"/> clears the override
    /// along with everything else, so the double is installed after the reset, never before. The same
    /// reset in <c>TearDown</c> is what destroys any lifecycle driver a test caused the manager to
    /// create — without it the hidden GameObject would outlive the test and the next one would find a
    /// driver it did not create.
    /// </para>
    /// </remarks>
    [TestFixture]
    public class HapticManagerLazyInitTests
    {
        /// <summary>The double installed for the current test. Fresh per test, so its counters start at zero.</summary>
        private TestHapticProvider _provider;

        /// <summary>
        /// The eventless pattern used by the muted-validation test, held here so teardown can destroy
        /// it even when the assertion it was built for fails.
        /// </summary>
        private HapticPattern _emptyPattern;

        /// <summary>Resets the manager to a fresh session, then installs the double it will select.</summary>
        [SetUp]
        public void SetUp()
        {
            HapticManager.ResetStatics();
            _provider = new TestHapticProvider();
            HapticManager.ProviderOverride = _provider;
        }

        /// <summary>Resets the manager, destroying any driver it created, and disposes the test pattern.</summary>
        [TearDown]
        public void TearDown()
        {
            HapticManager.ResetStatics();

            if (_emptyPattern != null)
            {
                UnityEngine.Object.DestroyImmediate(_emptyPattern);
                _emptyPattern = null;
            }
        }

        /// <summary>
        /// Asking whether the device can vibrate selects a provider but must never start one — a
        /// settings screen has to be able to read this without waking a motor.
        /// </summary>
        [Test]
        public void IsSupported_SelectsProviderWithoutInitializingIt()
        {
            var supported = HapticService.IsSupported;

            Assert.IsTrue(supported);
            Assert.AreEqual(0, _provider.InitializeCalls);
            Assert.IsNull(HapticManager.Driver);
        }

        /// <summary>The pattern capability query is the same cheap half of initialization as <c>IsSupported</c>.</summary>
        [Test]
        public void SupportsPatterns_SelectsProviderWithoutInitializingIt()
        {
            var supportsPatterns = HapticService.SupportsPatterns;

            Assert.IsTrue(supportsPatterns);
            Assert.AreEqual(0, _provider.InitializeCalls);
            Assert.IsNull(HapticManager.Driver);
        }

        /// <summary>The first dispatch pays for initialization exactly once and still plays the haptic that triggered it.</summary>
        [Test]
        public void FirstPlay_InitializesProviderOnceAndDispatches()
        {
            HapticService.Play(HapticPresetType.Medium);

            Assert.AreEqual(1, _provider.InitializeCalls);
            Assert.AreEqual(1, _provider.PlayPresetCalls);
            Assert.AreEqual(HapticPresetType.Medium, _provider.LastPreset);
        }

        /// <summary>
        /// The hidden lifecycle driver appears with the first dispatch and not before, since a session
        /// that never plays a haptic should carry no extra GameObject.
        /// </summary>
        [Test]
        public void FirstPlay_CreatesTheLifecycleDriver()
        {
            Assert.IsNull(HapticManager.Driver);

            HapticService.Play(HapticPresetType.Medium);

            Assert.IsNotNull(HapticManager.Driver);
        }

        /// <summary>Every dispatch after the first is a straight pass-through with no initialization cost.</summary>
        [Test]
        public void SecondPlay_DoesNotReinitialize()
        {
            HapticService.Play(HapticPresetType.Medium);

            HapticService.Play(HapticPresetType.Heavy);

            Assert.AreEqual(1, _provider.InitializeCalls);
            Assert.AreEqual(2, _provider.PlayPresetCalls);
            Assert.AreEqual(HapticPresetType.Heavy, _provider.LastPreset);
        }

        /// <summary>
        /// The driver is created at most once per session: a second dispatch reuses the instance rather
        /// than accumulating hidden GameObjects.
        /// </summary>
        [Test]
        public void SecondPlay_ReusesTheSameDriver()
        {
            HapticService.Play(HapticPresetType.Medium);
            var driver = HapticManager.Driver;

            HapticService.Play(HapticPresetType.Heavy);

            Assert.AreSame(driver, HapticManager.Driver);
        }

        /// <summary>
        /// Explicit pre-warming does the expensive half of initialization — provider and driver — while
        /// dispatching nothing, which is the whole point of calling it from a loading screen.
        /// </summary>
        [Test]
        public void Initialize_PreWarmsWithoutDispatching()
        {
            HapticService.Initialize();

            Assert.AreEqual(1, _provider.InitializeCalls);
            Assert.AreEqual(0, _provider.PlayPresetCalls);
            Assert.AreEqual(0, _provider.PlayPatternCalls);
            Assert.IsNotNull(HapticManager.Driver);
        }

        /// <summary>A pre-warmed session treats the first real dispatch like any later one.</summary>
        [Test]
        public void PlayAfterInitialize_DoesNotReinitialize()
        {
            HapticService.Initialize();

            HapticService.Play(HapticPresetType.Medium);

            Assert.AreEqual(1, _provider.InitializeCalls);
            Assert.AreEqual(1, _provider.PlayPresetCalls);
        }

        /// <summary>
        /// Muting cancels whatever is playing, so a user who turns haptics off mid-pattern is not left
        /// buzzing — and assigning the value the gate already holds does nothing at all.
        /// </summary>
        [Test]
        public void Disabling_StopsTheProviderExactlyOnce()
        {
            HapticService.Play(HapticPresetType.Medium);

            HapticService.IsEnabled = false;
            HapticService.IsEnabled = false;

            Assert.AreEqual(1, _provider.StopCalls);
        }

        /// <summary>While muted, nothing reaches the provider.</summary>
        [Test]
        public void Disabling_GatesFurtherPlays()
        {
            HapticService.Play(HapticPresetType.Medium);
            HapticService.IsEnabled = false;

            HapticService.Play(HapticPresetType.Heavy);

            Assert.AreEqual(1, _provider.PlayPresetCalls);
            Assert.AreEqual(HapticPresetType.Medium, _provider.LastPreset);
        }

        /// <summary>
        /// Unmuting restores dispatch against the provider that was already initialized, rather than
        /// starting a second session.
        /// </summary>
        [Test]
        public void ReEnabling_RestoresDispatch()
        {
            HapticService.Play(HapticPresetType.Medium);
            HapticService.IsEnabled = false;
            HapticService.Play(HapticPresetType.Heavy);

            HapticService.IsEnabled = true;
            HapticService.Play(HapticPresetType.Heavy);

            Assert.IsTrue(HapticService.IsEnabled);
            Assert.AreEqual(2, _provider.PlayPresetCalls);
            Assert.AreEqual(HapticPresetType.Heavy, _provider.LastPreset);
            Assert.AreEqual(1, _provider.InitializeCalls);
        }

        /// <summary>
        /// A defensive stop in some <c>OnDisable</c> must cost nothing: there is provably nothing to
        /// cancel before the first dispatch, so the call neither throws nor drags a backend up.
        /// </summary>
        [Test]
        public void Stop_BeforeAnyPlay_IsSafeAndInitializesNothing()
        {
            Assert.DoesNotThrow(() => HapticService.Stop());

            Assert.AreEqual(0, _provider.InitializeCalls);
            Assert.AreEqual(0, _provider.StopCalls);
            Assert.IsNull(HapticManager.Driver);
        }

        /// <summary>A missing pattern is a programming error, reported before anything is initialized.</summary>
        [Test]
        public void Play_NullPattern_Throws()
        {
            Assert.Throws<ArgumentNullException>(() => HapticService.Play((HapticPattern)null));

            Assert.AreEqual(0, _provider.InitializeCalls);
            Assert.AreEqual(0, _provider.PlayPatternCalls);
        }

        /// <summary>
        /// A destroyed pattern is a live C# reference wrapping a dead native object, and the null check
        /// uses Unity's overloaded comparison specifically so it is caught here rather than as a
        /// <c>MissingReferenceException</c> somewhere further down.
        /// </summary>
        [Test]
        public void Play_DestroyedPattern_Throws()
        {
            var pattern = HapticPattern.Create(new[] { new HapticEvent(0f, 1f) });
            UnityEngine.Object.DestroyImmediate(pattern);

            Assert.Throws<ArgumentNullException>(() => HapticService.Play(pattern));

            Assert.AreEqual(0, _provider.PlayPatternCalls);
        }

        /// <summary>
        /// Validation runs ahead of the mute gate, so muting haptics never turns broken pattern data
        /// into silence that only reappears when the player switches the setting back on.
        /// </summary>
        [Test]
        public void Play_InvalidPatternWhileMuted_StillThrows()
        {
            _emptyPattern = ScriptableObject.CreateInstance<HapticPattern>();
            HapticService.IsEnabled = false;

            Assert.Throws<HapticPatternInvalidException>(() => HapticService.Play(_emptyPattern));

            Assert.AreEqual(0, _provider.InitializeCalls);
            Assert.AreEqual(0, _provider.PlayPatternCalls);
        }
    }
}
