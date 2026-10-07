# InverseCollider

Keeps dynamic rigidbodies **inside** a volume instead of out of it: the walls are solid from the
inside only, so anything outside passes straight through them and falls in for free. One component
builds and owns a single inverted-winding, non-convex `MeshCollider`; the mesh it generates is
never rendered, and nothing runs per frame unless the optional escape recovery is switched on.

- **Assembly:** `UnityEssentials.Colliders` (`Runtime/`) — standalone, nothing beyond `UnityEngine`.
- **Namespace:** `UnityEssentials.Colliders`
- **Tests:** `UnityEssentials.Colliders.Tests` (EditMode only) — including the library's first
  `Physics.Simulate` fixture, which throws real rigidbodies at a real inverted wall.

Add **Inverse Collider** (Add Component ▸ UnityEssentials ▸ Colliders ▸ Inverse Collider) to an
empty GameObject, pick a shape in the Inspector, and press Play. The Scene view always shows a
faint cyan wireframe of the volume, drawn solid while the component is selected; the collider
itself is built in `Awake`.

```csharp
using UnityEngine;
using UnityEssentials.Colliders;

public sealed class ContainedBallSpawner : MonoBehaviour
{
    [SerializeField] private InverseCollider container;   // shape and size are set in the Inspector
    [SerializeField] private Rigidbody ballPrefab;

    private void Start()
    {
        container.EscapeRecovery = true;                  // optional failsafe, off by default

        var ball = Instantiate(ballPrefab, container.transform.position, Quaternion.identity);
        ball.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;   // do not skip this
        ball.linearVelocity = new Vector3(12f, 0f, 0f);   // straight at a wall, and it stays in
    }
}
```

---

## How it works

PhysX triangle-mesh contacts are **single-sided**: a non-convex `MeshCollider` only pushes back on
the side a triangle's winding faces. Every shape is authored with ordinary outward winding and then
its index buffer is flipped exactly once, so every triangle faces the volume's interior. A body
inside meets a wall. A body outside meets nothing at all.

Three invariants make that hold, and they are the reason the component owns the collider rather
than requiring one:

- **The `MeshCollider` is created at build time and owned.** It is not serialized and not something
  you add yourself, because only ownership lets the component keep the next two true. If you delete
  it in play mode, the next `Rebuild()` puts it back.
- **`convex` is forced `false` before the mesh is assigned**, so a convex cook never happens. A
  convex hull is solid from every direction — containment would silently invert into an ordinary
  obstacle.
- **The generated mesh is `HideAndDontSave` and never rendered.** A mesh assigned only to a
  collider has nothing drawing it — there is no `MeshRenderer` involved — so it is invisible by
  construction, not by a hiding trick. Exactly one generated mesh is alive at a time: a rebuild
  destroys the previous one, and destroying the component destroys the last.

A build is **skipped with one warning** when it cannot be honest: a non-kinematic `Rigidbody` sits
on the same GameObject; the `SourceMesh` shape has no mesh assigned and no sibling `MeshFilter`;
the source mesh is not readable; the source mesh has no triangle submeshes at all; or `shape`
holds an unknown value. A skipped or failed rebuild
tears the previous build down — generated mesh destroyed, `sharedMesh` cleared, `IsBuilt` `false` —
so stale walls never linger silently. The collider component itself stays, and the next successful
`Rebuild()` recovers.

---

## Shapes

| Shape | Inspector fields | Geometry |
|---|---|---|
| `Box` | Center, Size | 8 vertices, 12 triangles |
| `Sphere` | Center, Radius, Longitude Segments, Latitude Segments | UV sphere; the 24 × 16 default is 362 vertices / 720 triangles |
| `SourceMesh` | Source Mesh (or a sibling `MeshFilter`) | your mesh, winding flipped |

Center, size and radius are **local space**, so the volume moves, rotates and scales with the
transform without any rebuild. Physics Material is passed straight through to the collider's
`sharedMaterial`.

The `SourceMesh` path resolves Source Mesh first and falls back to the sibling `MeshFilter`'s
shared mesh. The mesh needs **Read/Write Enabled** in its import settings — an unreadable mesh
cannot be copied at runtime and the build is skipped. Every triangle submesh is combined into one
index buffer; a non-triangle submesh is skipped with a warning. The source asset is never touched:
positions and indices are copied out, and the copy inherits the source's index format, so meshes
over 65k vertices work. Normals, UVs and tangents are neither read nor written — PhysX cooking
consumes positions and indices only.

Inspector values are repaired as you type rather than validated: size components and radius floor
at `0.001`, Longitude Segments clamps to 8–64 and Latitude Segments to 4–32. Editing never throws.

---

## Escape recovery

Off by default, and a failsafe rather than the containment mechanism — the walls alone hold at
sane speeds. With `EscapeRecovery` on, every `FixedUpdate` the component overlaps its collider's
world-space AABB (masked by Recovery Layers, triggers ignored), records the last known **inside**
position of each non-kinematic rigidbody it finds there, and for any remembered body that is now
outside: teleports it back to that position and removes the outward component of its velocity,
keeping the tangential part so it slides along the wall instead of dead-stopping.

If the remembered position is no longer inside — the container moved since — the body goes to the
shape's center instead.

The containment test is exact for `Box` and `Sphere`. For `SourceMesh` it is the generated mesh's
**bounding box**, an approximation: a body that escapes a concave source is recaptured only once
it leaves that AABB, and a shallow escape into a concavity is not recaptured at all.

- Kinematic bodies are never tracked or recaptured, and the container filters its own collider out.
- Bodies on layers excluded from Recovery Layers are never tracked.
- A body never seen inside is never touched — recovery cannot pull in something that started out.
- Intentional teleports out are recaptured while recovery is enabled. Turn it off (or exclude that
  body's layer) before teleporting something out on purpose.
- The overlap buffer is a fixed `Collider[64]` and is not serialized. If more than 64 colliders sit
  in the AABB during a step, the excess is not *newly* tracked that step; bodies already tracked
  keep being recaptured.

A warm recovery step with nothing escaping **allocates nothing** — the overlap query writes into
that pre-allocated buffer and the tracking dictionary is walked with a struct enumerator. Building
is the opposite and unapologetically so: `Rebuild()` allocates its vertex and index arrays and a
`Mesh`, which is a load-time cost, not a per-frame one.

---

## Runtime changes

The serialized fields are a snapshot: they are read when the volume is built, not continuously.
Change one from code and call `Rebuild()` to apply it. In play mode the Inspector does that for
you — editing any field of a built component re-runs the build immediately, so you can drag Size
around and watch containment follow. That is self-limiting on failure: a build that fails clears
`IsBuilt`, so a broken source warns once instead of once per keystroke.

| Member | Behaviour |
|---|---|
| `bool IsBuilt { get; }` | Whether a generated mesh is currently live on the owned collider. `false` before `Awake`, after a skipped or failed build, and after the component is destroyed. |
| `bool EscapeRecovery { get; set; }` | The failsafe toggle, backed by the same field the Inspector shows. Safe to flip at any time; no rebuild involved. |
| `void Rebuild()` | (Re)builds the inverted volume from the current field values, destroying the previous generated mesh first. Cooks a mesh — call it on change, not per frame. |

Moving, rotating or scaling the container needs no rebuild at all. There is no static state
anywhere in the package either, so entering play mode with domain reload disabled inherits nothing:
every component builds its own volume in its own `Awake`.

---

## Gotchas

### Fast bodies tunnel — give them continuous detection

The walls have zero thickness and one side, which is exactly the case discrete collision detection
misses. Set `collisionDetectionMode` to `Continuous` on anything fast (`ContinuousDynamic` if it
must also sweep against other moving bodies — what the simulation tests use), and lower Fixed
Timestep if bodies are very fast. Escape recovery is the second line of defence, not the first.

### Inbound is always free

Single-sided walls cannot stop anything from outside, in either direction: a body outside the
volume falls in without a contact. That is inherent to the technique, not a setting. If you also
want the volume solid from the outside, add an ordinary collider for that job.

### Negative scale inverts containment

A `lossyScale` with a negative determinant (an odd number of mirrored axes) re-flips the winding,
making the walls solid from the outside and hollow from the inside. The component warns and builds
anyway — fix the scale on the container or its parents.

### The container must be static or kinematic

PhysX will not carry a non-convex `MeshCollider` on a non-kinematic `Rigidbody`, so that case
warns and skips the build rather than letting PhysX error out. A **kinematic** `Rigidbody` is fine:
moving and rotating containers work, and recovery reads the collider's world AABB each step.

### Cooking costs something on every `Rebuild()`

Each build cooks a fresh triangle mesh in PhysX with default cooking options. That is trivial for a
box and not free for a 50k-triangle source mesh. Build once and leave it alone.

### Bodies overlapping a wall depenetrate inward

A body straddling a wall when the volume is built — or spawned into one — is pushed to the inside,
because that is the direction the triangles face. Spawn near the center if you want the outcome to
be obvious.

### There is no public `Contains()`

Deliberate. An honest containment answer for an arbitrary source mesh needs a physics query, not a
bounding box, and the internal test recovery uses is exact only for `Box` and `Sphere`. Publishing
an AABB approximation as API would be a promise the package cannot keep. (`Collider.ClosestPoint`
is no way out either — it does not support non-convex mesh colliders.) Use `Physics.OverlapBox` or
your own volume check when you need to ask.

---

## Not in v1

Deliberate omissions, each of which would be a contained addition later:

- **Edit-mode colliders (`ExecuteAlways`).** The volume is built in `Awake`, play mode only; the
  Scene view gets a wireframe gizmo instead of a live collider.
- **A public `Contains()` / bounds API.** See above — it cannot be answered honestly for every
  shape without a physics query.
- **`cookingOptions` control.** The owned collider cooks with Unity's defaults; there is no
  inspector knob for welding, cleaning or vertex mapping.
- **A capsule shape.** Box, sphere and "any mesh you hand it" cover the cases so far.
- **Double-shell walls with real thickness.** A second, offset shell would resist tunneling
  geometrically instead of relying on continuous detection plus recovery.
