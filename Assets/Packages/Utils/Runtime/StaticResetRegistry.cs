using System;
using System.Collections.Generic;
using System.Diagnostics;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace UnityEssentials.Utilities
{
    /// <summary>
    /// Shared domain-reload plumbing for the Utilities package: generic systems register a reset
    /// action from their static constructor and this non-generic type replays them all on load.
    /// Also owns the main-thread id that every utility asserts against.
    /// </summary>
    internal static class StaticResetRegistry
    {
        // Inline initializers throughout: EditMode never fires RuntimeInitializeOnLoadMethod, so the
        // registry must be fully usable from the implicit static constructor alone. That constructor
        // runs on whichever thread first touches the registry, which in Unity is the main thread.
        internal static int MainThreadId = Environment.CurrentManagedThreadId;

        private static readonly List<Action> _resetActions = new List<Action>();

        // Called from the static constructors of generic systems: RuntimeInitializeOnLoadMethod
        // never fires on a generic type, and closed-generic statics cannot be enumerated, so a
        // system's reset is only reachable through a delegate it hands over itself.
        internal static void Register(Action resetAction)
        {
            if (resetAction == null)
            {
                throw new ArgumentNullException(nameof(resetAction));
            }

            _resetActions.Add(resetAction);
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        internal static void ResetStatics()
        {
            MainThreadId = Environment.CurrentManagedThreadId;

            // Actions are retained, never drained: with domain reload disabled the static
            // constructors that registered them do not run again, so draining would orphan every
            // registered system from the second reset onwards.
            for (var i = 0; i < _resetActions.Count; i++)
            {
                try
                {
                    _resetActions[i].Invoke();
                }
                catch (Exception exception)
                {
                    // One system failing to reset must not starve the systems queued behind it.
                    Debug.LogException(exception);
                }
            }
        }

        [Conditional("UNITY_EDITOR")]
        [Conditional("DEVELOPMENT_BUILD")]
        internal static void AssertMainThread()
        {
            if (Environment.CurrentManagedThreadId != MainThreadId)
            {
                throw new InvalidOperationException(
                    "The Utilities package may only be used from the Unity main thread. " +
                    "Move the call to the main thread and hand the result to the worker.");
            }
        }
    }
}
