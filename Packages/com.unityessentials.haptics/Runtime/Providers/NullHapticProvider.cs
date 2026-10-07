namespace UnityEssentials.Haptics
{
    /// <summary>
    /// The provider used wherever haptics cannot be produced: the Editor, desktop standalone players
    /// and every platform without a backend of its own. Every member is an explicit no-op and both
    /// capability flags are false.
    /// </summary>
    /// <remarks>
    /// This is the package's fallback rather than an error path — unsupported hardware is a normal
    /// condition, so nothing here logs, warns or throws. Callers see the difference through
    /// <see cref="IsSupported"/> and <see cref="SupportsPatterns"/>, which the manager surfaces as
    /// <c>HapticService.IsSupported</c> and <c>HapticService.SupportsPatterns</c>.
    /// <para>
    /// The class carries no <c>#if</c> guards at all: it is the fallback branch of the manager's
    /// platform selection and therefore must compile on every build target, including the Editor,
    /// where the EditMode tests construct it directly. It holds no state, so a single instance could
    /// be shared, but the manager creates one per selection to keep the provider lifetime uniform
    /// across platforms.
    /// </para>
    /// <para>
    /// Because this provider is never initialized into anything, the manager deliberately skips
    /// creating the lifecycle driver GameObject when it selects this class — there is nothing for a
    /// pause notification to cancel.
    /// </para>
    /// </remarks>
    internal sealed class NullHapticProvider : IHapticProvider
    {
        /// <summary>
        /// Always false: this device plays nothing. Safe to read before <see cref="Initialize"/>,
        /// which is trivially true here because nothing is ever acquired.
        /// </summary>
        public bool IsSupported
        {
            get { return false; }
        }

        /// <summary>
        /// Always false, as required whenever <see cref="IsSupported"/> is false. There is no
        /// degraded pattern path to fall back to.
        /// </summary>
        public bool SupportsPatterns
        {
            get { return false; }
        }

        /// <summary>
        /// Does nothing. There is no system service, engine or generator to acquire, so calling this
        /// costs nothing and can be called out of order without consequence.
        /// </summary>
        public void Initialize()
        {
        }

        /// <summary>Does nothing.</summary>
        /// <param name="preset">Ignored.</param>
        public void PlayPreset(HapticPresetType preset)
        {
        }

        /// <summary>
        /// Does nothing. The array is not read, so the manager's ordering and range guarantees are
        /// irrelevant here — and, as required of every provider, it is never written to.
        /// </summary>
        /// <param name="events">Ignored.</param>
        public void PlayPattern(HapticEvent[] events)
        {
        }

        /// <summary>Does nothing. Nothing can be playing, so there is nothing to cancel.</summary>
        public void Stop()
        {
        }

        /// <summary>Does nothing on either transition.</summary>
        /// <param name="paused">Ignored.</param>
        public void OnApplicationPause(bool paused)
        {
        }
    }
}
