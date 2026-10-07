using UnityEngine;
using UnityEngine.Rendering;

namespace UnityEssentials.Colliders
{
    /// <summary>
    /// Keeps rigidbodies inside a volume by building a hidden, inverted-winding non-convex
    /// <see cref="MeshCollider"/>: its walls are solid from the inside only, so contained bodies
    /// bounce off them while anything outside passes straight through. The collider and its mesh are
    /// created and owned by this component.
    /// </summary>
    [AddComponentMenu("UnityEssentials/Colliders/Inverse Collider")]
    [DisallowMultipleComponent]
    public sealed partial class InverseCollider : MonoBehaviour
    {
        private const string GeneratedMeshName = "InverseCollider (Generated)";

        [Tooltip("Which volume to invert. Box and Sphere are generated from the fields below; Source " +
                 "Mesh inverts an assigned mesh instead.")]
        [SerializeField] private InverseColliderShape shape = InverseColliderShape.Box;

        [Tooltip("Center of the generated volume, in local space. Ignored by Source Mesh, which is " +
                 "placed by its own vertices.")]
        [SerializeField] private Vector3 center;

        [Tooltip("Full extents of the box, in local space. Box shape only.")]
        [SerializeField] private Vector3 size = Vector3.one;

        [Tooltip("Radius of the sphere, in local space. Sphere shape only.")]
        [SerializeField] private float radius = 0.5f;

        [Tooltip("Segments around the equator. More segments mean rounder walls and a costlier cook. " +
                 "Sphere shape only.")]
        [SerializeField] private int longitudeSegments = 24;

        [Tooltip("Segments from pole to pole. More segments mean rounder walls and a costlier cook. " +
                 "Sphere shape only.")]
        [SerializeField] private int latitudeSegments = 16;

        [Tooltip("Mesh to invert, which must have Read/Write enabled in its import settings. Source " +
                 "Mesh shape only; leave empty to use a sibling MeshFilter's shared mesh.")]
        [SerializeField] private Mesh sourceMesh;

        [Tooltip("Physics material for the walls. Leave empty for the project default.")]
        [SerializeField] private PhysicsMaterial material;

        [Tooltip("Teleport tracked rigidbodies back inside when they escape and strip the outward part " +
                 "of their velocity. Costs one overlap query per physics step while enabled.")]
        [SerializeField] private bool escapeRecovery;

        [Tooltip("Layers escape recovery tracks. Bodies on any other layer are never tracked and never " +
                 "recaptured.")]
        [SerializeField] private LayerMask recoveryLayers = ~0;

        private MeshCollider _collider;
        private Mesh _mesh;

        public bool IsBuilt => _mesh != null;

        public Vector3 Center => center;

        public Vector3 Size => size;

        public bool EscapeRecovery
        {
            get => escapeRecovery;
            set => escapeRecovery = value;
        }

        internal MeshCollider OwnedCollider => _collider;

        internal Mesh GeneratedMesh => _mesh;

        private void Awake()
        {
            // Play mode only by design: there is no ExecuteAlways, so the editor shows the gizmo and
            // the collider exists only while the game runs.
            Rebuild();
        }

        private void OnDestroy()
        {
            TearDownBuild();
        }

        /// <summary>
        /// Rebuilds the inverted collision mesh from the current field values into the owned collider,
        /// warning and skipping the build when those values cannot produce one.
        /// </summary>
        public void Rebuild()
        {
            if (TryGetComponent(out Rigidbody body) && !body.isKinematic)
            {
                Debug.LogWarning(
                    $"InverseCollider: a non-kinematic Rigidbody on '{gameObject.name}' cannot carry a " +
                    "non-convex MeshCollider; build skipped.",
                    this);
                ClearBuiltMesh();
                return;
            }

            Mesh source = null;
            if (shape == InverseColliderShape.SourceMesh)
            {
                source = ResolveSourceMesh();

                if (source == null)
                {
                    Debug.LogWarning(
                        "InverseCollider: no source mesh — assign one or add a MeshFilter with a " +
                        "shared mesh; build skipped.",
                        this);
                    ClearBuiltMesh();
                    return;
                }

                if (!source.isReadable)
                {
                    Debug.LogWarning(
                        $"InverseCollider: source mesh '{source.name}' is not readable (enable " +
                        "Read/Write in its import settings); build skipped.",
                        this);
                    ClearBuiltMesh();
                    return;
                }
            }

            Vector3 lossyScale = transform.lossyScale;
            if (lossyScale.x * lossyScale.y * lossyScale.z < 0f)
            {
                Debug.LogWarning(
                    $"InverseCollider: negative scale on '{gameObject.name}' re-flips winding — " +
                    "containment will be inverted.",
                    this);
            }

            Vector3[] vertices;
            int[] triangles;
            IndexFormat format = IndexFormat.UInt16;

            switch (shape)
            {
                case InverseColliderShape.Box:
                    InverseColliderGeometry.BuildBoxGeometry(center, size, out vertices, out triangles);
                    break;

                case InverseColliderShape.Sphere:
                    InverseColliderGeometry.BuildSphereGeometry(
                        center, radius, longitudeSegments, latitudeSegments, out vertices, out triangles);
                    break;

                case InverseColliderShape.SourceMesh:
                    if (!TryBuildSourceGeometry(source, out vertices, out triangles))
                    {
                        ClearBuiltMesh();
                        return;
                    }

                    format = source.indexFormat;
                    break;

                default:
                    Debug.LogWarning($"InverseCollider: unknown shape value {(int)shape}; build skipped.", this);
                    ClearBuiltMesh();
                    return;
            }

            // The previous mesh dies before the new one exists, so exactly one is ever alive.
            ClearBuiltMesh();

            _mesh = new Mesh
            {
                name = GeneratedMeshName,
                hideFlags = HideFlags.HideAndDontSave,
                indexFormat = format
            };

            _mesh.vertices = vertices;
            _mesh.triangles = triangles;
            _mesh.RecalculateBounds();

            // Unity's null test, so a collider the user deleted by hand is replaced rather than used.
            if (_collider == null)
            {
                _collider = gameObject.AddComponent<MeshCollider>();
            }

            // convex is written before the mesh so a convex hull is never cooked, not even once.
            _collider.convex = false;
            _collider.sharedMesh = _mesh;
            _collider.sharedMaterial = material;
        }

        /// <summary>
        /// Replaces the box volume's local center and size, rebuilding the collider when one is
        /// already built. Box shape only; other shapes ignore these fields.
        /// </summary>
        public void SetBoxBounds(Vector3 boxCenter, Vector3 boxSize)
        {
            center = boxCenter;
            size = boxSize;

            if (IsBuilt)
            {
                Rebuild();
            }
        }

        internal void TearDownBuild()
        {
            ClearBuiltMesh();

            if (_collider == null)
            {
                return;
            }

            if (Application.isPlaying)
            {
                Destroy(_collider);
            }
            else
            {
                DestroyImmediate(_collider);
            }

            _collider = null;
        }

        private void ClearBuiltMesh()
        {
            // Cleared first so the collider is never left pointing at a destroyed mesh; a MeshCollider
            // with no shared mesh generates no contacts.
            if (_collider != null)
            {
                _collider.sharedMesh = null;
            }

            if (_mesh == null)
            {
                return;
            }

            if (Application.isPlaying)
            {
                Destroy(_mesh);
            }
            else
            {
                DestroyImmediate(_mesh);
            }

            _mesh = null;
        }

        private Mesh ResolveSourceMesh()
        {
            if (sourceMesh != null)
            {
                return sourceMesh;
            }

            return TryGetComponent(out MeshFilter filter) ? filter.sharedMesh : null;
        }

        private bool TryBuildSourceGeometry(Mesh source, out Vector3[] vertices, out int[] triangles)
        {
            // Both accessors hand back copies, so the flip below never touches the source asset.
            // Normals, UVs and tangents are left alone: cooking consumes vertices and indices only.
            vertices = source.vertices;
            triangles = CombineTriangleSubmeshes(source);

            if (triangles.Length == 0)
            {
                Debug.LogWarning(
                    $"InverseCollider: source mesh '{source.name}' has no triangle submeshes; build skipped.",
                    this);
                return false;
            }

            InverseColliderGeometry.FlipWindingInPlace(triangles);
            return true;
        }

        private int[] CombineTriangleSubmeshes(Mesh source)
        {
            // The common case owns GetTriangles' fresh copy outright, with no second buffer.
            if (source.subMeshCount == 1 && source.GetTopology(0) == MeshTopology.Triangles)
            {
                return source.GetTriangles(0);
            }

            int total = 0;
            bool skippedNonTriangle = false;

            for (int subMesh = 0; subMesh < source.subMeshCount; subMesh++)
            {
                if (source.GetTopology(subMesh) != MeshTopology.Triangles)
                {
                    skippedNonTriangle = true;
                    continue;
                }

                total += (int)source.GetIndexCount(subMesh);
            }

            int[] combined = new int[total];
            int offset = 0;

            for (int subMesh = 0; subMesh < source.subMeshCount; subMesh++)
            {
                if (source.GetTopology(subMesh) != MeshTopology.Triangles)
                {
                    continue;
                }

                int[] indices = source.GetTriangles(subMesh);
                indices.CopyTo(combined, offset);
                offset += indices.Length;
            }

            if (skippedNonTriangle)
            {
                Debug.LogWarning(
                    $"InverseCollider: source mesh '{source.name}' has non-triangle submeshes; they were skipped.",
                    this);
            }

            return combined;
        }

#if UNITY_EDITOR
        private const float MinimumSize = 0.001f;
        private const float MinimumRadius = 0.001f;
        private const int MinimumLongitudeSegments = 8;
        private const int MaximumLongitudeSegments = 64;
        private const int MinimumLatitudeSegments = 4;
        private const int MaximumLatitudeSegments = 32;

        // Repairs only. Unity calls this on load and on every inspector keystroke, so it never throws
        // and never logs. The rebuild is limited to a component that is already built and playing,
        // which also keeps AddComponent off this path.
        internal void OnValidate()
        {
            size = new Vector3(
                Mathf.Max(MinimumSize, size.x),
                Mathf.Max(MinimumSize, size.y),
                Mathf.Max(MinimumSize, size.z));

            radius = Mathf.Max(MinimumRadius, radius);
            longitudeSegments = Mathf.Clamp(longitudeSegments, MinimumLongitudeSegments, MaximumLongitudeSegments);
            latitudeSegments = Mathf.Clamp(latitudeSegments, MinimumLatitudeSegments, MaximumLatitudeSegments);

            if (Application.isPlaying && IsBuilt)
            {
                Rebuild();
            }
        }

        private static readonly Color GizmoColor = new Color(0.25f, 0.85f, 0.85f);

        // A faint outline is always visible so the volume reads at a glance; selecting the component
        // draws the same wireframe solid.
        private void OnDrawGizmos()
        {
            DrawShapeGizmo(new Color(GizmoColor.r, GizmoColor.g, GizmoColor.b, 0.35f));
        }

        private void OnDrawGizmosSelected()
        {
            DrawShapeGizmo(GizmoColor);
        }

        private void DrawShapeGizmo(Color color)
        {
            Gizmos.matrix = transform.localToWorldMatrix;
            Gizmos.color = color;

            switch (shape)
            {
                case InverseColliderShape.Box:
                    Gizmos.DrawWireCube(center, size);
                    break;

                case InverseColliderShape.Sphere:
                    Gizmos.DrawWireSphere(center, radius);
                    break;

                case InverseColliderShape.SourceMesh:
                {
                    // Wireframe of the source is edge-identical to the inverted mesh: flipping the
                    // winding moves no vertex.
                    Mesh source = ResolveSourceMesh();
                    if (source != null)
                    {
                        Gizmos.DrawWireMesh(source);
                    }

                    break;
                }
            }
        }
#endif
    }
}
