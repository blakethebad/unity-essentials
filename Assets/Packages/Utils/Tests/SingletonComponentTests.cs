using System.Collections.Generic;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace UnityEssentials.Utilities.Tests
{
    /// <summary>
    /// EditMode coverage for the MonoBehaviour singleton: auto-creation, discovery, duplicate
    /// handling, the quitting guard and the reset hook. Awake is driven through the test seam
    /// because AddComponent does not call it outside play mode.
    /// </summary>
    [TestFixture]
    public class SingletonComponentTests
    {
        private readonly List<GameObject> _createdObjects = new List<GameObject>();

        [SetUp]
        public void SetUp()
        {
            SingletonComponentRuntime.IsQuitting = false;
            StaticResetRegistry.ResetStatics();
        }

        [TearDown]
        public void TearDown()
        {
            SingletonComponentRuntime.IsQuitting = false;

            if (TestSingletonComponent.HasInstance)
            {
                Track(TestSingletonComponent.Instance.gameObject);
            }

            for (var i = 0; i < _createdObjects.Count; i++)
            {
                if (_createdObjects[i] != null)
                {
                    Object.DestroyImmediate(_createdObjects[i]);
                }
            }

            _createdObjects.Clear();
            StaticResetRegistry.ResetStatics();
        }

        [Test]
        public void Instance_WithNothingInTheScene_CreatesAHiddenHostGameObject()
        {
            Assert.IsFalse(TestSingletonComponent.HasInstance);

            var instance = TestSingletonComponent.Instance;

            Assert.IsFalse(instance == null);
            Track(instance.gameObject);
            Assert.AreEqual(HideFlags.HideAndDontSave, instance.gameObject.hideFlags);
            Assert.IsTrue(TestSingletonComponent.HasInstance);
            Assert.AreSame(instance, TestSingletonComponent.Instance);
        }

        [Test]
        public void Instance_WithAComponentAlreadyInTheScene_FindsAndCachesIt()
        {
            var existing = CreateComponent("Existing");

            var instance = TestSingletonComponent.Instance;

            Assert.AreSame(existing, instance);
            Assert.AreEqual(HideFlags.None, instance.gameObject.hideFlags);
            Assert.AreSame(existing, TestSingletonComponent.Instance);
        }

        [Test]
        public void Awake_OnADuplicate_DestroysOnlyTheComponentAndKeepsTheGameObject()
        {
            var original = CreateComponent("Original");
            original.InvokeAwake();

            var duplicate = CreateComponent("Duplicate");
            var duplicateHost = duplicate.gameObject;
            LogAssert.Expect(LogType.Warning, new Regex("duplicate"));

            duplicate.InvokeAwake();

            // Unity's == is required: the managed reference outlives the destroyed component.
            Assert.IsTrue(duplicate == null);
            Assert.IsFalse(duplicateHost == null);
            Assert.AreSame(original, TestSingletonComponent.Instance);
        }

        [Test]
        public void Instance_WhileQuitting_LogsAWarningAndReturnsNull()
        {
            SingletonComponentRuntime.IsQuitting = true;
            LogAssert.Expect(LogType.Warning, new Regex("quitting"));

            var instance = TestSingletonComponent.Instance;

            Assert.IsNull(instance);
            Assert.IsFalse(TestSingletonComponent.HasInstance);
        }

        [Test]
        public void ResetStatics_ClearsTheCachedInstance()
        {
            var existing = CreateComponent("Cached");
            existing.InvokeAwake();
            Assert.IsTrue(TestSingletonComponent.HasInstance);

            StaticResetRegistry.ResetStatics();

            Assert.IsFalse(TestSingletonComponent.HasInstance);
            Assert.IsFalse(existing == null);
        }

        private TestSingletonComponent CreateComponent(string hostName)
        {
            var host = new GameObject(hostName);
            Track(host);
            return host.AddComponent<TestSingletonComponent>();
        }

        private void Track(GameObject host)
        {
            if (host != null && !_createdObjects.Contains(host))
            {
                _createdObjects.Add(host);
            }
        }
    }
}
