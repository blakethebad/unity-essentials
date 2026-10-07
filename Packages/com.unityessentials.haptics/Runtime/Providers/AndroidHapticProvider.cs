#if UNITY_ANDROID
using System;
using UnityEngine;

namespace UnityEssentials.Haptics
{
    /// <summary>
    /// The Android backend, driving <c>android.os.Vibrator</c> through JNI reflection. Presets map to
    /// predefined <c>VibrationEffect</c>s where the platform has them, and custom patterns become a
    /// single <c>createWaveform</c> effect built by <see cref="AndroidWaveformConverter"/>.
    /// </summary>
    /// <remarks>
    /// Every JNI call site in the package lives in this class and nowhere else, so the timeline
    /// translation stays pure and testable and there is exactly one file to audit when Android's
    /// vibration API changes. The whole file is wrapped in <c>#if UNITY_ANDROID</c> because
    /// <c>AndroidJavaClass</c> and <c>AndroidJavaObject</c> only exist when the Android module
    /// compiles; on every other target it compiles to nothing, which is why the manager's selection
    /// branch is guarded by the same symbol.
    /// <para>
    /// Nothing is bundled with the package: <c>Vibrator</c>, <c>VibratorManager</c> and
    /// <c>VibrationEffect</c> are platform classes reached by name, so there is no native plugin to
    /// build or ship. The <c>VIBRATE</c> permission is injected into the generated Gradle manifest at
    /// build time — Unity's automatic permission detection only recognizes <c>Handheld.Vibrate</c>
    /// and never sees JNI reflection.
    /// </para>
    /// <para>
    /// Acquisition and lifetime. The vibrator handle and <c>Build.VERSION.SDK_INT</c> are resolved
    /// once, lazily, and then cached: the capability getters must answer before
    /// <see cref="Initialize"/> is ever called, so they trigger the same acquisition rather than
    /// duplicating it, and acquisition is the heaviest thing they are permitted to do.
    /// <see cref="Initialize"/> adds only the pre-warm the manager pays for up front — resolving the
    /// predefined effect ids — so the first <see cref="PlayPreset"/> makes no extra reflection calls.
    /// Every entry point is idempotent and every lazy step remembers that it was attempted, so a
    /// failure is not retried on every frame.
    /// </para>
    /// <para>
    /// Failure policy. A haptic is cosmetic, so no JNI failure may escape into the caller's frame:
    /// every boundary is wrapped, failures are logged once through
    /// <see cref="Debug.LogWarning(object)"/> with a <c>[Haptics]</c> prefix, and the provider
    /// degrades to a no-op. Intermediate Java objects — the activity, the vibrator manager, each
    /// effect — are disposed deterministically through <c>using</c> rather than left to the finalizer
    /// thread; only the long-lived vibrator handle and the <c>VibrationEffect</c> class are retained.
    /// </para>
    /// <para>
    /// Minimum API. The project's minimum SDK is 26, where <c>vibrate(VibrationEffect)</c>,
    /// <c>createOneShot</c> and <c>createWaveform</c> all exist, so the legacy pre-Oreo
    /// <c>vibrate(long)</c> and <c>vibrate(long[], int)</c> paths are deliberately omitted. Devices
    /// whose motor reports no <c>hasAmplitudeControl()</c> coerce every non-zero amplitude to their
    /// default strength: a documented fidelity caveat, not a case this class branches on, since the
    /// waveform's timing still plays correctly and only its dynamics are lost.
    /// </para>
    /// </remarks>
    internal sealed class AndroidHapticProvider : IHapticProvider
    {
        /// <summary>Prefix every log line from this provider carries, so device logs are greppable.</summary>
        private const string LogPrefix = "[Haptics] ";

        /// <summary>
        /// <c>Context.VIBRATOR_SERVICE</c>. A stable platform constant, so the literal is used rather
        /// than a reflection round-trip to read a string that has never changed.
        /// </summary>
        private const string VibratorServiceName = "vibrator";

        /// <summary>
        /// <c>Context.VIBRATOR_MANAGER_SERVICE</c>, added in API 31 and equally stable. Reading it
        /// reflectively would fail on the older devices that need the other name anyway.
        /// </summary>
        private const string VibratorManagerServiceName = "vibrator_manager";

        /// <summary>The Java class every effect factory used here is static on.</summary>
        private const string VibrationEffectClassName = "android.os.VibrationEffect";

        /// <summary>API 29 (Android 10) is where <c>createPredefined</c> and the effect ids arrive.</summary>
        private const int ApiLevelPredefinedEffects = 29;

        /// <summary>API 31 (Android 12) is where <c>Vibrator</c> is obtained through <c>VibratorManager</c>.</summary>
        private const int ApiLevelVibratorManager = 31;

        /// <summary>The cached <c>android.os.Vibrator</c>, or null when it could not be acquired.</summary>
        private AndroidJavaObject _vibrator;

        /// <summary>The cached <c>android.os.VibrationEffect</c> class, or null when it could not be resolved.</summary>
        private AndroidJavaClass _vibrationEffectClass;

        /// <summary>Cached <c>Build.VERSION.SDK_INT</c>; 0 until acquisition has run.</summary>
        private int _sdkInt;

        /// <summary>Cached <c>hasVibrator()</c>. The hardware cannot change under a running process.</summary>
        private bool _hasVibrator;

        /// <summary>True once acquisition has run, successfully or not, so it is never retried.</summary>
        private bool _vibratorAcquisitionAttempted;

        /// <summary>True once the effect class lookup has run, successfully or not.</summary>
        private bool _vibrationEffectClassAttempted;

        /// <summary>True once the predefined effect id lookup has run, successfully or not.</summary>
        private bool _predefinedEffectsAttempted;

        /// <summary>True only when all three predefined effect ids were read successfully.</summary>
        private bool _predefinedEffectsResolved;

        /// <summary>Cached <c>VibrationEffect.EFFECT_TICK</c>, valid only when <see cref="_predefinedEffectsResolved"/>.</summary>
        private int _effectTick;

        /// <summary>Cached <c>VibrationEffect.EFFECT_CLICK</c>, valid only when <see cref="_predefinedEffectsResolved"/>.</summary>
        private int _effectClick;

        /// <summary>Cached <c>VibrationEffect.EFFECT_HEAVY_CLICK</c>, valid only when <see cref="_predefinedEffectsResolved"/>.</summary>
        private int _effectHeavyClick;

        /// <summary>
        /// Whether the device has a vibrator: true when the system service resolved and
        /// <c>hasVibrator()</c> reported hardware.
        /// </summary>
        /// <remarks>
        /// Readable before <see cref="Initialize"/>. The first read acquires and caches the vibrator
        /// handle — the one piece of work a capability getter is allowed to do — and every read after
        /// that is a field test.
        /// </remarks>
        public bool IsSupported
        {
            get
            {
                EnsureVibrator();
                return _vibrator != null && _hasVibrator;
            }
        }

        /// <summary>
        /// Identical to <see cref="IsSupported"/>: any Android device that can vibrate at all can play
        /// a waveform, so there is no preset-only tier here and no degraded pattern path.
        /// </summary>
        /// <remarks>
        /// <c>VibrationEffect.createWaveform</c> exists from API 26 and the project's minimum SDK is
        /// 26, so the pattern capability never varies with the platform version — which is also why
        /// the legacy pre-Oreo <c>vibrate(long[], int)</c> fallback is deliberately not implemented.
        /// Readable before <see cref="Initialize"/>, with the same lazy acquisition.
        /// </remarks>
        public bool SupportsPatterns
        {
            get { return IsSupported; }
        }

        /// <summary>
        /// Acquires the vibrator and <c>SDK_INT</c> if a capability query has not already done so, and
        /// pre-reads the predefined effect ids so the first preset costs no extra reflection.
        /// </summary>
        /// <remarks>
        /// Called once by the manager before the first dispatch, and safe to call again: both steps
        /// are latched. Failure of either leaves the provider usable as a no-op rather than throwing —
        /// an unusable vibrator is reported through <see cref="IsSupported"/>, and unreadable effect
        /// ids simply route presets through the one-shot fallback.
        /// </remarks>
        public void Initialize()
        {
            EnsureVibrator();
            EnsurePredefinedEffects();
        }

        /// <summary>
        /// Plays one of the three built-in impacts: a predefined <c>VibrationEffect</c> on API 29 and
        /// above, otherwise a fixed one-shot pulse.
        /// </summary>
        /// <param name="preset">Which impact strength to play. Unrecognized values are ignored.</param>
        /// <remarks>
        /// The one-shot fallback covers API 26 to 28, which has no predefined effects, with hand-picked
        /// duration and amplitude pairs — Light 40 ms at 80, Medium 60 ms at 150, Heavy 90 ms at 255.
        /// Those are starting values chosen to keep the three tiers distinguishable, not measured ones;
        /// they are expected to be feel-tuned against real hardware, and tuning them changes nothing
        /// else in the package.
        /// </remarks>
        public void PlayPreset(HapticPresetType preset)
        {
            EnsureVibrator();
            if (_vibrator == null || !_hasVibrator)
            {
                return;
            }

            EnsurePredefinedEffects();

            try
            {
                using (var effect = CreatePresetEffect(preset))
                {
                    if (effect == null)
                    {
                        return;
                    }

                    _vibrator.Call("vibrate", effect);
                }
            }
            catch (Exception exception)
            {
                LogFailure("play the " + preset + " preset", exception);
            }
        }

        /// <summary>
        /// Plays a custom pattern as a single waveform effect, replacing anything already playing.
        /// </summary>
        /// <param name="events">
        /// The pattern's events, already validated, clamped and sorted by the manager. Read only —
        /// the array belongs to the pattern asset.
        /// </param>
        /// <remarks>
        /// The timeline is flattened by <see cref="AndroidWaveformConverter"/> and handed to
        /// <c>createWaveform(timings, amplitudes, -1)</c>; the <c>-1</c> repeat index means play once
        /// and stop, since looping is not part of this version. Issuing a new effect implicitly
        /// cancels the previous one, so no explicit stop is needed first.
        /// </remarks>
        public void PlayPattern(HapticEvent[] events)
        {
            EnsureVibrator();
            if (_vibrator == null || !_hasVibrator)
            {
                return;
            }

            var effectClass = GetVibrationEffectClass();
            if (effectClass == null)
            {
                return;
            }

            try
            {
                long[] timings;
                int[] amplitudes;
                AndroidWaveformConverter.Convert(events, out timings, out amplitudes);

                // createWaveform rejects an empty waveform, and an empty pattern is not something the
                // manager lets through — but a defensive check is cheaper than a Java exception.
                if (timings.Length == 0)
                {
                    return;
                }

                using (var effect = effectClass.CallStatic<AndroidJavaObject>("createWaveform", timings, amplitudes, -1))
                {
                    if (effect == null)
                    {
                        return;
                    }

                    _vibrator.Call("vibrate", effect);
                }
            }
            catch (Exception exception)
            {
                LogFailure("play a custom pattern", exception);
            }
        }

        /// <summary>
        /// Cancels whatever is playing through <c>Vibrator.cancel()</c>. Safe with nothing playing and
        /// safe before anything was ever acquired, in which case it does nothing.
        /// </summary>
        /// <remarks>
        /// Unlike the dispatch methods this deliberately does not acquire the vibrator: stopping a
        /// device that has never played anything has nothing to stop, and forcing a system-service
        /// lookup for it would be work with no possible effect.
        /// </remarks>
        public void Stop()
        {
            if (_vibrator == null)
            {
                return;
            }

            try
            {
                _vibrator.Call("cancel");
            }
            catch (Exception exception)
            {
                LogFailure("cancel playback", exception);
            }
        }

        /// <summary>
        /// Cancels playback when the application is backgrounded; does nothing on resume.
        /// </summary>
        /// <param name="paused">True when going to the background, false on resume.</param>
        /// <remarks>
        /// Android keeps a waveform running after the process loses focus, so a long pattern would
        /// otherwise buzz on in the user's pocket. Nothing is restarted on resume: the pattern's
        /// moment has passed, and silently replaying it would be worse than dropping it. Repeated
        /// notifications with the same value are harmless because cancelling twice is a no-op.
        /// </remarks>
        public void OnApplicationPause(bool paused)
        {
            if (paused)
            {
                Stop();
            }
        }

        /// <summary>
        /// Resolves and caches <c>SDK_INT</c> and the <c>Vibrator</c> handle on first use: through
        /// <c>VibratorManager.getDefaultVibrator()</c> on API 31 and above, and through the legacy
        /// <c>VIBRATOR_SERVICE</c> lookup below that (and as a fallback if the manager path yields
        /// nothing).
        /// </summary>
        /// <remarks>
        /// Latched by <see cref="_vibratorAcquisitionAttempted"/> before any work runs, so a failure
        /// costs one attempt for the lifetime of the provider rather than one per call. On failure the
        /// vibrator is left null, which every other member treats as "this device plays nothing".
        /// </remarks>
        private void EnsureVibrator()
        {
            if (_vibratorAcquisitionAttempted)
            {
                return;
            }

            _vibratorAcquisitionAttempted = true;

            try
            {
                _sdkInt = ReadSdkInt();

                using (var playerClass = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
                using (var activity = playerClass.GetStatic<AndroidJavaObject>("currentActivity"))
                {
                    if (activity == null)
                    {
                        LogFailure("acquire the current activity", null);
                        return;
                    }

                    if (_sdkInt >= ApiLevelVibratorManager)
                    {
                        using (var vibratorManager = activity.Call<AndroidJavaObject>("getSystemService", VibratorManagerServiceName))
                        {
                            if (vibratorManager != null)
                            {
                                _vibrator = vibratorManager.Call<AndroidJavaObject>("getDefaultVibrator");
                            }
                        }
                    }

                    if (_vibrator == null)
                    {
                        _vibrator = activity.Call<AndroidJavaObject>("getSystemService", VibratorServiceName);
                    }
                }

                _hasVibrator = _vibrator != null && _vibrator.Call<bool>("hasVibrator");
            }
            catch (Exception exception)
            {
                LogFailure("acquire the system vibrator", exception);
                _vibrator = null;
                _hasVibrator = false;
            }
        }

        /// <summary>
        /// Reads and caches the three predefined effect ids on API 29 and above. Does nothing on older
        /// platforms, where the constants do not exist and presets use the one-shot fallback.
        /// </summary>
        /// <remarks>
        /// The ids are read from the platform at runtime rather than hardcoded: they are documented
        /// constants, but reading them keeps the package honest if Android ever renumbers them, and
        /// costs three reflective field reads once per session. Latched like every other lazy step.
        /// </remarks>
        private void EnsurePredefinedEffects()
        {
            if (_predefinedEffectsAttempted)
            {
                return;
            }

            _predefinedEffectsAttempted = true;

            if (_sdkInt < ApiLevelPredefinedEffects)
            {
                return;
            }

            var effectClass = GetVibrationEffectClass();
            if (effectClass == null)
            {
                return;
            }

            try
            {
                _effectTick = effectClass.GetStatic<int>("EFFECT_TICK");
                _effectClick = effectClass.GetStatic<int>("EFFECT_CLICK");
                _effectHeavyClick = effectClass.GetStatic<int>("EFFECT_HEAVY_CLICK");
                _predefinedEffectsResolved = true;
            }
            catch (Exception exception)
            {
                LogFailure("read the predefined vibration effect ids", exception);
                _predefinedEffectsResolved = false;
            }
        }

        /// <summary>
        /// Returns the cached <c>android.os.VibrationEffect</c> class, resolving it on first use.
        /// </summary>
        /// <returns>The class handle, or null when it could not be resolved.</returns>
        /// <remarks>
        /// The handle is long-lived by design — it backs every effect this provider builds — so unlike
        /// the effect instances it is never disposed.
        /// </remarks>
        private AndroidJavaClass GetVibrationEffectClass()
        {
            if (_vibrationEffectClassAttempted)
            {
                return _vibrationEffectClass;
            }

            _vibrationEffectClassAttempted = true;

            try
            {
                _vibrationEffectClass = new AndroidJavaClass(VibrationEffectClassName);
            }
            catch (Exception exception)
            {
                LogFailure("resolve android.os.VibrationEffect", exception);
                _vibrationEffectClass = null;
            }

            return _vibrationEffectClass;
        }

        /// <summary>
        /// Builds the <c>VibrationEffect</c> for a preset: predefined where available, otherwise a
        /// one-shot pulse.
        /// </summary>
        /// <param name="preset">Which impact strength to build.</param>
        /// <returns>The effect, or null when the preset is unrecognized or the class is unavailable.</returns>
        /// <remarks>
        /// The caller owns the returned object and disposes it. The preset is switched on explicitly
        /// and anything unrecognized returns null, so a value from a newer version of the enum can
        /// never be cast blindly into a platform effect id.
        /// </remarks>
        private AndroidJavaObject CreatePresetEffect(HapticPresetType preset)
        {
            var effectClass = GetVibrationEffectClass();
            if (effectClass == null)
            {
                return null;
            }

            if (_predefinedEffectsResolved)
            {
                int effectId;
                switch (preset)
                {
                    case HapticPresetType.Light:
                        effectId = _effectTick;
                        break;
                    case HapticPresetType.Medium:
                        effectId = _effectClick;
                        break;
                    case HapticPresetType.Heavy:
                        effectId = _effectHeavyClick;
                        break;
                    default:
                        return null;
                }

                return effectClass.CallStatic<AndroidJavaObject>("createPredefined", effectId);
            }

            long durationMs;
            int amplitude;
            switch (preset)
            {
                case HapticPresetType.Light:
                    durationMs = 40L;
                    amplitude = 80;
                    break;
                case HapticPresetType.Medium:
                    durationMs = 60L;
                    amplitude = 150;
                    break;
                case HapticPresetType.Heavy:
                    durationMs = 90L;
                    amplitude = 255;
                    break;
                default:
                    return null;
            }

            return effectClass.CallStatic<AndroidJavaObject>("createOneShot", durationMs, amplitude);
        }

        /// <summary>Reads <c>android.os.Build.VERSION.SDK_INT</c>.</summary>
        /// <returns>The platform API level of the running device.</returns>
        private static int ReadSdkInt()
        {
            using (var versionClass = new AndroidJavaClass("android.os.Build$VERSION"))
            {
                return versionClass.GetStatic<int>("SDK_INT");
            }
        }

        /// <summary>
        /// Logs a swallowed JNI failure as a warning rather than an error: haptics degrading to
        /// silence is a nuisance, not a fault worth failing a build's log assertions over.
        /// </summary>
        /// <param name="what">What the provider was trying to do, phrased to follow "failed to".</param>
        /// <param name="exception">The failure, or null when the failure was a null result rather than a throw.</param>
        private static void LogFailure(string what, Exception exception)
        {
            var message = LogPrefix + "Android haptics failed to " + what + "; falling back to no vibration.";
            Debug.LogWarning(exception == null ? message : message + " " + exception);
        }
    }
}
#endif
