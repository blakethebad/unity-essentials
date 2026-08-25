# InverseCollider Package — Implementation Plan

## Context

The user wants a reusable "inverse collider" for the unity-essentials library: a component that keeps dynamic rigidbodies **inside** a volume — bodies within it collide with the walls from the inside, while objects outside pass through freely. The proposed mesh mechanism is viable and is the standard technique: PhysX triangle-mesh contacts (non-convex `MeshCollider`) are **single-sided** and follow triangle winding, so a mesh with inverted winding has solid interior walls. The mesh is never rendered — a code-built `Mesh` assigned only to a `MeshCollider` is inherently invisible, so "hiding the mesh" costs nothing. No fallback approach is needed.

New self-contained package `Assets/Packages/InverseCollider/`, assembly + namespace `UnityEssentials.Colliders`, following the repo's StateManager/Haptics template. Execution model (standing user directive): I act as lead — Opus agents implement via a Workflow; I personally review, fix, refactor, and verify.

## Settled decisions (asked & answered)

| Question | Decision |
|---|---|
| Shape sources | **All three**: Box (center+size), Sphere (center+radius+segments), SourceMesh (assigned Mesh, fallback to sibling MeshFilter) |
| Collider lifetime | **Play mode only** — built in `Awake`, wireframe gizmo preview in editor (no ExecuteAlways) |
| Escape failsafe | **Yes — optional recapture** (off by default): tracked bodies that escape get teleported back and their outward velocity removed |

## Lead rulings

- Folder `InverseCollider`, assembly `UnityEssentials.Colliders` (folder = product name, assembly = plural domain noun per repo pattern; NOT `.Physics` — that segment would shadow `UnityEngine.Physics` at every call site, including our tests).
- Runtime asmdef references stay **empty** (fully standalone; plain `Debug.LogWarning`, no Utils dependency).
- Component **creates and owns** a non-serialized `MeshCollider` (`AddComponent` at build time) — only ownership lets it enforce `convex = false` and `sharedMesh` as invariants; `[RequireComponent]` would expose a user-editable collider whose convex flag `OnValidate` cannot repair.
- No public `Contains()` (can't be honest for arbitrary source meshes without physics queries); escape recovery uses an **internal** `ContainsPoint` — analytic for Box/Sphere, cloned-mesh-AABB approximation for SourceMesh (documented).
- Every builder authors standard **outward** winding, then `FlipWindingInPlace` runs exactly once as the final step — one rule, one inversion point, one tested primitive.
- Non-kinematic `Rigidbody` on the container → warn **and skip build** (PhysX rejects non-convex + non-kinematic with its own error). Kinematic is allowed (moving containers are valid).
- Failed rebuild tears down (destroy generated mesh, clear `sharedMesh`, `IsBuilt` → false) but keeps the collider component; stale walls never persist silently.
- No statics anywhere → nothing to reset for domain reload.
- No `Editor/` folder (justified deviation: the gizmo must live on the component; no custom inspector in v1).
- `Physics.Simulate` does **not** invoke `FixedUpdate` (it only steps PhysX), so recovery's end-to-end wiring can't be sim-tested; `RecoveryStep()` is the internal test seam and the `FixedUpdate` wiring is a review-by-eye item.
- Recovery capacity is a `const int` 64 overlap buffer (documented in README, not serialized).

## Package structure

```
Assets/Packages/InverseCollider/
├── README.md                                   house format (~190 lines)
├── Runtime/
│   ├── UnityEssentials.Colliders.asmdef        clone of UnityEssentials.States.asmdef shape: references [], autoReferenced true
│   ├── AssemblyInfo.cs                         InternalsVisibleTo("UnityEssentials.Colliders.Tests") + comment naming the internals used
│   ├── InverseColliderShape.cs                 public enum Box=0, Sphere=1, SourceMesh=2 (explicit values, serialization stability)
│   ├── InverseColliderGeometry.cs              internal static PURE geometry (no UnityEngine.Mesh, no logging) ~170 lines
│   ├── InverseCollider.cs                      partial: fields, Awake/OnDestroy, Rebuild, source clone, warnings, OnValidate, gizmos ~280 lines
│   └── InverseCollider.Recovery.cs             partial: FixedUpdate, RecoveryStep, ContainsPoint, tracking state ~110 lines
└── Tests/
    ├── UnityEssentials.Colliders.Tests.asmdef  clone of UnityEssentials.Utilities.Tests.asmdef shape (Editor-only, TestRunner refs, nunit, UNITY_INCLUDE_TESTS, autoReferenced false)
    ├── ColliderTestFixture.cs                  Track<T>, TearDownBuild pass, newest-first DestroyImmediate, reflection SetField/GetField, CreateCollider ~80 lines
    ├── GeometryTests.cs                        pure array-level tests (runnable outside Unity) ~260 lines
    ├── ComponentTests.cs                       build/teardown/warnings/source-clone tests ~330 lines
    ├── EscapeRecoveryTests.cs                  RecoveryStep-driven tests, no simulation ~200 lines
    ├── ContainmentSimulationTests.cs           repo's first Physics.Simulate fixture ~180 lines
    └── PerformanceTests.cs                     zero-alloc pins ~80 lines
```

No `.meta` files are hand-authored — Unity generates them on next Editor focus. Asmdef references by NAME, never GUID.

## Public API (complete)

```csharp
namespace UnityEssentials.Colliders
{
    public enum InverseColliderShape { Box = 0, Sphere = 1, SourceMesh = 2 }

    [AddComponentMenu("UnityEssentials/Colliders/Inverse Collider")]
    [DisallowMultipleComponent]
    public sealed partial class InverseCollider : MonoBehaviour
    {
        public bool IsBuilt { get; }                    // => _mesh != null (Unity overloaded null)
        public bool EscapeRecovery { get; set; }        // wraps the serialized field
        public void Rebuild();                          // (re)builds the inverted mesh into the owned collider
    }
}
```

Serialized fields (all `[SerializeField] private`, camelCase, `[Tooltip]` on its own line above each — HapticsTester style):
`shape` (Box default), `center` (Vector3.zero), `size` (Vector3.one, Box), `radius` (0.5f, Sphere), `longitudeSegments` (24), `latitudeSegments` (16), `sourceMesh` (Mesh, SourceMesh shape), `material` (PhysicsMaterial passthrough — Unity 6 type), `escapeRecovery` (false), `recoveryLayers` (LayerMask, -1 = Everything).

Internal (tests via InternalsVisibleTo): `OwnedCollider`, `GeneratedMesh`, `TearDownBuild()` (idempotent full cleanup; OnDestroy delegates to it), `OnValidate()` (Unity calls it regardless of access), `RecoveryStep()`, `ContainsPoint(Vector3 worldPoint)`, and:

```csharp
internal static class InverseColliderGeometry
{
    internal static void FlipWindingInPlace(int[] triangles);   // swap [i+1]<->[i+2] per triple
    internal static void BuildBoxGeometry(Vector3 center, Vector3 size, out Vector3[] vertices, out int[] triangles);
    internal static void BuildSphereGeometry(Vector3 center, float radius, int longitudeSegments, int latitudeSegments, out Vector3[] vertices, out int[] triangles);
}
```

Throws (built-in, inline at call sites): FlipWindingInPlace — null → `ArgumentNullException`, length % 3 != 0 → `ArgumentException`. BuildSphereGeometry — lon < 3 or lat < 2 → `ArgumentOutOfRangeException`. Builders do NOT validate size/radius (degenerate extents produce well-formed degenerate arrays; quality floors are OnValidate's job).

## Geometry

**Inwardness invariant** (asserted for every triangle of every shape): with `g = (a+b+c)/3`, `dot(Cross(b−a, c−a), interiorPoint − g) > 0` where interiorPoint = the shape's `center`.

**Box** — `h = size/2`, 8 vertices `center + offset`:
```
0:(-h.x,-h.y,-h.z) 1:(+h.x,-h.y,-h.z) 2:(+h.x,-h.y,+h.z) 3:(-h.x,-h.y,+h.z)
4:(-h.x,+h.y,-h.z) 5:(+h.x,+h.y,-h.z) 6:(+h.x,+h.y,+h.z) 7:(-h.x,+h.y,+h.z)
```
Author this **outward** table (verified against the cross-product rule), then flip once:
```
Bottom(-y): (0,1,2)(0,2,3)   Top(+y):  (4,6,5)(4,7,6)
Front(-z):  (0,5,1)(0,4,5)   Back(+z): (3,2,6)(3,6,7)
Left(-x):   (0,7,4)(0,3,7)   Right(+x):(1,5,6)(1,6,2)
```

**Sphere** — UV sphere, `L = longitudeSegments`, `T = latitudeSegments`. Vertices `2 + (T−1)·L`: index 0 = north pole `center+(0,r,0)`; rings `i = 0..T−2` at polar angle `θ = π(i+1)/T` from +Y, ring vertex `lon`: `center + r·(sinθ·cos φ, cosθ, sinθ·sin φ)` with `φ = 2π·lon/L`, ring `i` starts at `1 + i·L`; last index = south pole. Triangles `2L(T−1)` authored outward then flipped once: per band (upper U, lower W), per lon with `n = (lon+1)%L`: `(U[lon],U[n],W[n])` and `(U[lon],W[n],W[lon])`; the pole bands emit only the non-degenerate half — north `(pole, ring0[n], ring0[lon])`, south `(last[lon], last[n], pole)`. Defaults 24×16 → 362 verts, 720 tris, 2160 indices.

**SourceMesh path** (in the component — `Mesh` API is banned from the pure class): resolve `sourceMesh` else sibling `MeshFilter.sharedMesh` (null → warn+skip); `!isReadable` → warn+skip; `vertices = source.vertices` (Unity returns a copy); concatenate `GetTriangles(s)` for every submesh with `GetTopology(s) == Triangles` (non-triangle submeshes: warn once, skip; empty result → warn+skip); `FlipWindingInPlace(combined)`; generated mesh takes `source.indexFormat` (>65k support). Normals/UVs/tangents never read or written — PhysX cooking consumes vertices + indices only.

## Component lifecycle

`Awake` → `Rebuild()`. `OnDestroy` → `TearDownBuild()`. **Rebuild() order** (no isPlaying guard — EditMode tests call it directly):
1. Sibling non-kinematic `Rigidbody` → warn + `ClearBuiltMesh()` + return.
2. (SourceMesh) resolve/readability guards as above.
3. `lossyScale.x·y·z < 0` → warn (winding re-flips; build continues).
4. Build arrays per `shape` (switch; `default:` → warn "unknown shape" + clear + return).
5. Destroy previous `_mesh` first (`Application.isPlaying ? Destroy : DestroyImmediate` — HapticsTester pattern); exactly one generated mesh ever alive.
6. `new Mesh { name = "InverseCollider (Generated)", hideFlags = HideFlags.HideAndDontSave }`; indexFormat (source path); assign vertices/triangles; `RecalculateBounds()`.
7. `if (_collider == null) _collider = gameObject.AddComponent<MeshCollider>();` (Unity null also self-heals a user-deleted collider).
8. `_collider.convex = false;` **before** `sharedMesh = _mesh` (no convex cook ever happens); `sharedMaterial = material`.

`ClearBuiltMesh()` (private): guarded-destroy `_mesh`, null it, `sharedMesh = null` if collider alive (a MeshCollider with null sharedMesh generates no contacts). `TearDownBuild()` (internal, idempotent): ClearBuiltMesh + guarded-destroy collider + null it.

**Canonical warning messages** (shared contract between component agent and test agent; tests match the stable mid-phrase via `LogAssert.Expect(LogType.Warning, new Regex(...))`):

| Case | Message |
|---|---|
| No source | `InverseCollider: no source mesh — assign one or add a MeshFilter with a shared mesh; build skipped.` |
| Unreadable | `InverseCollider: source mesh '{name}' is not readable (enable Read/Write in its import settings); build skipped.` |
| Non-triangle submesh | `InverseCollider: source mesh '{name}' has non-triangle submeshes; they were skipped.` |
| Negative scale | `InverseCollider: negative scale on '{gameObject.name}' re-flips winding — containment will be inverted.` |
| Non-kinematic Rigidbody | `InverseCollider: a non-kinematic Rigidbody on '{gameObject.name}' cannot carry a non-convex MeshCollider; build skipped.` |
| Unknown shape | `InverseCollider: unknown shape value {n}; build skipped.` |

**Gizmos** (`#if UNITY_EDITOR`, with OnValidate at the bottom of InverseCollider.cs): `OnDrawGizmosSelected` — `Gizmos.matrix = transform.localToWorldMatrix`, fixed color `(0.25, 0.85, 0.85)`; Box → `DrawWireCube(center, size)`, Sphere → `DrawWireSphere(center, radius)`, SourceMesh → `DrawWireMesh(resolved source)` (wireframe is edge-identical to the inverted mesh).

## Escape recovery (InverseCollider.Recovery.cs)

`FixedUpdate`: `if (!escapeRecovery || !IsBuilt) return; RecoveryStep();`

`RecoveryStep()` (internal seam), lazy-init `Collider[64] _recoveryBuffer`, `Dictionary<Rigidbody, Vector3> _lastInside`, `List<Rigidbody> _pruneScratch`:
1. `Bounds b = _collider.bounds` (world AABB — works for all shapes, moves with kinematic containers). `n = Physics.OverlapBoxNonAlloc(b.center, b.extents, _recoveryBuffer, Quaternion.identity, recoveryLayers, QueryTriggerInteraction.Ignore)`. The container's own collider filters out via null/kinematic `attachedRigidbody`.
2. Track pass: for each hit's `attachedRigidbody` — skip null/kinematic; if `ContainsPoint(rb.position)` → `_lastInside[rb] = rb.position` (dictionary write dedupes multi-collider bodies).
3. Recapture pass — enumerate `_lastInside` **without mutating it** (Unity's .NET Framework-profile Dictionary invalidates enumerators on indexer writes): destroyed rb (Unity `==`) → add to `_pruneScratch`; alive and `!ContainsPoint(rb.position)` → teleport `rb.position` to the stored position **if it still satisfies ContainsPoint, else to the world shape center** (moved-container fallback), and remove outward velocity: `outward = (escapedPos − worldShapeCenter).normalized` (skip projection when degenerate), `along = Dot(v, outward)`, `if (along > 0) rb.linearVelocity = v − along·outward`. Teleports don't touch the dictionary (stored value already equals the target), so they're enumeration-safe; only prunes are deferred.
4. Remove pruned entries after the loop; clear scratch.

`ContainsPoint(Vector3 world)` (internal): `p = transform.InverseTransformPoint(world)`; Box — `|p−center|` component-wise ≤ size/2; Sphere — `(p−center).magnitude ≤ radius`; SourceMesh — `_mesh.bounds.Contains(p)` (AABB approximation, documented). World shape center: `transform.TransformPoint(center)` for Box/Sphere, `_collider.bounds.center` for SourceMesh.

Behavior notes (documented): kinematic bodies never recaptured; bodies on excluded layers never tracked; intentional teleports out get recaptured while enabled (disable first or exclude the layer); >64 colliders in the AABB → excess not newly tracked that step.

## OnValidate (repairs only — never throws, never logs; `#if UNITY_EDITOR`)

`size` components ≥ 0.001; `radius` ≥ 0.001; `longitudeSegments` clamped 8–64; `latitudeSegments` clamped 4–32 (stricter than the builders' structural throws of 3/2 — quality floor vs validity floor, pinned by separate tests). Then `if (Application.isPlaying && IsBuilt) Rebuild();` — live inspector tweaking, self-limiting on failure (IsBuilt goes false, so a broken source warns once, not per keystroke). `center`/`shape`/`sourceMesh`/`material`/recovery fields unclamped. No AddComponent happens on this path (IsBuilt implies the collider exists), so the editor's OnValidate restriction isn't hit.

## Tests

**ColliderTestFixture.cs** (abstract base, mirrors `UISystem/Tests/UITestFixture.cs`): prefixed `[SetUp] ColliderTestFixtureSetUp` / `[TearDown] ColliderTestFixtureTearDown` (derived fixtures don't hide them); teardown first calls `TearDownBuild()` on tracked alive components (try/finally), then destroys tracked objects newest-first with `DestroyImmediate` (leak stopper — OnDestroy doesn't run in EditMode); Unity `==` for alive checks; `Track<T>`; `CreateCollider(name)` (tracked GO + component, no build); reflection `SetField`/`GetField` on private serialized fields (chosen over the JsonUtility house precedent because it uniformly covers enums and UnityEngine.Object refs; divergence noted in a comment).

**GeometryTests.cs** — all [pure, runs outside Unity; NUnit + Vector3/Mathf only]. In-file helpers: `AssertAllTrianglesFaceInward`, `AssertWatertightConsistentlyWound` (every directed edge appears exactly once, reverse exists).
FlipWindingInPlace: `_TriangleList_SwapsSecondAndThirdIndexOfEveryTriple`, `_AppliedTwice_RestoresOriginalOrder`, `_EmptyArray_IsNoOp`, `_NullTriangles_ThrowsArgumentNullException`, `_LengthNotMultipleOfThree_ThrowsArgumentException`.
BuildBoxGeometry: `_UnitCube_ProducesEightVerticesAndTwelveTriangles`, `_EveryTriangleFacesCenter` (against the exact table above), `_OffsetCenter_TranslatesAllVertices`, `_VerticesSpanExactHalfExtents`, `_IsWatertightWithConsistentWinding`.
BuildSphereGeometry: `_DefaultSegments_ProducesExpectedCounts` (362/2160), `_EveryVertexOnRadius` (1e-4), `_EveryTriangleFacesCenter`, `_ContainsNoDegenerateTriangles`, `_IsWatertightWithConsistentWinding`, `_LongitudeBelowThree_ThrowsArgumentOutOfRangeException`, `_LatitudeBelowTwo_ThrowsArgumentOutOfRangeException`.

**ComponentTests.cs** — [Unity objects]; private tracked mesh builders `CreateQuadMesh()`, `CreateTwoSubmeshMesh()` with known windings.
`Rebuild_BoxShape_CreatesNonConvexMeshColliderWithGeneratedMesh`, `Rebuild_GeneratedMesh_HasHideAndDontSaveFlags`, `Rebuild_CalledTwice_DestroysPreviousMeshAndAssignsNewOne`, `Rebuild_WithPhysicsMaterial_AssignsSharedMaterial`, `Rebuild_SphereShape_BuildsMeshWithExpectedVertexCount`, `Rebuild_SourceMeshShape_ClonesWithoutTouchingSource` (AreNotSame + source triangle sequence unchanged), `Rebuild_SourceMeshShape_CombinesAllSubmeshes`, `Rebuild_SourceMeshNullWithSiblingMeshFilter_UsesFilterSharedMesh`, `Rebuild_SourceMeshMissingEverywhere_WarnsAndSkipsBuild`, `Rebuild_UnreadableSourceMesh_WarnsAndSkipsBuild` (source via `UploadMeshData(true)`, with `Assert.IsFalse(source.isReadable)` precondition so editor-keeps-readable behavior fails loudly), `Rebuild_NegativeLossyScale_WarnsAndStillBuilds` (parent scaled (−1,1,1)), `Rebuild_NonKinematicRigidbodyOnContainer_WarnsAndSkipsBuild` (LogAssert's no-unexpected-logs default proves no PhysX internal error), `Rebuild_KinematicRigidbodyOnContainer_Builds`, `Rebuild_UnknownShapeValue_WarnsAndSkipsBuild` (`(InverseColliderShape)999`), `Rebuild_AfterFailedRebuild_RecoversAndBuilds`, `TearDownBuild_AfterRebuild_DestroysGeneratedMeshAndOwnedCollider`, `TearDownBuild_WithoutBuild_IsSafeNoOp`, `OnValidate_OutOfRangeFields_ClampsIntoRange`, `IsBuilt_BeforeRebuild_ReportsFalse`.

**EscapeRecoveryTests.cs** — [Unity objects, no simulation; drive `RecoveryStep()` directly; move bodies via `rb.position` and call `Physics.SyncTransforms()` after transform moves so overlap/bounds agree].
`RecoveryStep_TrackedBodyMovedOutsideBox_TeleportsBackToLastInsidePosition`, `RecoveryStep_TrackedBodyMovedOutsideSphere_TeleportsBack`, `RecoveryStep_EscapedBody_RemovesOutwardVelocityComponentKeepsTangential`, `RecoveryStep_BodyNeverInside_IsUntouched`, `RecoveryStep_KinematicBody_IsNeverRecaptured`, `RecoveryStep_BodyOnExcludedLayer_IsIgnored`, `RecoveryStep_DestroyedTrackedBody_IsPrunedWithoutError`, `RecoveryStep_StaleInsidePositionAfterContainerMoved_FallsBackToShapeCenter`, `ContainsPoint_SourceMeshShape_UsesClonedMeshBounds`.

**ContainmentSimulationTests.cs** — [physics sim]; the repo's first `Physics.Simulate` fixture. Setup saves `Physics.simulationMode` → `SimulationMode.Script`; teardown restores (runs even on failure, before the base fixture's). Rig at `(0, 1000, 0)` (the default physics scene also steps any editor-scene rigidbodies — remote position + class-summary note). Helper `Simulate(int steps)`: one `Physics.SyncTransforms()`, then `Physics.Simulate(0.02f)` loop. Bodies: `SphereCollider` r=0.25 + `Rigidbody { useGravity = false, collisionDetectionMode = ContinuousDynamic }`, `linearVelocity = (5,0,0)`; container Box 4×4×4 or Sphere r=2, built via explicit `Rebuild()`. 150 steps = 15 m of travel vs 2 m half-extent; contained ⇒ `|x−1000| < 2.5`, escaped ⇒ `> 5`.
`Simulate_DynamicBodyInsideBoxWithOutwardVelocity_StaysContained`, `Simulate_DynamicBodyWithoutInverseCollider_EscapesVolume` (the control), `Simulate_BodyEnteringFromOutside_PassesWallAndIsThenContained` (start `+(10,0,0)`, velocity `(−5,0,0)`), `Simulate_DynamicBodyInsideSphere_StaysContained`.

**PerformanceTests.cs** (StateManager idiom: `UnityEngine.TestTools.Constraints`, `Is` alias, warm-up, static sinks): `FlipWindingInPlace_WarmBuffer_DoesNotAllocate` (pre-allocated 2160-index buffer), `IsBuilt_WarmComponent_DoesNotAllocate`, `RecoveryStep_WarmWithNoEscapes_DoesNotAllocate` (lazy structures initialized by a warm-up step; OverlapBoxNonAlloc + struct dictionary enumerator = honestly zero-alloc). `Rebuild`/builders allocate by design — documented as build-time cost, not "optimized" or falsely pinned.

## README.md outline (house format)

`# InverseCollider` — 4-line intro (keep rigidbodies inside a volume; walls solid from the inside only; one flipped-winding non-convex MeshCollider; nothing rendered; no per-frame cost unless escape recovery is on). Bullet block: Assembly `UnityEssentials.Colliders` / Namespace / Tests (EditMode only, incl. the library's first `Physics.Simulate` fixture). Quick-start snippet (add component, contained body with `ContinuousDynamic` CD + `linearVelocity`). Sections: How it works (single-sided PhysX contacts, forced `convex = false`, owned collider, HideAndDontSave mesh); Shapes; Escape recovery (off by default; tracking via AABB overlap; SourceMesh containment is AABB-approximate; kinematic bodies exempt; intentional exits get recaptured while enabled; capacity 64); Runtime changes (fields are snapshots — call `Rebuild()`; play-mode inspector edits auto-rebuild); Gotchas (tunneling → CCD; inbound is always free; negative scale inverts containment; container must be static/kinematic; cooking cost per Rebuild; bodies overlapping a wall depenetrate inward; no public `Contains()` by design); Not in v1 (ExecuteAlways edit-mode colliders, public Contains/bounds API, cookingOptions, capsule shape, double-shell thickness walls).

## Style rules (enforced in every agent prompt + my review)

Built-in exceptions thrown inline — no throw-only helpers, no custom exception types. No field/property comments (Tooltips carry field docs); class XML summaries ≤4 lines; 1–2 line summaries on public methods; no XML on internal/private. Block-scoped `namespace UnityEssentials.Colliders` (subfolders add no segments); usings above namespace; Allman; 4-space; `_camelCase` private runtime fields, camelCase serialized fields; C# 9; no unsafe. OnValidate repairs, never throws/logs. Unity `==` for destroyed checks, never `is null`.

## Orchestration (Workflow, 4 parallel Opus agents implement — I review)

Step 0 (me): copy this plan to `Docs/InverseCollider-Plan.md` (repo `<Name>-Plan.md` convention).

| Agent | Writes (exactly, zero overlap) |
|---|---|
| A — Geometry | `Runtime/InverseColliderGeometry.cs`, `Runtime/AssemblyInfo.cs` |
| B — Component | `Runtime/InverseCollider.cs`, `Runtime/InverseCollider.Recovery.cs`, `Runtime/InverseColliderShape.cs` |
| C — Tests | all six files under `Tests/` |
| D — Docs + plumbing | `README.md`, both `.asmdef` files (field-for-field clones of the verified templates, names swapped) |

Each prompt carries the relevant plan sections verbatim (winding tables, warning strings, test names are cross-agent contracts). Lead review checklist: asmdef names ↔ InternalsVisibleTo string ↔ tests-asmdef reference agree; box table matches this plan verbatim; grep geometry file for `Mesh`/`Debug.` (must be absent); warning strings ↔ test regexes agree; the four one-line review-by-eye wirings (`Awake→Rebuild`, `OnDestroy→TearDownBuild`, OnValidate's isPlaying branch, `FixedUpdate→RecoveryStep`); dictionary never mutated during enumeration except deferred prunes; style pass per rules above. I fix findings directly, never re-delegate.

## Verification

1. **Roslyn offline compile** (memory `unity-compile-check-recipe`): compile Runtime with `/out:UnityEssentials.Colliders.dll` then Tests referencing it (exercises InternalsVisibleTo). **Add `UnityEngine.PhysicsModule.dll`** to the reference list (MeshCollider/Rigidbody/Physics live there; recipe's base list has CoreModule only).
2. **Pure-fixture reflection runner** (recreate per memory): executes `GeometryTests` outside Unity; component/sim/allocation fixtures classify environmental.
3. **Unity batchmode EditMode run**: `-runTests -testPlatform EditMode -assemblyNames "UnityEssentials.Colliders.Tests" -testResults Logs\editmode-results.xml -logFile Logs\test-run.log`. Blocked while the Editor has the project open — check `Get-Process Unity`/`Temp/UnityLockfile`, ask the user to close it (never kill). Never trust the exit code — parse the results XML; if missing, grep the log for `error CS` / "Exiting with code". This run also generates the `.meta` files — confirm they appeared.
4. **Manual play-mode smoke (user)**: drop rigidbodies into an inverse box in the editor, watch containment + recovery live; listed at the end of my report.

## Key risks

- Tunneling through zero-thickness single-sided walls at speed — owned by README (CCD guidance) + the recovery failsafe; the sim fixture uses ContinuousDynamic so the repo's first physics test never flakes on it.
- `UploadMeshData(true)` must clear `isReadable` in-editor for test 10 — guarded by a precondition assert; if editor behavior differs, drop that test and keep the runtime guard.
- Sim fixture steps the whole default physics scene — mitigated by the (0,1000,0) rig; documented in the fixture summary.
- PhysX cooking cost on every `Rebuild()` — documented, not optimized (default cookingOptions).
- EditMode lifecycle gap (Awake/OnDestroy don't run) — tests drive `Rebuild()`/`TearDownBuild()` explicitly; fixture teardown stops HideAndDontSave mesh leaks.
- Recovery vs. intentional gameplay teleports out — documented gotcha (disable recovery or exclude the layer).

## Files created

`Docs/InverseCollider-Plan.md` + everything under `Assets/Packages/InverseCollider/` per the tree (6 runtime/test .cs files + 2 fixtures/helpers among them, 2 .asmdef, AssemblyInfo.cs, README.md — 11 files total). No existing file is modified; `Assets/Demo/` (incl. `BoardController`) is untouched.
