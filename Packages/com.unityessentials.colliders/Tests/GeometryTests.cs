// Pure fixture: NUnit plus Vector3/Mathf and nothing else. No UnityEngine.Object is ever created
// here, because this file is also executed outside the Editor by a plain reflection runner — a Mesh,
// a GameObject or a Debug call would put the geometry beyond that runner's reach.
using System;
using NUnit.Framework;
using UnityEngine;

namespace UnityEssentials.Colliders.Tests
{
    /// <summary>
    /// Array-level coverage of <see cref="InverseColliderGeometry"/>: the winding flip that makes a
    /// volume solid from the inside, and the two builders it is applied to. Both shapes are held to
    /// the inwardness invariant and to being a watertight, consistently wound surface.
    /// </summary>
    [TestFixture]
    public class GeometryTests
    {
        private const float Tolerance = 1e-4f;

        // ---- FlipWindingInPlace ---------------------------------------------

        [Test]
        public void FlipWindingInPlace_TriangleList_SwapsSecondAndThirdIndexOfEveryTriple()
        {
            var triangles = new[] { 0, 1, 2, 3, 4, 5, 6, 7, 8 };

            InverseColliderGeometry.FlipWindingInPlace(triangles);

            CollectionAssert.AreEqual(new[] { 0, 2, 1, 3, 5, 4, 6, 8, 7 }, triangles);
        }

        [Test]
        public void FlipWindingInPlace_AppliedTwice_RestoresOriginalOrder()
        {
            var triangles = new[] { 4, 1, 7, 2, 9, 3 };
            var original = (int[])triangles.Clone();

            InverseColliderGeometry.FlipWindingInPlace(triangles);
            InverseColliderGeometry.FlipWindingInPlace(triangles);

            CollectionAssert.AreEqual(original, triangles);
        }

        [Test]
        public void FlipWindingInPlace_EmptyArray_IsNoOp()
        {
            var triangles = new int[0];

            InverseColliderGeometry.FlipWindingInPlace(triangles);

            Assert.AreEqual(0, triangles.Length);
        }

        [Test]
        public void FlipWindingInPlace_NullTriangles_ThrowsArgumentNullException()
        {
            Assert.Throws<ArgumentNullException>(() => InverseColliderGeometry.FlipWindingInPlace(null));
        }

        [Test]
        public void FlipWindingInPlace_LengthNotMultipleOfThree_ThrowsArgumentException()
        {
            Assert.Throws<ArgumentException>(() => InverseColliderGeometry.FlipWindingInPlace(new[] { 0, 1, 2, 3 }));
        }

        // ---- BuildBoxGeometry -----------------------------------------------

        [Test]
        public void BuildBoxGeometry_UnitCube_ProducesEightVerticesAndTwelveTriangles()
        {
            InverseColliderGeometry.BuildBoxGeometry(Vector3.zero, Vector3.one, out var vertices, out var triangles);

            Assert.AreEqual(8, vertices.Length);
            Assert.AreEqual(36, triangles.Length);
        }

        [Test]
        public void BuildBoxGeometry_EveryTriangleFacesCenter()
        {
            var center = new Vector3(1.5f, -2f, 0.25f);

            InverseColliderGeometry.BuildBoxGeometry(
                center, new Vector3(3f, 1f, 2f), out var vertices, out var triangles);

            AssertAllTrianglesFaceInward(vertices, triangles, center);
        }

        [Test]
        public void BuildBoxGeometry_OffsetCenter_TranslatesAllVertices()
        {
            var size = new Vector3(2f, 4f, 6f);
            var offset = new Vector3(-3f, 7f, 0.5f);

            InverseColliderGeometry.BuildBoxGeometry(Vector3.zero, size, out var atOrigin, out var originTriangles);
            InverseColliderGeometry.BuildBoxGeometry(offset, size, out var atOffset, out var offsetTriangles);

            // Only the vertices move: the index table is a property of the corner layout, not of where
            // the box sits.
            CollectionAssert.AreEqual(originTriangles, offsetTriangles);
            Assert.AreEqual(atOrigin.Length, atOffset.Length);

            for (var i = 0; i < atOrigin.Length; i++)
            {
                Assert.Less(
                    Vector3.Distance(atOrigin[i] + offset, atOffset[i]),
                    Tolerance,
                    $"vertex {i} did not translate with the center");
            }
        }

        [Test]
        public void BuildBoxGeometry_VerticesSpanExactHalfExtents()
        {
            var center = new Vector3(1f, 2f, 3f);
            var size = new Vector3(2f, 4f, 6f);
            var half = size * 0.5f;

            InverseColliderGeometry.BuildBoxGeometry(center, size, out var vertices, out _);

            // Every component sits on one half extent or the other, and the eight sign combinations
            // each occur once — which is what makes these the corners of exactly this box.
            var corners = 0;
            for (var i = 0; i < vertices.Length; i++)
            {
                var offset = vertices[i] - center;
                Assert.AreEqual(half.x, Mathf.Abs(offset.x), Tolerance, $"vertex {i} x");
                Assert.AreEqual(half.y, Mathf.Abs(offset.y), Tolerance, $"vertex {i} y");
                Assert.AreEqual(half.z, Mathf.Abs(offset.z), Tolerance, $"vertex {i} z");

                var corner = (offset.x > 0f ? 1 : 0) | (offset.y > 0f ? 2 : 0) | (offset.z > 0f ? 4 : 0);
                corners |= 1 << corner;
            }

            Assert.AreEqual(0xFF, corners, "the eight vertices are not the eight distinct corners");
        }

        [Test]
        public void BuildBoxGeometry_IsWatertightWithConsistentWinding()
        {
            InverseColliderGeometry.BuildBoxGeometry(
                Vector3.zero, new Vector3(2f, 3f, 4f), out _, out var triangles);

            AssertWatertightConsistentlyWound(triangles);
        }

        // ---- BuildSphereGeometry --------------------------------------------

        [Test]
        public void BuildSphereGeometry_DefaultSegments_ProducesExpectedCounts()
        {
            InverseColliderGeometry.BuildSphereGeometry(Vector3.zero, 1f, 24, 16, out var vertices, out var triangles);

            // The component's own defaults: 2 poles + (latitude - 1) rings of longitude vertices, and
            // 2 * longitude * (latitude - 1) triangles.
            Assert.AreEqual(362, vertices.Length);
            Assert.AreEqual(2160, triangles.Length);
        }

        [Test]
        public void BuildSphereGeometry_EveryVertexOnRadius()
        {
            var center = new Vector3(1f, 2f, 3f);
            const float radius = 2.5f;

            InverseColliderGeometry.BuildSphereGeometry(center, radius, 24, 16, out var vertices, out _);

            for (var i = 0; i < vertices.Length; i++)
            {
                Assert.AreEqual(radius, (vertices[i] - center).magnitude, Tolerance, $"vertex {i} is off the sphere");
            }
        }

        [Test]
        public void BuildSphereGeometry_EveryTriangleFacesCenter()
        {
            var center = new Vector3(-4f, 0.5f, 2f);

            InverseColliderGeometry.BuildSphereGeometry(center, 3f, 12, 8, out var vertices, out var triangles);

            AssertAllTrianglesFaceInward(vertices, triangles, center);
        }

        [Test]
        public void BuildSphereGeometry_ContainsNoDegenerateTriangles()
        {
            InverseColliderGeometry.BuildSphereGeometry(Vector3.zero, 1f, 24, 16, out var vertices, out var triangles);

            // The pole bands are where a naive UV sphere emits zero-area triangles, and PhysX cooking
            // rejects meshes that carry them.
            for (var i = 0; i < triangles.Length; i += 3)
            {
                var triangle = i / 3;
                Assert.AreNotEqual(triangles[i], triangles[i + 1], $"triangle {triangle} repeats a vertex index");
                Assert.AreNotEqual(triangles[i + 1], triangles[i + 2], $"triangle {triangle} repeats a vertex index");
                Assert.AreNotEqual(triangles[i + 2], triangles[i], $"triangle {triangle} repeats a vertex index");

                var a = vertices[triangles[i]];
                var b = vertices[triangles[i + 1]];
                var c = vertices[triangles[i + 2]];
                Assert.Greater(Vector3.Cross(b - a, c - a).magnitude, 1e-6f, $"triangle {triangle} has no area");
            }
        }

        [Test]
        public void BuildSphereGeometry_IsWatertightWithConsistentWinding()
        {
            InverseColliderGeometry.BuildSphereGeometry(Vector3.zero, 1f, 24, 16, out _, out var triangles);

            AssertWatertightConsistentlyWound(triangles);
        }

        [Test]
        public void BuildSphereGeometry_LongitudeBelowThree_ThrowsArgumentOutOfRangeException()
        {
            Assert.Throws<ArgumentOutOfRangeException>(
                () => InverseColliderGeometry.BuildSphereGeometry(Vector3.zero, 1f, 2, 16, out _, out _));
        }

        [Test]
        public void BuildSphereGeometry_LatitudeBelowTwo_ThrowsArgumentOutOfRangeException()
        {
            Assert.Throws<ArgumentOutOfRangeException>(
                () => InverseColliderGeometry.BuildSphereGeometry(Vector3.zero, 1f, 24, 1, out _, out _));
        }

        // ---- Helpers ---------------------------------------------------------

        // The inwardness invariant: with g = (a + b + c) / 3, the cross product of the triangle must
        // point at the interior. That single check is what makes a surface solid from the inside only.
        private static void AssertAllTrianglesFaceInward(Vector3[] vertices, int[] triangles, Vector3 interiorPoint)
        {
            for (var i = 0; i < triangles.Length; i += 3)
            {
                var a = vertices[triangles[i]];
                var b = vertices[triangles[i + 1]];
                var c = vertices[triangles[i + 2]];
                var centroid = (a + b + c) / 3f;

                Assert.Greater(
                    Vector3.Dot(Vector3.Cross(b - a, c - a), interiorPoint - centroid),
                    0f,
                    $"triangle {i / 3} ({triangles[i]}, {triangles[i + 1]}, {triangles[i + 2]}) faces away from the interior");
            }
        }

        // Watertight and consistently wound: every directed edge appears exactly once across the whole
        // index list, and the opposite edge exists for each one, so the surface closes with no seam and
        // no flipped face. Packing each pair into a long keeps the check sort-based, which is why this
        // fixture needs no collection types.
        private static void AssertWatertightConsistentlyWound(int[] triangles)
        {
            var edges = new long[triangles.Length];
            for (var i = 0; i < triangles.Length; i += 3)
            {
                edges[i] = PackEdge(triangles[i], triangles[i + 1]);
                edges[i + 1] = PackEdge(triangles[i + 1], triangles[i + 2]);
                edges[i + 2] = PackEdge(triangles[i + 2], triangles[i]);
            }

            Array.Sort(edges);

            for (var i = 1; i < edges.Length; i++)
            {
                Assert.AreNotEqual(
                    edges[i - 1],
                    edges[i],
                    $"directed edge {DescribeEdge(edges[i])} appears more than once — two faces share a winding");
            }

            for (var i = 0; i < edges.Length; i++)
            {
                var reverse = PackEdge((int)(edges[i] & 0xFFFFFFFFL), (int)(edges[i] >> 32));
                Assert.GreaterOrEqual(
                    Array.BinarySearch(edges, reverse),
                    0,
                    $"directed edge {DescribeEdge(edges[i])} has no opposite — the surface is not closed");
            }
        }

        private static long PackEdge(int from, int to)
        {
            return ((long)from << 32) | (uint)to;
        }

        private static string DescribeEdge(long edge)
        {
            return $"({edge >> 32} -> {edge & 0xFFFFFFFFL})";
        }
    }
}
