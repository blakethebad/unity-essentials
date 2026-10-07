using System;
using System.Reflection;
using NUnit.Framework;

namespace UnityEssentials.Utilities.Tests
{
    /// <summary>
    /// Clears the cached instance a singleton holds, so one test cannot inherit another's. Reflection
    /// because the cache is private: the runtime types expose no reset of their own.
    /// </summary>
    internal static class SingletonCacheReset
    {
        internal static void ClearAllPlainSingletons()
        {
            Clear<WellBehavedSingleton>();
            Clear<ThrowOnceSingleton>();
            Clear<DirectNewSingleton>();
            Clear<SelfResolvingSingleton>();
            Clear<ParallelSingleton>();
        }

        internal static void Clear<T>() where T : Singleton<T>
        {
            ClearInstanceField(typeof(Singleton<T>));
        }

        internal static void ClearComponent<T>() where T : SingletonComponent<T>
        {
            ClearInstanceField(typeof(SingletonComponent<T>));
        }

        private static void ClearInstanceField(Type closedType)
        {
            var field = closedType.GetField(
                "_instance", BindingFlags.Static | BindingFlags.NonPublic);

            Assert.IsNotNull(
                field,
                $"{closedType.Name} has no private static _instance field. Fixtures clear it to isolate " +
                "tests from one another, so renaming it silently leaks a cached instance between them.");

            field.SetValue(null, null);
        }
    }
}
