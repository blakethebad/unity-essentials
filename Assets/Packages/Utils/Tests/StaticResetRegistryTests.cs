using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using System.Threading;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace UnityEssentials.Utilities.Tests
{
    /// <summary>
    /// Covers the shared reset plumbing: registration validation, retention of actions across
    /// resets, isolation from a throwing action, main-thread id recapture and the thread guard.
    /// </summary>
    [TestFixture]
    public class StaticResetRegistryTests
    {
        private const string ProbeFailureMessage = "static reset probe failure";

        private static readonly List<string> _invocations = new List<string>();

        private static bool _probesRegistered;
        private static bool _probesActive;
        private static bool _firstProbeThrows;

        [OneTimeSetUp]
        public void OneTimeSetUp()
        {
            // The registry retains actions for the lifetime of the domain, so the fixture registers
            // its probes exactly once and keeps them inert unless the running test activates them.
            // Anything else would leak failures into every other fixture that resets statics.
            if (_probesRegistered)
            {
                return;
            }

            _probesRegistered = true;

            StaticResetRegistry.Register(() =>
            {
                if (!_probesActive)
                {
                    return;
                }

                _invocations.Add("first");
                if (_firstProbeThrows)
                {
                    throw new InvalidOperationException(ProbeFailureMessage);
                }
            });

            StaticResetRegistry.Register(() =>
            {
                if (_probesActive)
                {
                    _invocations.Add("second");
                }
            });
        }

        [SetUp]
        public void SetUp()
        {
            _invocations.Clear();
            _firstProbeThrows = false;
            _probesActive = true;
        }

        [TearDown]
        public void TearDown()
        {
            _probesActive = false;
            _firstProbeThrows = false;
            _invocations.Clear();
        }

        [Test]
        public void Register_NullAction_ThrowsArgumentNullException()
        {
            Assert.Throws<ArgumentNullException>(() => StaticResetRegistry.Register(null));
        }

        [Test]
        public void ResetStatics_InvokesRegisteredActions()
        {
            StaticResetRegistry.ResetStatics();

            CollectionAssert.AreEqual(new[] { "first", "second" }, _invocations);
        }

        [Test]
        public void ResetStatics_RetainsActions_SoASecondResetInvokesThemAgain()
        {
            StaticResetRegistry.ResetStatics();
            StaticResetRegistry.ResetStatics();

            CollectionAssert.AreEqual(new[] { "first", "second", "first", "second" }, _invocations);
        }

        [Test]
        public void ResetStatics_ThrowingAction_DoesNotPreventLaterActions()
        {
            _firstProbeThrows = true;
            LogAssert.Expect(LogType.Exception, new Regex(ProbeFailureMessage));

            StaticResetRegistry.ResetStatics();

            CollectionAssert.AreEqual(new[] { "first", "second" }, _invocations);
        }

        [Test]
        public void ResetStatics_RecapturesMainThreadId()
        {
            StaticResetRegistry.MainThreadId = -1;

            StaticResetRegistry.ResetStatics();

            Assert.AreEqual(Environment.CurrentManagedThreadId, StaticResetRegistry.MainThreadId);
        }

        [Test]
        public void AssertMainThread_OnMainThread_DoesNotThrow()
        {
            Assert.DoesNotThrow(() => StaticResetRegistry.AssertMainThread());
        }

        [Test]
        public void AssertMainThread_FromWorkerThread_ThrowsInvalidOperationException()
        {
            Exception captured = null;
            var worker = new Thread(() =>
            {
                try
                {
                    StaticResetRegistry.AssertMainThread();
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
