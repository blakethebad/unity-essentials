# SerializedDictionary

A dictionary Unity can serialize. `SerializedDictionary<TKey, TValue>` is a drop-in
`IDictionary<TKey, TValue>` that survives scenes, prefabs and assets, and shows up in the
inspector as a clean, Odin-style key/value table instead of not showing up at all.

- **Assembly:** `UnityEssentials.Collections` (`Runtime/`) — no dependencies beyond `UnityEngine`.
- **Namespace:** `UnityEssentials.Collections`
- **Editor:** `UnityEssentials.Collections.Editor` (`Editor/`) — the [inspector drawer](#inspector); Editor-only, never in builds.

---

## Quick start

```csharp
using UnityEngine;
using UnityEssentials.Collections;

public class Inventory : MonoBehaviour
{
    [SerializeField] private SerializedDictionary<string, int> _stock = new SerializedDictionary<string, int>();
    [SerializeField] private SerializedDictionary<ItemType, GameObject> _prefabs = new SerializedDictionary<ItemType, GameObject>();

    public bool TryTake(string item)
    {
        if (!_stock.TryGetValue(item, out var count) || count == 0)
        {
            return false;
        }

        _stock[item] = count - 1;
        return true;
    }
}
```

Use it exactly like a `Dictionary<TKey, TValue>`: `Add`, `TryAdd`, `Remove`, `TryGetValue`, the
indexer, `Keys`, `Values`, `ContainsKey`, `ContainsValue`, `Clear`, `foreach`. It implements
`IDictionary<TKey, TValue>` and `IReadOnlyDictionary<TKey, TValue>`, so it passes anywhere either
is expected. `new SerializedDictionary<TKey, TValue>(source)` copies an existing dictionary.

---

## How it syncs

Unity cannot serialize a `Dictionary`, so the type keeps two things side by side: a serialized
list of key/value rows, and a real `Dictionary<TKey, TValue>` that every lookup goes through.

- **Rows are the source of truth.** The inspector edits the rows. After every load or inspector
  edit, the dictionary is rebuilt from them.
- **Invalid rows are ignored, not deleted.** A row with a null key is skipped. When a key appears
  on several rows, the **first row wins** and the rest are ignored. Those rows stay in the list —
  loading never "cleans" your data — so a key you are halfway through typing is not lost, and the
  inspector can flag the conflict for you to fix.
- **Runtime changes are written back on the next serialize.** `Add`, `TryAdd`, `Remove` and the
  indexer only mark the dictionary dirty; the rows are rewritten when Unity next serializes the
  object (saving, `Instantiate`, opening it in the inspector). The rewrite keeps your row order:
  the first row of a key gets its current value, every row of a removed key is dropped, new keys
  are appended at the end, and null-key rows and later duplicates are left exactly where they are.
  A clean dictionary is never rewritten.
- **`Clear()` means empty.** It clears the dictionary *and* every row, invalid ones included.

`UnityEngine.Object` keys are null-checked with Unity's `==`, so unassigned, missing and destroyed
references all count as null keys — skipped at runtime, kept as rows.

---

## Supported types

Anything Unity can serialize works as a key or a value: primitives, strings, enums, Unity structs
(`Vector3`, `Color`, ...), `UnityEngine.Object` references, and your own `[Serializable]` classes
and structs. A key must also behave as a dictionary key — `Equals`/`GetHashCode` decide what counts
as a duplicate, and a key mutated after insertion is lost, as with any dictionary.

`[SerializeReference]` values are **not** supported: rows are serialized by value, so polymorphic
values collapse to their declared type.

---

## Performance

`GetEnumerator()` returns `Dictionary<TKey, TValue>.Enumerator`, so `foreach` allocates nothing.
Lookups, writes to existing keys, a clean serialize pass and a warm deserialize pass allocate
nothing either. A dirty serialize pass allocates one temporary `HashSet<TKey>`.

---

## Inspector

The editor assembly draws every `SerializedDictionary` field as a compact table:

- A foldout header with an entry-count badge and an **add** button.
- **Key | Value** columns with a draggable divider, and alternating row shading.
- A **remove** button on each row.
- Rows with a duplicate or null key are highlighted red with a warning icon, so conflicts the
  runtime is ignoring are obvious.
- Paging for large dictionaries, so a thousand entries do not stall the inspector.
- A header context menu with **Clear** and **Remove Invalid Entries** (drops every null-key and
  duplicate row in one go).
