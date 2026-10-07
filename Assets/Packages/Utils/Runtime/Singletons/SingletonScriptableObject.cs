using System;
using UnityEngine;

namespace UnityEssentials.Utilities
{
    public abstract class SingletonScriptableObject<T> : ScriptableObject where T : SingletonScriptableObject<T>
    {
        private static T _instance;

        public static T Instance
        {
            get
            {
                MainThreadGuard.AssertMainThread();

                if (_instance != null)
                {
                    return _instance;
                }

                return ResolveInstance(Resources.LoadAll<T>(string.Empty));
            }
        }

        public static bool HasInstance => _instance != null;

        internal static T ResolveInstance(T[] candidates)
        {
            if (candidates == null || candidates.Length == 0)
            {
                throw new InvalidOperationException(
                    $"No '{typeof(T).Name}' asset was found. Create one through the Assets > Create menu and " +
                    $"place it in a Resources folder of your project (for example Assets/Resources/{typeof(T).Name}.asset).");
            }

            if (candidates.Length > 1)
            {
                var names = new string[candidates.Length];
                for (var i = 0; i < candidates.Length; i++)
                {
                    names[i] = candidates[i] != null ? candidates[i].name : "<destroyed>";
                }

                throw new InvalidOperationException(
                    $"{candidates.Length} '{typeof(T).Name}' assets were found in Resources folders " +
                    $"({string.Join(", ", names)}), but exactly one is allowed. " +
                    "Delete the extras or move them out of Resources.");
            }

            _instance = candidates[0];
            return _instance;
        }

        internal static void SetInstanceForTests(T instance)
        {
            _instance = instance;
        }
    }
}
