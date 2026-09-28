# UI System — Trim to a single `UIBase`

What `Assets/Packages/UISystem/` actually ships today. `Docs/UISystem-Plan.md` describes the full
system this was cut down from, and is kept as the blueprint for expanding back to it.

## Why

The package was built as a full navigation framework: four element kinds
(`ScreenBase`/`PopupBase`/`PanelBase`, plus a parallel `UIWidget`/`WidgetBase` hierarchy), a
`WindowService` owning screen exclusivity, popup stacking, auto-opening panels, and a `UIHistory<T>`
back-stack. That is ~2100 lines of runtime and ~3800 lines of tests to carry into a small demo
project that needs none of it.

The goal was a version small enough to drop into demos and write UI against immediately: one
`UIBase` with show/hide, a `UIService` that instantiates and caches one instance per type, and
nothing that tracks what is open or in what order.

**Hard constraint: the trim is a strict subset.** Every deleted concept must be re-addable as a pure
addition. One place could have violated that — the binding guard — and it was resolved by choice
(see below), not by accident.

## What it is now

```
UIService  ──owns──>  UIWindow  ──owns──>  UIBase instances
(IDisposable)         (Canvas trio,        (one per concrete type,
                       element store)       Show / Hide)
        ▲
   WindowData (ScriptableObject: prefab list + CanvasSettings)
```

- `UIService.SwitchWindow(windowData)` validates the asset, tears down the live window, builds the
  new one and instantiates one deactivated, bound instance of every listed prefab.
- `ShowUI<T>()` / `HideUI<T>()` / `GetUI<T>()` / `TryGetUI<T>()` resolve by concrete type.
- `UIBase.Show(IUIData)` / `Hide()` drive a four-state machine
  (`Hidden → Showing → Shown → Hiding`) with a token-guarded transition seam.
- Events: `WindowSwitched`, `UIShown` (on reaching `Shown`), `UIHidden` (on *entering* `Hiding`).

Runtime: 10 files, ~1100 lines. Tests: EditMode only.

## Removed

`ScreenBase`, `PopupBase`, `PanelBase`, `UIWidget`, `WidgetBase`, `WidgetData`, `UIHistory<T>`, and
everything on the service that tracked open UI — `Back()`, `ShowPreviousScreen()`, `ActiveScreen`,
`PreviousScreen`, `ActivePopups`, `ActivePanels`, `HideAllPopups()`, `HideAllPanels()`, the eight
`On*Showing`/`On*Hiding` registration hooks, `ShowAutoPanel`, `AutoPanels`, `GetAutoPanelData`,
`AfterShowRequested`, `GetWidget`/`ReturnWidget`.

## Four decisions that protect the expansion path

**1. The hide-on-teardown pass moved into `UIWindow.UnloadWindow`.** It used to live in
`WindowService.UnloadActiveWindow`, which hid popups → panels → screen before destroying, because
`OnHide` is the author's teardown hook and must run while the object is alive. Deleting the service's
bookkeeping would have silently deleted that guarantee, leaking every subscription made in `OnShow`.
`UIWindow` already owns the complete element store, so it now walks `_elements` in reverse index
order and hides anything visible — no bookkeeping needed, and no array snapshot, since `Hide()` no
longer mutates a service-side list. The order is documented as *"authored order, reversed — current
layout, not a guarantee"*, so a future kind-aware version can tighten it to true reverse-show order
without that being a documented break.

**2. The binding guard sits on `UIBase.Show`.** The `Window == null` → `UIBindingException` check was
duplicated in the three kind-specific bases, while `WidgetBase` deliberately omitted it — widgets
worked unbound. One base class forces a single choice. Tolerating unbound elements would invite
consumers to use `UIBase` as a plain animated component, and re-introducing `ScreenBase` with its
throw would then break them. Guarding from day one is the strict-subset-safe option. The cost: this
is a UI-element base, not a general-purpose animated-component base.

**3. `Show`/`Hide` are non-virtual, but `ExecuteShow`/`ExecuteHide` stay factored out as
`private protected`.** Inlining them is the tempting "simplification" and it would delete the exact
seam the four leaves used. Adding `virtual` to `Show` later is source-compatible (this package ships
as source, so no precompiled subclass exists). The real trap is a future `ScreenBase` declaring
`public new void Show(...)`: `UIService.ShowUI<T>` calls through a `UIBase`-typed local, so a `new`
method is silently bypassed and registration never runs. `UIBase.Show` carries a `<remarks>` saying
a kind-specific `Show` must be an `override`, never a `new`.

**4. `UIWindow.GetUI<T>()` returns `T`.** It returned `UIBase` despite the `where T : UIBase`
constraint, so every call site cast. Return types can't change without a break; the rename window was
the only free moment.

## Expansion path

| To add back | Change needed | Breaking? |
|---|---|---|
| Screen exclusivity | `Show` gains `virtual`; `ScreenBase : UIBase` overrides it and calls new `internal` hooks; service regains `ActiveScreen` | No |
| Popup stacking | `PopupBase : UIBase` overriding `Show` with `transform.SetAsLastSibling()`. **No hierarchy change** — the shipped design never had per-kind containers; everything is parented directly under the window | No |
| Panels | `PanelBase : UIBase` + `ActivePanels`/`HideAllPanels()`. Pure addition | No |
| History / `Back()` | Restore `UIHistory<TEntry>` — it was always `internal` | No |
| Auto-panels | `ScreenBase` gains `AutoPanels`; `UIBase` needs its `private protected virtual AfterShowRequested()` hook back — invisible to consumers | No |
| Widgets | Restore `UIWidget`/`WidgetBase`/`WidgetData`; `WindowData.Create` needs a third overload | No, but see below |

The one wart: `WindowData.Create` lost its `WidgetData[]` parameter, so re-adding it means an
overload rather than a signature change. Acceptable — it is a test-only factory.

## Known limits

- **Async hide transitions are not awaited on teardown.** `UnloadWindow` hides every visible element
  then destroys the window, so an element whose `OnHideTransition` has not completed synchronously is
  destroyed mid-transition. `OnHide` still runs, so nothing leaks. v1 transitions are synchronous;
  this matters only once tweens are added, and it was equally true before the trim.
- **`UIHidden` fires on entering `Hiding`, `UIShown` on reaching `Shown`.** Temporally asymmetric, but
  behaviour-preserving: the old service raised `UIHidden` from `OnScreenHiding`, which also ran before
  `ExecuteHide`. The state flips at the start of a transition so listeners see intent.
- **`Dispose()` is idempotent `CloseWindow()`** and does not mark the service dead — a disposed
  service is reusable via a later `SwitchWindow`. Deliberate: a throwing `Dispose` is a footgun in
  test teardown, and the service holds no unmanaged state.
- `UIBase.Show` throws when unbound; `Hide()` is silent. Asymmetric on purpose — tearing down
  something that was never shown should not throw.

## Verification

Roslyn compile-check (works with the Editor open, unlike `-batchmode`) driving `csc.dll` from the
references and defines in Unity's own generated `UnityEssentials.UI.csproj` / `.Tests.csproj`, then
the EditMode run:

```powershell
& "C:\Program Files\Unity\Hub\Editor\6000.5.6f1\Editor\Unity.exe" -batchmode `
  -projectPath "C:\Users\user\Documents\UnityProjects\Personal\unity-essentials" `
  -runTests -testPlatform EditMode -testFilter "UnityEssentials.UI.Tests" `
  -testResults "Logs\ui-editmode.xml" -logFile "Logs\ui-test-run.log"
```
