# UI System

A small UGUI window system for Unity 6. One entry point for showing and hiding UI, one element base
class to derive from, and a ScriptableObject that declares what a window owns and how its canvas is
built.

- **Assembly:** `UnityEssentials.UI` (`Runtime/`) — references `UnityEngine.UI` only.
- **Namespace:** `UnityEssentials.UI`
- **Tests:** `UnityEssentials.UI.Tests` (EditMode only)

**You author prefabs, list them in a `WindowData`, construct a `UIService`, and call
`ShowUI<PauseMenu>()`.** The service instantiates one instance of every listed prefab when the
window loads and caches it by concrete type. It does **not** track which elements are open, stack
them, or order them — that is yours to decide, which is what keeps it small enough to drop into a
demo project and start writing UI.

**Deliberately not built:** show/hide tween animations (the transition hooks are the seam),
screen exclusivity, popup stacking, auto-opening panels, back navigation, multi-instance widgets.
The first is a subclass away; the rest are a documented expansion path — see
`Docs/UISystem-Trim-Plan.md`. Windows always render as **screen-space overlay** canvases, so there
is no camera dependency of any kind.

---

## Quick start

```csharp
using UnityEssentials.UI;
using UnityEngine;

public sealed class UIBootstrap : MonoBehaviour
{
    [SerializeField] private WindowData mainWindow;   // created via the asset menu

    private UIService _ui;

    private void Awake()
    {
        _ui = new UIService();
        _ui.SwitchWindow(mainWindow);   // builds everything, shows nothing
        _ui.ShowUI<MainMenu>();         // shows one element
    }

    private void OnDestroy() => _ui.Dispose();
}
```

1. Write an element — put it on the root of its own prefab:

   ```csharp
   public sealed class MainMenu : UIBase
   {
       [SerializeField] private Button playButton;

       protected override void OnShow(IUIData uiData) => playButton.onClick.AddListener(Play);
       protected override void OnHide() => playButton.onClick.RemoveListener(Play);
   }
   ```

2. Create the asset: **Assets ▸ Create ▸ UnityEssentials ▸ UI ▸ Window Data**, and drag your
   prefabs into **UI Prefabs**.
3. Put an `EventSystem` in your scene — the package warns if one is missing but never creates one
   (it cannot know which input module your project needs).

A consumer wanting global access can self-register:
`ServiceLocator.Register(new UIService()).AsSelf();` — the package itself has **no** dependency on
`UnityEssentials.Services`.

---

## Elements

Derive from `UIBase`. There is one element kind, so there is nothing to choose between.

```csharp
public sealed class SettingsPanel : UIBase
{
    protected override void OnShow(IUIData uiData)
    {
        if (uiData is SettingsData data) { ApplyTo(data); }
    }
}
```

| Member | Purpose |
|---|---|
| `Show(IUIData uiData = null)` | Activates, runs `OnShow`, then the show transition |
| `Hide()` | Runs `OnHide`, then the hide transition, then deactivates |
| `State` / `IsVisible` | Where it is in the cycle; `IsVisible` is `Showing \|\| Shown` |
| `Window` / `Service` | The owning window, and the service driving it |
| `OnShow(IUIData)` / `OnHide()` | Your per-show setup and teardown. Must tolerate a null payload |
| `OnShowTransition` / `OnHideTransition` | The tween seam, below |

`IUIData` is an empty marker interface — the package never reads it, you pattern-match it in
`OnShow`, and `null` is always legal.

**An element must be bound to a window before it can be shown.** Binding happens automatically when
a `UIWindow` instantiates it from a `WindowData`. Calling `Show()` on a `UIBase` you dragged into a
scene by hand throws `UIBindingException` — use a plain `MonoBehaviour` for anything a window does
not own. `Hide()` is deliberately silent on an unbound or already-hidden element, so teardown never
throws.

### Element states

`Hidden → Showing → Shown → Hiding → Hidden`, exposed as `State` and `IsVisible`. The state flips at
the *start* of a transition; transitions complete synchronously unless you override them, so from
outside you normally only observe `Shown` and `Hidden`. "Hidden" physically means `SetActive(false)`
— activation happens at the start of showing and deactivation at the end of hiding, so a tween
always runs on a live object.

### The tween seam

```csharp
protected override void OnShowTransition(Action complete)
{
    transform.localScale = Vector3.zero;
    transform.DOScale(1f, 0.25f).OnComplete(() => complete());
}
```

The base implementations invoke `complete()` immediately — that is all that makes the default
synchronous. An internal transition token makes a double-fired or superseded callback inert, and a
`Show` issued during a pending hide cancels the stale hide, so the element is never left deactivated
underneath a fresh show.

---

## Windows

A `WindowData` asset lists the prefabs one window owns and carries its `CanvasSettings`.
`UIService.SwitchWindow(data)` validates the asset, tears down whatever was loaded, then builds a
GameObject with the `Canvas`/`CanvasScaler`/`GraphicRaycaster` trio and instantiates one
deactivated, bound instance of every listed prefab.

```
UnityEssentials.WindowParent         DontDestroyOnLoad while playing
└── Window_MainWindow                Canvas + CanvasScaler + GraphicRaycaster + UIWindow
    ├── MainMenu                     one instance per listed prefab, parented directly
    ├── SettingsPanel
    └── ConfirmDialog
```

**One prefab per concrete type.** Elements are resolved by their concrete type, so a window cannot
hold two of the same — the validator rejects it naming both indices.

`SwitchWindow` validates *before* it unloads or builds anything, so a malformed asset leaves the
live window untouched and raises no event.

---

## Showing, hiding, resolving

```csharp
_ui.ShowUI<MainMenu>();                       // resolve + show
_ui.ShowUI<ShopScreen>(new ShopData(gold));   // with a payload
_ui.HideUI<MainMenu>();

var menu = _ui.GetUI<MainMenu>();             // throws on a miss
if (_ui.TryGetUI<MainMenu>(out var m)) { }    // false on a miss
```

Nothing is implicit. Showing a second element does not hide the first; if you want one-at-a-time
behaviour, hide the outgoing element yourself. Showing an element that is already shown re-runs
`OnShow` and the show transition.

Elements can also be driven directly — `menu.Show()` behaves identically to `_ui.ShowUI<MainMenu>()`,
because the service does nothing beyond resolving the type.

---

## Canvas configuration

Each `WindowData` carries a `CanvasSettings` describing the
`Canvas`/`CanvasScaler`/`GraphicRaycaster` trio. The render mode is always screen-space overlay.
Defaults: scale with screen size, 1920×1080 reference resolution, 0.5 width/height match — sensible
on every aspect ratio without touching anything.

Inspector numbers are clamped as you type (`OnValidate` never throws); building non-default settings
in code goes through the serializer:
`JsonUtility.FromJsonOverwrite("{\"sortingOrder\":7}", new CanvasSettings())` — field names are the
property names with a lower-case initial.

---

## Events

```csharp
_ui.WindowSwitched += (from, to) => _cachedMenu = null;   // drop GetUI caches here
_ui.UIShown  += element => Analytics.ScreenView(element.GetType().Name);
_ui.UIHidden += element => { };
```

| Event | Fires |
|---|---|
| `WindowSwitched(from, to)` | after a switch completes; `from` null on first load, `to` null on close. Exactly once per switch. |
| `UIShown(element)` | on entering `Shown` — when the show *transition completes*. |
| `UIHidden(element)` | on entering `Hiding` — on *intent*, while the element is still visible. |

The two are deliberately asymmetric in timing: state flips at the start of a transition, so
`UIHidden` tells you an element is on its way out rather than already gone. Handler exceptions
propagate; the package never swallows them.

Note that showing an already-shown element runs the full show again, so `UIShown` fires a second
time. If you drive analytics or audio off it, treat it as "this element was shown", not "this
element became visible", or guard on `IsVisible` before calling `ShowUI<T>` to refresh.

---

## Errors

All package exceptions derive from `UIException` and signal programming or authoring errors — fix
the wiring, do not catch and continue.

| Situation | Result |
|---|---|
| `SwitchWindow(null)` | `ArgumentNullException` |
| `SwitchWindow(activeAsset)` | no-op, returns the existing window |
| Null list entry / prefab with no `UIBase` on its root / two prefabs of the same concrete type | `WindowConfigurationException` naming asset, list and index (both indices for a duplicate) |
| `GetUI<T>` / `ShowUI<T>` / `HideUI<T>` miss | `UIElementNotFoundException` listing the types the window *does* carry |
| Any resolve or show with no active window | `NoActiveWindowException` |
| `Show()` on an unbound element; binding one element to a second window | `UIBindingException` |

Deliberately **not** errors: `Hide()` on a hidden or unbound element (no-op), `TryGetUI` miss
(`false` + null, including before the first `SwitchWindow`), `CloseWindow`/`Dispose` with nothing
loaded (no-op), disposing twice.

`Dispose()` is `CloseWindow()` and is idempotent. It does not mark the service dead — a disposed
service is reusable via a later `SwitchWindow`, which keeps it safe in test teardown and `using`
blocks that also close explicitly.

---

## Gotchas

### The package never creates an EventSystem

An `EventSystem` needs an input module, and which one is correct depends on your input backend —
`InputSystemUIInputModule` for the Input System package, `StandaloneInputModule` for legacy input.
Picking wrong silently kills all UI input, so the package logs one warning per session and leaves it
to you. Add one to your scene.

### `Awake` runs at window load, not at show

Elements are instantiated active and immediately deactivated, so Unity's lifecycle messages fire
once, at load. Cache component references in `Awake`; do per-show setup in `OnShow` and undo it in
`OnHide`, which is guaranteed to run before window teardown destroys anything.

### `UIBase` is not a general-purpose animated-component base

It only works bound to a window. If you want the show/hide + transition shape for something a window
does not own, copy the pattern onto a plain `MonoBehaviour` rather than deriving from `UIBase`.

### Draw order is authored order

There are no per-kind containers — every element is parented directly under the window, and UGUI
draws depth-first in hierarchy order. List prefabs in `WindowData` in the order they should draw.
Nothing re-sorts them at runtime, so an element that must sit on top belongs last in the list, or
call `transform.SetAsLastSibling()` yourself in `OnShow`.

### Hide transitions are not awaited on teardown

`SwitchWindow`/`CloseWindow` hide every visible element — so `OnHide` always runs and your
subscriptions are released — then destroy the window. An element whose `OnHideTransition` has not
completed synchronously is destroyed mid-transition. Only relevant once you add tweens.

### Namespace shadowing: never write `UI.`-qualified names

Inside `namespace UnityEssentials.UI`, a bare `UI.Image` binds to *our* namespace, not
`UnityEngine.UI`. Put `using UnityEngine.UI;` at the top of the file and use the bare type names
(`Image`, `Button`). No type in this package collides with a `UnityEngine.UI` type name, so the
usings coexist cleanly.

### Cached elements die with their window

`GetUI<T>()` references are valid for the life of the window — a switch destroys them and builds new
instances. Drop caches in a `WindowSwitched` handler.

### Testing destroyed objects

A destroyed Unity object is `== null` under Unity's overloaded operator but is **not** a null
reference — `Assert.IsNull` reports it as alive. Write `Assert.IsTrue(element == null)` in your own
teardown tests, as this package's do.

---

## API reference

```csharp
public interface IUIData { }

public enum UIElementState : byte { Hidden = 0, Showing = 1, Shown = 2, Hiding = 3 }

public class        UIException                  : Exception
public sealed class WindowConfigurationException : UIException
public sealed class UIElementNotFoundException   : UIException
public sealed class NoActiveWindowException      : UIException
public sealed class UIBindingException           : UIException

[DisallowMultipleComponent]
public abstract class UIBase : MonoBehaviour
{
    public    UIElementState State     { get; }
    public    bool           IsVisible { get; }          // Showing || Shown
    public    UIWindow       Window    { get; }
    protected UIService      Service   { get; }

    public void Show(IUIData uiData = null);             // UIBindingException when unbound
    public void Hide();                                  // no-op when Hidden/Hiding

    protected virtual void OnShow(IUIData uiData);
    protected virtual void OnHide();
    protected virtual void OnShowTransition(Action complete);   // base: complete()
    protected virtual void OnHideTransition(Action complete);   // base: complete()
}

[Serializable]
public sealed class CanvasSettings
{
    public string SortingLayerName { get; }   public int   SortingOrder  { get; }
    public bool   PixelPerfect     { get; }   public int   TargetDisplay { get; }

    public CanvasScaler.ScaleMode       UIScaleMode            { get; }
    public float                        ScaleFactor            { get; }
    public Vector2                      ReferenceResolution    { get; }
    public CanvasScaler.ScreenMatchMode ScreenMatchMode        { get; }
    public float                        MatchWidthOrHeight     { get; }
    public float                        ReferencePixelsPerUnit { get; }
    public CanvasScaler.Unit            PhysicalUnit           { get; }
    public float                        FallbackScreenDPI      { get; }
    public float                        DefaultSpriteDPI       { get; }

    public bool                             IgnoreReversedGraphics { get; }
    public GraphicRaycaster.BlockingObjects BlockingObjects        { get; }
    public LayerMask                        BlockingMask           { get; }
}

[CreateAssetMenu(menuName = "UnityEssentials/UI/Window Data", fileName = "WindowData")]
public sealed class WindowData : ScriptableObject
{
    public IReadOnlyList<GameObject> UIPrefabs { get; }
    public CanvasSettings            Canvas    { get; }

    public static WindowData Create(GameObject[] uiPrefabs, CanvasSettings canvas);
    public UIWindow GenerateWindow(Transform parent);
}

[DisallowMultipleComponent]
public sealed class UIWindow : MonoBehaviour
{
    public WindowData            Data     { get; }
    public UIService             Service  { get; }
    public Canvas                Canvas   { get; }
    public IReadOnlyList<UIBase> Elements { get; }

    public void   LoadWindow(UIService service);
    public void   UnloadWindow();                        // hides visible elements, then destroys
    public T      GetUI<T>() where T : UIBase;
    public UIBase GetUI(Type uiType);
    public bool   TryGetUI(Type uiType, out UIBase ui);
}

public sealed class UIService : IDisposable
{
    public UIWindow   ActiveWindow     { get; }
    public WindowData ActiveWindowData { get; }
    public bool       IsWindowLoaded   { get; }

    public event Action<WindowData, WindowData> WindowSwitched;
    public event Action<UIBase>                 UIShown;
    public event Action<UIBase>                 UIHidden;

    public UIWindow SwitchWindow(WindowData windowData);
    public void     CloseWindow();
    public T        ShowUI<T>(IUIData uiData = null) where T : UIBase;
    public void     HideUI<T>()                      where T : UIBase;
    public T        GetUI<T>()                       where T : UIBase;
    public bool     TryGetUI<T>(out T ui)            where T : UIBase;
    public void     Dispose();                       // == CloseWindow(), idempotent
}
```

---

## Expanding this later

`ScreenBase`/`PopupBase`/`PanelBase`, popup stacking, a navigation back-stack and multi-instance
widgets were all removed to get here, and the remaining API is shaped so they come back as pure
additions rather than breaking changes. `Docs/UISystem-Trim-Plan.md` records the four decisions that
keep that door open — most importantly that a future kind-specific `Show` must be an `override` of
`UIBase.Show`, never a `new` method, or `ShowUI<T>` will silently bypass it.
