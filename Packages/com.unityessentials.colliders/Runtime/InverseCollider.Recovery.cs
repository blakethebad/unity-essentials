using System.Collections.Generic;
using UnityEngine;

namespace UnityEssentials.Colliders
{
    /// <summary>
    /// The optional escape-recovery half of <see cref="InverseCollider"/>: it remembers where each
    /// tracked rigidbody last was inside the volume and, once one ends up outside, teleports it back
    /// and removes the outward part of its velocity. Kinematic bodies and bodies on excluded layers
    /// are never tracked, and nothing here runs unless recovery is enabled.
    /// </summary>
    public sealed partial class InverseCollider
    {
        private const int RecoveryCapacity = 64;
        private const float DegenerateOutwardSqrMagnitude = 1e-10f;

        private Collider[] _recoveryBuffer;
        private Dictionary<Rigidbody, Vector3> _lastInside;
        private List<Rigidbody> _pruneScratch;

        private void FixedUpdate()
        {
            // The collider check covers the one broken state a user can reach from outside: deleting
            // the owned collider by hand mid-play, which Rebuild() self-heals on its next call.
            if (!escapeRecovery || !IsBuilt || _collider == null)
            {
                return;
            }

            RecoveryStep();
        }

        // The seam the tests drive directly: Physics.Simulate steps PhysX without running
        // FixedUpdate, so the wiring above cannot be exercised by a simulated test.
        internal void RecoveryStep()
        {
            if (_recoveryBuffer == null)
            {
                _recoveryBuffer = new Collider[RecoveryCapacity];
                _lastInside = new Dictionary<Rigidbody, Vector3>();
                _pruneScratch = new List<Rigidbody>();
            }

            TrackContainedBodies();
            RecaptureEscapedBodies();
        }

        internal bool ContainsPoint(Vector3 worldPoint)
        {
            Vector3 point = transform.InverseTransformPoint(worldPoint);

            switch (shape)
            {
                case InverseColliderShape.Box:
                {
                    Vector3 offset = point - center;
                    Vector3 half = size * 0.5f;
                    return Mathf.Abs(offset.x) <= half.x
                        && Mathf.Abs(offset.y) <= half.y
                        && Mathf.Abs(offset.z) <= half.z;
                }

                case InverseColliderShape.Sphere:
                    return (point - center).magnitude <= radius;

                case InverseColliderShape.SourceMesh:
                    // Approximate on purpose: an arbitrary mesh has no cheap analytic inside test, so
                    // containment is judged against the generated mesh's local bounding box.
                    return _mesh != null && _mesh.bounds.Contains(point);

                default:
                    return false;
            }
        }

        private void TrackContainedBodies()
        {
            // The world AABB works for every shape and follows a kinematic container as it moves.
            Bounds bounds = _collider.bounds;
            int count = Physics.OverlapBoxNonAlloc(
                bounds.center,
                bounds.extents,
                _recoveryBuffer,
                Quaternion.identity,
                recoveryLayers,
                QueryTriggerInteraction.Ignore);

            for (int i = 0; i < count; i++)
            {
                // The container's own collider filters out here: it carries no rigidbody, or a
                // kinematic one.
                Rigidbody body = _recoveryBuffer[i].attachedRigidbody;
                if (body == null || body.isKinematic)
                {
                    continue;
                }

                if (ContainsPoint(body.position))
                {
                    _lastInside[body] = body.position;
                }
            }
        }

        private void RecaptureEscapedBodies()
        {
            Vector3 shapeCenter = WorldShapeCenter();

            // Enumerated without mutating: an indexer write invalidates this Dictionary's enumerator,
            // so prunes are deferred to the scratch list and a teleport touches only the rigidbody —
            // the stored position already equals what a write would put there.
            foreach (KeyValuePair<Rigidbody, Vector3> entry in _lastInside)
            {
                Rigidbody body = entry.Key;
                if (body == null)
                {
                    _pruneScratch.Add(body);
                    continue;
                }

                Vector3 escaped = body.position;
                if (ContainsPoint(escaped))
                {
                    continue;
                }

                // A container that moved can leave the stored position outside; the shape's center is
                // the fallback that is inside by construction.
                body.position = ContainsPoint(entry.Value) ? entry.Value : shapeCenter;
                RemoveOutwardVelocity(body, escaped, shapeCenter);
            }

            for (int i = 0; i < _pruneScratch.Count; i++)
            {
                _lastInside.Remove(_pruneScratch[i]);
            }

            _pruneScratch.Clear();
        }

        private Vector3 WorldShapeCenter()
        {
            return shape == InverseColliderShape.SourceMesh
                ? _collider.bounds.center
                : transform.TransformPoint(center);
        }

        private static void RemoveOutwardVelocity(Rigidbody body, Vector3 escapedPosition, Vector3 shapeCenter)
        {
            Vector3 outward = escapedPosition - shapeCenter;
            if (outward.sqrMagnitude < DegenerateOutwardSqrMagnitude)
            {
                return;
            }

            outward = outward.normalized;
            Vector3 velocity = body.linearVelocity;
            float along = Vector3.Dot(velocity, outward);

            if (along > 0f)
            {
                body.linearVelocity = velocity - along * outward;
            }
        }
    }
}
