using NUnit.Framework;
using UnityEngine;

namespace UnityEssentials.Colliders.Tests
{
    /// <summary>
    /// The recapture failsafe, driven through <c>RecoveryStep</c> directly: FixedUpdate never runs in
    /// EditMode and Physics.Simulate does not invoke it either, so nothing here is simulated. Bodies
    /// are moved by writing the rigidbody pose and its transform together, which keeps overlap
    /// queries, collider bounds and <c>rb.position</c> telling the same story.
    /// </summary>
    [TestFixture]
    public class EscapeRecoveryTests : ColliderTestFixture
    {
        private const float Tolerance = 0.001f;

        // ---- Recapture ---------------------------------------------------------

        [Test]
        public void RecoveryStep_TrackedBodyMovedOutsideBox_TeleportsBackToLastInsidePosition()
        {
            var container = CreateBoxContainer(4f);
            var body = CreateBody(new Vector3(1f, 0f, 0f));
            var lastInside = body.position;

            container.RecoveryStep();
            PlaceBody(body, new Vector3(5f, 0f, 0f));
            container.RecoveryStep();

            AssertPositionsEqual(lastInside, body.position, "the escaped body was not put back where it was last inside");
        }

        [Test]
        public void RecoveryStep_TrackedBodyMovedOutsideSphere_TeleportsBack()
        {
            var container = CreateSphereContainer(2f);
            var body = CreateBody(new Vector3(0f, 0.5f, 0f));
            var lastInside = body.position;

            container.RecoveryStep();
            PlaceBody(body, new Vector3(0f, 6f, 0f));
            container.RecoveryStep();

            AssertPositionsEqual(lastInside, body.position, "the escaped body was not put back inside the sphere");
        }

        [Test]
        public void RecoveryStep_EscapedBody_RemovesOutwardVelocityComponentKeepsTangential()
        {
            var container = CreateBoxContainer(4f);
            var body = CreateBody(new Vector3(1f, 0f, 0f));

            container.RecoveryStep();
            PlaceBody(body, new Vector3(5f, 0f, 0f));
            body.linearVelocity = new Vector3(3f, 2f, 0f);
            container.RecoveryStep();

            // Outward from the shape centre is +x, so the 3 m/s along it goes and the 2 m/s across it
            // survives — a recaptured body keeps whatever motion was not carrying it out.
            Assert.AreEqual(0f, body.linearVelocity.x, Tolerance);
            Assert.AreEqual(2f, body.linearVelocity.y, Tolerance);
            Assert.AreEqual(0f, body.linearVelocity.z, Tolerance);
            AssertPositionsEqual(new Vector3(1f, 0f, 0f), body.position, "the escaped body was not put back");
        }

        [Test]
        public void RecoveryStep_BodyNeverInside_IsUntouched()
        {
            var container = CreateBoxContainer(4f);
            var body = CreateBody(new Vector3(5f, 0f, 0f));
            body.linearVelocity = new Vector3(1f, 0f, 0f);

            container.RecoveryStep();
            container.RecoveryStep();

            AssertPositionsEqual(new Vector3(5f, 0f, 0f), body.position, "a body that was never inside was pulled in");
            Assert.AreEqual(1f, body.linearVelocity.x, Tolerance);
        }

        [Test]
        public void RecoveryStep_KinematicBody_IsNeverRecaptured()
        {
            var container = CreateBoxContainer(4f);
            var body = CreateBody(new Vector3(1f, 0f, 0f), isKinematic: true);

            container.RecoveryStep();
            PlaceBody(body, new Vector3(5f, 0f, 0f));
            container.RecoveryStep();

            AssertPositionsEqual(new Vector3(5f, 0f, 0f), body.position, "a kinematic body was recaptured");
        }

        [Test]
        public void RecoveryStep_BodyOnExcludedLayer_IsIgnored()
        {
            var container = CreateBoxContainer(4f);
            var body = CreateBody(new Vector3(1f, 0f, 0f));
            Assert.AreEqual(0, body.gameObject.layer, "the body is expected to sit on the Default layer");
            SetField(container, "recoveryLayers", (LayerMask)~(1 << 0));

            container.RecoveryStep();
            PlaceBody(body, new Vector3(5f, 0f, 0f));
            container.RecoveryStep();

            AssertPositionsEqual(new Vector3(5f, 0f, 0f), body.position, "a body on an excluded layer was recaptured");
        }

        [Test]
        public void RecoveryStep_DestroyedTrackedBody_IsPrunedWithoutError()
        {
            var container = CreateBoxContainer(4f);
            var body = CreateBody(new Vector3(1f, 0f, 0f));

            container.RecoveryStep();
            UnityEngine.Object.DestroyImmediate(body.gameObject);

            // Two more steps: the first prunes the dead entry, the second proves the prune stuck. An
            // unexpected error or exception log from either fails this test on its own.
            container.RecoveryStep();
            container.RecoveryStep();

            Assert.IsTrue(body == null);
        }

        [Test]
        public void RecoveryStep_StaleInsidePositionAfterContainerMoved_FallsBackToShapeCenter()
        {
            var container = CreateBoxContainer(4f);
            var body = CreateBody(new Vector3(1.5f, 0f, 0f));

            container.RecoveryStep();

            // The container leaves the body behind: the stored position is now outside the moved
            // volume, so the only target left that is honestly inside is the shape centre.
            container.transform.position = new Vector3(20f, 0f, 0f);
            Physics.SyncTransforms();
            container.RecoveryStep();

            AssertPositionsEqual(
                new Vector3(20f, 0f, 0f),
                body.position,
                "the stale inside position was used instead of the moved shape centre");
        }

        // ---- Containment queries -------------------------------------------------

        [Test]
        public void ContainsPoint_SourceMeshShape_UsesClonedMeshBounds()
        {
            var container = CreateCollider("MeshContainer");
            SetField(container, "shape", InverseColliderShape.SourceMesh);
            SetField(container, "sourceMesh", CreateSphereSourceMesh(2f));
            container.Rebuild();
            Assert.IsTrue(container.IsBuilt, "the container failed to build");

            Assert.IsTrue(container.ContainsPoint(Vector3.zero));
            // 3.29 m from the centre of a 2 m sphere, and still reported as inside: SourceMesh
            // containment is the documented AABB approximation, not a surface test.
            Assert.IsTrue(container.ContainsPoint(new Vector3(1.9f, 1.9f, 1.9f)));
            Assert.IsFalse(container.ContainsPoint(new Vector3(3f, 0f, 0f)));
        }

        // ---- Helpers ---------------------------------------------------------------

        private InverseCollider CreateBoxContainer(float side)
        {
            var container = CreateCollider("BoxContainer");
            SetField(container, "size", new Vector3(side, side, side));
            container.EscapeRecovery = true;
            container.Rebuild();
            Assert.IsTrue(container.IsBuilt, "the container failed to build");
            return container;
        }

        private InverseCollider CreateSphereContainer(float radius)
        {
            var container = CreateCollider("SphereContainer");
            SetField(container, "shape", InverseColliderShape.Sphere);
            SetField(container, "radius", radius);
            container.EscapeRecovery = true;
            container.Rebuild();
            Assert.IsTrue(container.IsBuilt, "the container failed to build");
            return container;
        }

        // Built from the geometry helper because only the bounds matter here, and a sphere is the shape
        // whose AABB corners a surface test would have rejected.
        private Mesh CreateSphereSourceMesh(float radius)
        {
            InverseColliderGeometry.BuildSphereGeometry(
                Vector3.zero, radius, 24, 16, out var vertices, out var triangles);

            var mesh = Track(new Mesh { name = "SphereSource" });
            mesh.vertices = vertices;
            mesh.triangles = triangles;
            mesh.RecalculateBounds();
            return mesh;
        }

        private Rigidbody CreateBody(Vector3 position, bool isKinematic = false)
        {
            var host = Track(new GameObject("Body"));
            host.transform.position = position;
            host.AddComponent<SphereCollider>().radius = 0.25f;

            var body = host.AddComponent<Rigidbody>();
            body.useGravity = false;
            body.isKinematic = isKinematic;
            Physics.SyncTransforms();
            return body;
        }

        // Both poses are written on every move: recovery reads rb.position, while the overlap query and
        // the collider bounds follow the transforms, and the next automatic sync would otherwise push a
        // stale transform straight back over the pose.
        private static void PlaceBody(Rigidbody body, Vector3 position)
        {
            body.transform.position = position;
            body.position = position;
            Physics.SyncTransforms();
        }

        private static void AssertPositionsEqual(Vector3 expected, Vector3 actual, string message)
        {
            Assert.Less(Vector3.Distance(expected, actual), Tolerance, $"{message} (expected {expected}, was {actual})");
        }
    }
}
