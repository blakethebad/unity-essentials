using UnityEngine;

namespace UnityEssentials.Utilities
{
    /// <summary>
    /// MonoBehaviour singleton: <c>Instance</c> returns the live instance, finds one in the loaded
    /// scenes, or creates a hidden host GameObject for it. Duplicates destroy their own component
    /// with a warning and leave the GameObject they sit on untouched.
    /// </summary>
    public abstract class SingletonComponent<T> : MonoBehaviour where T : SingletonComponent<T>
    {
        private static T _instance;

        static SingletonComponent()
        {
            StaticResetRegistry.Register(() => _instance = null);
        }

        public static T Instance
        {
            get
            {
                StaticResetRegistry.AssertMainThread();

                if (SingletonComponentRuntime.IsQuitting)
                {
                    Debug.LogWarning(
                        $"'{typeof(T).Name}.Instance' was accessed while the application is quitting, so no " +
                        "instance was created and null is returned. Guard teardown code with HasInstance.");
                    return null;
                }

                // Unity's overloaded == is load-bearing here: a destroyed component still has a live
                // managed reference, and that reference must be treated as "no instance".
                if (_instance != null)
                {
                    return _instance;
                }

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

                // In play mode AddComponent runs Awake synchronously, which already adopts the new
                // component; adopting it again here is idempotent and covers EditMode, where Awake
                // is never called automatically.
                Adopt(host.AddComponent<T>());
                return _instance;
            }
        }

        public static bool HasInstance => _instance != null;

        /// <summary>
        /// Claims the singleton slot, or destroys this component when another instance already owns
        /// it. Overrides must call <c>base.Awake()</c> first and return if this component was destroyed.
        /// </summary>
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

            // DontDestroyOnLoad is play-mode only and logs an error for non-root objects, so a
            // singleton parented under a scene object simply stays with its scene.
            if (Application.isPlaying && instance.transform.parent == null)
            {
                DontDestroyOnLoad(instance.gameObject);
            }
        }
    }

    internal static class SingletonComponentRuntime
    {
        internal static bool IsQuitting;

        // Non-generic on purpose: RuntimeInitializeOnLoadMethod never fires on a generic type, so
        // the quitting flag every SingletonComponent<T> reads has to live here.
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
