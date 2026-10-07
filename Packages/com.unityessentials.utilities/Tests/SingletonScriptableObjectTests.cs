using System;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using Object = UnityEngine.Object;

namespace UnityEssentials.Utilities.Tests
{
    /// <summary>
    /// EditMode coverage for the asset-backed singleton through its seams: ResolveInstance for the
    /// zero/one/many cases and SetInstanceForTests for injection. The repo holds no Resources folder,
    /// so the real Resources lookup is a manual play-mode check.
    /// </summary>
    [TestFixture]
    public class SingletonScriptableObjectTests
    {
        private readonly List<TestSingletonAsset> _createdAssets = new List<TestSingletonAsset>();

        [SetUp]
        public void SetUp()
        {
            TestSingletonAsset.SetInstanceForTests(null);
        }

        [TearDown]
        public void TearDown()
        {
            TestSingletonAsset.SetInstanceForTests(null);

            for (var i = 0; i < _createdAssets.Count; i++)
            {
                if (_createdAssets[i] != null)
                {
                    Object.DestroyImmediate(_createdAssets[i]);
                }
            }

            _createdAssets.Clear();
        }

        [Test]
        public void ResolveInstance_WithNoCandidates_ThrowsWithResourcesGuidance()
        {
            var exception = Assert.Throws<InvalidOperationException>(
                () => TestSingletonAsset.ResolveInstance(Array.Empty<TestSingletonAsset>()));

            StringAssert.Contains(nameof(TestSingletonAsset), exception.Message);
            StringAssert.Contains("Resources", exception.Message);
            Assert.IsFalse(TestSingletonAsset.HasInstance);
        }

        [Test]
        public void ResolveInstance_WithSeveralCandidates_ThrowsListingTheAssetNames()
        {
            var first = CreateAsset("Alpha");
            var second = CreateAsset("Beta");

            var exception = Assert.Throws<InvalidOperationException>(
                () => TestSingletonAsset.ResolveInstance(new[] { first, second }));

            StringAssert.Contains("Alpha", exception.Message);
            StringAssert.Contains("Beta", exception.Message);
            Assert.IsFalse(TestSingletonAsset.HasInstance);
        }

        [Test]
        public void ResolveInstance_WithASingleCandidate_CachesAndReturnsIt()
        {
            var asset = CreateAsset("Solo");

            var resolved = TestSingletonAsset.ResolveInstance(new[] { asset });

            Assert.AreSame(asset, resolved);
            Assert.IsTrue(TestSingletonAsset.HasInstance);
            Assert.AreSame(asset, TestSingletonAsset.Instance);
        }

        [Test]
        public void SetInstanceForTests_MakesInstanceReturnTheInjectedAsset()
        {
            var asset = CreateAsset("Injected");

            TestSingletonAsset.SetInstanceForTests(asset);

            Assert.IsTrue(TestSingletonAsset.HasInstance);
            Assert.AreSame(asset, TestSingletonAsset.Instance);
        }

        private TestSingletonAsset CreateAsset(string assetName)
        {
            var asset = ScriptableObject.CreateInstance<TestSingletonAsset>();
            asset.name = assetName;
            _createdAssets.Add(asset);
            return asset;
        }
    }
}
