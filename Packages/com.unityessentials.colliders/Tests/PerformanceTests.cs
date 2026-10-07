using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools.Constraints;
using Is = UnityEngine.TestTools.Constraints.Is;

namespace UnityEssentials.Colliders.Tests
{
    /// <summary>
    /// Pins the allocation profile of the paths a running game touches every frame: the winding flip
    /// over a buffer that already exists, the <c>IsBuilt</c> query, and a warm recovery step with
    /// nothing escaping. <c>Rebuild</c> and the builders allocate by design — that is build-time cost,
    /// and nothing here pretends otherwise.
    /// </summary>
    [TestFixture]
    public class PerformanceTests : ColliderTestFixture
    {
        // ---- Fixture state -----------------------------------------------------

        // The measured lambdas are deliberately void-returning: a value-returning lambda binds to a
        // different NUnit overload that the Not prefix does not forward to the constraint, so the
        // assertion would pass without measuring anything. The one query answer therefore lands in
        // _sink, typed bool rather than object because boxing would manufacture the allocation under
        // test.
        private static bool _sink;

        [SetUp]
        public void SetUp()
        {
            _sink = false;
        }

        // ---- Geometry ----------------------------------------------------------

        /// <summary>
        /// Flipping an index buffer that already exists allocates nothing: the inversion is an in-place
        /// swap per triple, which is what lets a rebuild pay for the arrays and nothing else.
        /// </summary>
        [Test]
        public void FlipWindingInPlace_WarmBuffer_DoesNotAllocate()
        {
            InverseColliderGeometry.BuildSphereGeometry(Vector3.zero, 1f, 24, 16, out _, out var triangles);
            Assert.AreEqual(2160, triangles.Length);

            // The warm-up flip charges the JIT to setup; the buffer is reused, so the measured flip
            // touches only elements that are already there.
            InverseColliderGeometry.FlipWindingInPlace(triangles);

            Assert.That(
                () => { InverseColliderGeometry.FlipWindingInPlace(triangles); },
                Is.Not.AllocatingGCMemory());
        }

        // ---- Component queries --------------------------------------------------

        /// <summary>
        /// A warm <c>IsBuilt</c> allocates nothing — it is the guard every FixedUpdate runs before it
        /// decides whether there is anything to do.
        /// </summary>
        [Test]
        public void IsBuilt_WarmComponent_DoesNotAllocate()
        {
            var container = CreateCollider();
            container.Rebuild();

            _sink = container.IsBuilt;
            Assert.IsTrue(_sink);

            Assert.That(() => { _sink = container.IsBuilt; }, Is.Not.AllocatingGCMemory());
        }

        // ---- Escape recovery ------------------------------------------------------

        /// <summary>
        /// A warm recovery step with every tracked body still inside allocates nothing: a non-alloc
        /// overlap query, dictionary writes on keys that already exist, and a struct enumerator.
        /// </summary>
        [Test]
        public void RecoveryStep_WarmWithNoEscapes_DoesNotAllocate()
        {
            var container = CreateCollider();
            SetField(container, "size", new Vector3(4f, 4f, 4f));
            container.EscapeRecovery = true;
            container.Rebuild();
            var body = CreateBody(Vector3.zero);

            // The warm-up is a full round of the real thing, and doubles as proof that the measurement
            // is not run against an empty dictionary: the body is tracked, escapes, is recaptured, then
            // goes back inside and is tracked again.
            container.RecoveryStep();
            PlaceBody(body, new Vector3(10f, 0f, 0f));
            container.RecoveryStep();
            Assert.Less(Vector3.Distance(Vector3.zero, body.position), 0.001f, "the warm-up body was never tracked");

            PlaceBody(body, Vector3.zero);
            container.RecoveryStep();

            Assert.That(() => { container.RecoveryStep(); }, Is.Not.AllocatingGCMemory());
        }

        // ---- Helpers ---------------------------------------------------------------

        private Rigidbody CreateBody(Vector3 position)
        {
            var host = Track(new GameObject("WarmBody"));
            host.transform.position = position;
            host.AddComponent<SphereCollider>().radius = 0.25f;

            var body = host.AddComponent<Rigidbody>();
            body.useGravity = false;
            Physics.SyncTransforms();
            return body;
        }

        // Both poses are written on every move, so the automatic transform sync inside a later step
        // cannot push a stale transform back over the pose recovery just set.
        private static void PlaceBody(Rigidbody body, Vector3 position)
        {
            body.transform.position = position;
            body.position = position;
            Physics.SyncTransforms();
        }
    }
}
