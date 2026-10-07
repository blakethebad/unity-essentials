# StateManager

A finite state machine keyed by your own enum. You register one state per enum value, declare
which transitions are legal, then initialize and drive it. The transition table
is **deny-all by default**: anything you did not declare throws instead of quietly succeeding —
which is the only reason to have a state machine rather than an enum field.

- **Assembly:** `UnityEssentials.States` (`Runtime/`) — no dependencies beyond `UnityEngine`,
  which only the optional MonoBehaviour host uses.
- **Namespace:** `UnityEssentials.States`
- **Editor:** `UnityEssentials.States.Editor` (`Editor/`) — the [debugging window](#editor-debugging-window); Editor-only, never in builds.
- **Tests:** `UnityEssentials.States.Tests` (EditMode only)

`BaseStateManager<TState>` and `BaseState<TManager, TState>` are plain C# — no Unity types, no
scene, no `GameObject` — so they run in EditMode tests, servers, and pure logic layers unchanged.
`StateManagerBehaviour<TState>` is the optional MonoBehaviour flavour: not an adapter around a
manager you wrote, but a state manager in its own right, with the same hooks wired to
`Awake`/`Start`/`Update`. Both implement `IStateManager<TState>`, the shared FSM surface states
and callers talk to (see [The `IStateManager<TState>` handle](#the-istatemanagertstate-handle)).
`BaseStateManager<TState>` itself is **abstract**: a machine carries its own configuration,
declaring its states in `OnInitialize()` and its legal pairs in `InsertTransitions()` (see
[Deriving your own manager](#deriving-your-own-manager)).

Warm `ChangeState`, `Tick`, `RestartState` and `CanChangeState` allocate nothing: enum-keyed
dictionary lookups with `EqualityComparer<T>.Default`, no boxing, no reflection, no LINQ, no
locks.

---

## Quick start

```csharp
using UnityEngine;
using UnityEssentials.States;

public enum GameState { MainMenu, Level, Pause }

public sealed class MainMenuState : BaseState<GameStateManager, GameState>
{
    public override GameState StateType => GameState.MainMenu;

    protected override void OnEnterState(GameState previousState)
    {
        // PreviousStateType is null only on the machine's very first entry.
        Debug.Log(Manager.PreviousStateType == null
            ? "Booting into the menu"
            : $"Back at the menu from {previousState}");
    }
}

public sealed class LevelState : BaseState<GameStateManager, GameState>
{
    private float _elapsed;

    public override GameState StateType => GameState.Level;

    protected override void OnEnterState(GameState previousState) => _elapsed = 0f;

    protected override void OnUpdate()
    {
        _elapsed += Time.deltaTime;
        if (_elapsed >= 30f)
        {
            Manager.ChangeState(GameState.MainMenu);   // legal from OnUpdate
        }
    }
}
```

```csharp
public sealed class GameStateManager : BaseStateManager<GameState>
{
    protected override void OnInitialize()
    {
        AddState(new MainMenuState())         // first registered = default initial state
            .AddState(new LevelState());
    }

    protected override void InsertTransitions(in Transitions<GameState> transitions)
    {
        transitions.Allow(GameState.MainMenu, GameState.Level)
            .Allow(GameState.Level, GameState.MainMenu);
    }
}
```

```csharp
var machine = new GameStateManager();

machine.Initialize();                     // runs both hooks, then enters MainMenu

machine.ChangeState(GameState.Level);     // drive it from outside...
machine.Tick();                           // ...or once per frame, from inside
```

Later snippets also use `PauseState`, a third `BaseState<GameStateManager, GameState>` subclass
written exactly like the two above with `StateType => GameState.Pause`.

---

## Defining states

Subclass `BaseState<TManager, TState>` — the manager type first, the enum second — answer for one
enum value, and override only the `On*` hooks you need; each has an empty default.

```csharp
public sealed class PauseState : BaseState<GameStateManager, GameState>
{
    public override GameState StateType => GameState.Pause;              // required, must be constant

    protected override void OnEnterState(GameState previousState) { }    // after the swap
    protected override void OnExitState(GameState nextState) { }         // before the swap
    protected override void OnUpdate() { }                               // once per Tick()
    public override void RestartState() { }                              // default: ExitState + EnterState
}
```

`TManager` is what the state's `Manager` property is typed as, and it is constrained to
`class, IStateManager<TState>`. Three choices, in ascending order of reusability:

| `TManager` | Use when |
|---|---|
| Your concrete manager (`GameStateManager`) | The state is written for one machine and wants its members — a shared blackboard, a save handle, a reference to the player. |
| Your `StateManagerBehaviour<TState>` subclass (`GameFlow`) | The state is scene-driven and wants the host component: `Manager.transform`, its serialized fields, `StartCoroutine`. |
| `IStateManager<TState>` | The state is reusable and only needs the FSM surface — `ChangeState`, `PreviousStateType`, `CanChangeState`. Then it drops into *any* machine keyed by that enum. |

The pairing is checked at registration: `AddState` throws `StateConfigurationException` if the
machine it is added to is not a `TManager`, naming both types. `BaseState<TState>` — the one-argument
base the machine itself talks to — is not derivable from your code; it exists so machines can hold
states closed over different `TManager`s side by side.

The machine does not call those hooks directly. It calls `EnterState(previous)`,
`ExitState(next)` and `UpdateState()` — **public and deliberately non-virtual** entry points
that forward to the matching `On*` method. Keeping the machine-facing edge separate from the
overridable hook is what lets the package add default behavior there later (logging, profiler
markers, assertions) without every existing state having to opt in, so do not call or hide the
wrappers; override the `On*` method instead.

| Member | Notes |
|---|---|
| `StateType` | The key the machine stores this state under. Must be constant for the object's lifetime and unique within a machine. |
| `Manager` | `protected`, typed as `TManager`; the owning machine, set by `AddState`. Null until then. This is how a state drives transitions. |
| `OnEnterState(previous)` | `protected virtual`, empty default. Runs *after* `CurrentStateType` has flipped to this state. |
| `OnExitState(next)` | `protected virtual`, empty default. Runs *before* the swap, while `CurrentStateType` still reports this state. |
| `OnUpdate()` | `protected virtual`, empty default. Pumped by `Tick()`. The only hook from which `ChangeState` is legal. |
| `EnterState` / `ExitState` / `UpdateState` | `public`, **non-virtual**. The machine's entry points; each forwards to the matching `On*` hook. |
| `RestartState()` | `public virtual`, not abstract. Default body is `ExitState(StateType); EnterState(StateType);` — the public wrappers, so future default behavior runs on a restart too. |

States are single-owner: `AddState` attaches the instance, and adding the same instance to a
*second* machine throws `StateConfigurationException`. Create one instance per machine.

---

## Building a machine

A machine configures itself. Derive from `BaseStateManager<TState>`, register the states in
`OnInitialize()`, declare the legal pairs in `InsertTransitions()`. Both are `protected abstract`,
so the compiler will not let you forget either half. Configuration is fluent: every manager call
returns the machine as an `IStateManager<TState>`, every builder call returns the builder.

```csharp
public sealed class GameStateManager : BaseStateManager<GameState>
{
    protected override void OnInitialize()
    {
        AddState(new MainMenuState())
            .AddState(new LevelState())
            .AddState(new PauseState())
            .SetInitialState(GameState.MainMenu);
    }

    protected override void InsertTransitions(in Transitions<GameState> transitions)
    {
        transitions.Allow(GameState.MainMenu, GameState.Level)
            .Allow(GameState.Level, GameState.Pause, GameState.MainMenu)
            .Allow(GameState.Pause, GameState.Level, GameState.MainMenu);
    }
}
```

```csharp
var machine = new GameStateManager();
machine.Initialize();
```

`Initialize()` seals the machine: after that every configuration method throws
`StateConfigurationException`, and before it `ChangeState`, `RestartState` and `Tick` do. The
observation properties are the exception — they never throw.

- `AddState(state)` — null throws `ArgumentNullException`; a second state for the same
  `StateType` throws.
- `transitions.Allow(...)` / `transitions.AllowAny()` inside `InsertTransitions()` — the only way
  to declare a pair. **Both endpoints must already be registered**, which is exactly why
  `OnInitialize()` runs first. An empty `to` array throws (it reads as though it granted
  something); a null one throws `ArgumentNullException`.
- `SetInitialState(state)` — optional; the state must be registered; the last call wins.
  Without it, the **first state passed to `AddState`** is the initial state.
- `Initialize()` — a second call throws, and so does reaching the end of the hooks with zero
  states registered.

### What `Initialize()` does, in order

1. Rejects a second call, and rejects being called from *inside* `OnInitialize()` or
   `InsertTransitions()` — both `StateConfigurationException`. The hooks configure the machine;
   they do not start it.
2. Runs `OnInitialize()`, then `InsertTransitions(...)` with a fresh
   [`Transitions<TState>`](#the-transitions-builder). Exactly once each, in that order, before
   anything is validated.
3. Requires at least one registered state, asks `GetInitialState()` where to start, and requires
   that answer to be registered.
4. Flips `IsInitialized`, sets the current state, registers with the
   [debug window](#editor-debugging-window), then runs the initial `EnterState` and fires
   `StateEntered`. A `ChangeState` the initial entry deferred runs right after, as an ordinary
   transition.

Anything the two configuration hooks throw propagates **unwrapped** — your own exception, not a
`StateManagerException` — and the machine stays uninitialized, so a bad configuration fails as
loudly as it would in a constructor. (The state hooks in step 4 are the opposite; see
[Hook failures are wrapped, not swallowed](#hook-failures-are-wrapped-not-swallowed).) Treat that
failure as fatal to the instance: any states the hooks did register are still there, so a second
`Initialize()` re-runs the hooks over that half-configured machine and the repeated `AddState`
throws.

Configuring from **outside** still works for *states*, and combines with the hooks: `AddState` and
`SetInitialState` are public, so anything you call between construction and `Initialize()` is
applied first and `OnInitialize()` adds to it — same methods, same rules, same exceptions.
Transitions are not part of that: they can only be declared through the builder inside
`InsertTransitions()`, so there is nothing to combine.

`Initialize()` is deliberately **not a transition**. It bypasses the table (a machine with no
declared pairs still starts), calls `EnterState(initial)` with the initial state as its own
"previous", and fires `StateEntered(initial, initial)`. No `ExitState`, no `StateExited` —
nothing was left. `IsInitialized` is already true while that entry runs, and stays true even if
`OnEnterState` throws there.

### Observing an uninitialized machine

The observation properties never throw; before `Initialize()` they report their "nothing yet"
values:

| Property | Before `Initialize()` |
|---|---|
| `IsInitialized` | `false` |
| `CurrentState` | `null` |
| `CurrentStateType` | `default(TState)` — the enum's zero value, which may well be a real state the machine is not in |
| `PreviousStateType` / `PreviousState` | `null`, as they are until the first *completed* `ChangeState` |

So `CurrentStateType` alone cannot tell "not started" from "started in the zero state". Check
`IsInitialized` (or `CurrentState` for null) when the difference matters — for instance from
another component's `Awake`, which runs before the host's `Start` has initialized anything.

### The `IStateManager<TState>` handle

`IStateManager<TState>` is the whole shared surface: everything a machine can be *asked* to do,
with none of the hooks that decide what it *is*. `BaseStateManager<TState>` implements it and
`StateManagerBehaviour<TState>` implements it by forwarding, so the same handle covers a pure
machine and a scene-hosted one:

```csharp
IStateManager<GameState> machine = new GameStateManager();   // ...or a GameFlow component
machine.Initialize();

machine.ChangeState(GameState.Level);
machine.StateEntered += (from, to) => Debug.Log($"entered {to}");
```

It carries the observation properties (`IsInitialized`, `CurrentState`, `CurrentStateType`,
`PreviousState`, `PreviousStateType`), both events, the runtime operations (`Initialize`,
`ChangeState`, `RestartState`, `Tick`, `CanChangeState`) and the two public configuration calls
(`AddState`, `SetInitialState`, which both return it so chains keep flowing). It does **not**
carry the configuration hooks — `OnInitialize`, `InsertTransitions` and `GetInitialState` are
`protected` on whichever base you derived from, and are not something a caller invokes.

Type a field or parameter as `IStateManager<GameState>` when the code drives a machine without
caring how it is hosted — an HUD that reacts to `StateEntered`, a test harness, a save system.
Type a state's `TManager` as it for the same reason.

### Deriving your own manager

Deriving is not optional — `BaseStateManager<TState>` is abstract. There are three extension
points, two of which you must implement (and `StateManagerBehaviour<TState>` declares exactly the
same three):

| Member | Required | Does |
|---|---|---|
| `protected abstract void OnInitialize()` | yes | Register the states with `AddState`, optionally pick a start with `SetInitialState`. Runs first. |
| `protected abstract void InsertTransitions(in Transitions<TState> transitions)` | yes | Declare the legal pairs through the [builder](#the-transitions-builder). Runs second, when every state is guaranteed registered. |
| `protected virtual TState GetInitialState()` | no | Decide where the machine starts — from a save file, a scene parameter, a debug flag. Runs after both hooks. |

The hooks run at `Initialize()` time, not construction time, so anything a constructor stored is
available to them:

```csharp
public sealed class GameStateManager : BaseStateManager<GameState>
{
    private readonly bool _resumeSave;

    public GameStateManager(bool resumeSave) => _resumeSave = resumeSave;

    protected override void OnInitialize()
    {
        AddState(new MainMenuState())
            .AddState(new LevelState())
            .AddState(new PauseState());
    }

    protected override void InsertTransitions(in Transitions<GameState> transitions)
    {
        transitions.Allow(GameState.MainMenu, GameState.Level)
            .Allow(GameState.Level, GameState.Pause, GameState.MainMenu)
            .Allow(GameState.Pause, GameState.Level, GameState.MainMenu);
    }

    protected override GameState GetInitialState()
    {
        return _resumeSave ? GameState.Level : GameState.MainMenu;
    }
}
```

```csharp
var machine = new GameStateManager(resumeSave: true);
machine.Initialize();                     // enters Level
```

- The default `GetInitialState()` returns the value given to `SetInitialState`, or the first
  state registered with `AddState` if there was none.
- An override **wins outright** over `SetInitialState` unless it calls `base.GetInitialState()`.
- Whatever it returns is validated: an unregistered value makes `Initialize()` throw
  `StateConfigurationException` naming `GetInitialState()` and the value.

Those three are the whole extension surface — the machine's fields stay private, and everything
else you need is already public. Both hooks are configuration-only: the machine is not
initialized while they run, so `ChangeState`, `RestartState` and `Tick` throw from them, and
calling `Initialize()` from inside one throws `StateConfigurationException` rather than
recursing.

If the machine belongs to a GameObject, do not wrap this class — derive
[`StateManagerBehaviour<TState>`](#the-monobehaviour-host) instead and override the very same three
hooks there. What travels between the two is the *states*: write them against
`IStateManager<TState>` and the same state classes configure a pure manager in an EditMode test
and a behaviour in a scene (one instance each — see [One state instance, one
machine](#one-state-instance-one-machine)).

---

## Transitions

**Nothing is legal until you declare it.** A fresh table denies every pair, including every
state's transition to itself, and `ChangeState` on an undeclared pair throws
`InvalidTransitionException`.

```csharp
transitions.Allow(GameState.Level, GameState.Pause);   // inside InsertTransitions

machine.ChangeState(GameState.Pause);   // at runtime, from Level: ok
machine.ChangeState(GameState.Level);   // throws — (Pause, Level) was never declared
```

**Declarations are directional.** `Allow(a, b)` says nothing about `(b, a)`; pause menus need
both pairs. `Allow(from, params to)` is fan-out sugar over one source, not a bidirectional
shorthand.

**Self-transitions need an explicit `(s, s)` entry.**

```csharp
transitions.Allow(GameState.Level, GameState.Level);   // inside InsertTransitions
machine.ChangeState(GameState.Level);   // full ExitState -> EnterState, both events, Previous == Level
```

That is a real transition and behaves like one. If you want "re-run this state" without the
table entry and without events, use [`RestartState`](#restartstate) instead.

**`AllowAny()`** opens every pair, self-transitions included, for prototyping. It is one-way —
nothing in the API takes permissions back — and it trades away the machine's main safety
property, so treat it as a temporary line of code.

### The transitions builder

`Transitions<TState>` is a `readonly struct` wrapping the machine being configured.
`InsertTransitions` receives one by `in` reference; each method returns the same builder, so
declarations chain into a single block.

| Member | Does |
|---|---|
| `Allow(TState from, TState to)` | One directed pair. |
| `Allow(TState from, params TState[] to)` | Fan-out from one source. |
| `AllowAny()` | Opens every pair, self-transitions included. |

Every rule is enforced on the call: an unregistered endpoint or an empty `to` array throws
`StateConfigurationException`, a null `to` array throws `ArgumentNullException`.

The builder is the *whole* transition surface — the manager exposes no public method for
declaring a pair, so there is no way to build the table from `OnInitialize`, from outside before
`Initialize()`, or from anywhere else. That is deliberate: the table reads as one block, in one
place, at the one moment when every state is guaranteed registered. Keeping a copy of the struct
past `InsertTransitions` buys nothing either — it points at the same machine, which by then is
sealed, so a late call throws `StateConfigurationException` telling you to declare transitions
inside `InsertTransitions()`.

**`CanChangeState(from, to)`** is a pure query on the declared pairs. It ignores the current
state, ignores whether the machine is initialized, and never throws, so it is the right way to
decide whether to offer a button:

```csharp
bool canResume = machine.CanChangeState(machine.CurrentStateType, GameState.Level);
```

Guard with `CanChangeState`; do not catch `InvalidTransitionException`. Note that after
`AllowAny()` it answers true for everything, including enum values no state was
registered for — `ChangeState` still rejects those with `StateConfigurationException`.

---

## Events and ordering

Two events, both `Action<TState, TState>` carrying `(from, to)`:

```csharp
machine.StateExited  += (from, to) => Debug.Log($"leaving {from} for {to}");
machine.StateEntered += (from, to) => Debug.Log($"entered {to} from {from}");
```

A successful `ChangeState(next)` runs exactly this sequence:

1. **Validate** — initialized, target registered, pair allowed. All of it up front, so a
   rejected call leaves the machine untouched and fires nothing. (A call made *during* a
   transition never reaches this sequence directly: from the entry half it is deferred, from the
   exit half it throws — see below.)
2. `current.ExitState(next)` — which forwards to your `OnExitState`.
3. `StateExited(from, to)` — `CurrentStateType` still reports the state being left.
4. **Swap** — `CurrentState`/`CurrentStateType` become the target, `PreviousState`/
   `PreviousStateType` become the state just left.
5. `next.EnterState(from)` — which forwards to your `OnEnterState`.
6. `StateEntered(from, to)` — the transition is complete; both properties report final values.

The swap sits between the two halves so that exit observers see the state being left and entry
observers see a finished transition.

Steps 2–6 run inside the re-entrancy guard, with one deliberate opening: a `ChangeState` made
from the **entry half** — `OnEnterState` (step 5) or a `StateEntered` handler (step 6) — is
**deferred** and performed as soon as the sequence above completes, as its own full transition
with its own events. By then the swap has happened, so the deferred pair is validated from the
state that requested it, and one such request may be made per transition (each deferred
transition may in turn defer the next). Everything else still throws `StateManagerException`:
`ChangeState` from the exit half (`OnExitState`, `StateExited`) or from a restart, and
`RestartState`/`Tick` from any hook or handler. Anything a hook or handler throws in those
steps also reaches the caller as a `StateManagerException`, wrapping the original (see
[Errors](#errors)).

`Initialize()` fires only `StateEntered(initial, initial)`. A change deferred from the initial
entry runs right after it, still inside `Initialize()` — the bootstrap pattern: an initial
state whose entry does startup work and immediately flows the machine onward. `RestartState()`
fires nothing.

---

## RestartState

`machine.RestartState()` re-runs the current state in place. It is not a transition:

- the table is bypassed — no `Allow(s, s)` needed;
- no events fire;
- `CurrentStateType` and `PreviousStateType` are unchanged.

It delegates to `BaseState<TState>.RestartState()`, whose default is `ExitState(StateType)`
followed by `EnterState(StateType)` — the public wrappers, so any package-level default
behavior runs on a restart too. `RestartState` is the one lifecycle member you override
directly; do so when a state can reset more cheaply than a full teardown and rebuild, and note
that the default path hands both hooks the state's *own* value:

```csharp
public sealed class LevelState : BaseState<GameStateManager, GameState>
{
    private float _elapsed;
    private int _score;

    public override GameState StateType => GameState.Level;

    public override void RestartState()
    {
        _elapsed = 0f;          // no ExitState/EnterState at all
        _score = 0;
    }
}
```

The guard is held for the duration, so restart logic cannot transition the machine out from
under itself.

---

## Per-frame updates

`Tick()` dispatches one call to the current state's `UpdateState()`, which forwards to your
`OnUpdate()`. Call it from `Update` (the [MonoBehaviour host](#the-monobehaviour-host) does it
for you):

```csharp
private void Update() => _machine.Tick();
```

The guard is deliberately *not* held during dispatch, which makes `OnUpdate` the sanctioned
place to drive the machine:

```csharp
protected override void OnUpdate()
{
    if (_health <= 0)
    {
        Manager.ChangeState(GameState.MainMenu);   // legal
    }
}
```

The current state is captured before dispatch, so a state entered mid-tick does **not** also
update on that tick — its first `OnUpdate` is the next one. That keeps one tick to one
state's worth of work and makes ping-ponging states impossible to turn into an infinite loop
inside a single frame.

`Tick()` before `Initialize()` throws `StateConfigurationException`; `Tick()` from inside a
lifecycle hook throws `StateManagerException`. Unlike the transition operations, a tick does
**not** wrap failures: whatever `OnUpdate` throws propagates out of `Tick()` unchanged, because
per-frame logic is ordinary code rather than a machine-driven lifecycle step.

---

## The MonoBehaviour host

`StateManagerBehaviour<TState>` **is** a state manager — a MonoBehaviour that implements
`IStateManager<TState>` and declares the same three configuration hooks as
`BaseStateManager<TState>`. There is no second class: you do not write a manager and host it, you
derive the behaviour and configure it in place. Its one type argument is the enum, and a generic
MonoBehaviour **cannot be attached to a GameObject**, so the class you derive closes it:

```csharp
public sealed class GameFlow : StateManagerBehaviour<GameState>
{
    [SerializeField] private float _menuTimeout = 30f;

    protected override void OnInitialize()
    {
        AddState(new MainMenuState())
            .AddState(new LevelState())
            .SetInitialState(GameState.MainMenu);
    }

    protected override void InsertTransitions(in Transitions<GameState> transitions)
    {
        transitions.Allow(GameState.MainMenu, GameState.Level)
            .Allow(GameState.Level, GameState.MainMenu);
    }
}
```

States added here see **the behaviour itself** as their manager, not some machine hidden behind
it, so a state closed over `GameFlow` reaches its serialized fields, its `transform` and its
coroutines through `Manager`:

```csharp
public sealed class MainMenuState : BaseState<GameFlow, GameState>
{
    public override GameState StateType => GameState.MainMenu;

    protected override void OnEnterState(GameState previousState)
    {
        Manager.StartCoroutine(FadeIn());
    }
}
```

(States closed over `IStateManager<GameState>` fit here too, and in a pure
`BaseStateManager<GameState>` as well — that is the point of the interface.)

Lifecycle:

| Callback | Does |
|---|---|
| `Awake` | Editor only: records the host `GameObject` for the [debug window](#editor-debugging-window). **Empty in player builds.** Configuration does *not* happen here. |
| `Start` | `Initialize()` — which runs `OnInitialize` and `InsertTransitions` — so every component's `Awake` has run before the machine is built and the first `EnterState` runs. |
| `Update` | `Tick()`, but only once `IsInitialized` — no extra guard needed. |
| `OnDestroy` | Editor only: releases the machine from the [debug window](#editor-debugging-window)'s registry. **Empty in player builds.** |

All four are `protected virtual`. **Override them and you must call `base.Awake()` /
`base.Start()` / `base.Update()` / `base.OnDestroy()`**, or — in that order — the debug window
loses track of which GameObject owns the machine, the machine is never configured and never
initialized, it is never ticked, or its card lingers after the GameObject is gone. Note that
`OnDestroy` does
*not* unsubscribe handlers you attached — see
[the host's `OnDestroy` is debug-only](#the-hosts-ondestroy-is-debug-only--and-you-must-call-baseondestroy).

The engine the behaviour delegates to is built in its constructor, so it exists before any Unity
callback — unconfigured, but present. Subscribing from another component's `Awake` is therefore
safe regardless of script execution order, and early enough to observe the initial entry raised in
`Start`.

```csharp
private void Awake() => _flow.StateEntered += OnStateEntered;   // _flow is a GameFlow reference
```

### Deferred initialization

Skip the `base.Start()` call to initialize later — after a scene load, a save-file read, a
network handshake. Because the configuration hooks run inside `Initialize()`, deferring it defers
the whole build, not just the first `EnterState`. `Update` stays inert until you do:

```csharp
public sealed class GameFlow : StateManagerBehaviour<GameState>
{
    protected override void Start() { }        // no base call: nothing is configured or initialized yet

    public void BeginAfterLoad() => Initialize();   // hooks run, ticking starts, from here

    // ...OnInitialize / InsertTransitions as above
}
```

Everything on `IStateManager<TState>` — `IsInitialized`, `CurrentStateType`, `CurrentState`,
`PreviousStateType`, `PreviousState`, `Initialize`, `ChangeState`, `RestartState`, `Tick`,
`CanChangeState`, `AddState`, `SetInitialState`, `StateEntered`, `StateExited` — is public on the
behaviour and forwards straight to the engine, so ordering, re-entrancy and exception rules are
exactly the machine's. `AddState` and `SetInitialState` return the *behaviour*, so chains inside
`OnInitialize` keep targeting it. The forwarded properties never throw either: between `Awake`
and `Start` — the window in which another component might read them — `CurrentState` is null
and `CurrentStateType` is `default(TState)`.

The engine itself is reachable as `protected BaseStateManager<TState> Machine`, but there is
nothing on it you need: it exists so the debug wiring and the hooks have something to talk to.

> **This is not `UnityEngine.StateMachineBehaviour`.** That type is Mecanim's per-Animator-state
> hook and has nothing to do with this package; the name here is `StateManagerBehaviour`
> precisely to avoid the collision. This machine knows nothing about the Animator.

---

## Editor debugging window

**Essentials → StateManager** opens a live dashboard of every state machine currently alive in
the Editor. There is nothing to attach, nothing to enable, and no API to call: a machine appears
the moment it is initialized.

Each machine gets a card showing:

| Element | What it tells you |
|---|---|
| Display name | The host `GameObject`'s name for a [MonoBehaviour-hosted](#the-monobehaviour-host) machine, otherwise the closed base signature — `BaseStateManager<GameState>`. Click it to ping the GameObject in the Hierarchy. |
| Current state | A colored pill, plus a live **time in state** readout that counts up while you watch. |
| Previous state | The state the machine last came from, or nothing at all until the first completed `ChangeState`. |
| Transition strip | One chip per registered state: filled for the current state, highlighted for every state reachable from it under the [transition table](#transitions), dim for the rest. This is the deny-all table made visible — if a chip is dim, `ChangeState` to it would throw. |
| History | The last **10** transitions, newest first, each as `From → To` with how long ago it happened. |

State colors are derived from the state's *name*, so `Level` is the same color in every machine,
in every card, in every session — worth relying on when two machines are meant to move in step.
Each machine also carries its own accent stripe so cards stay distinguishable at a glance.

### What registers, and when

Registration happens inside `Initialize()` — after the configuration hooks have run, before the
initial `EnterState` — so the very first entry already shows up in the history as
`Initial → Initial`, the same pair [`Initialize()` reports](#building-a-machine) through
`StateEntered`. A machine whose `OnInitialize` or `InsertTransitions` threw never registers.

- **Behaviour machines** (`StateManagerBehaviour<TState>`) are released in `OnDestroy` — so
  destroying the GameObject removes the card. See
  [the host's `OnDestroy` is debug-only](#the-hosts-ondestroy-is-debug-only--and-you-must-call-baseondestroy)
  for the one thing you owe an override.
- **Bare machines** (`new GameStateManager()`, a plain `BaseStateManager<TState>`) have no destruction
  callback to hook, so the window holds them weakly and drops the card once the garbage
  collector reclaims the machine. Unity's collector is conservative, so expect the card to
  outlive your last reference by some indeterminate amount — it is a debug view, not a leak
  detector.
- Leaving Play Mode clears the list.

Nothing about this changes the machine's behavior: the window is a passive observer of the same
`StateEntered` event you can subscribe to, and its handler is wrapped so that it can never abort
a transition of yours.

### Two things it deliberately does not show

**Restarts.** [`RestartState()`](#restartstate) fires no events, so the window cannot see it and
the history stays silent. That is consistent with the rest of the API — a restart is not a
transition — but it does mean a state that resets itself repeatedly looks idle here. If you want
restarts visible, an explicit self-transition (`Allow(s, s)` plus `ChangeState`) is a real
transition and does appear.

**Paused time.** The time-in-state readout is wall clock
(`Time.realtimeSinceStartupAsDouble`), not scaled or frame-driven, so it **keeps counting while
the Editor is paused** and while `Time.timeScale` is zero. It answers "how long since this state
was entered, in real seconds", which is what you want when you are stepping frames and comparing
against a stopwatch — not "how much game time has the state accumulated".

### Cost in a build: none

The entire feature — the window, the registry, the timestamps, the history, the hooks in
`Initialize()`, `Awake` and `OnDestroy` — is Editor-only and compiles out of player builds. Not
disabled at runtime, not behind a flag you can forget to clear: the code is not in the assembly.
The shipped `Initialize()` is byte-for-byte what it was before the window existed, and the
shipped `Awake` and `OnDestroy` have empty bodies.

---

## Errors

| Type | When |
|---|---|
| `StateManagerException` | Base type — catch it to catch all three. Thrown **directly** for illegal re-entrancy: `ChangeState` called from the exit half of a transition (`OnExitState`, `StateExited`) or during a restart; a second `ChangeState` from an entry that already deferred one; a chain of deferred changes that never settles; and `RestartState` or `Tick` called from any hook or event handler. (`ChangeState` from `OnEnterState` or a `StateEntered` handler does not throw — it defers.) Also the **wrapper** for hook failures, below. |
| `InvalidTransitionException` | `ChangeState` for a pair the table never opened — including a self-transition without an explicit `Allow(s, s)`. The message names the pair and the `transitions.Allow(from, to)` call that would legalize it. The only exception that means "the machine is fine, the request isn't". |
| `StateConfigurationException` | The machine is built or used wrongly: duplicate `StateType`; a state already attached to another machine; a state whose `TManager` does not match the machine it was added to; `Allow`/`SetInitialState` naming an unregistered value (`Allow` points you at `AddState()` in `OnInitialize()`); `Allow` with an empty target array; any configuration call after `Initialize()`, including a declaration through a builder copy kept past `InsertTransitions()`; `Initialize()` twice, from inside `OnInitialize()` or `InsertTransitions()`, with no states registered by the time the hooks are done, or with a `GetInitialState()` result that is not registered; `ChangeState` to an unregistered target; and calling `ChangeState`, `RestartState` or `Tick` before `Initialize()` — including from the configuration hooks. |

`ArgumentNullException` covers the two plain null arguments: `AddState(null)` and
`Allow(from, null)`.

### Hook failures are wrapped, not swallowed

Anything thrown by a state hook (`EnterState`, `ExitState`, `RestartState` — that is, your
`On*` overrides) or by a `StateExited`/`StateEntered` handler during `Initialize()`,
`ChangeState()` or `RestartState()` is rethrown as a `StateManagerException` whose message
names the failing stage — `initial EnterState`, `ExitState hook`, `StateExited handler`,
`EnterState hook`, `StateEntered handler` or `RestartState hook` — plus the state or from/to
pair involved, and repeats the original exception's type and message. The original is kept as
`InnerException`:

```csharp
try
{
    machine.ChangeState(GameState.Level);
}
catch (StateManagerException e)
{
    Debug.LogException(e.InnerException ?? e);   // the real failure, if this was a wrapper
}
```

Three deliberate exclusions. `Tick()` never wraps: an exception from `OnUpdate` propagates
unchanged. The **configuration** hooks never wrap either: whatever `OnInitialize` or
`InsertTransitions` throws leaves `Initialize()` as itself, with the machine still
uninitialized. And pre-validation failures — not initialized, already transitioning,
unregistered target, table-denied pair — are thrown directly, with no wrapper and no inner
exception.

All of these signal programming errors, not recoverable conditions. Guard with `CanChangeState`
and `IsInitialized` rather than catching; the observation properties need no guarding at all,
since they report null / `default(TState)` instead of throwing.

---

## Gotchas

### `ChangeState` mid-transition: entry defers, exit throws

"Enter A, then immediately go to B" works directly: a `ChangeState` from `OnEnterState` or a
`StateEntered` handler is deferred and performed once the current transition (or the initial
entry) completes — one request per transition, validated from the state that made it, and each
deferred transition may defer the next. A cycle of states endlessly deferring into each other
is cut off with a `StateManagerException` after 100 chained transitions.

The exit half is still sealed: `ChangeState` from `OnExitState`, a `StateExited` handler or
anywhere inside a restart throws `StateManagerException`, as do `RestartState` and `Tick` from
any hook or handler. Per-frame decisions belong in `OnUpdate`, which runs outside the guard.

### A throwing hook is not rolled back

The guard is released via `try/finally`, so a throwing hook never wedges the machine — but the
transition is not undone:

- a throwing `OnEnterState` (or `StateEntered` handler) leaves the machine **on the target
  state**, from where it remains usable. Unwinding into `OnExitState` would run cleanup for
  setup that never happened, so it does not happen by design.
- a throwing `OnExitState` (or `StateExited` handler) aborts before the swap, leaving the
  machine on the **old** state.

Either way the caller of `ChangeState` sees a `StateManagerException` naming the failing stage
and the from/to states, with the original exception as its `InnerException` — the same wrapping
applies to the initial entry from `Initialize()` and to `RestartState()`. Inspect
`InnerException` for the real failure; `Tick()` is the one operation that does not wrap.

### The initial entry passes the state its own value

`Initialize()` has no previous state, so `OnEnterState(previousState)` receives `StateType`
itself. Do not read that argument as "I came from here" without checking:

```csharp
protected override void OnEnterState(GameState previousState)
{
    if (Manager.PreviousStateType == null) { /* machine's very first entry */ }
}
```

`PreviousStateType` is null until the first *completed* `ChangeState`, is readable before
`Initialize()`, and is left untouched by `RestartState()`.

### One state instance, one machine

`AddState` attaches the instance; adding it to a second machine throws. States hold per-run
data, so sharing one across machines would share that data. Construct a new one per machine.
Writing a state against `IStateManager<TState>` makes the *class* reusable across machines — not
the instance.

### States are keyed by enum *value*

Two enum members with the same underlying value are the same key, so an aliased member
(`enum GameState { Level = 1, Playing = 1 }`) will collide on the second `AddState`.

### The `Allow` fan-out overload validates per element

`Allow(from, params to)` validates as the array is walked, so a call with a bad entry may leave
the earlier entries of that same call already declared. Harmless in practice — the exception aborts your
configuration anyway — but do not rely on it being atomic.

### Configuration happens in `Initialize()`, not in the constructor

A freshly constructed manager holds no states and no transitions — `OnInitialize` and
`InsertTransitions` do not run until `Initialize()` does. The observation properties still answer
with [their "nothing yet" values](#observing-an-uninitialized-machine), but a
`StateManagerBehaviour<TState>` is empty for the whole `Awake` phase and only becomes usable in
`Start`.

### Configuration is one-shot

There is no un-register, no un-allow, no reset. `AllowAny()` cannot be undone, and a
machine cannot be reconfigured after `Initialize()`. Nor is a *failed* `Initialize()` a clean
slate: whatever the hooks registered before the failure is still there, so calling it again
re-runs them over that half-configured machine and the repeated `AddState` throws. To rebuild a
machine, build a new one (and new state instances).

### The host's `OnDestroy` is debug-only — and you must call `base.OnDestroy()`

`StateManagerBehaviour<TState>` declares `protected virtual void OnDestroy()`, and it
exists for exactly one reason: in the Editor it releases the machine from the registry behind the
[debugging window](#editor-debugging-window). **In player builds the body is empty** — the
machine holds no unmanaged resources, so it dies with the GameObject either way.

That still makes it part of the host's contract, like `Awake`/`Start`/`Update`: **override it
and you must call `base.OnDestroy()`**, or the destroyed machine's card lingers in the debug
window. (It is only cosmetic, and the window prunes entries whose GameObject has gone
fake-null anyway, so a missed base call costs you nothing at runtime.)

> **Upgrading:** a subclass written against an earlier version that already declares its own
> `private void OnDestroy()` now *hides* the new virtual and the compiler warns **CS0114**.
> Change it to `protected override void OnDestroy()` and call `base.OnDestroy();` — that is the
> whole fix.

What has **not** changed: handlers *you* subscribed to `StateEntered`/`StateExited` are still
not unsubscribed for you. Nothing in the package touches your subscriptions. Unsubscribe in your
own `OnDestroy` — after the base call — if the subscriber outlives the host.

```csharp
protected override void OnDestroy()
{
    base.OnDestroy();                          // releases the editor debug registration
    StateEntered -= OnStateEntered;            // your subscriptions are still yours to clean up
}
```

### Main thread only

No locks anywhere, like the rest of the library. Configure, tick and transition from the Unity
main thread; do not touch a machine from jobs, threads, or async continuations that may resume
off it.

---

## API reference

```csharp
public interface IStateManager<TState> where TState : struct, Enum   // the shared FSM surface
{
    // observation — none of these throw
    bool IsInitialized { get; }
    TState CurrentStateType { get; }                   // default(TState) before Initialize()
    BaseState<TState> CurrentState { get; }            // null before Initialize()
    TState? PreviousStateType { get; }                 // null until the first ChangeState
    BaseState<TState> PreviousState { get; }           // null until the first ChangeState

    // events, both (from, to)
    event Action<TState, TState> StateExited;
    event Action<TState, TState> StateEntered;

    // runtime
    void Initialize();                                 // runs the hooks, then starts the machine
    void ChangeState(TState nextState);
    void RestartState();
    void Tick();
    bool CanChangeState(TState from, TState to);       // pure query, never throws

    // configuration — both throw StateConfigurationException after Initialize()
    // (transitions are not here: they are declared only through Transitions<TState>, below)
    IStateManager<TState> AddState(BaseState<TState> state);
    IStateManager<TState> SetInitialState(TState state);
}

public abstract class BaseStateManager<TState> : IStateManager<TState>
    where TState : struct, Enum                        // derive: it is abstract
{
    // configuration hooks — run by Initialize(), in this order, exactly once
    protected abstract void OnInitialize();            // AddState, optionally SetInitialState
    protected abstract void InsertTransitions(in Transitions<TState> transitions);

    // optional extension point
    protected virtual TState GetInitialState();        // default: SetInitialState value, else first registered

    // everything else is IStateManager<TState>, implemented here
}

public readonly struct Transitions<TState> where TState : struct, Enum
{
    // handed to InsertTransitions; the only way to declare a transition
    public Transitions<TState> Allow(TState from, TState to);
    public Transitions<TState> Allow(TState from, params TState[] to);
    public Transitions<TState> AllowAny();
}

public abstract class BaseState<TState> where TState : struct, Enum   // machine-facing; not derivable
{
    public abstract TState StateType { get; }

    // machine-facing entry points — public, non-virtual; do not call or hide them
    public void EnterState(TState previousState);      // -> OnEnterState
    public void ExitState(TState nextState);           // -> OnExitState
    public void UpdateState();                         // -> OnUpdate

    // override these instead; every default is empty
    protected virtual void OnEnterState(TState previousState);
    protected virtual void OnExitState(TState nextState);
    protected virtual void OnUpdate();

    public virtual void RestartState();   // default: ExitState(StateType); EnterState(StateType);
}

public abstract class BaseState<TManager, TState> : BaseState<TState>   // derive from this one
    where TManager : class, IStateManager<TState>
    where TState : struct, Enum
{
    protected TManager Manager { get; }   // set by AddState; a type mismatch there throws
}

public abstract class StateManagerBehaviour<TState> : MonoBehaviour, IStateManager<TState>
    where TState : struct, Enum
{
    // the same three hooks as BaseStateManager<TState>
    protected abstract void OnInitialize();
    protected abstract void InsertTransitions(in Transitions<TState> transitions);
    protected virtual TState GetInitialState();

    protected BaseStateManager<TState> Machine { get; }   // the engine it delegates to

    protected virtual void Awake();       // editor: record the host GameObject; empty in players
    protected virtual void Start();       // Initialize() — this is where the hooks run
    protected virtual void Update();      // Tick() once initialized
    protected virtual void OnDestroy();   // editor: release the debug registration; empty in players

    // the whole IStateManager<TState> surface, forwarded; AddState and
    // SetInitialState return this behaviour
}

public class StateManagerException : Exception { }
public sealed class InvalidTransitionException : StateManagerException { }
public sealed class StateConfigurationException : StateManagerException { }
```
