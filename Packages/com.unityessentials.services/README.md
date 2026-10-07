# ServiceLocator

A static, allocation-free service locator for Unity 6. Fluent registration, lazy shared
instances, transients, scopes with shadowing, and deterministic disposal.

- **Assembly:** `UnityEssentials.Services` (`Runtime/`) — no dependencies, `.NET Standard 2.1`.
- **Namespace:** `UnityEssentials.Services`
- **Tests:** `UnityEssentials.Services.Tests` (EditMode only)

**This is a service locator, not a DI container.** It does not scan types, build object
graphs, or inject constructors. You register something, you ask for it by type, you get it
back. Everything is explicit and visible at the call site. If you want automatic constructor
injection across a whole graph, use VContainer or Zenject — this is the deliberately small
alternative for projects that just need "one well-known instance of X, reachable from
anywhere".

Warm resolution of a shared service is one dictionary lookup and a cast: zero allocations,
no reflection, no LINQ, no locks.

---

## Quick start

```csharp
using UnityEssentials.Services;

ServiceLocator.Register<AudioService>().As<IAudioService>();   // once, at startup

var audio = ServiceLocator.Get<IAudioService>();               // anywhere
audio.Play("footstep");
```

The types used throughout this document:

```csharp
public interface IAudioService { void Play(string clip); }
public interface IMuteToggle   { void Mute(bool on); }

public sealed class AudioService : IAudioService, IMuteToggle, IDisposable
{
    public AudioService() { }
    public AudioService(float masterVolume) { }

    public void Play(string clip) { }
    public void Mute(bool on) { }
    public void Dispose() { }
}
```

---

## Registration

Three overloads. All of them commit immediately — the returned
`RegistrationBuilder<TConcrete>` is a fluent handle for options, **not** something you have
to `Build()`. Treat it as a temporary: chain on it and let it go, never store it in a field.

### 1. Type — the locator constructs it

```csharp
ServiceLocator.Register<AudioService>();                        // Shared (default)
ServiceLocator.Register<Projectile>(Lifetime.Transient);        // new instance per Get
```

Requires a public parameterless constructor (`where TConcrete : class, new()`).

### 2. Instance — you construct it

```csharp
var audio = new AudioService(masterVolume: 0.8f);
ServiceLocator.Register(audio).As<IAudioService>();
```

Instance registrations are **inherently Shared** — there is no lifetime parameter, because
the object already exists. `.NonLazy()` on one is a harmless no-op.

`TConcrete` is inferred from the *static* type of the variable you pass, so pass a
concretely-typed local (or name the type explicitly, `Register<AudioService>(audio)`) rather
than a variable already typed as the interface you are about to `.As<>()`.

### 3. Factory — you decide how it is built

```csharp
ServiceLocator.Register<AudioService>(() => new AudioService(0.8f));
ServiceLocator.Register<Projectile>(() => new Projectile(), Lifetime.Transient);
```

The factory runs on first `Get` for `Shared`, and on **every** `Get` for `Transient`.

### Lifetimes

| | `Lifetime.Shared` (default) | `Lifetime.Transient` |
|---|---|---|
| Created | lazily, on first `Get` | on every `Get` |
| Cached | yes — one instance per registration | never |
| Same reference on repeat `Get` | yes | no |
| Disposed on scope release | yes, if `IDisposable` and created | never — you own it |

Shared services are **lazy by default**: registering costs nothing but a dictionary entry;
the constructor does not run until somebody actually asks. Use `.NonLazy()` when you need
the side effects at startup.

`null` instance or `null` factory throws `ArgumentNullException`. A `Shared` factory that
returns `null` throws at creation time — the first `Get`, or at `Register` if `.NonLazy()`.

---

## Fluent options

### `.As<TInterface>()` — register under a different key (max 4)

```csharp
ServiceLocator.Register<AudioService>()
    .As<IAudioService>()
    .As<IMuteToggle>();

ServiceLocator.Get<IAudioService>();   // works
ServiceLocator.Get<IMuteToggle>();     // works — the same instance
ServiceLocator.Get<AudioService>();    // throws: the concrete key is locked
```

`.As<T>()` does two things: it publishes the interface key, and it **locks the concrete
key** (an access lock). That is the point — consumers see the interface you meant to expose,
not the implementation. The exception message for a locked key says so explicitly and tells
you to chain `.AsSelf()`.

- Up to **4** interfaces per registration; a 5th throws `ServiceRegistrationException`.
- Assignability is checked once, at registration: `.As<T>()` where `TConcrete` does not
  implement `T` throws `ServiceRegistrationException` immediately, not at resolution time.
- Abstract base classes work as keys too, not just interfaces.

### `.AsSelf()` — keep the concrete key resolvable

```csharp
ServiceLocator.Register<AudioService>().As<IAudioService>().AsSelf();
ServiceLocator.Register<AudioService>().AsSelf().As<IAudioService>();   // identical
```

Order does not matter. With no `.As<>()` at all, the self key is published implicitly —
`.AsSelf()` is only needed to undo the lock.

### `.NonLazy()` — construct now

```csharp
ServiceLocator.Register<SaveService>().As<ISaveService>().NonLazy();
```

Forces the Shared instance to be created during `Register`. Throws on a `Transient`
registration (there is nothing to eagerly create); no-op on an instance registration.

### `.ExternallyOwned()` — hands off disposal

```csharp
var client = new NetworkClient();   // owned and disposed by something else
ServiceLocator.Register(client).As<INetworkClient>().ExternallyOwned();
```

The locator will never call `Dispose()` on this service. Use it when something else owns
the lifetime — a `MonoBehaviour`, a plugin, a pooled object you intend to reuse.

---

## Resolution

```csharp
var audio = ServiceLocator.Get<IAudioService>();   // throws ServiceNotFoundException if missing

if (ServiceLocator.TryGet<IAudioService>(out var maybeAudio))
    maybeAudio.Play("ui_click");                   // never throws; false + null on miss
```

Use `Get` for hard dependencies (fail loud, fail early) and `TryGet` for optional systems
(analytics, debug overlays, platform-specific services).

**Lookup is unified.** `ServiceLocator.Get<T>()` finds services registered in *any* live
scope, not just the global ones. You never have to find the right scope first:

```csharp
var scope = ServiceLocator.CreateScope("Gameplay");
scope.Register<EnemySpawner>().As<ISpawner>();

ServiceLocator.Get<ISpawner>();   // found — no scope handle needed at the call site
```

`ServiceScope.Get<T>()` / `TryGet<T>()` are scope-first: they prefer the scope's own
binding and fall back to the unified lookup if the scope does not have one.

---

## Scopes

A scope is a named, releasable group of registrations. Get one from
`ServiceLocator.CreateScope(name)`; register through the handle; release it when the phase
of the game it belongs to ends.

```csharp
var gameplay = ServiceLocator.CreateScope("Gameplay");

gameplay.Register<EnemySpawner>().As<ISpawner>();
gameplay.Register<CombatLog>().NonLazy();

gameplay.Release();   // unpublishes every binding, then disposes what it created
```

`ServiceScope` implements `IDisposable`, so `using` works and is the recommended form for
anything with a lexical lifetime:

```csharp
using (var level = ServiceLocator.CreateScope("Level01"))
{
    level.Register<LevelState>().As<ILevelState>();
    RunLevel();
}   // Dispose() => Release()
```

Scope rules:

| | |
|---|---|
| `Name` | must be non-null, non-empty and unique among live scopes (`"<global>"` is reserved); otherwise throws |
| `Release()` | idempotent — calling it twice is a no-op |
| `IsReleased` | `true` after release; registering or resolving through a released scope throws |
| Re-creating a scope | fine — the name is free again once released |
| Nesting | scopes are flat, not hierarchical; precedence comes from shadowing (below) |

### Shadowing: newest wins, release un-shadows

The same key may be registered in different scopes. Registrations for a key form a stack —
**the most recent one wins**, and releasing it restores whatever it was covering, even from
the middle of the stack:

```csharp
ServiceLocator.Register<MenuMusic>().As<IMusic>();       // global

var a = ServiceLocator.CreateScope("A");
a.Register<CombatMusic>().As<IMusic>();
ServiceLocator.Get<IMusic>();                            // CombatMusic

var b = ServiceLocator.CreateScope("B");
b.Register<BossMusic>().As<IMusic>();
ServiceLocator.Get<IMusic>();                            // BossMusic

a.Release();                 // mid-stack release
ServiceLocator.Get<IMusic>();                            // still BossMusic

b.Release();
ServiceLocator.Get<IMusic>();                            // MenuMusic again
```

Shadowing is **per key**, not per registration: `.As<IMusic>()` shadowing another
`IMusic` says nothing about `IMuteToggle` or the concrete keys.

Registering the same key **twice in the same scope** is a mistake, not shadowing, and
throws `ServiceRegistrationException`.

---

## Disposal semantics

Releasing a scope (or calling `ReleaseAll`) does two things, in this order: unpublish all
of its bindings, then dispose what it owns in **reverse registration order** — so a service
registered later, which may depend on an earlier one, is torn down first.

| Service | Disposed on release? |
|---|---|
| Shared, `IDisposable`, was created | yes |
| Shared, `IDisposable`, never resolved (lazy, never built) | no — it never existed |
| Registered instance, `IDisposable` | yes, by default |
| Anything marked `.ExternallyOwned()` | no |
| Transient | never — the locator does not track transients |
| Not `IDisposable` | nothing to do |

If a `Dispose()` throws, it is reported via `Debug.LogException` and release **continues** —
one broken service cannot strand the rest.

```csharp
ServiceLocator.ReleaseAll();
```

`ReleaseAll()` releases every live scope in reverse creation order, then the global
registrations, disposing along the way, and leaves the locator empty and reusable.
Re-registering after a release or a `ReleaseAll` is fully supported.

There is no `Unregister<T>()`: scopes are the removal mechanism.

---

## Errors

| Type | When |
|---|---|
| `ServiceLocatorException` | base type; also thrown for circular resolution |
| `ServiceNotFoundException` | `Get<T>()` for an unregistered or access-locked key |
| `ServiceRegistrationException` | duplicate key in a scope, 5th `.As<>()`, unassignable `.As<>()`, `.NonLazy()` on a transient, duplicate/null scope name |
| `ArgumentNullException` | null instance, null factory |

Circular resolution (A's constructor `Get`s B, whose constructor `Get`s A) is detected and
throws `ServiceLocatorException` instead of overflowing the stack.

---

## Gotchas

### Main thread only

There are no locks anywhere. Register and resolve from the Unity main thread. A cheap
main-thread assertion runs in the Editor and in development builds and is compiled out of
release builds — do not call this from jobs, threads, or async continuations that may
resume off the main thread.

### Domain-reload reset does not dispose

Statics are hard-reset on `RuntimeInitializeOnLoadMethod(SubsystemRegistration)`, so entering
play mode with domain reload disabled never leaves stale services behind. That reset
**drops** the old registrations without disposing them — with reload enabled the previous
session's objects are already gone, and with it disabled they are unreachable anyway.

If disposal actually matters (file handles, sockets, native buffers, native plugins), call
`ReleaseAll()` yourself on shutdown:

```csharp
public sealed class ServiceBootstrap : MonoBehaviour
{
    private void Awake()
    {
        ServiceLocator.Register<SaveService>().As<ISaveService>().NonLazy();
        ServiceLocator.Register<AudioService>().As<IAudioService>();
    }

    private void OnDestroy() => ServiceLocator.ReleaseAll();
}
```

### IL2CPP: prefer the factory overload for high-frequency transients

`Register<T>()` relies on the `new()` constraint, which IL2CPP implements as
`Activator.CreateInstance` — reflection. That is irrelevant for a Shared service (one call,
ever), but it lands on the hot path for a transient resolved every frame. Use the factory
overload there; a lambda is a direct constructor call:

```csharp
ServiceLocator.Register<Projectile>(Lifetime.Transient);                 // fine, but reflective on IL2CPP
ServiceLocator.Register<Projectile>(() => new Projectile(), Lifetime.Transient);   // preferred
```

### Registration order matters with `.NonLazy()`

A `.NonLazy()` service is constructed *during* `Register`, so anything its constructor
resolves must already be registered:

```csharp
ServiceLocator.Register<Config>().As<IConfig>();                  // first
ServiceLocator.Register<SaveService>().As<ISaveService>().NonLazy();   // ctor may Get<IConfig>()
```

Default (lazy) registrations sidestep this entirely — construction happens at first `Get`,
by which time everything registered at startup exists. Reach for `.NonLazy()` only when the
constructor has side effects you need at a specific moment.

### It is still a locator

Resolution is a hidden dependency. Keep `Get` calls in composition roots, `Awake`, and cold
paths; cache the result in a field rather than calling `Get` inside `Update`. A warm `Get`
is cheap, but a dependency you can see in a field is cheaper to reason about.

---

## API reference

```csharp
public static class ServiceLocator
{
    public static RegistrationBuilder<TConcrete> Register<TConcrete>(Lifetime lifetime = Lifetime.Shared)
        where TConcrete : class, new();
    public static RegistrationBuilder<TConcrete> Register<TConcrete>(TConcrete instance)
        where TConcrete : class;
    public static RegistrationBuilder<TConcrete> Register<TConcrete>(Func<TConcrete> factory, Lifetime lifetime = Lifetime.Shared)
        where TConcrete : class;

    public static T Get<T>() where T : class;
    public static bool TryGet<T>(out T service) where T : class;

    public static ServiceScope CreateScope(string name);
    public static void ReleaseAll();
}

public readonly struct RegistrationBuilder<TConcrete> where TConcrete : class
{
    public RegistrationBuilder<TConcrete> As<TInterface>() where TInterface : class;
    public RegistrationBuilder<TConcrete> AsSelf();
    public RegistrationBuilder<TConcrete> NonLazy();
    public RegistrationBuilder<TConcrete> ExternallyOwned();
}

public sealed class ServiceScope : IDisposable
{
    public string Name { get; }
    public bool IsReleased { get; }

    // the same three Register overloads as ServiceLocator
    public T Get<T>() where T : class;
    public bool TryGet<T>(out T service) where T : class;
    public void Release();
}

public enum Lifetime : byte { Shared, Transient }
```
