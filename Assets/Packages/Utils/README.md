# Utils

Five small, self-contained utilities for Unity 6: a `Timer`, a logging facade, singleton base
classes, a typed `EventBus`, and an `AllocationCounter`.

---

## Assembly

- **Assembly:** `UnityEssentials.Utilities` (`Runtime/`) — no dependencies.
- **Namespace:** `UnityEssentials.Utilities` — flat; subfolders add no namespace segments.
- **Tests:** `UnityEssentials.Utilities.Tests` (EditMode only).

---

## Quick start

```csharp
using UnityEssentials.Utilities;

// Timer — no MonoBehaviour, no coroutine.
var countdown = new Timer(3f);
countdown.Completed += () => Log.Info("boom");
countdown.Start();

// Logging — stripped from release builds.
Log.Warning("low ammo");
var save = new CustomLogger("Save");   // keep it in a static readonly field
save.Info("profile written");          // [Save] profile written
Log.Interval(transform.position, 20);  // in Update: logs the first call, then every 20th

// AllocationCounter — did this block allocate?
var allocations = AllocationCounter.StartNew();
RebuildPath();
allocations.Stop();
Log.Info(allocations.AllocatedBytes);

// Singletons.
public sealed class GameClock : Singleton<GameClock> { private GameClock() { } }
var clock = GameClock.Instance;   // lazily constructed on this first access

// EventBus — one channel space per bus discriminator.
public struct GameBus : IEventBus { }
public struct EnemyKilled : IEvent { public int Score; }

EventBus<GameBus>.Subscribe<EnemyKilled>(OnEnemyKilled);
EventBus<GameBus>.Publish(new EnemyKilled { Score = 10 });
EventBus<GameBus>.Unsubscribe<EnemyKilled>(OnEnemyKilled);
```

**Main thread only**, with one exception. `Timer`, `EventBus`, `SingletonComponent<T>`,
`SingletonScriptableObject<T>` and the `Interval` log methods assert the calling thread in the
Editor and in development builds (the check is compiled out of release builds). Only
`Singleton<T>` is thread-safe: its `Instance` is double-checked-locked, so a worker thread may
create it.

---

## Timer

```csharp
public enum TimerMode { Stopwatch, Countdown }

public sealed class Timer
{
    public Timer(bool useUnscaledTime = false);                                       // stopwatch
    public Timer(float durationSeconds, bool loop = false, bool useUnscaledTime = false); // countdown

    public event Action Completed;

    public TimerMode Mode { get; }
    public bool UseUnscaledTime { get; }
    public bool IsRunning { get; }
    public bool IsCompleted { get; }

    public float Duration { get; }      // countdown only; 0 for a stopwatch
    public float Remaining { get; }     // countdown only; clamped at 0
    public float Progress { get; }      // countdown only; 0..1

    public TimeSpan Elapsed { get; }
    public double ElapsedSeconds { get; }
    public double ElapsedMilliseconds { get; }
    public double ElapsedMinutes { get; }
    public double ElapsedHours { get; }

    public void Start();     // starts, or resumes from where Stop paused
    public void Stop();      // pause — elapsed is kept
    public void Reset();     // stop, zero elapsed, clear IsCompleted
    public void Restart();   // Reset + Start
}
```

There is no MonoBehaviour and no coroutine. Running timers are ticked by one `Awaitable` frame
loop (`TimerRunner`) that starts lazily on the first `Start()` in play mode and ends with play
mode via `Application.exitCancellationToken`.

```csharp
var stopwatch = new Timer();
stopwatch.Start();
stopwatch.Stop();                       // pause
stopwatch.Start();                      // resume from the paused elapsed
Log.Info(stopwatch.ElapsedSeconds);

var lap = new Timer(1f, loop: true);    // fires every second, forever
lap.Completed += OnLap;
lap.Start();
```

Semantics:

| | |
|---|---|
| `Stop()` | pauses; it is not a cancel. `Reset()`/`Restart()` are the way back to zero |
| `Start()` on a finished countdown | ignored — use `Restart()` |
| Non-looping completion | elapsed clamps to `Duration`, `IsRunning` goes false, `IsCompleted` latches, the timer unregisters itself |
| Looping completion | `Completed` fires per lap, the overflow rolls into the next lap, `IsCompleted` never latches |
| One large delta | fires `Completed` once per whole lap it crosses |
| Non-positive delta | ignored — a timer never rewinds |
| `useUnscaledTime: true` | ticks on `Time.unscaledDeltaTime` instead of `Time.deltaTime` |
| Stopwatch mode | `Duration`, `Remaining` and `Progress` are 0 and `Completed` never fires |

A `Completed` handler may `Stop`, `Reset` or `Restart` its own timer from inside the callback;
the remaining laps of that tick are dropped and the new state wins.

Errors: a countdown duration that is not a finite value greater than zero (`0`, negative, `NaN`,
infinity) throws `ArgumentOutOfRangeException` from the constructor.

Exception handling: `Completed` is a plain multicast delegate. A handler that throws is reported
through `Debug.LogException` and the timer stays consistent (and keeps looping), but the throw
still ends that invocation list — subscribers queued behind the faulty one are skipped for that
raise.

---

## Logging

```csharp
public enum LogSeverity { Info, Warning, Error, Critical }

public static class Log
{
    public static void Info(object message);
    public static void Warning(object message);
    public static void Error(object message);
    public static void Critical(object message);
    public static void Message(LogSeverity severity, object message);
    public static void Interval(object message, int interval);                        // Info
    public static void Interval(LogSeverity severity, object message, int interval);
}

public sealed class CustomLogger
{
    public CustomLogger();                              // no header, no colour — bare messages
    public CustomLogger(string header);                 // colour hashed from the header
    public CustomLogger(string header, Color color);    // explicit colour

    public string Header { get; }
    public Color Color { get; }

    // the same seven methods as Log
}
```

Create a `CustomLogger` per project aspect; it prefixes every message with
`<color=#RRGGBB>[Header]</color> `. Without an explicit colour the header is hashed (FNV-1a) into
a fixed 12-entry palette, so a subsystem keeps the same colour in every project and every session,
and the palette is mid-brightness so it reads on both editor skins. `Log` is the header-less
facade: it forwards to one lazily created header-less `CustomLogger`.

```csharp
private static readonly CustomLogger _analytics = new CustomLogger("Analytics");
private static readonly CustomLogger _ui = new CustomLogger("UI", Color.cyan);

_analytics.Info("session started");        // [Analytics] session started
_ui.Critical("atlas missing");             // [UI] atlas missing  — body in red
Log.Message(severity, report);             // severity picked at runtime
```

| Severity | Console channel | Body |
|---|---|---|
| `Info` | `Debug.Log` | plain |
| `Warning` | `Debug.LogWarning` | plain |
| `Error` | `Debug.LogError` | plain |
| `Critical` | `Debug.LogError` | wrapped in `<color=#E04B4B>` |

The prefix is composed once in the constructor, so keep loggers in `static readonly` fields
rather than building one per call site.

Every log method carries `[Conditional("UNITY_EDITOR")]` and `[Conditional("DEVELOPMENT_BUILD")]`:
in a release build the call **and its arguments** disappear. See Gotchas.

### Interval logging

For values you want to watch from `Update` without a log entry every frame:

```csharp
private void Update()
{
    Log.Interval(_velocity, 20);                       // logs call 1, then calls 21, 41, ...
    _physics.Interval(LogSeverity.Warning, _drift, 300);
}
```

Counting is per **call site**: the compiler stamps every call with its source file and line
(`[CallerFilePath]`/`[CallerLineNumber]`), so each `Interval` statement owns one counter with no
key or setup, and separate statements never interfere. The first call at a site always logs;
after that every `interval`-th call logs. The unit is calls, not time — called once per frame,
`interval: 20` means roughly every 20 frames. Counters are static (two loggers sharing one line
share its counter) and are cleared on domain reload. Unlike the other log methods, `Interval`
asserts the main thread, because the counters are shared state.

Errors: an empty or whitespace header throws `ArgumentException`; a `LogSeverity` value outside
the enum throws `ArgumentOutOfRangeException`, as does an `interval` less than 1. A `null` message
(or one whose `ToString()` returns null) renders as `Null` rather than throwing.

---

## Singletons

Three bases, one per kind of object. All of them expose `Instance` and `HasInstance` and reset
their cached instance on domain reload.

### `Singleton<T>` — plain C# object

```csharp
public abstract class Singleton<T> where T : Singleton<T>
{
    public static T Instance { get; }
    public static bool HasInstance { get; }
    protected Singleton();
}
```

```csharp
public sealed class SaveSystem : Singleton<SaveSystem>
{
    private SaveSystem() { }          // private ctor: Instance builds it
    public void Save() { }
}

SaveSystem.Instance.Save();
```

Lazily created on first access with a double-checked lock, so concurrent first access still
yields exactly one instance. Constructing the subclass with `new` throws
`InvalidOperationException`, before or after the first access. If the subclass constructor
throws, the original exception surfaces unwrapped (not a `TargetInvocationException`) and nothing
is cached — the next access constructs again.

### `SingletonComponent<T>` — MonoBehaviour

```csharp
public abstract class SingletonComponent<T> : MonoBehaviour where T : SingletonComponent<T>
{
    public static T Instance { get; }
    public static bool HasInstance { get; }
    protected virtual void Awake();
}
```

`Instance` resolves in this order:

1. Quitting? log a warning and return `null`.
2. The cached instance, if it is still alive (Unity's `==`, so a destroyed component does not count).
3. `FindAnyObjectByType<T>()` — a component you placed in the scene wins.
4. Otherwise a new `HideFlags.HideAndDontSave` GameObject named `<T> (Singleton)` with the component on it.

`DontDestroyOnLoad` is applied in play mode only, and only to a root object — a singleton
parented under a scene object stays with its scene.

A duplicate that wakes up while another instance owns the slot destroys **its own component
only**, with a warning; the GameObject it sits on and every other component are left alone. An
override must call `base.Awake()` first and bail out if the base destroyed it:

```csharp
public sealed class AudioRig : SingletonComponent<AudioRig>
{
    protected override void Awake()
    {
        base.Awake();
        if (this == null) return;     // destroyed as a duplicate
        // ...
    }
}
```

### `SingletonScriptableObject<T>` — asset

```csharp
public abstract class SingletonScriptableObject<T> : ScriptableObject where T : SingletonScriptableObject<T>
{
    public static T Instance { get; }
    public static bool HasInstance { get; }
}
```

```csharp
[CreateAssetMenu(menuName = "Config/Balance")]
public sealed class BalanceConfig : SingletonScriptableObject<BalanceConfig>
{
    [SerializeField] private float _damageScale = 1f;
    public float DamageScale => _damageScale;
}

var scale = BalanceConfig.Instance.DamageScale;
```

The first access runs `Resources.LoadAll<T>("")` and caches the result. Finding zero assets
throws `InvalidOperationException` telling you to create one in a `Resources` folder; finding two
or more throws and lists their names instead of picking one.

---

## EventBus

```csharp
public interface IEvent { }       // marker for a struct payload
public interface IEventBus { }    // marker for a struct bus discriminator

public static class EventBus<TBus> where TBus : struct, IEventBus
{
    public static void Subscribe<TEvent>(Action<TEvent> handler) where TEvent : struct, IEvent;
    public static void Unsubscribe<TEvent>(Action<TEvent> handler) where TEvent : struct, IEvent;
    public static void Publish<TEvent>(in TEvent evt) where TEvent : struct, IEvent;
    public static void Clear<TEvent>() where TEvent : struct, IEvent;
}
```

`TBus` is an empty struct used purely as a discriminator: each one closes the generic over its
own statics, so `EventBus<GameBus>` and `EventBus<UiBus>` cannot see each other's handlers even
for the same event type. Events are structs, so a warm publish allocates nothing.

```csharp
public struct GameBus : IEventBus { }
public struct DamageTaken : IEvent { public int Amount; }

public sealed class HealthBar : MonoBehaviour
{
    private void OnEnable()  => EventBus<GameBus>.Subscribe<DamageTaken>(OnDamage);
    private void OnDisable() => EventBus<GameBus>.Unsubscribe<DamageTaken>(OnDamage);

    private void OnDamage(DamageTaken evt) { /* ... */ }
}

EventBus<GameBus>.Publish(new DamageTaken { Amount = 12 });
```

- Handlers run in subscription order.
- Subscribing the same delegate twice registers it twice — it fires twice, and each `Unsubscribe`
  removes one registration. Unsubscribing something that was never subscribed is a no-op.
- A handler that throws is reported through `Debug.LogException`, stays subscribed, and the
  handlers behind it still run.
- `Clear<TEvent>()` drops every handler for that event on that bus.
- `null` handler throws `ArgumentNullException`.

Unsubscribing needs the same delegate you subscribed. A method group (`OnDamage` above) works
because both conversions produce equal delegates; an inline lambda does not — store it in a field
if you ever need to remove it.

---

## AllocationCounter

```csharp
public struct AllocationCounter
{
    public static AllocationCounter StartNew();

    public long AllocatedBytes { get; }      // accumulated total; live while running
    public bool IsRunning { get; }
    public bool CollectionOccurred { get; }  // a GC ran inside a measured span

    public void Start();     // begins or resumes a span; no-op while running
    public void Stop();      // folds the span's heap growth into the total
    public void Reset();     // stop, zero the total, clear the flag
    public void Restart();   // Reset + Start
}
```

A stopwatch for managed-heap growth, for answering "does this block allocate?" from inside the
code itself:

```csharp
var allocations = AllocationCounter.StartNew();
RebuildNavigationGraph();
allocations.Stop();

if (allocations.AllocatedBytes > 0)
{
    Log.Warning($"rebuild allocated {allocations.AllocatedBytes} B");
}
```

| | |
|---|---|
| The reading | `GC.GetTotalMemory(false)` deltas — managed-heap bytes, process-wide, so other threads and engine code inside the span count too |
| A GC inside a span | erases part of the growth, so the result is a **lower bound**; `CollectionOccurred` latches true until `Reset` |
| A shrinking span | clamped to 0 per span — the accumulated total never decreases |
| Mutable struct | the counter itself never allocates, but keep it in one local: a copy (field, lambda capture, by-value parameter) counts on its own |
| Availability | present in every build — not `[Conditional]`-stripped, no profiler needed |

It is a coarse probe, not a profiler: for callstacks and per-frame breakdowns use the Unity
Profiler, and for asserting a code path allocates nothing in a test, Unity's `AllocatingGCMemory`
constraint.

---

## Gotchas

### `[Conditional]` strips the arguments too, not just the call

In a build that defines neither `UNITY_EDITOR` nor `DEVELOPMENT_BUILD`, the compiler removes the
whole call expression. Anything you compute inside the argument list is never evaluated:

```csharp
Log.Info($"saved {BuildReport()}");   // BuildReport() does not run in release — free, as intended
Log.Info(_queue.Dequeue());           // BUG: the dequeue silently stops happening in release
```

Keep side effects out of log arguments. The upside is the reason for the design: expensive string
interpolation costs nothing in a shipping build.

### Stop timers you abandon

A running timer is held by `TimerRunner`'s active list until it stops, completes or the statics
reset — dropping your last reference is not enough to make it go away.

```csharp
private void OnDisable() => _timer.Stop();
```

Non-looping countdowns unregister themselves on completion; stopwatches and looping countdowns
run until you stop them.

### EventBus snapshot semantics

`Publish` captures the handler array once and iterates that snapshot, so mutations made from
inside a handler apply to the **next** publish:

| Mutation during a publish | Effect on the publish in flight |
|---|---|
| A handler unsubscribes itself | it already ran; it is gone from the next publish |
| A handler unsubscribes a later handler | the later handler still receives this event |
| A handler subscribes a new handler | the newcomer starts with the next publish |
| A handler calls `Clear` | the rest of this publish still runs |
| A handler publishes the same event again | the inner publish takes a fresh snapshot and completes before the outer one resumes |

This is what makes subscribe/unsubscribe from inside a handler safe, and it is what makes the warm
publish path allocation free.

### `SingletonScriptableObject` needs the asset in *your* project

`Instance` resolves through `Resources.LoadAll<T>("")`, which only sees assets inside a folder
literally named `Resources`. This repo ships none on purpose — a library must not force a
`Resources` folder on its consumers. In the consuming project, create the asset (for example
`Assets/Resources/BalanceConfig.asset`) or `Instance` throws.

### EditMode is not play mode

- `AddComponent<T>()` does **not** call `Awake` outside play mode, so a `SingletonComponent<T>`
  created in EditMode does not claim the slot until something calls `Instance` (the tests use an
  `InvokeAwake` seam on a test double instead).
- `DontDestroyOnLoad` is applied only while `Application.isPlaying`.
- `TimerRunner`'s frame loop only exists in play mode, so timers do not advance in EditMode; the
  tests drive the internal `Tick` seam directly.
- Duplicate components are removed with `DestroyImmediate` outside play mode and `Destroy` inside it.

### Domain reload resets, it does not clean up

On `RuntimeInitializeOnLoadMethod(SubsystemRegistration)` every registered system clears its
statics: cached singleton instances, all EventBus channels, the interval log counters, and the
timer runner's active list (the running `Awaitable` loop is retired through a generation counter).
Nothing is disposed and no
`Completed` handler is called — entering play mode with domain reload disabled starts clean, but
if a singleton owns a file handle or a socket, close it yourself.

### Verified manually in play mode

Not reachable from EditMode tests; re-check these after touching the runner or the component
singleton:

- The `Awaitable` tick loop starts on the first `Start()`, stops when the last timer stops, and is
  cancelled when play mode exits (no stale continuations, no leaked loop after a second entry).
- `DontDestroyOnLoad` survives a real scene load and does not produce a second instance.
- The real `Application.quitting` event makes `Instance` return null with a warning instead of
  resurrecting a singleton during teardown.
- `SingletonScriptableObject<T>` discovery from a real `Resources` folder in a consuming project.
