using System;
using System.Diagnostics;

namespace UnityEssentials.Utilities
{
    internal static class MainThreadGuard
    {
        private static readonly int _mainThreadId = Environment.CurrentManagedThreadId;

        [Conditional("UNITY_EDITOR")]
        [Conditional("DEVELOPMENT_BUILD")]
        internal static void AssertMainThread()
        {
            if (Environment.CurrentManagedThreadId != _mainThreadId)
            {
                throw new InvalidOperationException(
                    "The Utilities package may only be used from the Unity main thread. " +
                    "Move the call to the main thread and hand the result to the worker.");
            }
        }
    }
}
