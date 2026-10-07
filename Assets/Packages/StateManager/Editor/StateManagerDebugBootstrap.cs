using UnityEditor;
using UnityEssentials.States;

namespace UnityEssentials.States.Editor
{
    [InitializeOnLoad]
    internal static class StateManagerDebugBootstrap
    {
        static StateManagerDebugBootstrap()
        {
            EditorApplication.playModeStateChanged -= OnPlayModeStateChanged;
            EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
        }

        private static void OnPlayModeStateChanged(PlayModeStateChange change)
        {
            if (change == PlayModeStateChange.EnteredEditMode)
            {
                StateMachineDebugRegistry.Clear();
            }
        }
    }
}
