using System;
using System.Threading;
using NUnit.Framework;

namespace UnityEssentials.Utilities.Tests
{
    /// <summary>
    /// Covers the package's main-thread contract: the guard passes on the thread that first touched
    /// it and throws for every other one.
    /// </summary>
    [TestFixture]
    public class MainThreadGuardTests
    {
        [Test]
        public void AssertMainThread_OnMainThread_DoesNotThrow()
        {
            Assert.DoesNotThrow(() => MainThreadGuard.AssertMainThread());
        }

        [Test]
        public void AssertMainThread_FromWorkerThread_ThrowsInvalidOperationException()
        {
            MainThreadGuard.AssertMainThread();

            Exception captured = null;
            var worker = new Thread(() =>
            {
                try
                {
                    MainThreadGuard.AssertMainThread();
                }
                catch (Exception exception)
                {
                    captured = exception;
                }
            });

            worker.Start();
            worker.Join();

            Assert.IsInstanceOf<InvalidOperationException>(captured);
        }
    }
}
