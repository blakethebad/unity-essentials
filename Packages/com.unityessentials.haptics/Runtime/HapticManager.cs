using System;
using System.Diagnostics;
using UnityEngine;

namespace UnityEssentials.Haptics
{
    /// <summary>
    /// All mutable haptics state plus the platform selection, two-stage lazy initialization and mute
    /// gate that sit between the public API and the active provider. Internal by design:
    /// <see cref="HapticService"/> is the API.
    /// </summary>
    /// <remarks>
    /// Nothing here depends on <see cref="RuntimeInitializeOnLoadMethodAttribute"/> having fired: the
    /// state that needs a non-default starting value carries an inline initializer, so the manager is
    /// equally usable in EditMode, where that attribute never runs. The implicit static constructor
    /// captures <see cref="MainThreadId"/> from the thread that first touches the manager, which in
    /// Unity is the main thread; <see cref="ResetStatics"/> recaptures it. No locks are taken anywhere
    /// — haptics are main-thread only, because the JNI and P/Invoke calls underneath are.
    /// <para>
    /// Initialization is deliberately split in two. <see cref="EnsureProviderSelected"/> only decides
    /// which class handles this platform and costs a single allocation, so the capability queries
    /// <see cref="IsSupported"/> and <see cref="SupportsPatterns"/> can be answered by a menu screen
    /// without waking a vibration motor or starting a haptic engine.
    /// <see cref="EnsureFullyInitialized"/> is the expensive half — it acquires system services,
    /// prepares generators and creates the lifecycle driver — and runs on the first actual dispatch,
    /// or earlier if the consumer opts into pre-warming through <see cref="Initialize"/>.
    /// </para>
    /// <para>
    /// The provider is never branched on. An unsupported device gets <see cref="NullHapticProvider"/>
    /// and every call still routes through it, so there is exactly one code path from
    /// <see cref="HapticService"/> to the platform and unsupported hardware is a normal condition
    /// rather than an error the manager has to special-case.
    /// </para>
    /// </remarks>
    internal static class HapticManager
    {
        /// <summary>Managed id of the thread that owns the haptics package.</summary>
        internal static int MainThreadId = Environment.CurrentManagedThreadId;

        /// <summary>
        /// A provider injected in place of the one <see cref="CreateProvider"/> would build. Null in
        /// every real session; the EditMode tests set it to a call-counting double so lazy
        /// initialization, the mute gate and the driver's once-only creation can be observed without a
        /// device.
        /// </summary>
        /// <remarks>
        /// Only read while selecting the provider, so it must be assigned before the first call that
        /// touches haptics and is ignored afterwards — assigning it mid-session does not swap out a
        /// provider that has already been selected. <see cref="ResetStatics"/> clears it, which is what
        /// keeps one test's double from leaking into the next.
        /// </remarks>
        internal static IHapticProvider ProviderOverride;

        /// <summary>
        /// The hidden GameObject's component that forwards Unity's pause callback to the provider, or
        /// null while no driver exists.
        /// </summary>
        /// <remarks>
        /// Created at most once per session, by the first full initialization, and only when the
        /// selected provider is not <see cref="NullHapticProvider"/> — an unsupported device has
        /// nothing for a pause notification to cancel. Internal rather than private so the tests can
        /// assert that a second dispatch does not create a second driver.
        /// </remarks>
        internal static HapticLifecycleDriver Driver;

        /// <summary>The provider chosen for this platform, or null until the first query or dispatch.</summary>
        private static IHapticProvider _provider;

        /// <summary>
        /// True once <see cref="IHapticProvider.Initialize"/> has run, which is also what makes
        /// <see cref="Stop"/> and <see cref="NotifyApplicationPause"/> safe to forward.
        /// </summary>
        private static bool _fullyInitialized;

        /// <summary>Backing field for <see cref="IsEnabled"/>. Starts enabled, as a fresh session must.</summary>
        private static bool _isEnabled = true;

        /// <summary>
        /// The global mute gate. While false, <see cref="Play(HapticPresetType)"/> and
        /// <see cref="Play(HapticPattern)"/> dispatch nothing; setting it false also stops whatever is
        /// currently playing, so a user muting haptics mid-pattern is not left buzzing.
        /// </summary>
        /// <remarks>
        /// Muting is not initialization: turning the gate off before anything has ever played does not
        /// acquire a provider, and turning it back on does not pre-warm one. Assigning the value it
        /// already holds does nothing at all.
        /// <para>
        /// The setting is deliberately not persisted. This package never touches
        /// <see cref="PlayerPrefs"/> or writes a file, because a library that silently owns a slot in
        /// the consuming project's preferences is a library that fights with its settings menu.
        /// Consumers save the flag wherever the rest of their options live and restore it into this
        /// property on startup.
        /// </para>
        /// </remarks>
        internal static bool IsEnabled
        {
            get { return _isEnabled; }
            set
            {
                if (_isEnabled == value)
                {
                    return;
                }

                _isEnabled = value;

                // Only a provider that has been initialized can have anything playing, and calling
                // Stop on one that has not would drag the whole engine up just to silence silence.
                if (!value && _fullyInitialized)
                {
                    _provider.Stop();
                }
            }
        }

        /// <summary>
        /// Whether this device can play haptics at all. False in the Editor, on desktop players and on
        /// mobile hardware without a vibration motor.
        /// </summary>
        /// <remarks>
        /// Selects the provider if that has not happened yet, but deliberately does not initialize it:
        /// answering "can this device vibrate" must never be the call that starts a haptic engine or
        /// binds a system service, so a settings screen can show or hide its haptics toggle for free.
        /// </remarks>
        internal static bool IsSupported
        {
            get
            {
                EnsureProviderSelected();
                return _provider.IsSupported;
            }
        }

        /// <summary>
        /// Whether this device can play a full custom pattern rather than degrading it to a single
        /// impact. Always false when <see cref="IsSupported"/> is false.
        /// </summary>
        /// <remarks>Selects the provider without initializing it, exactly like <see cref="IsSupported"/>.</remarks>
        internal static bool SupportsPatterns
        {
            get
            {
                EnsureProviderSelected();
                return _provider.SupportsPatterns;
            }
        }

        /// <summary>
        /// Performs the expensive half of initialization now instead of on the first dispatch, so the
        /// cost lands during a loading screen rather than on the frame that wanted the haptic.
        /// </summary>
        /// <remarks>
        /// Entirely optional and idempotent — every dispatch does this anyway if it has not happened
        /// yet. Calling it on an unsupported device is harmless and creates nothing.
        /// </remarks>
        internal static void Initialize()
        {
            EnsureFullyInitialized();
        }

        /// <summary>
        /// Plays one of the three built-in impacts, initializing the backend on the first call.
        /// </summary>
        /// <param name="preset">Which impact strength to play. Unrecognized values are ignored by the provider.</param>
        /// <remarks>
        /// The mute gate is checked before anything else, so a muted session never initializes a
        /// backend it is not going to use. There is nothing to validate — the enum is the contract.
        /// </remarks>
        internal static void Play(HapticPresetType preset)
        {
            if (!_isEnabled)
            {
                return;
            }

            EnsureFullyInitialized();
            _provider.PlayPreset(preset);
        }

        /// <summary>
        /// Plays a custom pattern, replacing anything already playing and initializing the backend on
        /// the first call.
        /// </summary>
        /// <param name="pattern">The pattern to play. Must be non-null, alive and hold at least one finite event.</param>
        /// <remarks>
        /// The order of the four steps is load-bearing. The pattern is checked for null first, because
        /// nothing else can proceed without it. It is then validated first and gated second: a pattern
        /// with no events or with NaN in it is a bug in the caller's code, and
        /// muting haptics must not turn that bug into silence that only reappears when a user turns
        /// the setting back on. Only then does the gate return, and only after that is a backend
        /// initialized — so a muted session stays as cheap as it should be while still reporting bad
        /// data.
        /// <para>
        /// The events array is handed to the provider directly rather than copied: it is already
        /// clamped, finite and sorted, and dispatching a pattern every frame should allocate nothing.
        /// Providers read it and never write to it.
        /// </para>
        /// </remarks>
        /// <exception cref="ArgumentNullException"><paramref name="pattern"/> is null or has been destroyed.</exception>
        /// <exception cref="HapticPatternInvalidException">
        /// <paramref name="pattern"/> has no events, or one of its events carries NaN or infinity.
        /// </exception>
        internal static void Play(HapticPattern pattern)
        {
            // Unity's overloaded comparison, not a reference test: a ScriptableObject that has been
            // Destroyed is a live C# reference wrapping a dead native object, and dispatching one
            // would throw a MissingReferenceException from somewhere far less obvious than here.
            if (pattern == null)
            {
                throw new ArgumentNullException(
                    nameof(pattern),
                    "The haptic pattern to play must not be null. This check also rejects a destroyed " +
                    "HapticPattern ScriptableObject — one that was passed to Object.Destroy after " +
                    "HapticPattern.Create, or whose asset was unloaded — because such an instance is a " +
                    "live reference to an object that no longer exists.");
            }

            // Runs even while muted: invalid data is a programming error, and the mute gate has no
            // business hiding one until the player turns haptics back on.
            pattern.ThrowIfInvalid();

            if (!_isEnabled)
            {
                return;
            }

            EnsureFullyInitialized();
            _provider.PlayPattern(pattern.Events);
        }

        /// <summary>
        /// Cancels whatever is currently playing. Safe at any time, including before anything has ever
        /// been played and on devices that cannot play anything.
        /// </summary>
        /// <remarks>
        /// Stopping before the first dispatch is a no-op that deliberately does not initialize
        /// anything: there is provably nothing to cancel, and a defensive <c>Stop</c> in some
        /// <c>OnDisable</c> must not be what pays for acquiring a vibrator the app never used.
        /// </remarks>
        internal static void Stop()
        {
            if (!_fullyInitialized)
            {
                return;
            }

            _provider.Stop();
        }

        /// <summary>
        /// Forwards the application's pause state to the provider, called by
        /// <see cref="HapticLifecycleDriver"/>.
        /// </summary>
        /// <param name="paused">True when the application is going to the background, false on resume.</param>
        /// <remarks>
        /// Guarded by the initialization flag for the same reason as <see cref="Stop"/>: a provider
        /// that was never initialized has nothing to cancel or restart. In practice the driver only
        /// exists once full initialization has happened, so the guard is belt and braces rather than a
        /// live path — but the manager owns the invariant, not the driver.
        /// </remarks>
        internal static void NotifyApplicationPause(bool paused)
        {
            if (!_fullyInitialized)
            {
                return;
            }

            _provider.OnApplicationPause(paused);
        }

        /// <summary>
        /// Builds the provider for the current platform. Adding a platform is one class implementing
        /// <see cref="IHapticProvider"/> plus one branch here.
        /// </summary>
        /// <returns>A fresh provider instance; never null.</returns>
        /// <remarks>
        /// The <c>!UNITY_EDITOR</c> guards are mandatory, not stylistic. <c>UNITY_IOS</c> and
        /// <c>UNITY_ANDROID</c> are defined inside the Editor whenever that platform is the active
        /// build target, so without them an EditMode test run — or simply pressing Play with Android
        /// selected — would construct a provider that immediately reaches for JNI or a native symbol
        /// that is not linked into the Editor. The Editor therefore always gets
        /// <see cref="NullHapticProvider"/>, and there is a dedicated regression test that says so.
        /// <para>
        /// A fresh instance is returned per call rather than a cached singleton, so that the provider's
        /// lifetime is exactly the manager's session and a reset genuinely starts from nothing.
        /// </para>
        /// </remarks>
        internal static IHapticProvider CreateProvider()
        {
#if UNITY_IOS && !UNITY_EDITOR
            return new IOSHapticProvider();
#elif UNITY_ANDROID && !UNITY_EDITOR
            return new AndroidHapticProvider();
#else
            return new NullHapticProvider();
#endif
        }

        /// <summary>
        /// Hard-resets every static so a domain reload, or an entry into play mode with domain reload
        /// disabled, starts from a clean session.
        /// </summary>
        /// <remarks>
        /// Destroying the driver's GameObject is the part that earns its keep. Entering play mode with
        /// domain reload disabled keeps these statics alive across the transition while Unity destroys
        /// every runtime GameObject on stop, so a stale <see cref="Driver"/> reference would otherwise
        /// survive as a dead Unity object that the next session mistakes for a live driver and never
        /// replaces. The alive test is Unity's overloaded comparison, which answers false for exactly
        /// that case; at genuine <c>SubsystemRegistration</c> time after a reload the previous
        /// session's object is already gone and nothing is destroyed here.
        /// <para>
        /// <see cref="UnityEngine.Object.DestroyImmediate(UnityEngine.Object)"/> rather than
        /// <c>Destroy</c>, because this also runs from EditMode test teardown where there is no frame
        /// loop to process a deferred destruction and the object would linger into the next test.
        /// </para>
        /// </remarks>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        internal static void ResetStatics()
        {
            MainThreadId = Environment.CurrentManagedThreadId;

            if (Driver != null)
            {
                UnityEngine.Object.DestroyImmediate(Driver.gameObject);
            }

            Driver = null;
            _provider = null;
            ProviderOverride = null;
            _fullyInitialized = false;
            _isEnabled = true;
        }

        /// <summary>
        /// Throws if the caller is not on the thread that owns the package. Compiled out of release
        /// builds — it exists only in the Editor and development builds.
        /// </summary>
        /// <exception cref="HapticException">The caller is not on the Unity main thread.</exception>
        [Conditional("UNITY_EDITOR")]
        [Conditional("DEVELOPMENT_BUILD")]
        internal static void AssertMainThread()
        {
            if (Environment.CurrentManagedThreadId != MainThreadId)
            {
                throw new HapticException(
                    "Haptics may only be used from the Unity main thread. " +
                    "Trigger haptics from the main thread and hand background work the data it needs instead. " +
                    "The JNI and P/Invoke calls underneath this API are not thread-safe and crash a release " +
                    "build, where this check no longer exists.");
            }
        }

        /// <summary>
        /// Picks the provider for this platform if that has not happened yet. Cheap by contract: one
        /// allocation and nothing else, so the capability queries can call it freely.
        /// </summary>
        /// <remarks>
        /// <see cref="ProviderOverride"/> wins when set, which is the only seam the tests use to run
        /// the manager's logic without a device.
        /// </remarks>
        private static void EnsureProviderSelected()
        {
            if (_provider == null)
            {
                _provider = ProviderOverride ?? CreateProvider();
            }
        }

        /// <summary>
        /// Brings the package fully up: selects the provider if needed, initializes it exactly once and
        /// creates the lifecycle driver that will forward pause notifications to it.
        /// </summary>
        /// <remarks>
        /// The driver is not created for <see cref="NullHapticProvider"/>. Nothing can be playing on a
        /// device that plays nothing, so a hidden GameObject there would be pure overhead in every
        /// Editor session and every desktop build. The existing-driver test is Unity's overloaded
        /// comparison, so a driver destroyed out from under the manager is rebuilt rather than left as
        /// a dead reference.
        /// <para>
        /// The flag is set last, so a provider whose <see cref="IHapticProvider.Initialize"/> throws
        /// leaves the manager un-initialized and is retried on the next dispatch. Providers are
        /// contractually not allowed to throw, which makes this a safety net rather than a strategy.
        /// </para>
        /// </remarks>
        private static void EnsureFullyInitialized()
        {
            EnsureProviderSelected();

            if (_fullyInitialized)
            {
                return;
            }

            _provider.Initialize();

            if (!(_provider is NullHapticProvider) && Driver == null)
            {
                Driver = HapticLifecycleDriver.Create();
            }

            _fullyInitialized = true;
        }
    }
}
