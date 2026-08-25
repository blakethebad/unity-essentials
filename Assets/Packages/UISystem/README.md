# UI System

A UGUI window/screen/popup/panel/widget system for Unity 6. One entry point for showing,
hiding and navigating UI; a four-leaf element vocabulary; a ScriptableObject that
declares what a window owns and how its canvas is built.

- **Assembly:** `UnityEssentials.UI` (`Runtime/`) — references `UnityEngine.UI` only.
- **Namespace:** `UnityEssentials.UI`
- **Tests:** `UnityEssentials.UI.Tests` (EditMode only)

**The consuming game writes no UI plumbing.** You author prefabs, list them in a
`WindowData`, construct a `WindowService`, and call `ShowUI<MainMenuScreen>()`. Screen
exclusivity, popup stacking, auto-opening panels, back navigation and history bookkeeping
are all handled by the package.

**v1 is deliberately narrow.** Designed-for but not built: show/hide tween animations
(the transition hooks are the seam), widget pooling (`WidgetData` is the seam),
Addressables/on-demand loading, multiple canvases per window. Windows always render as
**screen-space overlay** canvases — no camera dependency of any kind.

---

## Quick start

```csharp
using UnityEssentials.UI;

public sealed class UIBootstrap : MonoBehaviour
{
    [SerializeField] private WindowData mainMenuWindow;   // created via the asset menu

    private WindowService _ui;

    private void Awake()
    {
        _ui = new WindowService();
        _ui.SwitchWindow(mainMenuWindow);   // builds everything, shows nothing
        _ui.ShowUI<MainMenuScreen>();       // shows the first screen
    }

    private void OnDestroy() => _ui.CloseWindow();
}
```

1. Write your elements: `public sealed class MainMenuScreen : ScreenBase { }` (and
   popups/panels/widgets the same way), put each on the root of its own prefab.
2. Create the asset: **Assets ▸ Create ▸ UnityEssentials ▸ UI ▸ Window Data**, drag the
   screen/popup/panel prefabs into **UI Prefabs** and widget prefabs into **Widgets**.
3. Put an `EventSystem` in your scene — the package warns if one is missing but never
   creates one (it cannot know which input module your project needs).

A consumer wanting global access can self-register:
`ServiceLocator.Register(new WindowService()).AsSelf();` — the package itself has **no**
dependency on `UnityEssentials.Services`.

---

## The four element kinds

Every element derives from one of exactly four bases. Screens, popups and panels root at
`UIBase`; widgets are deliberately a **separate hierarchy** rooted at `UIWidget`, so a
widget type can never be handed to `GetUI`/`ShowUI` — it does not compile. `Show`/`Hide`
are `sealed` on all four leaves, so the registration each kind performs cannot be skipped
or forgotten.

| | Registered in | Enters history | Exclusive | Created | Works unbound |
|---|---|---|---|---|---|
| `ScreenBase` | `ActiveScreen` | visible + back-trail | yes — one at a time | at window load | no |
| `PopupBase` | `ActivePopups` | visible only | no — stacks | at window load | no |
| `PanelBase` | `ActivePanels` | never | no | at window load | no |
| `WidgetBase` | nothing | never | no | spawned on demand | **yes** |

- **Screens** are destinations: the main menu, the shop. Showing one supersedes the
  previous screen and closes every open popup and panel.
- **Popups** are transient overlays: dialogs, modals. Any number stack; the newest draws
  on top; `Back()` closes them first.
- **Panels** are screen composition: nav bars, currency headers. Tracked only so they can
  be closed with the screen that owns them; invisible to navigation.
- **Widgets** are spawned, repeatable elements: health bars, damage numbers, toasts. They
  register with nothing and work with no window at all, which makes `WidgetBase` double as
  a base class for any plain animated element.

Override `OnShow(IUIData)` for per-show setup, `OnHide()` for teardown, and the transition
hooks for animation. **Never `Awake` for per-show work**: elements are instantiated active
at window load and immediately deactivated, so `Awake`/`OnEnable`/`OnDisable` run exactly
once, at load — author prefabs active and let the window hide them.

### Element states

`Hidden → Showing → Shown → Hiding → Hidden`, exposed as `element.State` and
`element.IsVisible` (`Showing || Shown`). The state flips at the *start* of a transition;
in v1 transitions complete synchronously, so from outside you only ever observe `Shown`
and `Hidden`. "Hidden" physically means `SetActive(false)` — activation happens at the
start of showing, deactivation at the end of hiding, so a future tween always runs on a
live object.

### The tween seam

```csharp
protected override void OnShowTransition(Action complete)
{
    transform.localScale = Vector3.zero;
    transform.DOScale(1f, 0.25f).OnComplete(() => complete());
}
```

The base implementations invoke `complete()` immediately — that is all that makes v1
synchronous. An internal transition token makes a double-fired or superseded callback
inert, and a `Show` issued during a pending hide cancels the stale hide so the element is
never deactivated underneath a fresh show.

---

## Windows

A **window** is the unit of loading: one `WindowData` asset, one overlay canvas, one
instance of every listed screen/popup/panel, built in one call and destroyed in one call.
Group prefabs by game phase (a main-menu window, an in-game HUD window) rather than
putting the whole game's UI in one asset.

```csharp
_ui.SwitchWindow(hudWindowData);   // validates, tears down the old window, builds the new
_ui.CloseWindow();                 // tears down; the game now has no UI
```

- **Loading shows nothing.** The idiom is always `SwitchWindow(data); ShowUI<T>();`.
- **Validate before you build.** The entire asset is checked *before* the live window is
  touched — a malformed asset throws and leaves the running UI fully intact.
- **Same-asset switch is a no-op** returning the existing window.
- **Teardown order:** popups (reverse show order), then panels (reverse), then the active
  screen, then destroy — every element's `OnHide` runs while it is still alive.
- **History is cleared on every switch**: `Back()` never crosses a window boundary.

The window hierarchy: each service parents its windows under its own deliberately visible
`UnityEssentials.WindowParent` object (`DontDestroyOnLoad` while playing), created on
demand and destroyed by `CloseWindow`:

```
UnityEssentials.WindowParent
└── Window_MainMenu          RectTransform + Canvas + CanvasScaler + GraphicRaycaster + UIWindow
    ├── MainMenuScreen       every element sits directly under the window;
    ├── SettingsPopup        sibling order = the order authored in WindowData
    └── NavBarPanel
```

There are no per-kind containers: elements draw in the order the asset lists them, except
popups, which move themselves to the last sibling on every show so the newest always draws
on top. Spawned widgets default to the window itself; pass an explicit parent to
`GetWidget` to place one anywhere else.

Under the hood the lifecycle is three calls: `WindowData.GenerateWindow(parent)` creates
the window GameObject with its canvas trio configured, `UIWindow.LoadWindow(service)`
instantiates the elements and registers the widget prototypes, and
`UIWindow.UnloadWindow()` releases everything the window loaded and destroys it.
`SwitchWindow` orchestrates all three — call it rather than driving the window by hand.

---

## Showing, hiding, resolving

```csharp
var shop = _ui.ShowUI<ShopScreen>(new ShopScreenData { Currency = 420 });
_ui.HideUI<SettingsPopup>();

var screen = _ui.GetUI<ShopScreen>();          // throws with the window's inventory on a miss
if (_ui.TryGetUI<DebugOverlayPanel>(out var overlay)) { }   // false + null on a miss
```

`IUIData` is an empty marker interface — define a small class per element that needs data
and pattern-match it in `OnShow`. The parameter defaults to `null` everywhere
(`Show()`, `ShowUI<T>()`), and every `OnShow` must tolerate `null`.

Two properties worth knowing:

- **Element-first registration.** `myScreen.Show(data)` called directly — from a button
  handler holding a serialized reference — updates `ActiveScreen`, the lists and the
  history exactly as `ShowUI` does. The service is a typed convenience, not the only door.
- **Same-screen re-show is a refresh.** `ShowUI<ShopScreen>(newData)` on the already
  active screen re-runs only `OnShow`: no history churn, no panel flicker.

---

## Navigation

```csharp
if (!_ui.Back())
{
    PromptQuit();   // false = nothing to go back to; never throws
}
```

`Back()` closes the topmost popup if any popup is open (the screen is untouched);
otherwise it returns to the previous screen. Repeated presses terminate: each screen step
*truncates* the back-trail, so `A → B → C` walks back `C → B → A → false`, bounded by the
number of distinct screens.

**The history also remembers each screen's `IUIData`.** Going back re-shows the previous
screen with the payload it was last opened with — a screen opened with `null` is restored
with `null`, and a same-screen refresh updates the stored payload.

The trail records *how the user got here*, not which screens exist:

- **Superseded screens are retained** — after `A → B`, A is the previous screen.
- **Screens closed on their own are pruned** — after `A → B` then `HideUI<B>()`, nothing
  is visible, and the previous screen is A.
- **Menu ping-pong dedups** — `A → B → A` leaves the trail `[B, A]`, never growing.

`ShowPreviousScreen()` is the separate, deliberate restore: unlike `Back()` it works when
nothing is visible at all (the state after closing the last screen). Two names, two
meanings — a back press must not resurrect a screen the game deliberately closed.

Also available: `HideAllPopups()` / `HideAllPanels()` (newest first; safe no-ops with no
window), `ActiveScreen`, `PreviousScreen`, `ActivePopups`, `ActivePanels` (live,
non-allocating views in show order).

---

## Auto-panels

A screen declares the panels that compose it; they open on every show, in declaration
order, after the screen's `OnShow` (so they can read state it just prepared) and before
its transition (so screen and panels animate in parallel once tweens exist).

```csharp
public sealed class MainMenuScreen : ScreenBase
{
    private static readonly Type[] Panels = { typeof(CurrencyPanel), typeof(NavBarPanel) };

    protected override Type[] AutoPanels => Panels;   // static readonly: no per-show allocation

    protected override IUIData GetAutoPanelData(Type panelType) =>
        panelType == typeof(CurrencyPanel) ? new CurrencyData(_wallet.Balance) : null;
}
```

Both failure modes are wiring bugs and throw: an entry that is not a `PanelBase` is a
`WindowConfigurationException` (the declaration is wrong); a panel the active window does
not carry is a `UIElementNotFoundException` naming both the screen and the panel (the
prefab is missing from the `WindowData`). An override returning `null` means "no panels".

Panels never outlive their screen: every screen change closes all open panels, and the
incoming screen re-declares what it wants.

---

## Widgets

```csharp
var damage = _ui.GetWidget<DamageNumberWidget>(null, new DamageData(1250));
// ... later
_ui.ReturnWidget(damage);   // hides (OnHide runs), then destroys
```

Widget prefabs live in `WindowData.Widgets` and are stored as **prototypes** — nothing is
instantiated at load; each `GetWidget` clones one. `null` parent means the window itself;
an explicit parent is honoured anywhere, including outside the window — the window tracks
what it spawned and tears it all down on unload, wherever it ended up.

**Every widget you get, you return.** The get/return pair exists because a pooling
mechanism will want the instance back; in v1 returning destroys, but callers should not
rely on that. Returning a widget the window did not spawn is a `UIBindingException`, not
a silent destroy.

Widgets are not resolvable — `GetUI<SomeWidget>()`/`ShowUI<SomeWidget>()` do not even
compile, because `WidgetBase` roots at `UIWidget`, not `UIBase`. There is no single
instance a window could return.

Pooling is a planned addition behind these exact signatures — write `OnShow`/`OnHide` to
fully reset the widget's visual state rather than relying on a fresh instance.

---

## Canvas configuration

Each `WindowData` carries a `CanvasSettings` describing the
`Canvas`/`CanvasScaler`/`GraphicRaycaster` trio. The render mode is always screen-space
overlay. Defaults: scale with screen size, 1920×1080 reference resolution, 0.5
width/height match — sensible on every aspect ratio without touching anything.

Inspector numbers are clamped as you type (`OnValidate` never throws); building non-default
settings in code goes through the serializer:
`JsonUtility.FromJsonOverwrite("{\"sortingOrder\":7}", new CanvasSettings())` — field names
are the property names with a lower-case initial.

---

## Events

```csharp
_ui.WindowSwitched += (from, to) => _cachedScreen = null;  // drop GetUI caches here
_ui.UIShown  += element => Analytics.ScreenView(element.GetType().Name);
_ui.UIHidden += element => { };
```

| Event | Fires |
|---|---|
| `WindowSwitched(from, to)` | after a switch completes; `from` null on first load, `to` null on `CloseWindow`. Exactly once per switch. |
| `UIShown(element)` | on entering `Shown` — i.e. when the show *transition completes*. Screens, popups and panels only; widgets are a separate process and raise nothing. |
| `UIHidden(element)` | on entering `Hiding` — on *intent*, while the element is still visible, after the lists/history are already updated. Screens, popups and panels only. |

Handler exceptions propagate; the package never swallows them.

---

## Errors

All package exceptions derive from `UIException` and signal programming or authoring
errors — fix the wiring, do not catch and continue.

| Situation | Result |
|---|---|
| `SwitchWindow(null)` | `ArgumentNullException` |
| `SwitchWindow(activeAsset)` | no-op, returns the existing window |
| Null list entry / prefab missing its component / duplicate concrete type / prefab in the wrong list | `WindowConfigurationException` naming asset, list and index (both indices for a duplicate) |
| `GetUI<T>` / `ShowUI<T>` / `HideUI<T>` miss | `UIElementNotFoundException` listing the types the window *does* carry |
| A widget `Type` handed to `UIWindow.GetUI(Type)` | `UIElementNotFoundException`: widgets are spawned, not resolved — use `GetWidget<T>()` (the generic surface rejects widget types at compile time) |
| Any resolve/show/spawn with no active window | `NoActiveWindowException` |
| `GetWidget<T>` for an unregistered type | `UIElementNotFoundException` listing spawnable types |
| `ReturnWidget` of a foreign widget | `UIBindingException` |
| `Show`/`Hide` on an unbound screen/popup/panel; double-`Bind` | `UIBindingException` |

Deliberately **not** errors: `Back()` with nothing to close (`false`), `Hide()` on a
hidden element (no-op), `TryGetUI` miss (`false` + null — including before the first
`SwitchWindow`), `HideAll*`/`CloseWindow` with nothing loaded (no-ops).

---

## Gotchas

### The package never creates an EventSystem

An `EventSystem` needs an input module, and which one is correct depends on your input
backend — `InputSystemUIInputModule` for the Input System package,
`StandaloneInputModule` for legacy input. Picking wrong silently kills all UI input, so
the package logs one warning per session and leaves it to you. Add one to your scene.

### `Awake` runs at window load, not at show

Elements are instantiated active and immediately deactivated, so Unity's lifecycle
messages fire once, at load. Cache component references in `Awake`; do per-show setup in
`OnShow`, and undo it in `OnHide` (guaranteed to run before window teardown destroys
anything).

### Element draw order is authored order

There are no per-kind containers: list prefabs in `WindowData` in the order they should
draw, screens first. Popups manage themselves (newest on top); everything else keeps its
authored sibling order.

### Namespace shadowing: never write `UI.`-qualified names

Inside `namespace UnityEssentials.UI`, a bare `UI.Image` binds to *our* namespace, not
`UnityEngine.UI`. Put `using UnityEngine.UI;` at the top of the file and use the bare
type names (`Image`, `Button`). No type in this package collides with a `UnityEngine.UI`
type name, so the usings coexist cleanly.

### Cached elements die with their window

`GetUI<T>()` references are valid for the life of the window — a switch destroys them and
builds new instances. Drop caches in a `WindowSwitched` handler.

### Testing destroyed objects

A destroyed Unity object is `== null` under Unity's overloaded operator but is **not** a
null reference — `Assert.IsNull` reports it as alive. Write
`Assert.IsTrue(widget == null)` in your own teardown tests, as this package's do.

---

## API reference

```csharp
public interface IUIData { }
public enum UIElementState : byte { Hidden = 0, Showing = 1, Shown = 2, Hiding = 3 }

public class UIException : Exception { }
public sealed class WindowConfigurationException : UIException { }
public sealed class UIElementNotFoundException  : UIException { }
public sealed class NoActiveWindowException     : UIException { }
public sealed class UIBindingException          : UIException { }

[DisallowMultipleComponent]
public abstract class UIBase : MonoBehaviour
{
    public UIElementState State { get; }
    public bool IsVisible { get; }                     // Showing || Shown
    public UIWindow Window { get; }                    // null until bound
    protected WindowService Service { get; }           // reads through Window

    public abstract void Show(IUIData uiData = null);
    public abstract void Hide();

    protected virtual void OnShow(IUIData uiData);
    protected virtual void OnHide();
    protected virtual void OnShowTransition(Action complete);   // v1: complete() immediately
    protected virtual void OnHideTransition(Action complete);
}

public abstract class ScreenBase : UIBase
{
    public sealed override void Show(IUIData uiData = null);
    public sealed override void Hide();
    protected virtual Type[] AutoPanels { get; }                  // default: empty
    protected virtual IUIData GetAutoPanelData(Type panelType);   // default: null
}

public abstract class PopupBase  : UIBase { /* sealed Show/Hide */ }
public abstract class PanelBase  : UIBase { /* sealed Show/Hide */ }

[DisallowMultipleComponent]
public abstract class UIWidget : MonoBehaviour   // separate hierarchy: widgets are not UIBase
{
    public UIElementState State { get; }
    public bool IsVisible { get; }                     // Showing || Shown
    public UIWindow Window { get; }                    // null until spawned by a window

    public abstract void Show(IUIData uiData = null);
    public abstract void Hide();

    protected virtual void OnShow(IUIData uiData);
    protected virtual void OnHide();
    protected virtual void OnShowTransition(Action complete);
    protected virtual void OnHideTransition(Action complete);
}

public abstract class WidgetBase : UIWidget { /* sealed Show/Hide; no binding required */ }

[Serializable]
public sealed class WidgetData
{
    public WidgetData();
    public WidgetData(GameObject prefab);
    public GameObject Prefab { get; }
}

[Serializable]
public sealed class CanvasSettings
{
    public CanvasSettings();   // ScaleWithScreenSize, 1920x1080, match 0.5; always overlay
    // get-only: SortingLayerName, SortingOrder, PixelPerfect, TargetDisplay, UIScaleMode,
    // ScaleFactor, ReferenceResolution, ScreenMatchMode, MatchWidthOrHeight,
    // ReferencePixelsPerUnit, PhysicalUnit, FallbackScreenDPI, DefaultSpriteDPI,
    // IgnoreReversedGraphics, BlockingObjects, BlockingMask
}

[CreateAssetMenu(menuName = "UnityEssentials/UI/Window Data", fileName = "WindowData")]
public sealed class WindowData : ScriptableObject
{
    public static WindowData Create(GameObject[] uiPrefabs, WidgetData[] widgets, CanvasSettings canvas);
    public UIWindow GenerateWindow(Transform parent);     // canvas trio + UIWindow; loads nothing
    public IReadOnlyList<GameObject> UIPrefabs { get; }   // screens/popups/panels only
    public IReadOnlyList<WidgetData> Widgets { get; }
    public CanvasSettings Canvas { get; }
}

[DisallowMultipleComponent]
public sealed class UIWindow : MonoBehaviour     // the untyped store; the service is the typed facade
{
    public WindowData Data { get; }
    public WindowService Service { get; }
    public Canvas Canvas { get; }
    public IReadOnlyList<UIBase> Elements { get; }
    public IReadOnlyList<WidgetBase> SpawnedWidgets { get; }

    public void LoadWindow(WindowService service);   // instantiate elements, register prototypes
    public void UnloadWindow();                      // release loaded references, destroy itself

    public UIBase GetUI<T>() where T : UIBase;   // returns UIBase by design — cast at the service
    public UIBase GetUI(Type uiType);
    public bool TryGetUI(Type uiType, out UIBase ui);
    public WidgetBase GetWidget(Type widgetType, Transform parent, IUIData uiData);
    public void ReturnWidget(WidgetBase widget);
}

public sealed class WindowService
{
    public WindowService();

    public UIWindow ActiveWindow { get; }
    public WindowData ActiveWindowData { get; }
    public bool IsWindowLoaded { get; }
    public ScreenBase ActiveScreen { get; }
    public ScreenBase PreviousScreen { get; }
    public IReadOnlyList<PopupBase> ActivePopups { get; }
    public IReadOnlyList<PanelBase> ActivePanels { get; }

    public event Action<WindowData, WindowData> WindowSwitched;   // (from, to)
    public event Action<UIBase> UIShown;                          // on entering Shown
    public event Action<UIBase> UIHidden;                         // on entering Hiding

    public UIWindow SwitchWindow(WindowData windowData);
    public void CloseWindow();

    public T ShowUI<T>(IUIData uiData = null) where T : UIBase;
    public void HideUI<T>() where T : UIBase;
    public T GetUI<T>() where T : UIBase;
    public bool TryGetUI<T>(out T ui) where T : UIBase;

    public T GetWidget<T>(Transform parent = null, IUIData uiData = null) where T : WidgetBase;
    public void ReturnWidget(WidgetBase widget);

    public bool Back();                          // restores the previous screen with its stored IUIData
    public bool ShowPreviousScreen();
    public void HideAllPopups();
    public void HideAllPanels();
}
```
