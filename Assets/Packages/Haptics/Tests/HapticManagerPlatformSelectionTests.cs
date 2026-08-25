using NUnit.Framework;

namespace UnityEssentials.Haptics.Tests
{
    /// <summary>
    /// The regression fixture for <see cref="HapticManager.CreateProvider"/>, the package's
    /// platform-selection seam. It pins the two properties the manager relies on: the Editor always
    /// gets <see cref="NullHapticProvider"/>, and every call hands back a fresh instance rather than
    /// a cached one.
    /// </summary>
    /// <remarks>
    /// <see cref="HapticManager.CreateProvider"/> is a pure factory — it reads nothing and assigns
    /// nothing — so this fixture deliberately has no <c>SetUp</c> or <c>TearDown</c> and never calls
    /// <c>ResetStatics</c>: there is no manager state for these tests to dirty, and reaching for the
    /// reset would imply otherwise. Nothing here constructs a Unity object either, which keeps the
    /// fixture runnable outside the Editor as well as inside the EditMode runner.
    /// </remarks>
    [TestFixture]
    public class HapticManagerPlatformSelectionTests
    {
        [Test]
        public void CreateProvider_UnderTheEditorTestRunner_ReturnsNullProvider()
        {
            // This is the whole point of the fixture. UNITY_IOS and UNITY_ANDROID are defined inside
            // the Editor whenever that platform is the active build target, so if the mandatory
            // !UNITY_EDITOR guards were ever dropped, an EditMode run would construct a live
            // JNI/P-Invoke provider — this test is what fails first when that happens.
            var created = HapticManager.CreateProvider();

            Assert.IsInstanceOf<NullHapticProvider>(created);
        }

        [Test]
        public void CreateProvider_UnderTheEditorTestRunner_ReturnsAnUnsupportedProvider()
        {
            var created = HapticManager.CreateProvider();

            Assert.IsFalse(created.IsSupported);
            Assert.IsFalse(created.SupportsPatterns);
        }

        [Test]
        public void CreateProvider_ReturnsAFreshInstancePerCall()
        {
            var first = HapticManager.CreateProvider();
            var second = HapticManager.CreateProvider();

            Assert.IsNotNull(first);
            Assert.IsNotNull(second);
            Assert.AreNotSame(first, second);
        }
    }
}
