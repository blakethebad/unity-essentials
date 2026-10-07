using System;
using System.Reflection;
using System.Runtime.ExceptionServices;

namespace UnityEssentials.Utilities
{
    public abstract class Singleton<T> where T : Singleton<T>
    {
        private static readonly object _instanceLock = new object();

        [ThreadStatic]
        private static bool _isConstructing;

        private static volatile T _instance;

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
                ExceptionDispatchInfo.Capture(exception.InnerException).Throw();
                throw;
            }
            finally
            {
                _isConstructing = false;
            }
        }
    }
}
