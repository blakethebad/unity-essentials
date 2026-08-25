# UI System (UGUI) — Implementation Plan

A reusable UGUI window/screen/popup/panel/widget package for `Assets/Packages/UISystem/`, following this repo's library conventions (see `ServiceLocator`, `StateManager` and `Docs/HapticSystem-Plan.md`).

## Context

`unity-essentials` is a **library project**: a catalogue of self-contained, portable Unity systems (`ServiceLocator`, `StateManager`, `Haptics`) exported into real games. It currently has **zero runtime UI code** — no `Canvas`, no `RectTransform`, no screen/popup infrastructure anywhere under `Assets/`.

This adds the fourth package: a UGUI-based **UI System** at `Assets/Packages/UISystem/`, assembly and namespace `UnityEssentials.UI`. It gives games one entry point (`WindowService`) for showing, hiding and navigating UI, a closed inheritance vocabulary (`ScreenBase` / `PopupBase` / `PanelBase` / `WidgetBase`), and a `WindowData` ScriptableObject declaring which prefabs a window owns and how its Canvas is built.

The outcome: a consuming game writes no UI plumbing. It authors prefabs, fills a `WindowData`, constructs a `WindowService`, and calls `ShowUI<MainMenuScreen>()`. Screen exclusivity, popup stacking, auto-opening panels, back navigation and history bookkeeping are all handled by the package.

**v1 is deliberately narrow.** Designed-for but explicitly *not built*: show/hide tween animations, widget pooling, Addressables loading, on-demand UI, multiple canvas configurations per window. Each has a named seam below so adding it later does not change the public API.

### Decisions settled with you
| Question | Decision |
|---|---|
| Where does `WidgetBase` sit? | **`WidgetBase : UIBase`** — sealed-overrides `Show`/`Hide` into `OnShow`/`OnHide`, but registers with nothing. One `GetComponent<UIBase>()` check covers every prefab. |
| How does a `ScreenBase` reach `WindowService`? | **Injected at spawn.** `UIWindow` calls `internal Bind(window)` right after `Instantiate`. No statics on the service, no ServiceLocator dependency. |
| How does a screen declare auto-open panels? | **Code-side override**: `protected virtual Type[] AutoPanels`. No serialized type names, no property drawer. |
| Samples? | **None.** Runtime + Tests only — same shape as the `ServiceLocator` package. |

### Naming note
`UnityEssentials.UI` matches the repo's convention (folder named for the system, assembly for the domain). One hazard: inside `namespace UnityEssentials.UI`, a bare `UI.Foo` binds to our namespace, not `UnityEngine.UI`. Mitigation is one README rule — `using UnityEngine.UI;` at file top, never write a `UI.`-qualified name. No type in this package collides with a `UnityEngine.UI` type name.

---

## Package layout

No `Editor/` folder: the code-side `AutoPanels` decision removed the only thing needing a property drawer. Mirrors `ServiceLocator` exactly.

```
Assets/Packages/UISystem/
├── README.md                       long-form docs (Quick start / sections / Errors / Gotchas / API reference)
├── Runtime/
│   ├── UnityEssentials.UI.asmdef   references: ["UnityEngine.UI"] — the only reference
│   ├── AssemblyInfo.cs             [assembly: InternalsVisibleTo("UnityEssentials.UI.Tests")]
│   ├── IUIData.cs                  public empty marker interface
│   ├── UIElementState.cs           public enum : byte { Hidden = 0, Showing, Shown, Hiding }
│   ├── UIElementKind.cs            internal enum : byte { Screen, Popup, Panel, Widget }
│   ├── UIExceptions.cs             UIException base + 4 sealed subclasses (3-ctor convention)
│   ├── UIObjects.cs                internal static: Destroy/DestroyImmediate picker + guarded DontDestroyOnLoad
│   ├── UIRoot.cs                   internal static: the one unified WindowParent; lazy Ensure(); ResetStatics()
│   ├── UIHistory.cs                internal sealed UIHistory<TEntry> — PURE C#, zero Unity types
│   ├── UIWindow.cs                 sealed MonoBehaviour: builds the Canvas trio + 4 containers, loads/unloads UI
│   ├── WindowService.cs            public plain-C# entry point (IDisposable)
│   ├── Elements/
│   │   ├── UIBase.cs               abstract MonoBehaviour: state machine, transition token + seam, Bind
│   │   ├── ScreenBase.cs           sealed Show/Hide; supersedes previous screen; opens AutoPanels
│   │   ├── PopupBase.cs            sealed Show/Hide; registers; SetAsLastSibling
│   │   ├── PanelBase.cs            sealed Show/Hide; registers only, never touches history
│   │   └── WidgetBase.cs           sealed Show/Hide; registers with nothing
│   └── Data/
│       ├── WindowData.cs           sealed SO: prefab list + WidgetData list + CanvasSettings
│       ├── WidgetData.cs           [Serializable] sealed class wrapping one prefab — future pooling config
│       ├── CanvasSettings.cs       [Serializable] sealed class: every Canvas/Scaler/Raycaster knob + Apply
│       └── WindowDataValidator.cs  internal static: all list validation in one testable place
└── Tests/
    ├── UnityEssentials.UI.Tests.asmdef
    ├── UITestFixture.cs            base fixture: tracked-object teardown + UIRoot.ResetStatics()
    ├── TestUIElements.cs           all doubles + runtime prefab builders
    ├── UIHistoryTests.cs           WindowLoadTests.cs        ScreenFlowTests.cs
    ├── UIElementStateTests.cs      WindowSwitchTests.cs      PopupPanelTests.cs
    ├── RegistrationTests.cs        CanvasSettingsTests.cs    BackNavigationTests.cs
    └── WindowDataValidationTests.cs  WindowServiceResolutionTests.cs  WidgetTests.cs
```

---

## Public API surface

```csharp
namespace UnityEssentials.UI
{
    public interface IUIData { }

    public enum UIElementState : byte { Hidden = 0, Showing = 1, Shown = 2, Hiding = 3 }
    internal enum UIElementKind : byte { Screen, Popup, Panel, Widget }

    public class UIException : Exception { /* 3-ctor set */ }
    public sealed class WindowConfigurationException : UIException { }
    public sealed class UIElementNotFoundException  : UIException { }
    public sealed class NoActiveWindowException     : UIException { }
    public sealed class UIBindingException          : UIException { }

    // ---- Element hierarchy ----------------------------------------------

    [DisallowMultipleComponent]
    public abstract class UIBase : MonoBehaviour
    {
        public UIElementState State { get; private set; }
        public bool IsVisible { get; }                       // Showing || Shown
        public UIWindow Window { get; }                      // null until bound
        protected WindowService Service { get; }             // Window?.Service

        public abstract void Show(IUIData uiData);
        public abstract void Hide();

        internal abstract UIElementKind Kind { get; }        // closes the hierarchy — see below
        internal void Bind(UIWindow window);

        protected virtual void OnShow(IUIData uiData) { }
        protected virtual void OnHide() { }

        // v1 calls complete() synchronously. Tweens override and call it when the tween ends.
        protected virtual void OnShowTransition(Action complete);
        protected virtual void OnHideTransition(Action complete);

        private protected void ExecuteShow(IUIData uiData);
        private protected void ExecuteHide();
        private protected virtual void AfterShowRequested() { }   // ScreenBase opens AutoPanels here
        private protected void RequireBound();
    }

    public abstract class ScreenBase : UIBase
    {
        public sealed override void Show(IUIData uiData);
        public sealed override void Hide();
        protected virtual Type[] AutoPanels { get; }                     // default: shared empty array
        protected virtual IUIData GetAutoPanelData(Type panelType);      // default: null
    }

    public abstract class PopupBase  : UIBase { public sealed override void Show(IUIData d); public sealed override void Hide(); }
    public abstract class PanelBase  : UIBase { public sealed override void Show(IUIData d); public sealed override void Hide(); }
    public abstract class WidgetBase : UIBase { public sealed override void Show(IUIData d); public sealed override void Hide(); }

    // ---- Data -----------------------------------------------------------

    [Serializable]
    public sealed class WidgetData
    {
        public WidgetData();
        public WidgetData(GameObject prefab);
        public GameObject Prefab { get; }
        // Future: InitialPoolSize, MaxPoolSize, ExpandWhenEmpty
    }

    [Serializable]
    public sealed class CanvasSettings
    {
        public CanvasSettings();      // Overlay, ScaleWithScreenSize, 1920x1080, match 0.5

        public RenderMode RenderMode { get; }
        public float PlaneDistance { get; }
        public string SortingLayerName { get; }
        public int SortingOrder { get; }
        public bool PixelPerfect { get; }
        public int TargetDisplay { get; }

        public CanvasScaler.ScaleMode UIScaleMode { get; }
        public float ScaleFactor { get; }
        public Vector2 ReferenceResolution { get; }
        public CanvasScaler.ScreenMatchMode ScreenMatchMode { get; }
        public float MatchWidthOrHeight { get; }
        public float ReferencePixelsPerUnit { get; }
        public CanvasScaler.Unit PhysicalUnit { get; }
        public float FallbackScreenDPI { get; }
        public float DefaultSpriteDPI { get; }

        public bool IgnoreReversedGraphics { get; }
        public GraphicRaycaster.BlockingObjects BlockingObjects { get; }
        public LayerMask BlockingMask { get; }

        internal void Normalize();                                       // never throws
        internal void ThrowIfInvalid(Camera uiCamera);
        internal void Apply(Canvas c, CanvasScaler s, GraphicRaycaster r, Camera uiCamera);
    }

    [CreateAssetMenu(menuName = "UnityEssentials/UI/Window Data", fileName = "WindowData")]
    public sealed class WindowData : ScriptableObject
    {
        public static WindowData Create(GameObject[] uiPrefabs, WidgetData[] widgets, CanvasSettings canvas);
        public IReadOnlyList<GameObject> UIPrefabs { get; }   // screens/popups/panels only
        public IReadOnlyList<WidgetData> Widgets { get; }
        public CanvasSettings Canvas { get; }
        private void OnValidate();                            // clamps, never throws
        internal void ThrowIfInvalid(Camera uiCamera);
    }

    // ---- Window ---------------------------------------------------------

    [DisallowMultipleComponent]
    public sealed class UIWindow : MonoBehaviour
    {
        public WindowData Data { get; }
        public WindowService Service { get; }
        public Canvas Canvas { get; }
        public RectTransform ScreenRoot { get; }
        public RectTransform PanelRoot { get; }
        public RectTransform WidgetRoot { get; }
        public RectTransform PopupRoot { get; }
        public IReadOnlyList<UIBase> Elements { get; }
        public IReadOnlyList<WidgetBase> SpawnedWidgets { get; }

        // Deliberately untyped, per requirement 4: the window is the store,
        // WindowService is the typed facade.
        public UIBase GetUI<T>() where T : UIBase;
        public UIBase GetUI(Type uiType);
        public bool TryGetUI(Type uiType, out UIBase ui);

        public WidgetBase SpawnWidget(Type widgetType, Transform parent, IUIData uiData);
        public void DespawnWidget(WidgetBase widget);

        internal static UIWindow Create(WindowData data, WindowService service, Transform parent);
        internal void Unload();
        internal RectTransform ContainerFor(UIElementKind kind);
    }

    // ---- Service --------------------------------------------------------

    public sealed class WindowService : IDisposable
    {
        public WindowService();
        public Camera UICamera { get; set; }                  // required for non-Overlay render modes

        public UIWindow ActiveWindow { get; }
        public WindowData ActiveWindowData { get; }
        public bool IsWindowLoaded { get; }
        public ScreenBase ActiveScreen { get; }
        public ScreenBase PreviousScreen { get; }
        public IReadOnlyList<PopupBase> ActivePopups { get; }
        public IReadOnlyList<PanelBase> ActivePanels { get; }

        public event Action<WindowData, WindowData> WindowSwitched;   // (from, to); from may be null
        public event Action<UIBase> UIShown;                          // on entering Shown
        public event Action<UIBase> UIHidden;                         // on entering Hiding

        public UIWindow SwitchWindow(WindowData windowData);
        public void CloseWindow();

        public T ShowUI<T>(IUIData uiData = null) where T : UIBase;
        public void HideUI<T>() where T : UIBase;
        public T GetUI<T>() where T : UIBase;
        public bool TryGetUI<T>(out T ui) where T : UIBase;

        public T SpawnWidget<T>(Transform parent = null, IUIData uiData = null) where T : WidgetBase;
        public void DespawnWidget(WidgetBase widget);

        public bool Back();
        public bool ShowPreviousScreen();
        public void HideAllPopups();
        public void HideAllPanels();
        public void Dispose();                                // → CloseWindow()

        // Registration surface invoked from the sealed Show/Hide of the element bases.
        internal void OnScreenShowing(ScreenBase screen);
        internal void OnScreenHiding(ScreenBase screen);
        internal void OnPopupShowing(PopupBase popup);
        internal void OnPopupHiding(PopupBase popup);
        internal void OnPanelShowing(PanelBase panel);
        internal void OnPanelHiding(PanelBase panel);
        internal void ShowAutoPanel(ScreenBase owner, Type panelType, IUIData data);
        internal void RaiseShown(UIBase element);
    }

    internal sealed class UIHistory<TEntry> where TEntry : class
    {
        internal IReadOnlyList<TEntry> VisibleEntries { get; }   // screens + popups, show order
        internal IReadOnlyList<TEntry> ScreenTrail { get; }      // how the user got here
        internal TEntry Top { get; }
        internal TEntry TopScreen { get; }
        internal void PushScreen(TEntry screen);
        internal void PushOverlay(TEntry overlay);
        internal void Hide(TEntry entry, bool isScreen);
        internal TEntry PreviousScreen(TEntry current);
        internal bool TruncateTopScreen(TEntry current);
        internal void Clear();
    }
}
```

---

## Key design decisions

### 0. `WindowService` follows an existing plain-class precedent

The repo's dominant idiom is a *static* facade (`ServiceLocator`, `HapticService`), which "regular C# class" rules out. But there is already an exact precedent: `Assets/Packages/ServiceLocator/Runtime/ServiceScope.cs` is a `public sealed class ... : IDisposable` holding all its own state with no statics. `WindowService` copies that shape, including the `[Conditional("UNITY_EDITOR")]`/`[Conditional("DEVELOPMENT_BUILD")]` main-thread assert used across the repo.

A consumer wanting global access registers it themselves — `ServiceLocator.Register(new WindowService()).AsSelf()` — and `IDisposable` means scope release cleans it up. The package itself takes **no** dependency on `UnityEssentials.Services`.

### 1. The hierarchy is closed at exactly four leaves

`UIBase` declares `internal abstract UIElementKind Kind { get; }`. An assembly outside `UnityEssentials.UI` **cannot** implement an internal abstract member, so `class MyThing : UIBase` fails to compile. Consumers must derive from `ScreenBase`/`PopupBase`/`PanelBase`/`WidgetBase`, which is what makes the sealed `Show`/`Hide` an actual guarantee rather than a convention. The XML doc must say this loudly — the raw compiler error (CS0534) is cryptic.

`Kind` also does the discrimination requirement 3 asks for: one uniform `prefab.GetComponent<UIBase>()` check, then `Kind` routes the instance to the right container and the right dictionary.

`WidgetBase` implements `Show`/`Hide` as `sealed override` but performs **no `WindowService` registration** — requirement 2's sentence is about *registration*, not the type graph. Widgets never appear in `ActiveScreen`, `ActivePopups`, `ActivePanels` or either history, and they need no binding to work, which is what lets them be used as plain animated elements.

### 2. Registration lives in the `*Base` classes (requirement 7)

The **trigger** is in `ScreenBase.Show`; the **bookkeeping body** is an `internal` method on `WindowService`, which owns the lists. Calling `myScreen.Show(data)` directly, with no service call, still updates `ActiveScreen`, the active lists and history — pinned by a dedicated `RegistrationTests` fixture.

Elements are bound by `UIWindow` immediately after `Instantiate`, before they are ever shown. `UIBase.Window` is the single stored reference; `Service` reads through it, so a future feature that moves an element between windows rebinds one field.

An unbound screen/popup/panel that is shown throws `UIBindingException` naming the type and pointing at `WindowData`. Rebinding to a second window also throws. Widgets are exempt.

### 3. History: two lists, and no transition-mode flag

`UIHistory<TEntry> where TEntry : class` is **pure C# with no `UnityEngine` reference** (the repo's `TransitionTable<TState>` pattern), so the whole navigation model runs in the local reflection-based fixture runner without opening Unity. It holds:

- **`_visible`** — every currently-visible screen and popup, in show order. Panels excluded (requirement 6).
- **`_screens`** — the screen **back-trail**: how the user got here. Not "screens that exist".

| Operation | `_visible` | `_screens` |
|---|---|---|
| `PushScreen(s)` | remove `s` if present, append | remove `s` if present, append |
| `PushOverlay(p)` | remove `p` if present, append | untouched |
| `Hide(e, isScreen: false)` | remove `e` | untouched |
| `Hide(e, isScreen: true)` | remove `e` | remove `e` **only if `e` is the trail top** |
| `TruncateTopScreen(cur)` | untouched | remove `cur` if it is the trail top |

**The conditional prune is the whole trick, and it removes the need for any transition-mode state.** A screen hidden *because it was superseded* is not the trail top — the new screen was pushed first — so it stays, and it *is* the "previous screen". A screen hidden *on its own* **is** the trail top, so it is pruned and the trail retreats. That is requirement 6, with no flag to get wrong.

**Hard invariant, stated in bold in the XML docs:** `OnScreenShowing` must `PushScreen(newScreen)` **before** hiding the outgoing screen. Reversing those two lines silently produces the wrong previous screen. `ScreenFlowTests` pins it.

`Back()` **truncates rather than pushes**, giving real back-stack semantics:

```
Back():
  if (ActiveWindow == null) return false
  top = history.Top;  if (top == null) return false
  if (top is not a screen) { top.Hide(); return true }     // popup wins, even out of order
  return ShowPreviousScreen()

ShowPreviousScreen():
  previous = history.PreviousScreen(ActiveScreen)
  if (previous == null) return false
  history.TruncateTopScreen(ActiveScreen)                  // the trail retreats
  previous.Show(null)                                      // ScreenBase re-pushes it on top
  return true
```

Trace `A → B → C`, trail `[A,B,C]`: Back → truncate C → `[A,B]` → show B (dedup, append) → `[A,B]` → Back → `[A]` → Back → `PreviousScreen(A)` is null → `false`. Bounded, terminates, no ping-pong.

**Edge cases, all tested:**

| Case | Behaviour |
|---|---|
| Screen shown twice (A→B→A) | `PushScreen` dedups (move-to-top). Trail `[B,A]`. Prevents unbounded growth on menu ping-pong. |
| Popup closed out of order | Removed by identity from `_visible`; the rest intact. |
| Screen opened then closed (A→B, `B.Hide()`) | B is trail top → pruned → `[A]`; `ActiveScreen` null; `PreviousScreen == A`. ✔ req 6 |
| Screen superseded (A→B→C) | B is not trail top → retained. `PreviousScreen(C) == B`. ✔ |
| Window switch | Both lists `Clear()`. Non-negotiable: entries are about to be destroyed, and a destroyed Unity object is `== null` but not `ReferenceEquals(null)`, so a stale entry poisons identity comparison. Back does not cross window boundaries. |
| `Back()` with empty history | `false`, no throw. Back is input-driven, not a programming error; `false` is the caller's cue to quit the app. |
| `Back()` after closing the last screen | `false`. `ShowPreviousScreen()` is the separate method for deliberately restoring it. Two names, two meanings. |
| Re-`Show` the already-active screen | Fast path: re-run `OnShow(uiData)` only. No history change, no self-hide, panels/popups untouched — so `ShowUI<ShopScreen>(newData)` is a legal refresh without panel flicker. |
| `Hide()` on an already-`Hidden` element | Idempotent no-op. |

### 4. What "hidden" means, and the tween seam

v1 uses `gameObject.SetActive(false)` — the only option that removes the subtree from canvas rebuild, layout and raycasting at once, on any prefab, with no extra component. What makes it survive contact with tweens is **ordering**: activate at the *start* of showing, deactivate at the *end* of hiding, so a tween always runs on an active object.

```csharp
private protected void ExecuteShow(IUIData uiData)
{
    var token = ++_transitionToken;
    State = UIElementState.Showing;
    if (!gameObject.activeSelf) { gameObject.SetActive(true); }
    OnShow(uiData);
    AfterShowRequested();                      // ScreenBase opens its AutoPanels here
    OnShowTransition(() => CompleteShow(token));
}

private void CompleteShow(int token)
{
    if (token != _transitionToken) { return; } // superseded; ignore
    State = UIElementState.Shown;
    Service?.RaiseShown(this);
}

protected virtual void OnShowTransition(Action complete) { complete(); }   // v1: synchronous
```

The `int` token buys three properties at zero v1 cost:
1. A buggy tween callback firing twice cannot corrupt state.
2. **`Show` during a pending `Hiding` cancels the stale hide**, so the element is never left deactivated underneath a fresh show. This is the classic bug in hand-rolled UI systems.
3. `Show`/`Hide` stay `void` and stay sealed forever; only the `protected virtual` hooks change.

**Registration timing is also a seam decision:** the service is notified on entering `Showing` and on entering `Hiding`, not on `Shown`/`Hidden`. So `ActiveScreen`/`ActivePopups` reflect *intent*, and a `Back()` pressed mid-transition targets the right element.

### 5. Parenting, layering and the canvas

```
UnityEssentials.WindowParent          plain Transform, DontDestroyOnLoad when playing, VISIBLE
└── Window_MainMenu                   RectTransform + Canvas + CanvasScaler + GraphicRaycaster + UIWindow
    ├── Screens ├── Panels ├── Widgets └── Popups      RectTransforms, full-stretch
```

**Four containers, not sibling sorting.** UGUI draws depth-first in hierarchy order, so containers give correct self-maintaining layering with no per-show re-sorting and no nested canvases. Order `Screens < Panels < Widgets < Popups`: panels are screen composition so they sit above screens; widgets (health bars, floating text) are transient overlays above that; popups are last because a modal must never be occluded by a damage number. A caller wanting a widget above popups passes an explicit parent — which is why `SpawnWidget` takes one. Within `Popups`, `PopupBase.Show` calls `SetAsLastSibling()` so the newest renders on top.

The window GameObject is created in one call — `new GameObject(name, typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster), typeof(UIWindow))` — for deterministic component order.

The `WindowParent` is a plain `Transform` (a root canvas drives its own rect from the screen, so the parent's transform type is irrelevant) and — unlike `HapticLifecycleDriver` — carries **no hide flags**. Haptics hides invisible plumbing; this object holds the user's entire UI and must be inspectable while debugging. `DontDestroyOnLoad` is applied **only when `Application.isPlaying`**, the exact `HapticLifecycleDriver.Create` guard, which is what makes EditMode tests possible. It lives in an `internal static UIRoot` with a `[RuntimeInitializeOnLoadMethod(SubsystemRegistration)]` reset — mandatory here because this project runs with domain reload disabled.

**The package does not create an `EventSystem`.** An `EventSystem` needs an input module, and which one is correct depends on the consuming project's input backend; picking wrong silently kills all UI input. It is also effectively a scene singleton, and creating one under `DontDestroyOnLoad` would leak a project-wide input owner out of a UI library. Instead: after building the first window, if `Application.isPlaying && EventSystem.current == null`, log **one** warning naming the fix. The `isPlaying` guard keeps the EditMode test run quiet.

**Camera-dependent render modes:** a ScriptableObject cannot reference a scene `Camera`, so `CanvasSettings` carries the mode and `WindowService.UICamera` carries the camera. `ScreenSpaceCamera` with a null `UICamera` throws during pre-flight validation rather than silently falling back to Overlay, which produces a bug that looks like a shader problem. `WorldSpace` is rejected in v1 — it needs a placement contract that is out of scope.

**Elements are instantiated active, then deactivated**, so each runs `Awake`/`OnEnable`/`OnDisable` once at window-load. README: "your `Awake` runs at window load; do per-show setup in `OnShow`."

### 6. Auto-panels

```csharp
public sealed class MainMenuScreen : ScreenBase
{
    private static readonly Type[] Panels = { typeof(CurrencyPanel), typeof(NavBarPanel) };
    protected override Type[] AutoPanels => Panels;
}
```
The static-readonly-array pattern is documented so overrides do not allocate per show. Entries must be assignable to `PanelBase` (else `WindowConfigurationException`) and must resolve in the active window (else `UIElementNotFoundException` naming screen and panel) — both are wiring bugs. Panels open in declaration order, after the screen's `OnShow` and *before* its transition hook, so screen and panels animate in parallel once tweens exist.

The future serialized path is purely additive: add `[SerializeField] private UITypeReference[] autoPanels` to `ScreenBase` and have the *base* `AutoPanels` project it. Every existing override keeps working.

**All active panels and popups close on every screen change** — invariant: panels never outlive the screen that was active when they were shown.

### 7. Errors

**Governing principle — validate before you build.** `SwitchWindow` runs `WindowData.ThrowIfInvalid(UICamera)` over the entire asset *before* creating a single GameObject and *before* unloading the current window, so a malformed asset leaves the live UI untouched and leaks nothing. This mirrors `HapticPattern.Create`.

| Situation | Result |
|---|---|
| `SwitchWindow(null)` | `ArgumentNullException` |
| `SwitchWindow(activeAsset)` | No-op, returns existing window |
| Null entry / prefab with no `UIBase` / duplicate concrete type / widget in the wrong list | `WindowConfigurationException` naming list + index (+ both indices for a duplicate) |
| `ScreenSpaceCamera` with null `UICamera`; `WorldSpace` | `WindowConfigurationException` |
| `GetUI<T>()` miss | `UIElementNotFoundException` whose message lists the types the window *does* carry |
| `TryGetUI<T>(out T)` miss | `false`, `ui = null` — mirrors `ServiceLocator.Get`/`TryGet` |
| `GetUI<T>`/`ShowUI<T>` where `T : WidgetBase` | `UIElementNotFoundException`: "widgets are spawned, not resolved — use `SpawnWidget<T>()`" |
| Any resolve/show/spawn with no active window | `NoActiveWindowException` |
| `SpawnWidget<T>` for an unregistered type; `DespawnWidget` of a foreign widget | `UIElementNotFoundException` / `UIBindingException` |
| `Show`/`Hide` on an unbound screen/popup/panel; double-`Bind` | `UIBindingException` |
| `Back()` with nothing visible; `Hide()` on a hidden element | `false` / no-op — not errors |
| Any call after `Dispose()` | `ObjectDisposedException` |

`WindowData.OnValidate` **never throws** (Unity calls it mid-keystroke): it delegates to `CanvasSettings.Normalize()`, clamping match to 0–1, resolution components to ≥ 1, scale/DPI fields to > 0. All list validation is dispatch-time only. This is the two-tier `HapticPattern` split.

---

## Lifecycle traces

**(i) `SwitchWindow(dataA)` — first call.** Null/same-asset guards → `ThrowIfInvalid(UICamera)` (nothing created yet, so a throw leaves the world untouched) → `UnloadActiveWindow()` (no-op) → `UIRoot.Ensure()` creates `WindowParent` with the play-mode-guarded `DontDestroyOnLoad` → `UIWindow.Create`: build the GameObject with the Canvas trio, `CanvasSettings.Apply`, create the four containers, then for each prefab `GetComponent<UIBase>()` → `Instantiate` into `ContainerFor(Kind)` → `Bind(this)` → `SetActive(false)` → `_elements[concreteType] = inst`; widget prototypes stored **without** instantiating → warn once if no `EventSystem` → `WindowSwitched(null, dataA)`. **Shows nothing.** The idiom is `SwitchWindow(data); ShowUI<MainMenuScreen>();`

**(ii) `ShowUI<ShopScreen>()` with `MainMenuScreen` active and 2 panels open.** Resolve (window/widget/miss guards) → `ShopScreen.Show(null)` → `RequireBound` → not the active screen → `OnScreenShowing`: `_activeScreen = Shop` and **`PushScreen(Shop)` first**, then `HideAllPopups()`, `HideAllPanels()` (reverse order), then `MainMenu.Hide()` → its `OnScreenHiding` sees the trail top is Shop, not MainMenu, so **MainMenu is retained** as the previous screen → back in `ExecuteShow`: `SetActive(true)`, `Showing`, `OnShow`, then `AfterShowRequested` opens `AutoPanels` in declaration order, then the transition completes to `Shown`. End: trail `[MainMenu, Shop]`, `PreviousScreen == MainMenu`. ✔

**(iii) `Back()` with a popup open.** `Top` is the popup, not a screen → `popup.Hide()` → `OnPopupHiding` removes it from `ActivePopups` and `_visible`; the trail is untouched; `ActiveScreen` unchanged → `true`. A second `Back()` sees a screen on top and runs `ShowPreviousScreen()`.

**(iv) Switching to a second window.** `ThrowIfInvalid(dataB)` first, while window A is still live → `HideAllPopups`/`HideAllPanels`/`ActiveScreen.Hide()` in that order (hide-before-destroy is deliberate: `OnHide` is the author's teardown hook — unsubscribing events, stopping coroutines — and skipping it would leak subscriptions) → `history.Clear()` → `Unload()` despawns tracked widgets (including externally parented ones) and destroys the window GameObject → `UIRoot` reused → new window built as in (i) → `WindowSwitched(dataA, dataB)`.

---

## Test plan

All EditMode, plain NUnit `[Test]` — no `[UnityTest]`, no PlayMode, matching every existing fixture in the repo. MonoBehaviours are built with `new GameObject().AddComponent<T>()`; `Instantiate` on a scene GameObject behaves like a prefab, so `WindowData.Create(...)` is fed runtime-built "prefabs".

Logic deliberately extracted so it is testable without Unity objects, following the repo's `TransitionTable` / `AndroidWaveformConverter` precedent: **`UIHistory<TEntry>`** (zero Unity types — the highest-risk logic in the package, driven in tests by plain `object`s), `CanvasSettings.Normalize/ThrowIfInvalid`, `WindowDataValidator`, `UIObjects`.

| File | Asserts |
|---|---|
| `UITestFixture`, `TestUIElements` | *(support)* tracked-object teardown + `UIRoot.ResetStatics()`; doubles incl. `DeferredTransitionScreen` (holds the `complete` callback) and `CountingScreen`. |
| `UIHistoryTests` | **Runs with zero Unity objects.** Push/dedup move-to-top; supersede retains the non-top screen; closing the top screen prunes it; `PreviousScreen` across A→B, A→B→C, A→B→A and after a close; the `TruncateTopScreen` back sequence terminates; popup removed out of order; overlay push never touches the trail; empty-history queries. |
| `UIElementStateTests` | Everything starts `Hidden`; synchronous v1 transitions; deferred screen stays `Showing` until `complete()`; **double-`complete()` is a no-op**; **`Show` during a pending `Hiding` invalidates the stale token so the object is never left deactivated**; `IsVisible` across all four states. |
| `RegistrationTests` | **Requirement 7's core:** `Show`/`Hide` called directly on a spawned element keeps `ActiveScreen`, both lists and both histories correct; unbound screen/popup/panel throws; unbound **widget** works; double-`Bind` throws. |
| `WindowDataValidationTests` | Null entry, missing `UIBase`, duplicate type, widget/non-widget in the wrong list → exception naming list + index; `OnValidate` never throws on any of them. |
| `CanvasSettingsTests` | `Apply` writes every field onto real components; `Normalize` clamps and never throws; camera-mode without a camera and `WorldSpace` throw; defaults are Overlay/1920×1080/0.5. |
| `WindowLoadTests` | `UIRoot` lazy, once, reused, recreated after reset; no `DontDestroyOnLoad` outside play mode; container order; Canvas trio configured; elements instantiated inactive and `Hidden`; dictionary keyed by concrete type; widget prototypes stored **uninstantiated**. |
| `WindowSwitchTests` | Popups, panels and screen all get `OnHide` **before** anything is destroyed, in the documented order; histories cleared; `UIRoot` reused; old instances destroyed; same-asset switch is a no-op; **invalid `WindowData` throws without unloading the live window**; `WindowSwitched` args. |
| `WindowServiceResolutionTests` | `GetUI<T>` hit/miss with a message listing available types; `TryGetUI` false + null out; every API before `SwitchWindow` throws `NoActiveWindowException`; widget-type resolve gives the "use `SpawnWidget`" message. |
| `ScreenFlowTests` | One screen visible; previous `OnHide` runs exactly once; `AutoPanels` open in declaration order after `OnShow`; invalid/missing panel entries throw the right type; **same-screen re-show refreshes without re-pushing history**; previous screen's panels all close; `GetAutoPanelData` reaches the panel. |
| `PopupPanelTests` | Popups stack; `SetAsLastSibling` ordering in `PopupRoot`; out-of-order hide; list contents and order; `HideAll*` hide in **reverse** order (asserted via `OnHide` call order); panels never enter history. |
| `BackNavigationTests` | Popup precedence; screen back-step; repeated `Back()` terminates; `false` on empty history, single screen, and after a window switch; `ShowPreviousScreen` works when nothing is visible. |
| `WidgetTests` | `SpawnWidget` instantiates/parents/binds/shows; null parent defaults to `WidgetRoot`; external parent honoured; unregistered type throws; `Despawn` hides then destroys and untracks; widgets touch no service list or history; unload despawns externally parented widgets. |

---

## Delegation plan — 10 agents via Workflow, disjoint file ownership

Per the established division of labour: agents implement, I review/fix/refactor directly and run verification. Phase 0 freezes the signature block above and hands it verbatim to every later agent; no agent may change a signature.

| Phase | Agents | Owns |
|---|---|---|
| **0 — Contracts** (blocking) | 1 | All 2 asmdefs, `AssemblyInfo.cs`, `IUIData`, `UIElementState`, `UIElementKind`, `UIExceptions`, `UIObjects`. Zero design freedom. |
| **1 — Independent** (parallel) | 3 | **A** `UIHistory.cs` (pure; gets the §3 table + traces as spec). **B** `Data/*` (4 files). **C** `Elements/UIBase.cs` + `WidgetBase.cs` (gets the §4 code block verbatim). |
| **2 — Integration** (parallel) | 3 | **D** `UIRoot.cs` + `UIWindow.cs`. **E** `WindowService.cs` (traces are its acceptance criteria). **F** `ScreenBase`/`PopupBase`/`PanelBase` (gets "push before hide" in bold). |
| **3 — Test scaffold** (blocking) | 1 | **G** `UITestFixture.cs` + `TestUIElements.cs` — also the first real consumer of the API, surfacing friction before three agents write against it. |
| **4 — Tests** (parallel) | 3 | **H** history/state/registration. **I** data/canvas/load/switch/resolution. **J** screen/popup-panel/back/widget. |
| **5 — Docs** | lead | `README.md` in the house structure — written last, because it documents actual behaviour. |
| **6 — Lead pass** | not delegated | Line-by-line review with direct fixes; XML-doc density audit against `ServiceLocator`/`StateManager`; then verification below. |

The E↔F dependency cycle (`WindowService.ActiveScreen` needs `ScreenBase`; `ScreenBase.Show` calls `Service.OnScreenShowing`) is exactly what the Phase 0 frozen contract exists to break.

---

## Verification

1. **Roslyn compile-check** (works with the Editor open): `dotnet exec <sdk>\Roslyn\bincore\csc.dll /noconfig /nostdlib+ /langversion:9.0 /define:UNITY_EDITOR;UNITY_INCLUDE_TESTS` against Unity's 4.7.1 API profile plus `UnityEngine.CoreModule.dll`, `Unity.Scripting.dll`, `Managed/UnityEngine/UnityEngine.UIModule.dll`, and `Library/ScriptAssemblies/UnityEngine.UI.dll` — **both confirmed present on this machine**. Compile with the real assembly names so `InternalsVisibleTo` is exercised. Exit 0 ⇒ compiles in Unity.
2. **Pure-fixture reflection run** of `UIHistoryTests` outside Unity (it touches no `GameObject`), per the existing PowerShell runner recipe. Everything else needs `AddComponent` and waits for step 4.
3. **`.meta` generation, headless.** Files are created via CLI so Unity must stamp GUIDs. The Editor is currently **closed**, so this needs no manual step:
   ```powershell
   & "C:\Program Files\Unity\Hub\Editor\6000.5.6f1\Editor\Unity.exe" -batchmode -quit `
     -projectPath "C:\Users\user\Documents\UnityProjects\Personal\unity-essentials" -logFile "Logs\ui-import.log"
   ```
   Then check the log for compile errors before running tests.
4. **Full EditMode batchmode run** (Editor must stay closed — check `Get-Process Unity` / `Temp/UnityLockfile`):
   ```powershell
   & "C:\Program Files\Unity\Hub\Editor\6000.5.6f1\Editor\Unity.exe" -batchmode `
     -projectPath "C:\Users\user\Documents\UnityProjects\Personal\unity-essentials" `
     -runTests -testPlatform EditMode -testFilter "UnityEssentials.UI.Tests" `
     -testResults "Logs\ui-results.xml" -logFile "Logs\ui-test-run.log"
   ```
5. **Manual smoke** (yours, optional): create a `WindowData` via `Assets ▸ Create ▸ UnityEssentials ▸ UI ▸ Window Data`, add a screen prefab, construct a `WindowService` from a bootstrap MonoBehaviour, then `SwitchWindow` + `ShowUI<T>()`. Add an `EventSystem` to the scene — the package warns but never creates one.
