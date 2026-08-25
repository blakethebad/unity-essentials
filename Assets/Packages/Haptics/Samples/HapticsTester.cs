using UnityEngine;

namespace UnityEssentials.Haptics.Samples
{
    /// <summary>
    /// An on-screen test panel for verifying haptics on a real device: the three presets, a demo
    /// custom pattern, a stop button, the global enable toggle and the two capability flags. Drop it
    /// on any GameObject in any scene and build — it needs no other component, asset or scene setup.
    /// </summary>
    /// <remarks>
    /// This exists because haptics cannot be verified anywhere but on hardware. Neither the iOS
    /// Simulator nor the Android Emulator renders vibration, and in the Editor the package selects its
    /// no-op provider on purpose, so the panel will report both capability flags as false there and
    /// every button will do nothing. That is the correct Editor behaviour, not a failure.
    /// <para>
    /// The panel is deliberately IMGUI: it draws from <c>OnGUI</c> with no canvas, no prefab and no UI
    /// package dependency, so it survives being copied into any project. The whole layout is scaled by
    /// the screen's density, because unscaled IMGUI controls are roughly fingernail-sized on a modern
    /// phone.
    /// </para>
    /// <para>
    /// The sample also models the ownership rule for runtime patterns. A <see cref="HapticPattern"/>
    /// built with <see cref="HapticPattern.Create"/> is a ScriptableObject that belongs to whoever
    /// created it and leaks until destroyed, so this component builds its demo pattern lazily on first
    /// use and destroys it in <c>OnDestroy</c>.
    /// </para>
    /// </remarks>
    [AddComponentMenu("UnityEssentials/Haptics/Haptics Tester")]
    [DisallowMultipleComponent]
    public sealed class HapticsTester : MonoBehaviour
    {
        /// <summary>Screen density the layout constants are authored against, in dots per inch.</summary>
        private const float ReferenceDpi = 160f;

        /// <summary>Distance from the panel to the screen edges, in scaled points.</summary>
        private const float Margin = 12f;

        /// <summary>Height of every button and toggle, in scaled points — large enough to hit with a thumb.</summary>
        private const float ControlHeight = 44f;

        [Tooltip("Extra multiplier applied on top of the automatic density scale, for screens where " +
                 "the panel still reads too small or too large. Ignored when zero or negative.")]
        [SerializeField] private float uiScaleMultiplier = 1f;

        [Tooltip("Initialize the haptics backend on Start instead of letting the first button press " +
                 "pay for it. Mirrors pre-warming during a loading screen.")]
        [SerializeField] private bool prewarmOnStart = true;

        /// <summary>
        /// The demo pattern, created on first use and owned by this component. Null until then, and
        /// null again after <c>OnDestroy</c>.
        /// </summary>
        private HapticPattern _demoPattern;

        /// <summary>The most recent action, echoed on screen so a device test is readable without a log.</summary>
        private string _status = "Ready.";

        /// <summary>
        /// The demo pattern, built on first access. The null test is Unity's overloaded comparison, so a
        /// pattern destroyed out from under this component is rebuilt rather than dispatched dead.
        /// </summary>
        private HapticPattern DemoPattern
        {
            get
            {
                if (_demoPattern == null)
                {
                    _demoPattern = HapticPattern.Create(BuildDemoEvents());
                }

                return _demoPattern;
            }
        }

        /// <summary>
        /// Optionally pre-warms the haptics backend so the first button press is not also the call that
        /// starts an engine or acquires a system service.
        /// </summary>
        private void Start()
        {
            if (prewarmOnStart)
            {
                HapticService.Initialize();
                _status = "Pre-warmed on Start.";
            }
        }

        /// <summary>
        /// Stops anything still playing and destroys the demo pattern this component created. A runtime
        /// pattern is a Unity object rather than a plain managed one, so dropping the last reference
        /// does not free it — the creator has to.
        /// </summary>
        /// <remarks>
        /// The destroy call is chosen by play state because <c>Destroy</c> is not allowed outside play
        /// mode; the pattern can only ever be built from <c>OnGUI</c> during play, but the guarded form
        /// is what consuming code should copy.
        /// </remarks>
        private void OnDestroy()
        {
            HapticService.Stop();

            if (_demoPattern == null)
            {
                return;
            }

            if (Application.isPlaying)
            {
                Destroy(_demoPattern);
            }
            else
            {
                DestroyImmediate(_demoPattern);
            }

            _demoPattern = null;
        }

        /// <summary>
        /// Draws the whole panel, scaled for the current screen density.
        /// </summary>
        /// <remarks>
        /// The GUI matrix is scaled rather than each rect so that fonts, padding and hit testing all
        /// scale together, and it is restored before returning so nothing else drawing this frame
        /// inherits the transform.
        /// </remarks>
        private void OnGUI()
        {
            float scale = ComputeUiScale();
            Matrix4x4 previousMatrix = GUI.matrix;
            GUI.matrix = Matrix4x4.TRS(Vector3.zero, Quaternion.identity, new Vector3(scale, scale, 1f));

            float panelWidth = Screen.width / scale - Margin * 2f;
            float panelHeight = Screen.height / scale - Margin * 2f;
            GUILayout.BeginArea(new Rect(Margin, Margin, panelWidth, panelHeight), GUI.skin.box);

            DrawCapabilities();
            DrawEnableToggle();
            DrawPresetButtons();
            DrawPatternButton();
            DrawStopButton();

            GUILayout.FlexibleSpace();
            GUILayout.Label(_status);

            GUILayout.EndArea();
            GUI.matrix = previousMatrix;
        }

        /// <summary>
        /// Draws the two read-only capability flags. Reading them is cheap: it selects the platform
        /// provider but never starts it.
        /// </summary>
        private void DrawCapabilities()
        {
            GUILayout.Label("Haptics Tester");
            GUILayout.Label("IsSupported: " + HapticService.IsSupported);
            GUILayout.Label("SupportsPatterns: " + HapticService.SupportsPatterns);
        }

        /// <summary>
        /// Draws the global mute gate. The property is assigned only when the toggle actually changes,
        /// because setting it to false also stops whatever is playing.
        /// </summary>
        private void DrawEnableToggle()
        {
            bool isEnabled = GUILayout.Toggle(HapticService.IsEnabled, " Haptics enabled", GUILayout.Height(ControlHeight));
            if (isEnabled != HapticService.IsEnabled)
            {
                HapticService.IsEnabled = isEnabled;
                _status = isEnabled ? "Haptics enabled." : "Haptics disabled (playback stopped).";
            }
        }

        /// <summary>
        /// Draws one button per built-in preset. Presets are the only haptic guaranteed to work on
        /// hardware that cannot render patterns, so they are the first thing to check on a new device.
        /// </summary>
        private void DrawPresetButtons()
        {
            if (GUILayout.Button("Light", GUILayout.Height(ControlHeight)))
            {
                PlayPreset(HapticPresetType.Light);
            }

            if (GUILayout.Button("Medium", GUILayout.Height(ControlHeight)))
            {
                PlayPreset(HapticPresetType.Medium);
            }

            if (GUILayout.Button("Heavy", GUILayout.Height(ControlHeight)))
            {
                PlayPreset(HapticPresetType.Heavy);
            }
        }

        /// <summary>
        /// Draws the custom pattern button, labelled so a tester can tell when the pattern is played in
        /// full from when the device is degrading it to a single impact.
        /// </summary>
        private void DrawPatternButton()
        {
            string label = HapticService.SupportsPatterns
                ? "Demo Pattern"
                : "Demo Pattern (degrades to one impact)";

            if (GUILayout.Button(label, GUILayout.Height(ControlHeight)))
            {
                HapticService.Play(DemoPattern);
                _status = "Played the demo pattern.";
            }
        }

        /// <summary>
        /// Draws the stop button. Stopping with nothing playing is legal and worth testing, so the
        /// button is never disabled.
        /// </summary>
        private void DrawStopButton()
        {
            if (GUILayout.Button("Stop", GUILayout.Height(ControlHeight)))
            {
                HapticService.Stop();
                _status = "Stopped.";
            }
        }

        /// <summary>
        /// Plays a preset and records it in the status line.
        /// </summary>
        /// <param name="preset">The impact strength to play.</param>
        private void PlayPreset(HapticPresetType preset)
        {
            HapticService.Play(preset);
            _status = "Played preset: " + preset + ".";
        }

        /// <summary>
        /// Converts the screen's density into a GUI scale, never shrinking the panel below its authored
        /// size.
        /// </summary>
        /// <returns>The factor to scale the GUI matrix by. Always positive.</returns>
        /// <remarks>
        /// <c>Screen.dpi</c> reports 0 on platforms that cannot determine density, and desktop screens
        /// report well under the mobile reference, so the density term is floored at 1: the panel grows
        /// on dense phone screens and is left alone everywhere else.
        /// </remarks>
        private float ComputeUiScale()
        {
            float dpi = Screen.dpi;
            float densityScale = dpi > 0f ? dpi / ReferenceDpi : 1f;
            float multiplier = uiScaleMultiplier > 0f ? uiScaleMultiplier : 1f;
            return Mathf.Max(1f, densityScale) * multiplier;
        }

        /// <summary>
        /// Builds the demo timeline: two opening transients, a three-step rising ramp of continuous
        /// events and a closing transient, just under a second in total.
        /// </summary>
        /// <returns>The events, authored in time order and free of overlap.</returns>
        /// <remarks>
        /// The events deliberately never overlap. Overlapping events are legal but their combined feel
        /// is not defined across platforms — the Android converter resolves them by letting the later
        /// event truncate the earlier one — and a demo pattern has to feel the same on every device it
        /// is checked on. Sharpness is set on every event even though only iOS renders it, which is
        /// exactly why the pattern is also distinguishable by intensity and timing alone.
        /// </remarks>
        private static HapticEvent[] BuildDemoEvents()
        {
            return new[]
            {
                // Two quick taps: transient events, zero duration.
                new HapticEvent(0.00f, 1.00f, 1.0f, 0.00f),
                new HapticEvent(0.12f, 0.55f, 0.8f, 0.00f),

                // A ramp: back-to-back continuous events rising in intensity and sharpness.
                new HapticEvent(0.24f, 0.20f, 0.2f, 0.16f),
                new HapticEvent(0.40f, 0.50f, 0.4f, 0.16f),
                new HapticEvent(0.56f, 0.80f, 0.6f, 0.16f),

                // A hard transient to close, after a short gap.
                new HapticEvent(0.80f, 1.00f, 1.0f, 0.00f)
            };
        }
    }
}
