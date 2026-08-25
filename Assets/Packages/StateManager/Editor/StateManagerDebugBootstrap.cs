using UnityEditor;
using UnityEssentials.States;

namespace UnityEssentials.States.Editor
{
    /// <summary>
    /// Empties the debug registry when the editor returns to edit mode, so the window never shows
    /// machines from a play session that has already ended.
    /// </summary>
    /// <remarks>
    /// Without this, exiting play mode would leave the registry holding entries whose machines are
    /// gone. They would eventually be pruned — the owner turns fake-null, or the machine is collected —
    /// but "eventually" depends on the garbage collector, and a dashboard that shows stale machines for
    /// an indeterminate time after the run is worse than one that shows none.
    /// <para>
    /// The clear is driven from here rather than from the window because it must happen whether or not
    /// the window is open: a registry left full would otherwise be the first thing a user sees when
    /// they open the window later. It is driven from an editor callback rather than from the runtime's
    /// <c>SubsystemRegistration</c> reset because that reset only runs when the domain reloads, and
    /// domain reloading is routinely disabled for fast enter/exit play mode.
    /// </para>
    /// <para>
    /// <see cref="PlayModeStateChange.EnteredEditMode"/> is the chosen moment rather than
    /// <see cref="PlayModeStateChange.ExitingPlayMode"/>: clearing while play mode is still tearing down
    /// would drop entries for objects whose <c>OnDestroy</c> has not run yet, and any machine that
    /// re-registers during that teardown would reappear in a list that is about to be irrelevant.
    /// </para>
    /// <para>
    /// The subscription is made from a static constructor under
    /// <see cref="InitializeOnLoadAttribute"/> and never removed. That is the intended shape for this
    /// attribute: the static state is discarded wholesale on every domain reload, and the constructor
    /// runs again afterwards to re-subscribe.
    /// </para>
    /// </remarks>
    [InitializeOnLoad]
    internal static class StateManagerDebugBootstrap
    {
        /// <summary>
        /// Subscribes to play-mode transitions as soon as the editor's scripts are loaded.
        /// </summary>
        static StateManagerDebugBootstrap()
        {
            // Unsubscribe first: harmless when not subscribed, and it makes the constructor idempotent
            // should the editor ever run it twice against a surviving delegate list.
            EditorApplication.playModeStateChanged -= OnPlayModeStateChanged;
            EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
        }

        /// <summary>
        /// Clears the registry once the editor is fully back in edit mode.
        /// </summary>
        /// <param name="change">The transition the editor just made.</param>
        private static void OnPlayModeStateChanged(PlayModeStateChange change)
        {
            if (change == PlayModeStateChange.EnteredEditMode)
            {
                StateMachineDebugRegistry.Clear();
            }
        }
    }
}
