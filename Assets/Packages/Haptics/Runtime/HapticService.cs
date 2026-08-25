using System;

namespace UnityEssentials.Haptics
{
    /// <summary>
    /// The haptics API: play one of three built-in impacts or an authored <see cref="HapticPattern"/>,
    /// stop playback, mute everything, and ask what the device is capable of. One static entry point,
    /// no component to add, no asset to wire up.
    /// </summary>
    /// <remarks>
    /// Nothing needs initializing. The first <see cref="Play(HapticPresetType)"/> or
    /// <see cref="Play(HapticPattern)"/> selects the platform backend, starts it and creates the hidden
    /// object that keeps it in step with the application's lifecycle; every call after that goes
    /// straight through. <see cref="Initialize"/> exists only to move that one-time cost off the frame
    /// that wanted the haptic and onto a loading screen.
    /// <para>
    /// Unsupported hardware is never an error. The Editor, desktop players and phones without a motor
    /// all resolve to a no-op backend, so haptic calls are safe to leave in code that runs everywhere;
    /// <see cref="IsSupported"/> and <see cref="SupportsPatterns"/> report the difference for callers
    /// that want to adapt their UI. Exceptions are reserved for programming errors — a null or invalid
    /// pattern, or a call from a background thread.
    /// </para>
    /// <para>
    /// Main-thread only. A cheap assertion runs in the Editor and in development builds and is compiled
    /// out of release builds, so an off-thread call that merely throws during development becomes a
    /// crash in a shipped player. Trigger haptics from the main thread.
    /// </para>
    /// </remarks>
    public static class HapticService
    {
        /// <summary>
        /// The global mute gate. While false, both <c>Play</c> overloads do nothing; setting it false
        /// also stops anything currently playing. True by default.
        /// </summary>
        /// <value>False to mute every haptic this package would produce.</value>
        /// <remarks>
        /// This is the property a "Vibration" toggle in an options menu should drive. Muting is not the
        /// same as unsupported hardware: <see cref="IsSupported"/> keeps reporting what the device can
        /// do, so the toggle stays meaningful and can be hidden on devices where it would be a lie.
        /// <para>
        /// The value is deliberately not persisted. This package never touches <c>PlayerPrefs</c> or
        /// writes a file, so save the flag wherever the rest of your options live and assign it back on
        /// startup — a fresh session always starts enabled.
        /// </para>
        /// <para>
        /// Invalid pattern data is still reported while muted: <see cref="Play(HapticPattern)"/>
        /// validates before it consults this gate, because a broken pattern is a bug rather than
        /// something a user preference should hide.
        /// </para>
        /// </remarks>
        /// <exception cref="HapticException">
        /// Set from a thread other than Unity's main thread. Editor and development builds only. The
        /// setter is guarded because disabling can forward a native <c>Stop</c> to the provider; the
        /// getter is a plain field read and is not.
        /// </exception>
        public static bool IsEnabled
        {
            get { return HapticManager.IsEnabled; }
            set
            {
                HapticManager.AssertMainThread();
                HapticManager.IsEnabled = value;
            }
        }

        /// <summary>
        /// Whether this device can play haptics at all. False in the Editor, in desktop players and on
        /// mobile hardware with no vibration motor.
        /// </summary>
        /// <value>True when at least the three presets will actually be felt.</value>
        /// <remarks>
        /// Cheap and side-effect free by design: reading it selects the platform backend but never
        /// starts an engine, binds a system service beyond what the question requires, or creates any
        /// object, so a settings screen can query it every frame if it wants to.
        /// <para>
        /// This is informational, not a guard. Calls on an unsupported device are already no-ops, so
        /// there is no need to wrap <c>Play</c> in a check.
        /// </para>
        /// </remarks>
        /// <exception cref="HapticException">
        /// Called from a thread other than Unity's main thread. Editor and development builds only.
        /// </exception>
        public static bool IsSupported
        {
            get
            {
                HapticManager.AssertMainThread();
                return HapticManager.IsSupported;
            }
        }

        /// <summary>
        /// Whether this device renders a custom <see cref="HapticPattern"/> in full, rather than
        /// degrading it to a single impact. Always false when <see cref="IsSupported"/> is false.
        /// </summary>
        /// <value>True when an authored timeline plays as authored.</value>
        /// <remarks>
        /// What it means per platform. On iOS it requires both iOS 13 or later and CoreHaptics
        /// hardware support: a modern iPhone says true; a device on iOS 10 to 12 says false while
        /// still playing presets — there <see cref="Play(HapticPattern)"/> falls back to one impact
        /// scaled to the pattern's peak intensity rather than going silent; and hardware without a
        /// Taptic Engine at all (an iPad) reports false for <see cref="IsSupported"/> as well on
        /// iOS 13 and later. On Android it is identical to <see cref="IsSupported"/>:
        /// <c>VibrationEffect.createWaveform</c> exists on every device the package supports, so any
        /// Android phone that can vibrate can play a pattern. In the Editor and on desktop it is false,
        /// like everything else.
        /// <para>
        /// True does not promise fidelity. Android ignores <see cref="HapticEvent.Sharpness"/> entirely
        /// and devices without <c>hasAmplitudeControl()</c> flatten every non-zero intensity to their
        /// default strength — the timing plays, the dynamics do not. Reading it is as cheap as
        /// <see cref="IsSupported"/> and starts nothing.
        /// </para>
        /// </remarks>
        /// <exception cref="HapticException">
        /// Called from a thread other than Unity's main thread. Editor and development builds only.
        /// </exception>
        public static bool SupportsPatterns
        {
            get
            {
                HapticManager.AssertMainThread();
                return HapticManager.SupportsPatterns;
            }
        }

        /// <summary>
        /// Pre-warms the backend so the first haptic does not also pay for starting it. Entirely
        /// optional — <c>Play</c> does this itself if you never call it.
        /// </summary>
        /// <remarks>
        /// Call it from a loading screen or a splash sequence, where acquiring the system vibrator or
        /// preparing the iOS impact generators costs nothing anyone will notice. Idempotent, safe on
        /// unsupported devices (where it creates nothing), and safe while
        /// <see cref="IsEnabled"/> is false.
        /// </remarks>
        /// <exception cref="HapticException">
        /// Called from a thread other than Unity's main thread. Editor and development builds only.
        /// </exception>
        public static void Initialize()
        {
            HapticManager.AssertMainThread();
            HapticManager.Initialize();
        }

        /// <summary>
        /// Plays one of the three built-in impacts. This is the everyday call: presets are the only
        /// haptic guaranteed to work on hardware that cannot render custom patterns.
        /// </summary>
        /// <param name="preset">Which impact strength to play.</param>
        /// <remarks>
        /// Fire and forget. Presets are impacts rather than timelines, so they cannot be cancelled once
        /// triggered and <see cref="Stop"/> does not affect them.
        /// <para>
        /// Does nothing while <see cref="IsEnabled"/> is false, and nothing on a device that reports
        /// <see cref="IsSupported"/> as false. The first call initializes the backend.
        /// </para>
        /// </remarks>
        /// <exception cref="HapticException">
        /// Called from a thread other than Unity's main thread. Editor and development builds only.
        /// </exception>
        public static void Play(HapticPresetType preset)
        {
            HapticManager.AssertMainThread();
            HapticManager.Play(preset);
        }

        /// <summary>
        /// Plays a custom pattern, replacing anything already playing.
        /// </summary>
        /// <param name="pattern">
        /// The pattern to play: an authored asset, or one built at runtime with
        /// <see cref="HapticPattern.Create"/>. Must be alive and hold at least one finite event.
        /// </param>
        /// <remarks>
        /// The pattern is validated on every call, including while <see cref="IsEnabled"/> is false, so
        /// a pattern that is empty or carries NaN throws rather than silently doing nothing. Range and
        /// ordering problems are not errors — the asset's own validation clamps intensity and sharpness
        /// into 0 to 1 and sorts events by time as you edit — so only data no backend could ever play
        /// reaches this exception.
        /// <para>
        /// Playback varies by hardware, and the variation is documented rather than hidden. A device
        /// that reports <see cref="SupportsPatterns"/> as false plays a single impact scaled to the
        /// pattern's peak intensity instead of the timeline. Android ignores
        /// <see cref="HapticEvent.Sharpness"/> entirely — it is a CoreHaptics parameter with no
        /// Android equivalent — so a pattern must never rely on sharpness alone to be distinguishable.
        /// </para>
        /// <para>
        /// Overlapping events are legal but their combined feel is explicitly not defined across
        /// platforms: Android flattens the timeline into a single-channel waveform and resolves an
        /// overlap by letting the later event truncate the earlier one, while iOS hands the events to
        /// CoreHaptics, which mixes them. Author patterns without overlap when they have to feel the
        /// same everywhere.
        /// </para>
        /// <para>
        /// Playing the same pattern asset repeatedly allocates nothing. A pattern from
        /// <see cref="HapticPattern.Create"/> belongs to the caller and must be destroyed when finished
        /// with; build one and reuse it rather than creating one per playback.
        /// </para>
        /// </remarks>
        /// <exception cref="ArgumentNullException"><paramref name="pattern"/> is null or has been destroyed.</exception>
        /// <exception cref="HapticPatternInvalidException">
        /// <paramref name="pattern"/> has no events, or one of its events carries NaN or infinity.
        /// </exception>
        /// <exception cref="HapticException">
        /// Called from a thread other than Unity's main thread. Editor and development builds only.
        /// </exception>
        public static void Play(HapticPattern pattern)
        {
            HapticManager.AssertMainThread();
            HapticManager.Play(pattern);
        }

        /// <summary>
        /// Stops the pattern that is currently playing, if any.
        /// </summary>
        /// <remarks>
        /// Safe at any time: with nothing playing, before anything has ever been played, and on devices
        /// that cannot play anything. Calling it before the first <c>Play</c> deliberately does not
        /// initialize the backend, so a defensive stop in <c>OnDisable</c> costs nothing.
        /// <para>
        /// Only pattern playback can be stopped. A preset is an instantaneous impact that has already
        /// happened by the time you could cancel it.
        /// </para>
        /// </remarks>
        /// <exception cref="HapticException">
        /// Called from a thread other than Unity's main thread. Editor and development builds only.
        /// </exception>
        public static void Stop()
        {
            HapticManager.AssertMainThread();
            HapticManager.Stop();
        }
    }
}
