using System;
using System.Reflection;
using System.Runtime.ExceptionServices;

namespace UnityEssentials.Utilities
{
    /// <summary>
    /// Thread-safe, lazily created singleton for plain C# types: <c>Instance</c> builds the subclass
    /// through its parameterless constructor on first access. Constructing a subclass directly with
    /// <c>new</c> throws, and a constructor that throws is never cached — the next access retries.
    /// </summary>
    public abstract class Singleton<T> where T : Singleton<T>
    {
        private static readonly object _instanceLock = new object();

        // Per-thread: a `new T()` racing the factory on another thread must still be rejected.
        [ThreadStatic]
        private static bool _isConstructing;

        private static volatile T _instance;

        static Singleton()
        {
            StaticResetRegistry.Register(() => _instance = null);
        }

        protected Singleton()
        {
            if (!_isConstructing)
            {
                throw new InvalidOperationException(
                    $"'{typeof(T).FullName}' is a singleton and must not be constructed with 'new'. " +
                    $"Access it through {typeof(T).Name}.Instance and give the type a private parameterless constructor.");
            }
        }

        public static T Instance
        {
            get
            {
                var instance = _instance;
                if (instance != null)
                {
                    return instance;
                }

                lock (_instanceLock)
                {
                    if (_instance == null)
                    {
                        _instance = Create();
                    }

                    return _instance;
                }
            }
        }

        public static bool HasInstance => _instance != null;

        private static T Create()
        {
            // Reentrancy means the subclass constructor resolved its own Instance; without this
            // guard that recursion only surfaces as an uncatchable stack overflow.
            if (_isConstructing)
            {
                throw new InvalidOperationException(
                    $"'{typeof(T).FullName}' accessed its own Instance from inside its constructor. " +
                    "Resolve the dependency lazily instead of in the constructor.");
            }

            _isConstructing = true;
            try
            {
                return (T)Activator.CreateInstance(typeof(T), true);
            }
            catch (TargetInvocationException exception) when (exception.InnerException != null)
            {
                // Activator wraps whatever the subclass constructor threw. Unwrap it so callers see
                // the real error with its stack intact; the assignment above never happens, so the
                // failure is not cached and the next access constructs again.
                ExceptionDispatchInfo.Capture(exception.InnerException).Throw();
                throw; // Unreachable: Throw() always rethrows. Keeps definite assignment happy.
            }
            finally
            {
                _isConstructing = false;
            }
        }
    }
}
