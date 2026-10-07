using UnityEngine;

namespace UnityEssentials.Haptics
{
    /// <summary>
    /// The package's only MonoBehaviour: a hidden, scene-independent GameObject whose single job is to
    /// receive Unity's application pause callback and hand it to <see cref="HapticManager"/>.
    /// </summary>
    /// <remarks>
    /// It exists because a static class cannot receive Unity's lifecycle messages and the backends need
    /// one: an Android waveform keeps vibrating after the process loses focus, and iOS tears the
    /// CoreHaptics engine down while the app is suspended and needs it restarted on resume. Rather than
    /// make consumers wire that up, the manager creates this object itself.
    /// <para>
    /// Lifetime. Created lazily by the manager's first full initialization and never created for
    /// <see cref="NullHapticProvider"/> — a device that plays nothing has nothing to cancel or restart,
    /// so the Editor and desktop builds carry no extra object at all. At most one exists per session;
    /// <see cref="HapticManager.ResetStatics"/> destroys it so a domain reload, or entering play mode
    /// with domain reload disabled, does not accumulate drivers.
    /// </para>
    /// <para>
    /// <c>OnApplicationFocus</c> is deliberately not implemented. On mobile it fires alongside
    /// <c>OnApplicationPause</c> with an ordering that varies by platform, OS version and how the app
    /// was interrupted, so handling both would deliver the same transition twice in an order nothing
    /// can rely on. Pause alone is the signal that actually corresponds to the app being suspended,
    /// and it is what the providers are written against.
    /// </para>
    /// </remarks>
    internal sealed class HapticLifecycleDriver : MonoBehaviour
    {
        /// <summary>
        /// Name given to the driver's GameObject. Fully qualified on purpose: the object is hidden from
        /// the hierarchy, so the only place a developer ever meets this string is a profiler capture or
        /// a memory snapshot, where it has to identify the package that created it without context.
        /// </summary>
        internal const string GameObjectName = "UnityEssentials.HapticLifecycleDriver";

        /// <summary>
        /// Creates the hidden host GameObject and returns the driver component on it.
        /// </summary>
        /// <returns>The newly created driver. Never null.</returns>
        /// <remarks>
        /// <see cref="HideFlags.HideAndDontSave"/> keeps the object out of the hierarchy window, out of
        /// saved scenes and out of <c>Resources.UnloadUnusedAssets</c>, so it neither clutters a
        /// consumer's scene nor gets serialized into one by accident.
        /// <para>
        /// <see cref="Object.DontDestroyOnLoad(Object)"/> is applied only while playing. It is what
        /// carries the driver across scene loads, but outside play mode it is not a legal call and logs
        /// an error — and the EditMode tests construct a driver exactly there. The hide flags already
        /// cover the edit-mode case, where nothing loads a scene out from under it anyway.
        /// </para>
        /// <para>
        /// The manager owns the result: it holds the only reference and is the only thing that destroys
        /// it. Nothing else in the package, or outside it, should call this.
        /// </para>
        /// </remarks>
        internal static HapticLifecycleDriver Create()
        {
            var host = new GameObject(GameObjectName);
            host.hideFlags = HideFlags.HideAndDontSave;

            if (Application.isPlaying)
            {
                DontDestroyOnLoad(host);
            }

            return host.AddComponent<HapticLifecycleDriver>();
        }

        /// <summary>
        /// Unity's application pause message, forwarded straight to the manager.
        /// </summary>
        /// <param name="pause">True when the application is going to the background, false on resume.</param>
        /// <remarks>
        /// Deliberately does nothing but forward. Every decision about what a pause means — cancel the
        /// vibrator on Android, stop and restart the engine on iOS, ignore it entirely on an
        /// unsupported device — belongs to the provider, and routing through the manager keeps the
        /// driver ignorant of which provider is active.
        /// <para>
        /// The message may arrive repeatedly with the same value, and on some platforms it arrives once
        /// during startup; providers are written to tolerate both.
        /// </para>
        /// </remarks>
        private void OnApplicationPause(bool pause)
        {
            HapticManager.NotifyApplicationPause(pause);
        }
    }
}
