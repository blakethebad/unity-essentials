using System.Runtime.CompilerServices;

// The EditMode test assembly exercises internals deliberately outside the public contract:
// InverseColliderGeometry, and InverseCollider's OwnedCollider, GeneratedMesh, TearDownBuild,
// OnValidate, RecoveryStep and ContainsPoint.
[assembly: InternalsVisibleTo("UnityEssentials.Colliders.Tests")]
