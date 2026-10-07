using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace UnityEssentials.Colliders.Tests
{
    /// <summary>
    /// Build, rebuild and teardown of the component itself: what lands on the owned MeshCollider, how a
    /// source mesh is cloned and inverted, and every guard that warns and skips rather than leaving
    /// stale walls behind. Awake never runs on AddComponent in EditMode, so each build is driven by
    /// calling <c>Rebuild</c> directly.
    /// </summary>
    [TestFixture]
    public class ComponentTests : ColliderTestFixture
    {
        // ---- Build ------------------------------------------------------------

        [Test]
        public void Rebuild_BoxShape_CreatesNonConvexMeshColliderWithGeneratedMesh()
        {
            var container = CreateCollider();

            container.Rebuild();

            Assert.IsTrue(container.IsBuilt);
            Assert.IsFalse(container.OwnedCollider == null);
            Assert.AreSame(container.OwnedCollider, container.GetComponent<MeshCollider>());
            Assert.IsFalse(container.OwnedCollider.convex, "a convex cook would make the walls solid from the outside");
            Assert.AreSame(container.GeneratedMesh, container.OwnedCollider.sharedMesh);
            Assert.AreEqual(8, container.GeneratedMesh.vertexCount);
            Assert.AreEqual(36, container.GeneratedMesh.triangles.Length);
        }

        [Test]
        public void Rebuild_GeneratedMesh_HasHideAndDontSaveFlags()
        {
            var container = CreateCollider();

            container.Rebuild();

            Assert.AreEqual(HideFlags.HideAndDontSave, container.GeneratedMesh.hideFlags);
        }

        [Test]
        public void Rebuild_CalledTwice_DestroysPreviousMeshAndAssignsNewOne()
        {
            var container = CreateCollider();
            container.Rebuild();
            var first = container.GeneratedMesh;

            container.Rebuild();

            var second = container.GeneratedMesh;
            Assert.AreNotSame(first, second);
            // Unity's ==: the managed reference outlives the destroyed mesh.
            Assert.IsTrue(first == null, "the previous generated mesh leaked");
            Assert.IsFalse(second == null);
            Assert.AreSame(second, container.OwnedCollider.sharedMesh);
        }

        [Test]
        public void Rebuild_WithPhysicsMaterial_AssignsSharedMaterial()
        {
            var container = CreateCollider();
            var material = Track(new PhysicsMaterial("InverseColliderTestMaterial"));
            SetField(container, "material", material);

            container.Rebuild();

            Assert.AreSame(material, container.OwnedCollider.sharedMaterial);
        }

        [Test]
        public void Rebuild_SphereShape_BuildsMeshWithExpectedVertexCount()
        {
            var container = CreateCollider();
            SetField(container, "shape", InverseColliderShape.Sphere);

            container.Rebuild();

            // The serialized defaults are 24 x 16.
            Assert.IsTrue(container.IsBuilt);
            Assert.AreEqual(362, container.GeneratedMesh.vertexCount);
            Assert.AreEqual(2160, container.GeneratedMesh.triangles.Length);

            // And the segment fields are read, not baked: 8 x 4 is 2 poles + 3 rings of 8.
            SetField(container, "longitudeSegments", 8);
            SetField(container, "latitudeSegments", 4);

            container.Rebuild();

            Assert.AreEqual(26, container.GeneratedMesh.vertexCount);
            Assert.AreEqual(144, container.GeneratedMesh.triangles.Length);
        }

        // ---- Source mesh ------------------------------------------------------

        [Test]
        public void Rebuild_SourceMeshShape_ClonesWithoutTouchingSource()
        {
            var container = CreateCollider();
            var source = CreateQuadMesh();
            var sourceTriangles = source.triangles;
            SetField(container, "shape", InverseColliderShape.SourceMesh);
            SetField(container, "sourceMesh", source);

            container.Rebuild();

            Assert.IsTrue(container.IsBuilt);
            Assert.AreNotSame(source, container.GeneratedMesh);
            CollectionAssert.AreEqual(sourceTriangles, source.triangles, "the source mesh was inverted in place");
            CollectionAssert.AreEqual(new[] { 0, 2, 1, 0, 3, 2 }, container.GeneratedMesh.triangles);
            Assert.AreEqual(source.vertexCount, container.GeneratedMesh.vertexCount);
        }

        [Test]
        public void Rebuild_SourceMeshShape_CombinesAllSubmeshes()
        {
            var container = CreateCollider();
            SetField(container, "shape", InverseColliderShape.SourceMesh);
            SetField(container, "sourceMesh", CreateTwoSubmeshMesh());

            container.Rebuild();

            // Submesh 0 then submesh 1, concatenated into one triangle list and flipped once.
            Assert.AreEqual(1, container.GeneratedMesh.subMeshCount);
            CollectionAssert.AreEqual(new[] { 0, 2, 1, 0, 3, 2 }, container.GeneratedMesh.triangles);
        }

        [Test]
        public void Rebuild_SourceMeshNullWithSiblingMeshFilter_UsesFilterSharedMesh()
        {
            var container = CreateCollider();
            var source = CreateQuadMesh();
            container.gameObject.AddComponent<MeshFilter>().sharedMesh = source;
            SetField(container, "shape", InverseColliderShape.SourceMesh);

            container.Rebuild();

            Assert.IsTrue(container.IsBuilt);
            Assert.AreEqual(source.vertexCount, container.GeneratedMesh.vertexCount);
            CollectionAssert.AreEqual(new[] { 0, 2, 1, 0, 3, 2 }, container.GeneratedMesh.triangles);
        }

        [Test]
        public void Rebuild_SourceMeshMissingEverywhere_WarnsAndSkipsBuild()
        {
            var container = CreateCollider();
            SetField(container, "shape", InverseColliderShape.SourceMesh);
            LogAssert.Expect(LogType.Warning, new Regex("no source mesh"));

            container.Rebuild();

            Assert.IsFalse(container.IsBuilt);
            Assert.IsTrue(container.OwnedCollider == null);
        }

        [Test]
        public void Rebuild_UnreadableSourceMesh_WarnsAndSkipsBuild()
        {
            var container = CreateCollider();
            var source = CreateQuadMesh("UnreadableQuad");
            source.UploadMeshData(true);

            // Precondition, not decoration: if the Editor ever kept an uploaded mesh readable, the
            // guard below would go untested and this fails loudly instead.
            Assert.IsFalse(source.isReadable, "UploadMeshData(true) no longer clears isReadable in the Editor");

            SetField(container, "shape", InverseColliderShape.SourceMesh);
            SetField(container, "sourceMesh", source);
            LogAssert.Expect(LogType.Warning, new Regex("not readable"));

            container.Rebuild();

            Assert.IsFalse(container.IsBuilt);
        }

        [Test]
        public void Rebuild_NonTriangleSubmesh_WarnsAndKeepsTriangleSubmeshes()
        {
            var container = CreateCollider();
            var source = CreateQuadMesh("MixedTopologyQuad");
            // A second submesh of lines over the same vertices: PhysX cooks triangles only, so the
            // triangle submesh survives and the rest is reported and dropped.
            source.subMeshCount = 2;
            source.SetIndices(new[] { 0, 1, 1, 2 }, MeshTopology.Lines, 1);
            SetField(container, "shape", InverseColliderShape.SourceMesh);
            SetField(container, "sourceMesh", source);
            LogAssert.Expect(LogType.Warning, new Regex("non-triangle submeshes"));

            container.Rebuild();

            Assert.IsTrue(container.IsBuilt);
            CollectionAssert.AreEqual(new[] { 0, 2, 1, 0, 3, 2 }, container.GeneratedMesh.triangles);
        }

        [Test]
        public void Rebuild_SourceMeshWithOnlyNonTriangleSubmeshes_WarnsAndSkipsBuild()
        {
            var container = CreateCollider();
            var source = Track(new Mesh { name = "LinesOnly" });
            source.vertices = new[]
            {
                new Vector3(0f, 0f, 0f),
                new Vector3(1f, 0f, 0f),
                new Vector3(1f, 1f, 0f)
            };
            source.SetIndices(new[] { 0, 1, 1, 2 }, MeshTopology.Lines, 0);
            SetField(container, "shape", InverseColliderShape.SourceMesh);
            SetField(container, "sourceMesh", source);
            // Both warnings, in emit order: the skipped submesh is reported first, the empty result
            // that makes the build impossible second.
            LogAssert.Expect(LogType.Warning, new Regex("non-triangle submeshes"));
            LogAssert.Expect(LogType.Warning, new Regex("no triangle submeshes"));

            container.Rebuild();

            Assert.IsFalse(container.IsBuilt);
            Assert.IsTrue(container.OwnedCollider == null);
        }

        // ---- Guards -----------------------------------------------------------

        [Test]
        public void Rebuild_NegativeLossyScale_WarnsAndStillBuilds()
        {
            var parent = Track(new GameObject("MirroredParent"));
            parent.transform.localScale = new Vector3(-1f, 1f, 1f);
            var container = CreateCollider();
            container.transform.SetParent(parent.transform, false);
            LogAssert.Expect(LogType.Warning, new Regex("negative scale"));

            container.Rebuild();

            // The warning is advice, not a veto: containment is inverted, but the collider is real.
            Assert.IsTrue(container.IsBuilt);
            Assert.IsFalse(container.OwnedCollider == null);
        }

        [Test]
        public void Rebuild_NonKinematicRigidbodyOnContainer_WarnsAndSkipsBuild()
        {
            var container = CreateCollider();
            var body = container.gameObject.AddComponent<Rigidbody>();
            body.isKinematic = false;
            LogAssert.Expect(LogType.Warning, new Regex("non-kinematic Rigidbody"));

            container.Rebuild();

            // Nothing is cooked, so PhysX never gets to log its own error about the combination —
            // an unexpected error log would fail this test on its own.
            Assert.IsFalse(container.IsBuilt);
            Assert.IsTrue(container.OwnedCollider == null);
        }

        [Test]
        public void Rebuild_KinematicRigidbodyOnContainer_Builds()
        {
            var container = CreateCollider();
            var body = container.gameObject.AddComponent<Rigidbody>();
            body.isKinematic = true;

            container.Rebuild();

            Assert.IsTrue(container.IsBuilt);
            Assert.IsFalse(container.OwnedCollider.convex);
        }

        [Test]
        public void Rebuild_UnknownShapeValue_WarnsAndSkipsBuild()
        {
            var container = CreateCollider();
            SetField(container, "shape", (InverseColliderShape)999);
            LogAssert.Expect(LogType.Warning, new Regex("unknown shape"));

            container.Rebuild();

            Assert.IsFalse(container.IsBuilt);
            Assert.IsTrue(container.OwnedCollider == null);
        }

        [Test]
        public void Rebuild_AfterFailedRebuild_RecoversAndBuilds()
        {
            var container = CreateCollider();
            container.Rebuild();
            var collider = container.OwnedCollider;

            SetField(container, "shape", InverseColliderShape.SourceMesh);
            LogAssert.Expect(LogType.Warning, new Regex("no source mesh"));
            container.Rebuild();

            // The failure clears the walls but keeps the collider, so the component can build again.
            Assert.IsFalse(container.IsBuilt);
            Assert.IsFalse(collider == null);
            Assert.IsTrue(collider.sharedMesh == null, "a failed rebuild left the previous walls in place");

            SetField(container, "shape", InverseColliderShape.Box);
            container.Rebuild();

            Assert.IsTrue(container.IsBuilt);
            Assert.AreSame(collider, container.OwnedCollider);
            Assert.AreSame(container.GeneratedMesh, collider.sharedMesh);
        }

        // ---- Teardown and state ------------------------------------------------

        [Test]
        public void TearDownBuild_AfterRebuild_DestroysGeneratedMeshAndOwnedCollider()
        {
            var container = CreateCollider();
            container.Rebuild();
            var mesh = container.GeneratedMesh;
            var collider = container.OwnedCollider;

            container.TearDownBuild();

            Assert.IsFalse(container.IsBuilt);
            Assert.IsTrue(mesh == null);
            Assert.IsTrue(collider == null);
            Assert.IsTrue(container.GeneratedMesh == null);
            Assert.IsTrue(container.OwnedCollider == null);
        }

        [Test]
        public void TearDownBuild_WithoutBuild_IsSafeNoOp()
        {
            var container = CreateCollider();

            container.TearDownBuild();

            Assert.IsFalse(container.IsBuilt);
            Assert.IsTrue(container.GeneratedMesh == null);
            Assert.IsTrue(container.OwnedCollider == null);
            Assert.IsFalse(container == null);
        }

        [Test]
        public void OnValidate_OutOfRangeFields_ClampsIntoRange()
        {
            var container = CreateCollider();
            SetField(container, "size", new Vector3(0f, -3f, 0.0005f));
            SetField(container, "radius", -1f);
            SetField(container, "longitudeSegments", 200);
            SetField(container, "latitudeSegments", 1);

            container.OnValidate();

            var size = GetField<Vector3>(container, "size");
            Assert.AreEqual(0.001f, size.x, 1e-6f);
            Assert.AreEqual(0.001f, size.y, 1e-6f);
            Assert.AreEqual(0.001f, size.z, 1e-6f);
            Assert.AreEqual(0.001f, GetField<float>(container, "radius"), 1e-6f);
            Assert.AreEqual(64, GetField<int>(container, "longitudeSegments"));
            Assert.AreEqual(4, GetField<int>(container, "latitudeSegments"));

            // The other end of both segment ranges: the quality floor is stricter than the builders'
            // structural minimum of 3 and 2.
            SetField(container, "longitudeSegments", 2);
            SetField(container, "latitudeSegments", 99);

            container.OnValidate();

            Assert.AreEqual(8, GetField<int>(container, "longitudeSegments"));
            Assert.AreEqual(32, GetField<int>(container, "latitudeSegments"));
        }

        [Test]
        public void IsBuilt_BeforeRebuild_ReportsFalse()
        {
            var container = CreateCollider();

            Assert.IsFalse(container.IsBuilt);
            Assert.IsTrue(container.GeneratedMesh == null);
            Assert.IsTrue(container.OwnedCollider == null);
        }

        // ---- Helpers ------------------------------------------------------------

        // Two triangles over four corners, wound counter-clockwise seen from +Z, so the inverted clone
        // is exactly { 0, 2, 1, 0, 3, 2 }.
        private Mesh CreateQuadMesh(string name = "Quad")
        {
            var mesh = Track(new Mesh { name = name });
            mesh.vertices = new[]
            {
                new Vector3(-0.5f, -0.5f, 0f),
                new Vector3(0.5f, -0.5f, 0f),
                new Vector3(0.5f, 0.5f, 0f),
                new Vector3(-0.5f, 0.5f, 0f)
            };
            mesh.triangles = new[] { 0, 1, 2, 0, 2, 3 };
            mesh.RecalculateBounds();
            return mesh;
        }

        // The same quad split across two triangle submeshes, so a combine pass that stops after the
        // first one loses half the surface.
        private Mesh CreateTwoSubmeshMesh(string name = "TwoSubmeshQuad")
        {
            var mesh = Track(new Mesh { name = name });
            mesh.vertices = new[]
            {
                new Vector3(-0.5f, -0.5f, 0f),
                new Vector3(0.5f, -0.5f, 0f),
                new Vector3(0.5f, 0.5f, 0f),
                new Vector3(-0.5f, 0.5f, 0f)
            };
            mesh.subMeshCount = 2;
            mesh.SetTriangles(new[] { 0, 1, 2 }, 0);
            mesh.SetTriangles(new[] { 0, 2, 3 }, 1);
            mesh.RecalculateBounds();
            return mesh;
        }
    }
}
