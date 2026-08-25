# Haptic System — Implementation Plan

A reusable cross-platform haptics package for `Assets/Packages/Haptics/`, following this repo's library conventions (see `ServiceLocator` and `StateManager`). iOS and Android ship in v1; the architecture is built so future platforms are added as one provider class plus one `#elif` branch.

## Requirements

1. `HapticService` — the public entry point end users call.
2. An internal engine underneath that controls all haptic usage.
3. Platform providers behind an interface, initialized **lazily**: the provider is selected and set up on the first `Play`, and all subsequent usage goes through it.
4. Built-in Light/Medium/Heavy presets, plus user-authored **custom patterns** with arbitrary values.
5. Native integration packaged inside the asset folder so it survives export/import as one self-contained unit.

## Architecture

```
HapticService (public static facade)
   └── HapticManager (internal static engine: lazy init, enable gate, thread assert, reset)
         ├── IHapticProvider (internal interface)
         │     ├── NullHapticProvider     — Editor / Standalone / unsupported (no-op)
         │     ├── AndroidHapticProvider  — android.os.Vibrator / VibrationEffect via JNI reflection
         │     └── IOSHapticProvider      — P/Invoke into a source .mm (UIKit generators + CoreHaptics)
         └── HapticLifecycleDriver (hidden MonoBehaviour, forwards OnApplicationPause)
```

- **Facade/engine split mirrors `ServiceLocator`/`ServiceRegistry`** exactly: thin public static class → `internal static HapticManager` holding all mutable state, reset via `[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]`, main-thread asserts via the `[Conditional("UNITY_EDITOR")]`/`[Conditional("DEVELOPMENT_BUILD")]` pattern. No `Singleton<T>` base class — that idiom doesn't exist in this repo and shouldn't be introduced.
- **Two-stage lazy init.** `EnsureProviderSelected()` is cheap (picks the provider class via `#if`, no engine/JNI startup) and powers `IsSupported`/`SupportsPatterns`. `EnsureFullyInitialized()` (first `Play`, or an explicit opt-in `HapticService.Initialize()` for pre-warming during loading screens) calls `provider.Initialize()` and lazily creates the `HapticLifecycleDriver` GameObject (`HideFlags.HideAndDontSave` + `DontDestroyOnLoad`). The driver is **not** created for `NullHapticProvider`. `ResetStatics()` nulls the driver reference too — Stop-in-Play-mode destroys runtime GameObjects even with Domain Reload disabled.
- **Platform selection**:

```csharp
#if UNITY_IOS && !UNITY_EDITOR
        return new IOSHapticProvider();
#elif UNITY_ANDROID && !UNITY_EDITOR
        return new AndroidHapticProvider();
#else
        return new NullHapticProvider();
#endif
```

The `!UNITY_EDITOR` guards are **mandatory**: `UNITY_IOS`/`UNITY_ANDROID` are defined inside the Editor whenever that platform is the active build target, so without them an EditMode test run would construct a real provider and hit live JNI/native calls. This gets a dedicated regression test.

## Package structure

```
Assets/Packages/Haptics/
├── README.md                               — long-form docs; add-a-platform guide; manual device checklist
├── Runtime/
│   ├── UnityEssentials.Haptics.asmdef      — name/rootNamespace "UnityEssentials.Haptics", same shape as ServiceLocator's asmdef
│   ├── AssemblyInfo.cs                     — [assembly: InternalsVisibleTo("UnityEssentials.Haptics.Tests")] + why-comment
│   ├── HapticService.cs                    — public static: Play(preset), Play(pattern), Stop(), Initialize(), IsEnabled, IsSupported, SupportsPatterns
│   ├── HapticManager.cs                    — internal static engine; ProviderOverride test seam; ResetStatics; AssertMainThread
│   ├── HapticLifecycleDriver.cs            — internal sealed MonoBehaviour; created lazily; forwards OnApplicationPause (OnApplicationFocus deliberately unused — unreliable ordering on mobile)
│   ├── HapticPresetType.cs                 — public enum : byte { Light = 0, Medium = 1, Heavy = 2 }
│   ├── HapticEvent.cs                      — public [Serializable] struct { Time, Intensity, Sharpness, Duration } — seconds; Intensity/Sharpness 0..1; Duration 0 = transient; Sharpness is iOS-only (documented)
│   ├── HapticPattern.cs                    — public sealed ScriptableObject; [CreateAssetMenu("UnityEssentials/Haptics/Haptic Pattern")]; runtime Create(events) factory; OnValidate clamps+sorts (never throws); internal ThrowIfInvalid() throws at dispatch; Events guaranteed sorted by Time
│   ├── HapticException.cs                  — public HapticException base + sealed HapticPatternInvalidException, 3-constructor convention
│   └── Providers/
│       ├── IHapticProvider.cs              — internal: IsSupported, SupportsPatterns, Initialize(), PlayPreset, PlayPattern, Stop(), OnApplicationPause(bool)
│       ├── NullHapticProvider.cs           — all no-op; both capability flags false
│       ├── AndroidHapticProvider.cs        — JNI call sites only; VibratorManager.getDefaultVibrator() on API 31+, VIBRATOR_SERVICE below; caches vibrator handle + SDK_INT at Initialize
│       ├── AndroidWaveformConverter.cs     — PURE static: HapticEvent[] → (long[] timings, int[] amplitudes) for createWaveform; unit-testable
│       ├── IOSHapticProvider.cs            — [DllImport("__Internal")] declarations only; int-not-bool marshaling at the P/Invoke boundary
│       └── IOSPatternFlattener.cs          — PURE static: HapticEvent[] → 4 parallel float[]; unit-testable
├── Plugins/
│   └── iOS/UnityEssentialsHaptics.mm       — extern "C" bridge, auto-compiled by Unity into the Xcode project's UnityFramework target
├── Editor/
│   ├── UnityEssentials.Haptics.Editor.asmdef   — includePlatforms ["Editor"]; NO platform-extension references (see design notes)
│   ├── HapticsIOSBuildPostprocessor.cs     — entire file in #if UNITY_IOS; IPostprocessBuildWithReport; weak-links CoreHaptics.framework
│   └── HapticsAndroidManifestPostprocessor.cs  — entire file in #if UNITY_ANDROID; IPostGenerateGradleAndroidProject; injects VIBRATE permission
├── Samples/
│   ├── UnityEssentials.Haptics.Samples.asmdef  — references the Runtime asmdef
│   └── HapticsTester.cs                    — IMGUI panel (3 presets, a sample pattern, Stop) for on-device verification
└── Tests/
    ├── UnityEssentials.Haptics.Tests.asmdef    — same shape as ServiceLocator.Tests (EditMode, nunit precompiled ref, UNITY_INCLUDE_TESTS)
    ├── TestHapticProvider.cs               — call-counting IHapticProvider double (mirrors the TestServices.cs idiom)
    ├── HapticPatternValidationTests.cs
    ├── AndroidWaveformConverterTests.cs
    ├── IOSPatternFlattenerTests.cs
    ├── NullHapticProviderTests.cs
    ├── HapticManagerPlatformSelectionTests.cs
    ├── HapticManagerLazyInitTests.cs
    └── HapticLifecycleDriverTests.cs
```

## Native integration — how and why

### iOS (the only real "plugin")

No prebuilt binary. Unity automatically compiles any `.mm` under a `Plugins/iOS` folder — including one nested inside the package — into the generated Xcode project (into the **`UnityFramework`** target since Unity 2019.3). Two Apple APIs:

- **`UIImpactFeedbackGenerator`** (UIKit, iOS 10+): backs the three presets. Generators are cached and `prepare()`d.
- **`CoreHaptics` / `CHHapticEngine`** (iOS 13+, gate on `CHHapticEngine.capabilitiesForHardware().supportsHaptics`): backs custom patterns via `CHHapticPattern` with transient/continuous events carrying intensity + sharpness. The engine stops on backgrounding — the `.mm` stops it on pause, restarts it on resume, and installs `resetHandler`/`stoppedHandler`. All CoreHaptics code sits behind `@available(iOS 13.0, *)`.

`CoreHaptics.framework` is a *system* framework — nothing is bundled; `HapticsIOSBuildPostprocessor` weak-links it via `PBXProject.AddFrameworkToProject(frameworkTargetGuid, "CoreHaptics.framework", weak: true)` onto **`GetUnityFrameworkTargetGuid()`** (where the `.mm` compiles — linking only the main app target fails at link time) and, defensively, the main target.

**Degrade rule**: on devices with presets but no CoreHaptics (iOS 10-12, or `supportsHaptics == false`), `PlayPattern` plays a single impact scaled to the pattern's peak intensity — documented, not silent. `SupportsPatterns` exposes the distinction to callers.

C# reaches the `.mm` via `[DllImport("__Internal")]` — a P/Invoke declaration resolved at iOS link time, so the C# side compiles and is verifiable on Windows.

### Android (no plugin at all)

`android.os.Vibrator` / `VibrationEffect` / `VibratorManager` are reached purely via `AndroidJavaClass`/`AndroidJavaObject` (built-in `com.unity.modules.androidjni`, already in the manifest). The project's `AndroidMinSdkVersion` is **26**, so the legacy pre-Oreo `vibrate(long)` fallback is deliberately omitted (noted in a doc comment).

- Presets: `VibrationEffect.createPredefined(EFFECT_TICK / EFFECT_CLICK / EFFECT_HEAVY_CLICK)` on API 29+; `createOneShot(duration, amplitude)` fallback on 26-28 — starting values Light 40 ms/80, Medium 60 ms/150, Heavy 90 ms/255 (on-device feel-tuning expected). Effect ids fetched via `GetStatic<int>` at runtime, not hardcoded; service-name strings (`"vibrator"`, `"vibrator_manager"`) are stable constants and may be literals.
- Custom patterns: `AndroidWaveformConverter` turns the event timeline into `createWaveform(long[] timings, int[] amplitudes, -1)`.
- The **`VIBRATE` permission** is injected at build time by `HapticsAndroidManifestPostprocessor` (`IPostGenerateGradleAndroidProject`) into the generated `unityLibrary` manifest. *Not* shipped as an `AndroidManifest.xml` in the package: a root-level manifest is the custom main-manifest override (a library must never claim it), nested bare manifests are not a documented merge mechanism, and `.androidlib` folders are merge-conflict-prone. Unity's automatic permission detection only recognizes `Handheld.Vibrate()`, never JNI reflection, so explicit injection is required.

### Editor assembly — why it references nothing

`UnityEditor.iOS.Extensions.Xcode` / the Android extension assembly are **not** referenced in the Editor asmdef. On a machine without that platform module installed (this Windows machine has neither, and iOS Build Support cannot be installed on Windows at all), an explicit reference produces a standing "Unable to resolve reference" console error. Instead each postprocessor file wraps its entire body in `#if UNITY_IOS` / `#if UNITY_ANDROID`: it compiles to an empty file unless that platform is the active build target (which implies the module exists). Whether the default auto-reference suffices on a Mac (vs. needing `precompiledReferences`) is on the manual checklist below.

## Key behavioral decisions

- **Intensity → amplitude mapping**: intensity ≤ 0 → amplitude **0** (true silence — clamping to a minimum of 1 would turn intended gaps into a faint buzz); otherwise `Clamp(Round(intensity * 255), 1, 255)`. Devices without `hasAmplitudeControl()` coerce amplitudes to default strength — documented fidelity caveat, no code branch.
- **Transient events** (`Duration == 0`): CoreHaptics transient event on iOS; a fixed `MinimumEventDurationMs` (20 ms) pulse on Android, which has no instantaneous primitive.
- **Overlapping events**: not rejected; Android converter applies documented "last event wins" truncation. Cross-platform overlap behavior is explicitly not well-defined.
- **Backgrounding**: `HapticLifecycleDriver.OnApplicationPause` → provider. Android calls `vibrator.cancel()` (waveforms otherwise continue in background); iOS stops/restarts the engine as above.
- **`IsEnabled`**: global mute gate in `HapticManager`; setting `false` also calls `Stop()`. Deliberately **not persisted** — the library never touches `PlayerPrefs`; consumers persist the toggle.
- **Validation tiers**: `OnValidate` clamps ranges and stable-sorts by `Time` (never throws — Unity convention); internal `ThrowIfInvalid()` throws `HapticPatternInvalidException` for empty/non-finite data, called by the manager before every pattern dispatch (even when disabled — bad input is a bug, not a mute).
- **Null checks on patterns** use Unity's overloaded `== null` so destroyed ScriptableObjects are caught, not just C# nulls.
- **Main-thread asserts** on `Play`, `Stop`, `IsSupported`, `SupportsPatterns` — off-thread JNI/P-Invoke crashes in release builds where Unity's own editor-only guards don't exist.
- **`HapticPattern.Create(...)`** (runtime factory for procedural patterns): the caller owns the instance and must `Destroy()` it.

## Implementation rules

- **C# 9 ceiling**: block-scoped namespaces only, no C# 10+ features (Unity 6 language level; the local Roslyn compile-check enforces the same).
- **Stable sort** for events: `Array.Sort` with a comparer is unstable — use an index tiebreaker so equal-`Time` events keep deterministic order across validations.
- **`.mm` defensive spec**: switch explicitly on `presetId` (no blind casts to `UIImpactFeedbackGeneratorStyle`); `@try/@catch` + `NSLog` everywhere — an ObjC exception must never propagate into Unity.
- **XML documentation density** must match `ServiceLocator`/`StateManager`: `<summary>`, `<remarks>` with invariants and lifecycle rules, `<exception>` tags on every public member.
- **`.meta` sequencing**: files are created via CLI, so the Unity Editor must be opened once to generate `.meta` files and confirm the `.mm` PluginImporter is iOS-only — before any batchmode test run (which requires the Editor closed).

## Tests (EditMode, all runnable on this machine)

| File | Asserts |
|---|---|
| `HapticPatternValidationTests` | `Create(null)` → `ArgumentNullException`; empty/NaN/Infinity → `HapticPatternInvalidException`; clamping of negatives and >1 values; stable sort incl. equal-`Time` determinism |
| `AndroidWaveformConverterTests` | transient → 20 ms minimum; gaps → zero-amplitude segments; **intensity 0 → amplitude 0**; boundary rounding at 0 and 1; overlap = documented last-wins |
| `IOSPatternFlattenerTests` | N events → 4 parallel arrays, order and values preserved |
| `NullHapticProviderTests` | all members no-op; `IsSupported`/`SupportsPatterns` both false |
| `HapticManagerPlatformSelectionTests` | factory returns `NullHapticProvider` under the Editor test runner — the `!UNITY_EDITOR` regression test; returns a fresh instance per call |
| `HapticManagerLazyInitTests` | via `ProviderOverride` + `TestHapticProvider`: `IsSupported` doesn't `Initialize()`; first `Play` initializes exactly once and creates the driver exactly once; second `Play` doesn't; explicit `Initialize()` pre-warms; `IsEnabled = false` gates `Play` and triggers `Stop`; `Stop` before any `Play` is safe; `[TearDown]` calls `ResetStatics()` |
| `HapticLifecycleDriverTests` | `Create()` sets `HideAndDontSave`; `NotifyApplicationPause` forwards to the active provider (manager-level — EditMode has no player loop) |

## Non-goals for v1 (future work, listed in README)

Pattern looping/repeat; runtime intensity modulation of in-flight patterns; gamepad rumble (would be another `IHapticProvider`); `IsEnabled` persistence.

## Execution plan

Implementation is delegated to Opus 5 agents via a Workflow (per the established division of labor: agents implement, the lead reviews/fixes/refactors directly and runs verification).

- **Phase 0 — Contracts** (1 agent): `HapticPresetType`, `HapticEvent`, `HapticException`, `IHapticProvider`, all asmdefs, `AssemblyInfo.cs`. Fixed signatures, no design freedom.
- **Phase 1 — Parallel implementation** (4 agents, disjoint files): A `HapticPattern`; B Android provider + converter + Null; C iOS provider + flattener + `.mm`; D both Editor postprocessors + Samples tester.
- **Phase 2 — Integration** (1 agent): `HapticManager`, `HapticLifecycleDriver`, `HapticService`, README.
- **Phase 3 — Parallel tests** (4 agents): per the table above.
- **Phase 4 — Lead pass** (not delegated): line-by-line review of every generated file with direct fixes/refactors; Roslyn compile-check of all C# (works for the P/Invoke and JNI code — neither needs native SDKs to compile); reflection-run of the pure fixtures; full EditMode batchmode run.

## Verification

**Local (this machine):**
1. Roslyn compile-check against Unity's API profile for every Runtime/Tests file. The Editor postprocessors compiling to empty bodies is the *expected* result here (no iOS/Android modules installed).
2. One-time Editor open: generate `.meta` files, confirm zero console errors (especially the Editor asmdef), confirm the `.mm` importer is iOS-only.
3. EditMode batchmode: `Unity.exe -batchmode -runTests -testPlatform EditMode -testResults Logs\editmode-results.xml` (fails if the Editor has the project open — check `Get-Process Unity` / `Temp/UnityLockfile` first).

**Manual checklist (requires Mac / physical devices — neither the iOS Simulator nor the Android Emulator emulates haptics):**
- [ ] `UnityEssentialsHaptics.mm` compiles in a real iOS build (Xcode).
- [ ] Editor asmdef resolves `UnityEditor.iOS.Xcode` types on a Mac with iOS Build Support without an explicit reference (else add `precompiledReferences`).
- [ ] `CoreHaptics.framework` weak-linked onto the `UnityFramework` target; symbols resolve.
- [ ] Merged Gradle manifest contains the injected `VIBRATE` permission.
- [ ] Preset feel on both platforms (tune the API 26-28 one-shot values).
- [ ] iOS: engine restart after background/foreground; pattern degrade path on a preset-only device.
- [ ] Android: waveform playback on an API 29+ and an API 26-28 device; vibration stops on backgrounding.
