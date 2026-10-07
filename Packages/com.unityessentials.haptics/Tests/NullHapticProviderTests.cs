using NUnit.Framework;

namespace UnityEssentials.Haptics.Tests
{
    /// <summary>
    /// Covers <see cref="NullHapticProvider"/>, the fallback the manager selects in the Editor, in
    /// desktop players and on any platform without a backend. Two things are asserted: both
    /// capability flags are false, and every member of <see cref="IHapticProvider"/> is a no-op that
    /// can be called in any order — before initialization, after it, and repeatedly — without
    /// throwing.
    /// </summary>
    /// <remarks>
    /// The provider holds no state and touches no engine, so there is nothing to tear down: each test
    /// builds its own instance in <see cref="SetUp"/> and lets it fall out of scope. Nothing here
    /// constructs a Unity object, which keeps the fixture runnable outside the Editor as well as
    /// inside the EditMode runner.
    /// </remarks>
    [TestFixture]
    public class NullHapticProviderTests
    {
        /// <summary>A fresh, never-initialized provider, rebuilt before every test.</summary>
        private NullHapticProvider _provider;

        /// <summary>Small three-event pattern payload used by the dispatch tests.</summary>
        private HapticEvent[] _events;

        [SetUp]
        public void SetUp()
        {
            _provider = new NullHapticProvider();
            _events = new[]
            {
                new HapticEvent(0f, 1f, 0.5f, 0f),
                new HapticEvent(0.1f, 0.5f, 0f, 0.2f),
                new HapticEvent(0.4f, 0f, 1f, 0.05f)
            };
        }

        [Test]
        public void IsSupported_IsFalse()
        {
            Assert.IsFalse(_provider.IsSupported);
        }

        [Test]
        public void SupportsPatterns_IsFalse()
        {
            Assert.IsFalse(_provider.SupportsPatterns);
        }

        [Test]
        public void CapabilityFlags_AreReadableBeforeInitialize()
        {
            // The interface contract requires both getters to answer without an Initialize() first;
            // the manager reads them from IsSupported/SupportsPatterns without ever initializing.
            Assert.IsFalse(_provider.IsSupported);
            Assert.IsFalse(_provider.SupportsPatterns);

            _provider.Initialize();

            Assert.IsFalse(_provider.IsSupported);
            Assert.IsFalse(_provider.SupportsPatterns);
        }

        [Test]
        public void Initialize_DoesNotThrow()
        {
            Assert.DoesNotThrow(() => _provider.Initialize());
        }

        [Test]
        public void Initialize_IsRepeatable()
        {
            Assert.DoesNotThrow(() =>
            {
                _provider.Initialize();
                _provider.Initialize();
                _provider.Initialize();
            });
        }

        [Test]
        public void PlayPreset_DoesNotThrow_ForEveryPreset()
        {
            _provider.Initialize();

            Assert.DoesNotThrow(() =>
            {
                _provider.PlayPreset(HapticPresetType.Light);
                _provider.PlayPreset(HapticPresetType.Medium);
                _provider.PlayPreset(HapticPresetType.Heavy);
            });
        }

        [Test]
        public void PlayPreset_DoesNotThrow_BeforeInitialize()
        {
            Assert.DoesNotThrow(() =>
            {
                _provider.PlayPreset(HapticPresetType.Light);
                _provider.PlayPreset(HapticPresetType.Medium);
                _provider.PlayPreset(HapticPresetType.Heavy);
            });
        }

        [Test]
        public void PlayPattern_DoesNotThrow()
        {
            _provider.Initialize();

            Assert.DoesNotThrow(() => _provider.PlayPattern(_events));
        }

        [Test]
        public void PlayPattern_DoesNotThrow_BeforeInitialize()
        {
            Assert.DoesNotThrow(() => _provider.PlayPattern(_events));
        }

        [Test]
        public void PlayPattern_DoesNotWriteIntoTheEventArray()
        {
            // The array belongs to the pattern asset that supplied it, so no provider may sort,
            // clamp or otherwise modify it in place.
            var expected = (HapticEvent[])_events.Clone();

            _provider.PlayPattern(_events);

            Assert.AreEqual(expected.Length, _events.Length);
            for (var i = 0; i < expected.Length; i++)
            {
                Assert.AreEqual(expected[i].Time, _events[i].Time);
                Assert.AreEqual(expected[i].Intensity, _events[i].Intensity);
                Assert.AreEqual(expected[i].Sharpness, _events[i].Sharpness);
                Assert.AreEqual(expected[i].Duration, _events[i].Duration);
            }
        }

        [Test]
        public void Stop_DoesNotThrow_BeforeInitialize()
        {
            Assert.DoesNotThrow(() => _provider.Stop());
        }

        [Test]
        public void Stop_DoesNotThrow_WithNothingPlaying()
        {
            _provider.Initialize();

            Assert.DoesNotThrow(() =>
            {
                _provider.Stop();
                _provider.Stop();
            });
        }

        [Test]
        public void OnApplicationPause_DoesNotThrow_OnBothTransitions()
        {
            _provider.Initialize();

            Assert.DoesNotThrow(() =>
            {
                _provider.OnApplicationPause(true);
                _provider.OnApplicationPause(false);
            });
        }

        [Test]
        public void OnApplicationPause_DoesNotThrow_OnRepeatedSameValue()
        {
            // Mobile platforms can deliver the same pause state twice; a provider must tolerate it.
            _provider.Initialize();

            Assert.DoesNotThrow(() =>
            {
                _provider.OnApplicationPause(true);
                _provider.OnApplicationPause(true);
                _provider.OnApplicationPause(false);
                _provider.OnApplicationPause(false);
            });
        }

        [Test]
        public void EveryMember_IsCallableInAnyOrder()
        {
            // Deliberately scrambled: dispatch and lifecycle calls before Initialize, then after it.
            Assert.DoesNotThrow(() =>
            {
                _provider.Stop();
                _provider.OnApplicationPause(true);
                _provider.PlayPreset(HapticPresetType.Heavy);
                _provider.PlayPattern(_events);
                _provider.Initialize();
                _provider.OnApplicationPause(false);
                _provider.PlayPreset(HapticPresetType.Light);
                _provider.Stop();
            });

            Assert.IsFalse(_provider.IsSupported);
            Assert.IsFalse(_provider.SupportsPatterns);
        }
    }
}
