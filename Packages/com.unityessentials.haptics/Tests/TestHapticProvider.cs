namespace UnityEssentials.Haptics.Tests
{
    /// <summary>
    /// An <see cref="IHapticProvider"/> that plays nothing and records everything: how many times each
    /// member was called, and what the last call carried. It is the only way the manager's lazy
    /// initialization, mute gate and driver lifetime can be observed on a machine that has no
    /// vibration motor and no platform backend.
    /// </summary>
    /// <remarks>
    /// Installed through <see cref="HapticManager.ProviderOverride"/> before the manager selects a
    /// provider, which in a fixture means immediately after <see cref="HapticManager.ResetStatics"/> in
    /// <c>SetUp</c>. The override is consulted exactly once, on the first capability query or dispatch,
    /// so assigning it later in a test would not swap out an already-selected provider; the same
    /// <c>ResetStatics</c> call clears it again in <c>TearDown</c>, which is what stops one test's
    /// double from leaking into the next.
    /// <para>
    /// The counters are plain fields and there is no reset method: every test constructs its own
    /// instance, so a stale count is impossible by construction rather than by discipline.
    /// </para>
    /// <para>
    /// Deliberately not a <see cref="NullHapticProvider"/> and not derived from one. The manager skips
    /// creating its lifecycle driver for exactly that type, so a double related to it could never
    /// exercise the driver's once-only creation.
    /// </para>
    /// <para>
    /// Nothing here throws, matching the provider contract. A test that wants to observe a failing
    /// backend is testing the manager's safety net, not this double.
    /// </para>
    /// </remarks>
    internal sealed class TestHapticProvider : IHapticProvider
    {
        /// <summary>How many times <see cref="Initialize"/> has been called on this instance.</summary>
        /// <remarks>
        /// The central assertion of the lazy-initialization fixture: zero after a capability query,
        /// one after any number of dispatches.
        /// </remarks>
        public int InitializeCalls;

        /// <summary>How many times <see cref="PlayPreset"/> has been called on this instance.</summary>
        public int PlayPresetCalls;

        /// <summary>The preset handed to the most recent <see cref="PlayPreset"/> call.</summary>
        /// <remarks>
        /// Meaningless until <see cref="PlayPresetCalls"/> is non-zero, because the default value of
        /// the enum is <see cref="HapticPresetType.Light"/> rather than a sentinel. Tests that assert
        /// on it use <see cref="HapticPresetType.Medium"/> or <see cref="HapticPresetType.Heavy"/> so
        /// that a dispatch which never happened cannot be mistaken for one that did.
        /// </remarks>
        public HapticPresetType LastPreset;

        /// <summary>How many times <see cref="PlayPattern"/> has been called on this instance.</summary>
        public int PlayPatternCalls;

        /// <summary>The array handed to the most recent <see cref="PlayPattern"/> call, or null before the first.</summary>
        /// <remarks>
        /// Stored by reference rather than copied, which is what lets a test assert that the manager
        /// passes the pattern's own array straight through instead of allocating a copy per dispatch.
        /// Like every provider, this one never writes to it.
        /// </remarks>
        public HapticEvent[] LastEvents;

        /// <summary>How many times <see cref="Stop"/> has been called on this instance.</summary>
        /// <remarks>
        /// Counts only forwarded stops. The manager swallows a <c>Stop</c> issued before the first
        /// dispatch, so this staying at zero is a meaningful assertion in its own right.
        /// </remarks>
        public int StopCalls;

        /// <summary>How many times <see cref="OnApplicationPause"/> has been called on this instance.</summary>
        public int PauseCalls;

        /// <summary>The value handed to the most recent <see cref="OnApplicationPause"/> call.</summary>
        /// <remarks>
        /// Meaningless until <see cref="PauseCalls"/> is non-zero: false is both the default and a
        /// legitimate value, so the count has to be checked alongside it.
        /// </remarks>
        public bool LastPauseValue;

        /// <summary>
        /// Whether this double claims the device can play anything. Settable so a test can drive the
        /// manager down its unsupported path; true by default, because the interesting behaviour to
        /// observe is what a working backend does.
        /// </summary>
        /// <remarks>
        /// True here is also what distinguishes the double from the <see cref="NullHapticProvider"/>
        /// the Editor would otherwise select, so a fixture can prove the override took effect by
        /// reading <c>HapticService.IsSupported</c> and finding it true.
        /// </remarks>
        public bool IsSupported { get; set; } = true;

        /// <summary>
        /// Whether this double claims the device renders a full custom pattern. Settable so a test can
        /// drive the degraded path; true by default.
        /// </summary>
        /// <remarks>
        /// The interface requires this to be false whenever <see cref="IsSupported"/> is false. The
        /// double does not enforce that pairing — a test that clears one flag is expected to clear
        /// both when it wants a coherent device.
        /// </remarks>
        public bool SupportsPatterns { get; set; } = true;

        /// <summary>Records the call. Acquires nothing, because there is nothing to acquire.</summary>
        public void Initialize()
        {
            InitializeCalls++;
        }

        /// <summary>Records the call and the preset it carried.</summary>
        /// <param name="preset">The impact strength the manager dispatched.</param>
        public void PlayPreset(HapticPresetType preset)
        {
            PlayPresetCalls++;
            LastPreset = preset;
        }

        /// <summary>Records the call and keeps a reference to the array it carried.</summary>
        /// <param name="events">The pattern's own events, retained by reference and never written to.</param>
        public void PlayPattern(HapticEvent[] events)
        {
            PlayPatternCalls++;
            LastEvents = events;
        }

        /// <summary>Records the call.</summary>
        public void Stop()
        {
            StopCalls++;
        }

        /// <summary>Records the call and the pause state it carried.</summary>
        /// <param name="paused">True when the application is going to the background, false on resume.</param>
        public void OnApplicationPause(bool paused)
        {
            PauseCalls++;
            LastPauseValue = paused;
        }
    }
}
