using System;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;

namespace UnityEssentials.Utilities.Tests
{
    /// <summary>
    /// Covers the plain C# singleton: lazy creation, identity, the direct-<c>new</c> guard, exception
    /// unwrapping with retry and concurrent first access.
    /// </summary>
    [TestFixture]
    public class SingletonTests
    {
        [SetUp]
        public void SetUp()
        {
            SingletonCacheReset.ClearAllPlainSingletons();
            WellBehavedSingleton.ConstructionCount = 0;
            ParallelSingleton.ConstructionCount = 0;
            ThrowOnceSingleton.ResetProbe();
        }

        [TearDown]
        public void TearDown()
        {
            SingletonCacheReset.ClearAllPlainSingletons();
            ThrowOnceSingleton.ShouldThrow = false;
        }

        [Test]
        public void Instance_IsCreatedLazilyOnFirstAccess()
        {
            Assert.IsFalse(WellBehavedSingleton.HasInstance);
            Assert.AreEqual(0, WellBehavedSingleton.ConstructionCount);

            var instance = WellBehavedSingleton.Instance;

            Assert.IsNotNull(instance);
            Assert.IsTrue(WellBehavedSingleton.HasInstance);
            Assert.AreEqual(1, WellBehavedSingleton.ConstructionCount);
        }

        [Test]
        public void Instance_RepeatedAccess_ReturnsTheSameReference()
        {
            var first = WellBehavedSingleton.Instance;
            var second = WellBehavedSingleton.Instance;

            Assert.AreSame(first, second);
            Assert.AreEqual(1, WellBehavedSingleton.ConstructionCount);
        }

        [Test]
        public void Constructor_CalledDirectlyBeforeFirstAccess_ThrowsInvalidOperationException()
        {
            Assert.IsFalse(DirectNewSingleton.HasInstance);

            Assert.Throws<InvalidOperationException>(() => new DirectNewSingleton());
        }

        [Test]
        public void Constructor_CalledDirectlyAfterFirstAccess_ThrowsInvalidOperationException()
        {
            Assert.IsNotNull(DirectNewSingleton.Instance);

            Assert.Throws<InvalidOperationException>(() => new DirectNewSingleton());
        }

        [Test]
        public void Instance_WhenConstructorThrows_SurfacesTheOriginalExceptionUnwrapped()
        {
            var exception = Assert.Throws<NotSupportedException>(() => _ = ThrowOnceSingleton.Instance);

            Assert.AreEqual(ThrowOnceSingleton.FailureMessage, exception.Message);
            Assert.IsFalse(ThrowOnceSingleton.HasInstance);
        }

        [Test]
        public void Instance_AfterAFailedConstruction_RetriesOnTheNextAccess()
        {
            Assert.Throws<NotSupportedException>(() => _ = ThrowOnceSingleton.Instance);

            var instance = ThrowOnceSingleton.Instance;

            Assert.IsNotNull(instance);
            Assert.IsTrue(ThrowOnceSingleton.HasInstance);
            Assert.AreEqual(2, ThrowOnceSingleton.ConstructionAttempts);
            Assert.AreSame(instance, ThrowOnceSingleton.Instance);
        }

        [Test]
        public void Instance_WhenConstructorResolvesItself_ThrowsInsteadOfOverflowing()
        {
            var exception = Assert.Throws<InvalidOperationException>(
                () => _ = SelfResolvingSingleton.Instance);

            StringAssert.Contains("constructor", exception.Message);
            Assert.IsFalse(SelfResolvingSingleton.HasInstance);
        }

        [Test]
        public void Instance_AccessedConcurrently_CreatesExactlyOneInstance()
        {
            const int workerCount = 8;

            // Touch a static first so the type initializer (and its registry registration) runs on
            // this thread: the race under test is the instance creation, not the class constructor.
            Assert.IsFalse(ParallelSingleton.HasInstance);

            var results = new ParallelSingleton[workerCount];
            var tasks = new Task[workerCount];
            using (var gate = new ManualResetEventSlim(false))
            {
                for (var i = 0; i < workerCount; i++)
                {
                    var index = i;
                    tasks[index] = Task.Run(() =>
                    {
                        gate.Wait();
                        results[index] = ParallelSingleton.Instance;
                    });
                }

                gate.Set();
                Assert.IsTrue(Task.WaitAll(tasks, 5000), "Parallel first-access tasks did not finish in time.");
            }

            Assert.AreEqual(1, ParallelSingleton.ConstructionCount);
            for (var i = 0; i < workerCount; i++)
            {
                Assert.AreSame(results[0], results[i]);
            }
        }

    }
}
