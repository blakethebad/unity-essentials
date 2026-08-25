# Utils Package — Implementation Plan

## Context

unity-essentials is a library repo of portable, self-contained Unity systems (`Assets/Packages/<System>/`). This adds a new **Utils** package: small 1–2 script utilities importable into other projects — four systems for v1: **Timer**, **Logger**, **Singletons (×3)**, **EventBus**. Namespace is flat `UnityEssentials.Utilities` (settled with user; matches repo asmdef=rootNamespace convention).

Execution model (user directive): I act as Lead Engineer. **Opus 5 agents implement via a dynamic Workflow; I review, compile-check, test, and refactor everything myself before shipment.**

## Settled decisions (asked & answered)

| Question | Decision |
|---|---|
| Namespace | `UnityEssentials.Utilities` (folder `Assets/Packages/Utils/`) |
| Timer scope | Stopwatch **and** countdown (duration + `Completed` event, optional loop) |
| Log stripping | **Strip from release**: `[Conditional("UNITY_EDITOR")] [Conditional("DEVELOPMENT_BUILD")]` on all log methods |
| SingletonComponent | Auto-create hidden GO + `DontDestroyOnLoad`; duplicates destroy self with warning; `Application.quitting` guard |

Lead rulings: instance logger is named **`LogChannel`** (a `Logger` class is ambiguous with `UnityEngine.Logger` in any `using UnityEngine;` file); `Stop` = pause (Stopwatch semantics: `Start`/`Stop`/`Reset`/`Restart`, no separate Pause API); shared `[Conditional]` `AssertMainThread` guard is kept despite the inline-throw rule (inlining it would need `#if` blocks at every call site to stay release-stripped — repo precedent in `ServiceRegistry.cs:70`).

## Package structure

```
Assets/Packages/Utils/
├── README.md
├── Runtime/
│   ├── UnityEssentials.Utilities.asmdef      (clone of UnityEssentials.Services.asmdef template)
│   ├── AssemblyInfo.cs                       (InternalsVisibleTo("UnityEssentials.Utilities.Tests"))
│   ├── StaticResetRegistry.cs                (shared: reset actions for closed-generic statics, MainThreadId, AssertMainThread)
│   ├── Timer/Timer.cs                        (TimerMode enum + sealed Timer)
│   ├── Timer/TimerRunner.cs                  (internal static; single Awaitable frame loop ticks all timers)
│   ├── Logging/Log.cs                        (LogSeverity enum + static Log facade)
│   ├── Logging/LogChannel.cs                 (instance logger: header + color)
│   ├── Logging/LogFormatting.cs              (internal: palette, FNV-1a, compose, dispatch)
│   ├── Singletons/Singleton.cs
│   ├── Singletons/SingletonComponent.cs      (+ internal static SingletonComponentRuntime in same file)
│   ├── Singletons/SingletonScriptableObject.cs
│   └── Events/IEvent.cs, IEventBus.cs, EventBus.cs
└── Tests/
    ├── UnityEssentials.Utilities.Tests.asmdef (clone of Services.Tests template: Editor-only, UNITY_INCLUDE_TESTS, nunit)
    ├── StaticResetRegistryTests.cs
    ├── TimerStopwatchTests.cs, TimerCountdownTests.cs, TimerRunnerTests.cs
    ├── LogTests.cs, LogChannelTests.cs
    ├── TestSingletons.cs, SingletonTests.cs, SingletonComponentTests.cs, SingletonScriptableObjectTests.cs
    ├── TestEvents.cs, EventBusTests.cs, EventBusMutationDuringPublishTests.cs
    └── PerformanceTests.cs                   (Is.Not.AllocatingGCMemory idiom from ServiceLocator/Tests/PerformanceTests.cs)
```

No `Editor/` folder, no `package.json`, no `Resources/` folder, no hand-authored `.meta` files (Unity generates them).

## Public API (condensed)

**Timer** — `new Timer(bool useUnscaledTime = false)` (stopwatch) / `new Timer(float durationSeconds, bool loop = false, bool useUnscaledTime = false)` (countdown). `Start/Stop/Reset/Restart`, `event Action Completed`, `IsRunning`, `IsCompleted`, `Progress`, `Elapsed` (TimeSpan) + `ElapsedSeconds/Milliseconds/Minutes/Hours`, `Duration`, `Remaining`. `internal Tick(float dt)` is the EditMode test seam. `TimerRunner`: internal static list of active timers, one `async` loop `await Awaitable.NextFrameAsync(Application.exitCancellationToken)` started lazily in play mode; generation counter kills stale loops; per-timer try/catch + `Debug.LogException` in runner tick; snapshot-count iteration with deferred compaction. No coroutines anywhere.

**Logger** — `Log.Info/Warning/Error/Critical/Message(severity, ...)` static facade; `LogChannel("Analytics")` or `LogChannel("Analytics", color)` instances producing `<color=#RRGGBB>[Analytics]</color> message`. Auto-color = FNV-1a(header) → ~12-entry palette readable on both editor skins; prefix precomputed in ctor. Severity→channel: Info→`Debug.Log`, Warning→`LogWarning`, Error/Critical→`LogError` (Critical also wraps body in red). Every log method `[Conditional]`-stripped from release. `internal FormatMessage` seam for exact-string tests (LogAssert only for channel-mapping regex checks — rich text fights exact LogAssert).

**Singletons** —
- `Singleton<T> where T : Singleton<T>`: thread-safe double-checked lazy `Instance`; creates via `Activator.CreateInstance(typeof(T), nonPublic: true)` with a `[ThreadStatic] _isConstructing` flag; protected base ctor throws `InvalidOperationException` inline on direct `new` (before or after first access). `HasInstance`. Unwrap `TargetInvocationException` via `ExceptionDispatchInfo` (ServiceRegistry precedent) so a throwing subclass ctor surfaces cleanly and retry works.
- `SingletonComponent<T>`: `Instance` → quitting? null+warning : cached-alive : `FindAnyObjectByType<T>()` : create hidden GO (`HideFlags.HideAndDontSave`) + `AddComponent`. `DontDestroyOnLoad` only under `Application.isPlaying`. Duplicate `Awake` destroys **the component only** (never the GO) with a warning; `Destroy` vs `DestroyImmediate` branched on `isPlaying`. Unity-`==` for destroyed checks (never `is null`). Non-generic `SingletonComponentRuntime` owns `IsQuitting` + `[RuntimeInitializeOnLoadMethod]` (the attribute never fires on generic types).
- `SingletonScriptableObject<T>`: lazy `Instance` = `Resources.LoadAll<T>("")` → `internal ResolveInstance(T[] candidates)` (throws with guidance on 0, lists names on ≥2) — `ResolveInstance` is the unit-test seam; `SetInstanceForTests` injection for `CreateInstance`-made assets. Consumer project supplies the Resources asset; repo stays Resources-free.

**EventBus** — `EventBus<TBus> where TBus : struct, IEventBus` with `Subscribe<TEvent>(Action<TEvent>)`, `Unsubscribe`, `Publish<TEvent>(in TEvent evt)`, `Clear<TEvent>()`; `where TEvent : struct, IEvent`. Storage: nested `internal static class Channel<TEvent>` holding **copy-on-write `Action<TEvent>[]`** (never null). Publish iterates a locally captured array → zero-alloc warm, mutation-during-publish trivially safe (snapshot semantics: handler unsubscribed mid-publish still gets this one; subscribed mid-publish starts next one — documented + pinned by tests). Handler exceptions: catch + `Debug.LogException` + continue. Multicast delegate rejected (per-handler catch would force `GetInvocationList()` alloc per publish).

**StaticResetRegistry** (shared) — `[RuntimeInitializeOnLoadMethod]` never fires on generic classes and closed-generic statics can't be enumerated, so every generic system registers a clear-action from its static ctor; registry drains but **retains** actions (static ctors don't re-run with domain reload off). Also owns `MainThreadId` + `[Conditional] AssertMainThread`. All statics get inline initializers (EditMode never fires RuntimeInitializeOnLoadMethod — `ServiceRegistry.cs:16-19` precedent).

## Exceptions & style rules (enforced in every agent prompt + my review)

- Built-in exception types only, thrown **inline** at call sites — no throw-only helpers, no custom exception classes (nothing here needs a catchable domain type).
- Comment policy: no field/property comments; class XML summaries ≤4 lines; 1–2 line XML summaries on public methods; **no XML docs on internal/private methods**. (Overrides the older packages' docs-everywhere style.)
- Block-scoped `namespace UnityEssentials.Utilities` in every file (subfolders add no segments); usings above namespace; Allman; `_camelCase` privates; C# 9; no unsafe.

## Orchestration (dynamic Workflow, Opus 5 implements — I review)

1. **Docs first (me):** write `Docs/Utils-Plan.md` (repo `<Name>-Plan.md` convention, structure mirroring `HapticSystem-Plan.md`) from this plan.
2. **Workflow phase "Scaffold" (1 Opus agent):** shared files only — both asmdefs (exact clones of the ServiceLocator templates), `AssemblyInfo.cs`, `StaticResetRegistry.cs`, `StaticResetRegistryTests.cs`, README skeleton.
3. **Workflow phase "Implement" (4 Opus agents in parallel, disjoint file ownership):** Timer / Logger / Singletons / EventBus — each writes its Runtime files + its Tests files per the tree above. Agents may read scaffold files, never edit them. Each prompt carries the API contract, design decisions, style rules, and its exact file list.
4. **Workflow phase "Integrate" (1 Opus agent, after all four):** `Tests/PerformanceTests.cs` (spans systems: warm publish, runner tick, elapsed accessors, warm singleton access — logging exempt by design) + fill README from the implemented API (repo README convention: assembly/namespace block, quick start, API reference, gotchas).
5. **Lead review (me, no agents):** read every file; verify API contracts, style rules, snapshot semantics, reset correctness; refactor directly where needed; fix all findings myself.
6. **Verification (me)** — see below.

## Verification

1. **Roslyn offline compile** (memory: `unity-compile-check-recipe`): `dotnet exec <sdk>\Roslyn\bincore\csc.dll` with `/noconfig /nostdlib+ /langversion:9.0 /define:UNITY_EDITOR;UNITY_INCLUDE_TESTS`, Unity 4.7.1-api profile refs + `UnityEngine.CoreModule.dll` + `Unity.Scripting.dll` (UnityEngine subfolder) + TestRunner dlls + custom nunit. Compile runtime with `/out:UnityEssentials.Utilities.dll`, then tests referencing it, so InternalsVisibleTo is exercised for real. Note: `Awaitable` lives in CoreModule — confirm; add modules if csc reports missing types.
2. **Pure NUnit fixtures via reflection runner** (same memory; recreate `run-pure-fixtures.ps1` in scratchpad): executes Timer, EventBus, Log formatting, pure Singleton fixtures outside Unity. MonoBehaviour/ScriptableObject/LogAssert/allocation fixtures are classified environmental and skipped.
3. **Unity batchmode EditMode run** (full suite incl. GameObject + allocation tests): `& "C:\Program Files\Unity\Hub\Editor\6000.5.6f1\Editor\Unity.exe" -batchmode -projectPath ... -runTests -testPlatform EditMode -testResults "Logs\editmode-results.xml" -logFile "Logs\test-run.log"`. **Blocked while the Editor has the project open** (check `Get-Process Unity` / `Temp/UnityLockfile`; never kill it — ask user to close). This run also generates all `.meta` files — confirm they appeared.
4. **Manual play-mode smoke (user, post-ship):** Awaitable loop start/stop/cancellation, `DontDestroyOnLoad` across scene load, real `Application.quitting`, Resources discovery in a consuming project. Listed in README gotchas.

## Key risks tracked

- `[Conditional]` strips argument evaluation in release (side effects in log args vanish) — README note.
- `async void` Awaitable continuations can outlive play mode → exitCancellationToken + generation counter + catch-all log.
- EditMode: `AddComponent` doesn't auto-call `Awake`; `Destroy` illegal → `InvokeAwake` seam on test double + `isPlaying` branches.
- Abandoned running timers are held by the runner until `ResetStatics` — "Stop timers you abandon" README note.
- Perf tests must warm up first (static ctor + JIT alloc on first touch) — copy `_sink`/void-lambda idiom verbatim.

## Files created (summary)

`Docs/Utils-Plan.md` + everything under `Assets/Packages/Utils/` per the tree above (19 .cs files, 2 .asmdef, 1 README).
