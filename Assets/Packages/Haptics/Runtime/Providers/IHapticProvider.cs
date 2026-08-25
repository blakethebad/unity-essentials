namespace UnityEssentials.Haptics
{
    /// <summary>
    /// The platform abstraction every haptic backend implements. One provider is selected by
    /// <c>HapticManager</c> at first use and owns every native call the package makes from then on.
    /// </summary>
    /// <remarks>
    /// The contract is deliberately narrow so adding a platform is one class plus one branch in the
    /// manager's selection switch.
    /// <para>
    /// Lifecycle. The manager constructs a provider, may read <see cref="IsSupported"/> and
    /// <see cref="SupportsPatterns"/> immediately, then calls <see cref="Initialize"/> exactly once
    /// before the first dispatch. Because the capability getters power a cheap pre-initialization
    /// query, they MUST be callable before <see cref="Initialize"/> and must never start an engine,
    /// allocate generators or spin up threads — the most they may do is lazily acquire a system
    /// service handle needed to answer the question. Everything expensive belongs in
    /// <see cref="Initialize"/>. After that, <see cref="PlayPreset"/>, <see cref="PlayPattern"/>,
    /// <see cref="Stop"/> and <see cref="OnApplicationPause"/> may be called in any order and at any
    /// time, including <see cref="Stop"/> with nothing playing.
    /// </para>
    /// <para>
    /// Threading and failure. Every member is called on Unity's main thread. No member may let an
    /// exception escape to the caller: JNI, P/Invoke and Objective-C boundaries must be wrapped and
    /// their failures logged and swallowed, because a haptic is cosmetic and must never take down the
    /// frame that requested it.
    /// </para>
    /// </remarks>
    internal interface IHapticProvider
    {
        /// <summary>
        /// Whether this device can play anything at all. False means every dispatch is a no-op, and
        /// the manager still routes calls here rather than branching on the flag.
        /// </summary>
        /// <remarks>Readable before <see cref="Initialize"/> and must not start anything.</remarks>
        bool IsSupported { get; }

        /// <summary>
        /// Whether this device can play a full custom pattern. False on hardware that has presets but
        /// no pattern engine (an iOS device without CoreHaptics support), where
        /// <see cref="PlayPattern"/> degrades to a single impact scaled to the pattern's peak
        /// intensity instead of failing.
        /// </summary>
        /// <remarks>
        /// Readable before <see cref="Initialize"/> and must not start anything. Always false when
        /// <see cref="IsSupported"/> is false.
        /// </remarks>
        bool SupportsPatterns { get; }

        /// <summary>
        /// Acquires whatever the backend needs to play — system services, cached feedback generators,
        /// the pattern engine, cached platform version numbers.
        /// </summary>
        /// <remarks>
        /// Called exactly once by the manager, before the first dispatch. Implementations need no
        /// re-entrancy guard of their own, but must leave the provider usable (as a no-op if
        /// necessary) when initialization fails.
        /// </remarks>
        void Initialize();

        /// <summary>
        /// Plays one of the three built-in impacts.
        /// </summary>
        /// <param name="preset">Which impact strength to play.</param>
        /// <remarks>
        /// Implementations must switch explicitly on the value and ignore anything unrecognized
        /// rather than casting it blindly into a native enumeration.
        /// </remarks>
        void PlayPreset(HapticPresetType preset);

        /// <summary>
        /// Plays a custom pattern, replacing anything currently playing.
        /// </summary>
        /// <param name="events">
        /// The pattern's events. The manager guarantees the array is non-null, non-empty, free of NaN
        /// and infinity, clamped to the documented ranges, and stably sorted by
        /// <see cref="HapticEvent.Time"/>, so providers revalidate nothing.
        /// </param>
        /// <remarks>
        /// The array belongs to the pattern asset that supplied it: providers must read it and must
        /// never sort, clamp or otherwise write into it. A provider that is supported but reports
        /// <see cref="SupportsPatterns"/> as false degrades here to a single impact scaled to the
        /// pattern's peak intensity rather than doing nothing silently; a provider that is not
        /// supported at all does nothing.
        /// </remarks>
        void PlayPattern(HapticEvent[] events);

        /// <summary>
        /// Cancels anything currently playing. Safe to call at any time after
        /// <see cref="Initialize"/>, including when nothing is playing.
        /// </summary>
        void Stop();

        /// <summary>
        /// Notifies the backend that the application was backgrounded or resumed, forwarded from the
        /// package's lifecycle driver.
        /// </summary>
        /// <param name="paused">True when the application is going to the background, false on resume.</param>
        /// <remarks>
        /// Backgrounding does not stop playback by itself on every platform — an Android waveform
        /// keeps vibrating — so implementations cancel on pause. Backends whose engine is torn down by
        /// the system while suspended restart it on resume. Safe to call at any time after
        /// <see cref="Initialize"/>, and may arrive repeatedly with the same value.
        /// </remarks>
        void OnApplicationPause(bool paused);
    }
}
