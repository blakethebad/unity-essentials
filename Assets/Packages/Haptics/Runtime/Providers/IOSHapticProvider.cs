#if UNITY_IOS
using System.Runtime.InteropServices;

namespace UnityEssentials.Haptics
{
    /// <summary>
    /// The iOS backend. Every member forwards to <c>UnityEssentialsHaptics.mm</c>, the Objective-C++
    /// bridge that ships with this package under <c>Plugins/iOS</c> and is compiled into the generated
    /// Xcode project's <c>UnityFramework</c> target: presets play through cached
    /// <c>UIImpactFeedbackGenerator</c> instances, custom patterns through a lazily started
    /// <c>CHHapticEngine</c>.
    /// </summary>
    /// <remarks>
    /// This class holds no state at all. Generators, the CoreHaptics engine, the running pattern
    /// player and the paused/resumed flag all live on the native side, because they must survive
    /// independently of anything the managed layer does and because only the bridge can reason about
    /// the platform version it is running on. That also means the provider is cheap to construct and
    /// safe to discard.
    /// <para>
    /// Failure handling is likewise native. Every entry point in the <c>.mm</c> wraps its body in
    /// <c>@try</c>/<c>@catch</c> and logs with an <c>[UnityEssentialsHaptics]</c> prefix, so an
    /// Objective-C exception can never cross back into managed code and no member here needs a
    /// <c>try</c> block of its own. The <c>"__Internal"</c> import target means the symbols are
    /// resolved when the app is linked rather than looked up at runtime, so a missing entry point is a
    /// build failure on a Mac, never a managed exception on device.
    /// </para>
    /// <para>
    /// The whole file is compiled only for the iOS build target. <c>HapticManager</c> additionally
    /// requires <c>!UNITY_EDITOR</c> before constructing it, so an Editor session with iOS as the
    /// active platform selects the null provider and never issues a P/Invoke into a framework that is
    /// not linked into the Editor.
    /// </para>
    /// </remarks>
    internal sealed class IOSHapticProvider : IHapticProvider
    {
        /// <summary>
        /// P/Invoke target for symbols statically linked into the application binary, which is how
        /// Unity links source plugins on iOS.
        /// </summary>
        private const string NativeLibraryName = "__Internal";

        /// <summary>
        /// Whether the device can play anything at all: on iOS 13 and later this is the CoreHaptics
        /// hardware capability, and below that it is true, because <c>UIImpactFeedbackGenerator</c>
        /// presets are available from iOS 10 and expose no capability query of their own.
        /// </summary>
        /// <remarks>
        /// Answered by two cheap native queries that start no engine and allocate no generator, so it
        /// is safe to read before <see cref="Initialize"/>, as the provider contract requires.
        /// </remarks>
        public bool IsSupported
        {
            get { return UnityEssentialsHaptics_IsSupported() != 0; }
        }

        /// <summary>
        /// Whether the device can play a full custom pattern, which requires both iOS 13 or later and
        /// hardware CoreHaptics support. False on, for example, an iPad or an older iPhone that still
        /// plays presets — there <see cref="PlayPattern"/> degrades rather than going silent.
        /// </summary>
        /// <remarks>Readable before <see cref="Initialize"/> and starts nothing.</remarks>
        public bool SupportsPatterns
        {
            get { return UnityEssentialsHaptics_SupportsPatterns() != 0; }
        }

        /// <summary>
        /// Pre-warms the native side: the three impact generators are created and prepared so the
        /// first preset does not pay the Taptic Engine's start-up latency.
        /// </summary>
        /// <remarks>
        /// The CoreHaptics engine is deliberately not created here. It is a heavier resource that an
        /// app playing only presets never needs, so the bridge creates and starts it on the first
        /// pattern instead.
        /// </remarks>
        public void Initialize()
        {
            UnityEssentialsHaptics_Initialize();
        }

        /// <summary>
        /// Plays one of the three built-in impacts through the matching cached generator.
        /// </summary>
        /// <param name="preset">Which impact strength to play.</param>
        /// <remarks>
        /// The enum is marshalled as its underlying numeric value and the bridge switches explicitly on
        /// 0, 1 and 2 rather than casting into <c>UIImpactFeedbackStyle</c>, so a value that
        /// is not one of the three presets is logged and ignored instead of selecting an arbitrary
        /// native style.
        /// </remarks>
        public void PlayPreset(HapticPresetType preset)
        {
            UnityEssentialsHaptics_PlayPreset((int)preset);
        }

        /// <summary>
        /// Plays a custom pattern, replacing anything currently playing.
        /// </summary>
        /// <param name="events">
        /// The pattern's events, guaranteed by the manager to be non-null, non-empty, finite, clamped
        /// and sorted by time. The array is only read; the flattened copies are what cross the
        /// boundary.
        /// </param>
        /// <remarks>
        /// The events are split into four parallel <see cref="float"/> arrays by
        /// <see cref="IOSPatternFlattener"/> and passed as blittable buffers, which the marshaller can
        /// pin in place rather than copy field by field. On a device that reports
        /// <see cref="SupportsPatterns"/> as false the bridge plays a single impact scaled to the
        /// pattern's peak intensity, so the caller still feels something.
        /// </remarks>
        /// <exception cref="System.ArgumentNullException"><paramref name="events"/> is null.</exception>
        public void PlayPattern(HapticEvent[] events)
        {
            float[] times;
            float[] intensities;
            float[] sharpnesses;
            float[] durations;
            IOSPatternFlattener.Flatten(events, out times, out intensities, out sharpnesses, out durations);

            UnityEssentialsHaptics_PlayPattern(times, intensities, sharpnesses, durations, times.Length);
        }

        /// <summary>
        /// Stops the running pattern player. Presets are fire-and-forget impacts that cannot be
        /// cancelled once triggered, so this only affects pattern playback.
        /// </summary>
        /// <remarks>Safe with nothing playing, and safe before the CoreHaptics engine has ever been created.</remarks>
        public void Stop()
        {
            UnityEssentialsHaptics_Stop();
        }

        /// <summary>
        /// Stops playback and the CoreHaptics engine when the app is backgrounded, and restarts the
        /// engine on resume if it had been running.
        /// </summary>
        /// <param name="paused">True when the application is going to the background, false on resume.</param>
        /// <remarks>
        /// The system tears the haptic engine down while an app is suspended, so surviving a
        /// background trip requires this notification; the bridge also installs a reset handler for
        /// the cases where the engine restarts underneath us without one.
        /// </remarks>
        public void OnApplicationPause(bool paused)
        {
            UnityEssentialsHaptics_OnApplicationPause(paused ? 1 : 0);
        }

        /// <summary>
        /// Returns 1 when the device can play haptics at all, otherwise 0. Booleans are marshalled as
        /// <see cref="int"/> throughout this boundary because C's <c>BOOL</c> width is a portability
        /// trap and the default managed marshalling of <see cref="bool"/> is a 4-byte Win32 BOOL.
        /// </summary>
        [DllImport(NativeLibraryName)]
        private static extern int UnityEssentialsHaptics_IsSupported();

        /// <summary>Returns 1 when the device can render a full CoreHaptics pattern, otherwise 0.</summary>
        [DllImport(NativeLibraryName)]
        private static extern int UnityEssentialsHaptics_SupportsPatterns();

        /// <summary>Creates and prepares the cached impact generators.</summary>
        [DllImport(NativeLibraryName)]
        private static extern void UnityEssentialsHaptics_Initialize();

        /// <summary>Plays the impact identified by <paramref name="presetId"/>.</summary>
        /// <param name="presetId">0 for Light, 1 for Medium, 2 for Heavy; anything else is logged and ignored natively.</param>
        [DllImport(NativeLibraryName)]
        private static extern void UnityEssentialsHaptics_PlayPreset(int presetId);

        /// <summary>Plays the pattern described by four parallel arrays of length <paramref name="count"/>.</summary>
        /// <param name="times">Event times in seconds from the start of the pattern.</param>
        /// <param name="intensities">Event intensities over 0 to 1.</param>
        /// <param name="sharpnesses">Event sharpnesses over 0 to 1.</param>
        /// <param name="durations">Event durations in seconds, where 0 selects a transient event.</param>
        /// <param name="count">Number of elements in each array. The native side ignores a count of 0 or less.</param>
        [DllImport(NativeLibraryName)]
        private static extern void UnityEssentialsHaptics_PlayPattern(
            float[] times,
            float[] intensities,
            float[] sharpnesses,
            float[] durations,
            int count);

        /// <summary>Stops the running pattern player, if any.</summary>
        [DllImport(NativeLibraryName)]
        private static extern void UnityEssentialsHaptics_Stop();

        /// <summary>Forwards the application's pause state to the haptic engine.</summary>
        /// <param name="paused">1 when backgrounding, 0 when resuming.</param>
        [DllImport(NativeLibraryName)]
        private static extern void UnityEssentialsHaptics_OnApplicationPause(int paused);
    }
}
#endif
