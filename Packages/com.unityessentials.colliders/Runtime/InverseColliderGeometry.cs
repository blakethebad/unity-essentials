using System;
using UnityEngine;

namespace UnityEssentials.Colliders
{
    /// <summary>
    /// Pure vertex and index builders for the inverse collider's primitive shapes. Every builder
    /// authors standard outward winding first, then flips it exactly once as its final step, so the
    /// finished triangles face the shape's interior and PhysX presents solid inner walls. Free of
    /// engine object types, logging and scene state, so it is testable in isolation.
    /// </summary>
    internal static class InverseColliderGeometry
    {
        internal static void FlipWindingInPlace(int[] triangles)
        {
            if (triangles == null)
            {
                throw new ArgumentNullException(nameof(triangles));
            }

            if (triangles.Length % 3 != 0)
            {
                throw new ArgumentException("Triangle index count must be a multiple of three.", nameof(triangles));
            }

            for (int i = 0; i < triangles.Length; i += 3)
            {
                int second = triangles[i + 1];
                triangles[i + 1] = triangles[i + 2];
                triangles[i + 2] = second;
            }
        }

        internal static void BuildBoxGeometry(Vector3 center, Vector3 size, out Vector3[] vertices, out int[] triangles)
        {
            Vector3 h = size * 0.5f;

            vertices = new Vector3[]
            {
                center + new Vector3(-h.x, -h.y, -h.z),
                center + new Vector3(+h.x, -h.y, -h.z),
                center + new Vector3(+h.x, -h.y, +h.z),
                center + new Vector3(-h.x, -h.y, +h.z),
                center + new Vector3(-h.x, +h.y, -h.z),
                center + new Vector3(+h.x, +h.y, -h.z),
                center + new Vector3(+h.x, +h.y, +h.z),
                center + new Vector3(-h.x, +h.y, +h.z),
            };

            // Standard outward winding; the flip below is the single inversion point for every shape.
            triangles = new int[]
            {
                0, 1, 2, 0, 2, 3, // bottom (-y)
                4, 6, 5, 4, 7, 6, // top (+y)
                0, 5, 1, 0, 4, 5, // front (-z)
                3, 2, 6, 3, 6, 7, // back (+z)
                0, 7, 4, 0, 3, 7, // left (-x)
                1, 5, 6, 1, 6, 2, // right (+x)
            };

            FlipWindingInPlace(triangles);
        }

        internal static void BuildSphereGeometry(Vector3 center, float radius, int longitudeSegments, int latitudeSegments, out Vector3[] vertices, out int[] triangles)
        {
            if (longitudeSegments < 3)
            {
                throw new ArgumentOutOfRangeException(nameof(longitudeSegments), longitudeSegments, "A sphere needs at least 3 longitude segments.");
            }

            if (latitudeSegments < 2)
            {
                throw new ArgumentOutOfRangeException(nameof(latitudeSegments), latitudeSegments, "A sphere needs at least 2 latitude segments.");
            }

            int ringCount = latitudeSegments - 1;
            vertices = new Vector3[2 + ringCount * longitudeSegments];

            int northPole = 0;
            int southPole = vertices.Length - 1;
            vertices[northPole] = center + new Vector3(0f, radius, 0f);
            vertices[southPole] = center + new Vector3(0f, -radius, 0f);

            for (int ring = 0; ring < ringCount; ring++)
            {
                float theta = Mathf.PI * (ring + 1) / latitudeSegments;
                float sinTheta = Mathf.Sin(theta);
                float cosTheta = Mathf.Cos(theta);
                int rowStart = 1 + ring * longitudeSegments;

                for (int lon = 0; lon < longitudeSegments; lon++)
                {
                    float phi = 2f * Mathf.PI * lon / longitudeSegments;
                    vertices[rowStart + lon] = center + radius * new Vector3(sinTheta * Mathf.Cos(phi), cosTheta, sinTheta * Mathf.Sin(phi));
                }
            }

            triangles = new int[6 * longitudeSegments * ringCount];
            int write = 0;
            int firstRing = 1;
            int lastRing = 1 + (ringCount - 1) * longitudeSegments;

            // Pole bands emit one triangle per longitude: the other half of the quad collapses onto the pole.
            for (int lon = 0; lon < longitudeSegments; lon++)
            {
                int next = (lon + 1) % longitudeSegments;
                triangles[write++] = northPole;
                triangles[write++] = firstRing + next;
                triangles[write++] = firstRing + lon;
            }

            for (int ring = 0; ring < ringCount - 1; ring++)
            {
                int upper = 1 + ring * longitudeSegments;
                int lower = upper + longitudeSegments;

                for (int lon = 0; lon < longitudeSegments; lon++)
                {
                    int next = (lon + 1) % longitudeSegments;
                    triangles[write++] = upper + lon;
                    triangles[write++] = upper + next;
                    triangles[write++] = lower + next;
                    triangles[write++] = upper + lon;
                    triangles[write++] = lower + next;
                    triangles[write++] = lower + lon;
                }
            }

            for (int lon = 0; lon < longitudeSegments; lon++)
            {
                int next = (lon + 1) % longitudeSegments;
                triangles[write++] = lastRing + lon;
                triangles[write++] = lastRing + next;
                triangles[write++] = southPole;
            }

            FlipWindingInPlace(triangles);
        }
    }
}
