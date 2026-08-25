namespace UnityEssentials.Haptics
{
    /// <summary>
    /// The three built-in impact strengths every supported platform can play without an authored
    /// pattern. Presets are the only haptic guaranteed to work on hardware that cannot render
    /// custom patterns, so prefer them for ordinary UI feedback.
    /// </summary>
    /// <remarks>
    /// The numeric values are a wire contract with the native iOS bridge: they are marshalled across
    /// the P/Invoke boundary as a plain integer and switched on by <c>UnityEssentialsHaptics.mm</c>.
    /// Never reorder the members and never insert a member between existing ones — appending only,
    /// and only alongside a matching change to the <c>.mm</c> switch.
    /// <para>
    /// Platform mapping. On iOS each preset selects a <c>UIImpactFeedbackGenerator</c> style:
    /// <see cref="Light"/> to <c>.light</c>, <see cref="Medium"/> to <c>.medium</c> and
    /// <see cref="Heavy"/> to <c>.heavy</c>. On Android API 29 and above each preset selects a
    /// predefined <c>VibrationEffect</c>: <see cref="Light"/> to <c>EFFECT_TICK</c>,
    /// <see cref="Medium"/> to <c>EFFECT_CLICK</c> and <see cref="Heavy"/> to
    /// <c>EFFECT_HEAVY_CLICK</c>. On Android API 26 to 28 there is no predefined effect, so each
    /// preset falls back to a <c>createOneShot</c> pulse of a fixed duration and amplitude, which
    /// feels coarser than the equivalent effect on newer devices.
    /// </para>
    /// </remarks>
    public enum HapticPresetType : byte
    {
        /// <summary>
        /// The subtlest impact, for high-frequency feedback such as selection changes or ticks.
        /// Maps to <c>UIImpactFeedbackGenerator</c> style <c>.light</c> on iOS and to
        /// <c>EFFECT_TICK</c> on Android API 29 and above.
        /// </summary>
        Light = 0,

        /// <summary>
        /// The default impact, for ordinary confirmations such as a button press. Maps to
        /// <c>UIImpactFeedbackGenerator</c> style <c>.medium</c> on iOS and to <c>EFFECT_CLICK</c>
        /// on Android API 29 and above.
        /// </summary>
        Medium = 1,

        /// <summary>
        /// The strongest impact, for significant or destructive events. Maps to
        /// <c>UIImpactFeedbackGenerator</c> style <c>.heavy</c> on iOS and to
        /// <c>EFFECT_HEAVY_CLICK</c> on Android API 29 and above.
        /// </summary>
        Heavy = 2
    }
}
