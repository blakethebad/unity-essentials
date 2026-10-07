namespace UnityEssentials.Colliders
{
    /// <summary>
    /// Which volume an <see cref="InverseCollider"/> inverts: an axis-aligned box (center and size),
    /// a UV sphere (center, radius and segment counts), or an assigned mesh, which falls back to a
    /// sibling <see cref="UnityEngine.MeshFilter"/>'s shared mesh. The values are serialized, so
    /// never reorder them.
    /// </summary>
    public enum InverseColliderShape
    {
        Box = 0,
        Sphere = 1,
        SourceMesh = 2
    }
}
