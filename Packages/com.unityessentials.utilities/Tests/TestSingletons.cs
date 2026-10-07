using System;

namespace UnityEssentials.Utilities.Tests
{
    /// <summary>Ordinary singleton with a private constructor: the happy path double.</summary>
    public class WellBehavedSingleton : Singleton<WellBehavedSingleton>
    {
        internal static int ConstructionCount;

        private WellBehavedSingleton()
        {
            ConstructionCount++;
        }
    }

    /// <summary>
    /// Constructor throws on the first attempt and succeeds afterwards, so one fixture can cover both
    /// exception unwrapping and the retry that follows a failed creation.
    /// </summary>
    public class ThrowOnceSingleton : Singleton<ThrowOnceSingleton>
    {
        internal const string FailureMessage = "throw-once singleton constructor failed";

        internal static int ConstructionAttempts;
        internal static bool ShouldThrow;

        private ThrowOnceSingleton()
        {
            ConstructionAttempts++;
            if (ShouldThrow)
            {
                ShouldThrow = false;
                throw new NotSupportedException(FailureMessage);
            }
        }

        internal static void ResetProbe()
        {
            ConstructionAttempts = 0;
            ShouldThrow = true;
        }
    }

    /// <summary>Public constructor so a test can attempt <c>new</c> and be rejected by the base guard.</summary>
    public class DirectNewSingleton : Singleton<DirectNewSingleton>
    {
        public DirectNewSingleton()
        {
        }
    }

    /// <summary>Constructor resolves its own Instance, so the circular-construction guard can be pinned.</summary>
    public class SelfResolvingSingleton : Singleton<SelfResolvingSingleton>
    {
        private SelfResolvingSingleton()
        {
            _ = Instance;
        }
    }

    /// <summary>Counts constructions so the parallel first-access test can prove there was only one.</summary>
    public class ParallelSingleton : Singleton<ParallelSingleton>
    {
        internal static int ConstructionCount;

        private ParallelSingleton()
        {
            ConstructionCount++;
        }
    }

    /// <summary>
    /// EditMode never calls Awake after AddComponent, so the fixture drives it through this seam.
    /// </summary>
    public class TestSingletonComponent : SingletonComponent<TestSingletonComponent>
    {
        internal void InvokeAwake()
        {
            Awake();
        }
    }

    /// <summary>Asset-backed singleton double; instances come from ScriptableObject.CreateInstance.</summary>
    public class TestSingletonAsset : SingletonScriptableObject<TestSingletonAsset>
    {
    }
}
