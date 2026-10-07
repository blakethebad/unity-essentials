using UnityEngine;

namespace UnityEssentials.Utilities
{
    public abstract class SingletonComponent<T> : MonoBehaviour where T : SingletonComponent<T>
    {
        private static T _instance;

        public static T Instance
        {
            get
            {
                MainThreadGuard.AssertMainThread();

                if (SingletonComponentRuntime.IsQuitting)
                {
                    Debug.LogWarning(
                        $"'{typeof(T).Name}.Instance' was accessed while the application is quitting, so no " +
                        "instance was created and null is returned. Guard teardown code with HasInstance.");
                    return null;
                }

                if (_instance != null)
                    return _instance;

                var existing = FindAnyObjectByType<T>();
                if (existing != null)
                {
                    Adopt(existing);
                    return _instance;
                }

                var host = new GameObject($"{typeof(T).Name} (Singleton)")
                {
                    hideFlags = HideFlags.HideAndDontSave
                };

                Adopt(host.AddComponent<T>());
                return _instance;
            }
        }

        public static bool HasInstance => _instance != null;

        protected virtual void Awake()
        {
            if (_instance != null && _instance != this)
            {
                Debug.LogWarning(
                    $"A duplicate '{typeof(T).Name}' on GameObject '{name}' was destroyed because " +
                    $"'{_instance.name}' already provides the instance. Only the component is removed; " +
                    "the GameObject and its other components are left alone.",
                    this);

                if (Application.isPlaying)
                {
                    Destroy(this);
                }
                else
                {
                    DestroyImmediate(this);
                }

                return;
            }

            Adopt((T)this);
        }

        private static void Adopt(T instance)
        {
            _instance = instance;

            if (Application.isPlaying && instance.transform.parent == null)
                DontDestroyOnLoad(instance.gameObject);
        }
    }

    internal static class SingletonComponentRuntime
    {
        internal static bool IsQuitting;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        internal static void Initialize()
        {
            IsQuitting = false;

            // With domain reload disabled the handler survives the previous session.
            Application.quitting -= OnApplicationQuitting;
            Application.quitting += OnApplicationQuitting;
        }

        private static void OnApplicationQuitting()
        {
            IsQuitting = true;
        }
    }
}
