# Haptics

Cross-platform haptic feedback for Unity 6: three built-in impact presets, authored custom
patterns, one static entry point, and no setup. iOS and Android are implemented; every other
platform resolves to a silent no-op so haptic calls are safe to leave in code that runs
everywhere.

- **Assembly:** `UnityEssentials.Haptics` (`Runtime/`) — no package dependencies beyond the
  built-in `androidjni` module Unity already ships.
- **Namespace:** `UnityEssentials.Haptics`
- **Editor assembly:** `UnityEssentials.Haptics.Editor` (build postprocessors, Editor-only)
- **Samples:** `UnityEssentials.Haptics.Samples` — an on-device IMGUI test panel
- **Tests:** `UnityEssentials.Haptics.Tests` (EditMode only)
- **Native:** `Plugins/iOS/UnityEssentialsHaptics.mm`, compiled by Unity into the generated
  Xcode project. Nothing prebuilt, no `.a`, no `.framework` to ship.

```csharp
using UnityEssentials.Haptics;

HapticService.Play(HapticPresetType.Light);   // that is the whole API for most games
```

---

## Design

```
HapticService                  public static facade — the only type you call
   │                           thin: asserts the main thread, forwards, documents the contract
   ▼
HapticManager                  internal static engine — all mutable state lives here
   │                           lazy init, mute gate, validation order, statics reset
   ├── IHapticProvider         internal platform abstraction
   │     ├── NullHapticProvider     Editor / desktop / unsupported — every member a no-op
   │     ├── AndroidHapticProvider  android.os.Vibrator + VibrationEffect via JNI reflection
   │     │     └── AndroidWaveformConverter   pure: HapticEvent[] → (long[] timings, int[] amplitudes)
   │     └── IOSHapticProvider      [DllImport("__Internal")] into UnityEssentialsHaptics.mm
   │           └── IOSPatternFlattener        pure: HapticEvent[] → 4 parallel float[]
   └── HapticLifecycleDriver   hidden MonoBehaviour — forwards OnApplicationPause to the provider
```

Three properties fall out of that shape:

**The facade is thin and the engine holds everything.** `HapticService` has no state at all; it
mirrors `ServiceLocator`/`ServiceRegistry` in this repo. All statics live in `HapticManager` and
are hard-reset on `RuntimeInitializeOnLoadMethod(SubsystemRegistration)`, so entering play mode
with domain reload disabled never inherits a half-initialized backend or a dead driver object.

**Initialization is lazy, in two stages.** Selecting the provider is one allocation and happens on
the first capability query or dispatch. Starting it — acquiring the system vibrator, preparing the
iOS impact generators, creating the lifecycle driver — happens on the first actual `Play`, or
earlier if you call `HapticService.Initialize()` during a loading screen. `IsSupported` and
`SupportsPatterns` deliberately only do the first stage, so a settings screen can ask what the
device can do without waking anything up.

**Timeline translation is pure.** Neither `AndroidWaveformConverter` nor `IOSPatternFlattener`
touches JNI, P/Invoke or `UnityEngine`, so the interesting logic — segment building, amplitude
mapping, overlap resolution — is covered by EditMode tests that run on any machine, and every
native call site is confined to one file per platform.

---

## Installing

Unity's Package Manager -> **+** -> *Install package from git URL*:

`https://github.com/blakethebad/unity-essentials.git?path=/Packages/com.unityessentials.haptics#com.unityessentials.haptics@1.0.0`

That is the whole procedure. The package is self-contained: the runtime code, the iOS bridge
source, the build postprocessors, the sample and the tests all live inside it, and nothing
references anything outside it.

Then, per platform:

- **iOS** — nothing to do. Unity compiles `Plugins/iOS/UnityEssentialsHaptics.mm` into the
  generated Xcode project's `UnityFramework` target automatically, and
  `HapticsIOSBuildPostprocessor` weak-links `CoreHaptics.framework` after every build.
- **Android** — nothing to do. `HapticsAndroidManifestPostprocessor` injects
  `android.permission.VIBRATE` into the generated Gradle manifest after every build. It has to:
  Unity's automatic permission detection only recognizes `Handheld.Vibrate()` and cannot see the
  JNI reflection this package uses, so without the injection every vibration is silently dropped
  at runtime.
- **Everything else** — nothing to do, and nothing happens. The no-op provider is selected and all
  calls return immediately.

Delete `Samples/` and `Tests/` if you do not want them; nothing in `Runtime/` or `Editor/` depends
on either.

---

## Quick start

### Presets

Three impact strengths, available on every supported device. This is what the majority of a game's
haptics should be.

```csharp
using UnityEssentials.Haptics;

HapticService.Play(HapticPresetType.Light);    // selection change, tick, scrub
HapticService.Play(HapticPresetType.Medium);   // button press, confirmation
HapticService.Play(HapticPresetType.Heavy);    // significant or destructive event
```

No initialization, no component, no asset. The first call brings the backend up.

### The mute toggle

```csharp
HapticService.IsEnabled = false;   // silences everything, and stops what is playing
```

Drive this from your options menu. It is **not persisted** — this package never touches
`PlayerPrefs` — so save it with the rest of your settings and restore it on startup.

Hide the toggle when the device cannot do anything with it:

```csharp
vibrationToggleRow.SetActive(HapticService.IsSupported);
```

### Authored patterns

Create the asset through **Assets ▸ Create ▸ UnityEssentials ▸ Haptics ▸ Haptic Pattern**, fill in
the events list in the inspector, and play it:

```csharp
[SerializeField] private HapticPattern explosionPattern;

private void OnExplosion() => HapticService.Play(explosionPattern);
```

Each event has four fields:

| Field | Meaning |
|---|---|
| `Time` | Seconds from the **start of the pattern** — absolute, not a delay since the previous event. |
| `Intensity` | Strength, 0 to 1. `0` is true silence, not a faint buzz. |
| `Sharpness` | Crispness, 0 (dull) to 1 (clicky). **iOS only** — Android ignores it entirely. |
| `Duration` | Length in seconds. `0` means transient: an instantaneous tap. |

Values are repaired as you type: negative times and durations become 0, intensity and sharpness are
clamped into 0 to 1, and events are stably sorted by time (events sharing a time keep the order you
authored them in). Inspector editing never throws.

### Runtime patterns

For procedural haptics with no authored asset behind them:

```csharp
var pattern = HapticPattern.Create(new[]
{
    new HapticEvent(0.00f, 1.00f, 1.0f, 0.00f),   // transient tap
    new HapticEvent(0.10f, 0.40f, 0.3f, 0.15f),   // 150 ms continuous rumble
    new HapticEvent(0.30f, 1.00f, 1.0f, 0.00f)    // transient tap
});

HapticService.Play(pattern);
```

**You own the returned instance.** A `HapticPattern` is a `ScriptableObject`, so dropping the last
reference does not free it — it leaks until the domain reloads. Destroy it when you are finished:

```csharp
private void OnDestroy()
{
    if (pattern != null)
    {
        Destroy(pattern);           // DestroyImmediate outside play mode
    }
}
```

Better still: build the patterns a system needs once, keep them in fields and reuse them. Playing
the same pattern repeatedly allocates nothing.

`Create` copies the array it is given, so you may reuse your own buffer immediately afterwards.

---

## API reference

Everything public in the package.

### `HapticService`

| Member | Behaviour |
|---|---|
| `static bool IsEnabled { get; set; }` | Global mute gate, `true` by default. Setting it `false` also stops playback. Not persisted. Never initializes the backend. |
| `static bool IsSupported { get; }` | Whether the device can play anything at all. Selects the provider; never starts an engine. |
| `static bool SupportsPatterns { get; }` | Whether a custom pattern plays in full rather than degrading to one impact. Always `false` when `IsSupported` is `false`. Same cost as `IsSupported`. |
| `static void Initialize()` | Optional pre-warm for a loading screen. Idempotent. Does everything the first `Play` would do. |
| `static void Play(HapticPresetType preset)` | Plays a built-in impact. No-op while muted or unsupported. Cannot be cancelled. |
| `static void Play(HapticPattern pattern)` | Plays a timeline, replacing anything playing. Validates the pattern **even while muted**. |
| `static void Stop()` | Cancels the running pattern. Safe before any `Play`, and does not initialize anything. Does not affect presets. |

### `HapticPresetType : byte`

`Light = 0`, `Medium = 1`, `Heavy = 2`. The numeric values are a wire contract with the iOS bridge —
append only, and only alongside a matching change to the `.mm` switch.

### `HapticEvent` (`[Serializable] struct`)

```csharp
public float Time;        // seconds from the start of the pattern
public float Intensity;   // 0 to 1
public float Sharpness;   // 0 to 1, iOS only
public float Duration;    // seconds; 0 = transient

public HapticEvent(float time, float intensity, float sharpness = 0f, float duration = 0f);
```

Public fields rather than properties because Unity's serializer and property drawers only see
fields. Nothing is validated or clamped by the struct itself; `HapticPattern` owns both.

### `HapticPattern : ScriptableObject`

```csharp
[CreateAssetMenu(menuName = "UnityEssentials/Haptics/Haptic Pattern")]

public static HapticPattern Create(HapticEvent[] events);
```

`Create` copies, normalizes and validates its input before allocating the instance, so a rejected
call leaves no orphaned Unity object behind. The caller owns the result and must destroy it.

### Exceptions

| Type | When |
|---|---|
| `HapticException` | Base type for the package. Also thrown directly by the main-thread assertion. |
| `HapticPatternInvalidException` | A dispatched or `Create`d pattern has no events, or carries NaN or infinity in any field. |
| `ArgumentNullException` | `Play(pattern)` with a null **or destroyed** pattern; `HapticPattern.Create(null)`. |

Unsupported hardware **never** throws. It is a normal condition reported through `IsSupported` and
`SupportsPatterns`.

---

## Platform behaviour

|  | Editor / Desktop | iOS 13+, CoreHaptics hardware | iOS, preset-only | Android 29+ | Android 26–28 |
|---|---|---|---|---|---|
| `IsSupported` | `false` | `true` | `true` | `hasVibrator()` | `hasVibrator()` |
| `SupportsPatterns` | `false` | `true` | `false` | same as `IsSupported` | same as `IsSupported` |
| Presets | no-op | `UIImpactFeedbackGenerator` (`.light` / `.medium` / `.heavy`) | same | `VibrationEffect.createPredefined` (`EFFECT_TICK` / `EFFECT_CLICK` / `EFFECT_HEAVY_CLICK`) | `createOneShot` — 40 ms @ 80, 60 ms @ 150, 90 ms @ 255 |
| Patterns | no-op | `CHHapticPattern`, transient + continuous events | **degrades** to one impact scaled to peak intensity | `VibrationEffect.createWaveform(timings, amplitudes, -1)` | same |
| `Sharpness` | — | rendered | ignored (impact has no sharpness) | ignored | ignored |
| `Stop()` | no-op | stops the pattern player | nothing to stop | `Vibrator.cancel()` | `Vibrator.cancel()` |
| On background | — | engine stopped, restarted on resume | — | `Vibrator.cancel()` | `Vibrator.cancel()` |

"Preset-only iOS" means a device on iOS 10–12, where CoreHaptics does not exist and presets are
assumed available — UIKit offers no capability query before iOS 13. On iOS 13 and later the two
flags collapse into `CHHapticEngine.capabilitiesForHardware().supportsHaptics`: hardware without a
Taptic Engine (an iPad, for instance) reports `IsSupported` false and plays nothing, rather than
landing in this column.

### The iOS degrade rule

On a device that plays presets but has no pattern engine, `Play(pattern)` does **not** go silent
and does **not** throw. It plays a single `UIImpactFeedbackGenerator` impact scaled to the
pattern's peak intensity: on iOS 13+ through the impact's intensity parameter, and below that by
bucketing the peak into the light, medium or heavy style. The same fallback catches a CoreHaptics
engine that refuses a pattern at runtime. `SupportsPatterns` is what exposes the distinction to
callers who need to adapt.

### Amplitude fidelity on Android

Intensity maps to amplitude as: `intensity <= 0` → **0** (true silence), otherwise
`clamp(round(intensity * 255), 1, 255)`. Zero maps to zero deliberately — clamping it up to the
minimum audible amplitude of 1 would turn every intended gap in a pattern into a faint continuous
buzz. Any positive intensity, however small, maps to at least 1, so a barely-there event is never
silently deleted.

Devices whose motor reports no `hasAmplitudeControl()` coerce **every** non-zero amplitude to their
default strength. The waveform's timing still plays correctly; only its dynamics are lost. This is
a documented caveat, not a case the package branches on.

### Transients on Android

Android has no instantaneous vibration primitive, so an event with `Duration == 0` becomes a
**20 ms** pulse. Shorter than that and the motor does not visibly move; longer and it stops reading
as a tap. On iOS the same event is a genuine CoreHaptics transient.

### Overlapping events

Legal, but the combined feel is **explicitly not defined across platforms**. Android flattens the
timeline into a single-channel waveform, where two events cannot sound at once: the converter
applies documented **last-event-wins** truncation — an event whose segment would run into the next
event's start is cut short there, and an event truncated to nothing (two events sharing a time, for
example) is dropped. iOS hands every event to CoreHaptics, which mixes them.

Author patterns without overlap when they have to feel the same on both platforms.

### Backgrounding

`HapticLifecycleDriver` — a hidden `HideAndDontSave` GameObject created on first initialization,
never for the no-op provider — forwards `OnApplicationPause` to the active provider. Android
cancels the vibrator, because a waveform otherwise keeps buzzing in the user's pocket after the app
loses focus. iOS stops the CoreHaptics engine and restarts it on resume, because the system tears
it down while the app is suspended.

Nothing is resumed on Android: the pattern's moment has passed, and silently replaying it would be
worse than dropping it. `OnApplicationFocus` is deliberately not handled — its ordering relative to
`OnApplicationPause` on mobile is not reliable, and handling both would deliver the same transition
twice.

### `IsEnabled` is not persisted

By design. A library that quietly claims a `PlayerPrefs` key is a library that fights with the
consuming project's settings system. Persist the flag yourself; a fresh session always starts
enabled.

### Main thread only

There are no locks anywhere, and the JNI and P/Invoke calls underneath are not thread-safe. A cheap
main-thread assertion runs on `Play`, `Stop`, `Initialize`, `IsSupported`, `SupportsPatterns` and
the `IsEnabled` setter (which can forward a native `Stop`) in the Editor and in development builds,
and is **compiled out of release builds** — so an off-thread
call that merely throws during development becomes a crash in a shipped player. Do not call this
API from jobs, threads, or async continuations that may resume off the main thread.

### The Editor is always silent

`UNITY_IOS` and `UNITY_ANDROID` are defined inside the Editor whenever that platform is the active
build target, so the platform selection additionally requires `!UNITY_EDITOR`. Pressing Play with
Android selected therefore uses the no-op provider rather than reaching for JNI that would not
resolve. Both capability flags report `false` in the Editor and every button in the sample panel
does nothing there. That is correct behaviour, not a failure — haptics can only be verified on
hardware.

---

## Adding a platform

The abstraction is deliberately narrow so that a new backend is a contained change.

**1. Implement `IHapticProvider`.** Put the class in `Runtime/Providers/`, wrap the whole file in
the platform's `#if` if it uses types that only exist on that platform, and follow the three rules
every provider is held to:

- `IsSupported` and `SupportsPatterns` must be answerable **before** `Initialize()` and must not
  start an engine, allocate generators or spin up threads. Everything expensive goes in
  `Initialize()`.
- No exception may escape any member. Wrap every native boundary, log the failure once with a
  `[Haptics]` prefix, and degrade to a no-op — a haptic is cosmetic and must never take down the
  frame that requested it.
- The `HapticEvent[]` handed to `PlayPattern` belongs to the pattern asset. Read it; never sort,
  clamp or write into it. The manager guarantees it is non-null, non-empty, finite, clamped and
  stably sorted by time, so revalidate nothing.

If the timeline needs reshaping for the platform's API, put that in a **pure static** converter
class alongside the provider — no `UnityEngine` types, no `#if` guards — the way
`AndroidWaveformConverter` and `IOSPatternFlattener` do. That is what makes it testable in EditMode
on any machine.

**2. Add one branch to `HapticManager.CreateProvider`:**

```csharp
#if UNITY_IOS && !UNITY_EDITOR
        return new IOSHapticProvider();
#elif UNITY_ANDROID && !UNITY_EDITOR
        return new AndroidHapticProvider();
#elif UNITY_YOURPLATFORM && !UNITY_EDITOR      // ← the new branch
        return new YourHapticProvider();
#else
        return new NullHapticProvider();
#endif
```

The `!UNITY_EDITOR` guard is mandatory on every branch, for the reason above.

**3. Optionally add a build postprocessor** in `Editor/`, if the platform needs a permission, a
framework link or a manifest entry. Wrap the **entire file** in that platform's `#if`, as
`HapticsIOSBuildPostprocessor` and `HapticsAndroidManifestPostprocessor` do, and do **not** add a
reference to the platform extension assembly in the Editor asmdef: an explicit reference raises a
standing "Unable to resolve reference" error on any machine without that build-support module
installed. The `#if` guard reduces the file to nothing there instead.

Nothing else changes. `HapticService`, the manager, the pattern types and the tests are all
platform-agnostic.

---

## Verifying on device

Haptics cannot be verified anywhere but on real hardware — neither the iOS Simulator nor the
Android Emulator renders vibration, and the Editor deliberately selects the no-op provider. Drop
`Samples/HapticsTester.cs` on a GameObject in any scene and build; it needs no other component,
asset or scene setup.

Manual checklist:

- [ ] `UnityEssentialsHaptics.mm` compiles in a real iOS build (Xcode).
- [ ] The Editor asmdef resolves `UnityEditor.iOS.Xcode` types on a Mac with iOS Build Support
      without an explicit reference (if not, add `precompiledReferences`).
- [ ] `CoreHaptics.framework` is weak-linked onto the `UnityFramework` target and its symbols
      resolve.
- [ ] The merged Gradle manifest contains the injected `VIBRATE` permission.
- [ ] Preset feel on both platforms — tune the API 26–28 one-shot duration/amplitude values against
      real hardware.
- [ ] iOS: the engine restarts after a background/foreground trip, and the degrade path plays a
      scaled impact on a preset-only device.
- [ ] Android: waveform playback on an API 29+ **and** an API 26–28 device; vibration stops when the
      app is backgrounded.

---

## Not in v1

Deliberate omissions, each of which would be a contained addition later:

- **Pattern looping / repeat.** `createWaveform` is issued with a repeat index of `-1` (play once);
  looping would need a matching CoreHaptics story and a stop contract that outlives one dispatch.
- **Runtime intensity modulation of an in-flight pattern.** CoreHaptics has dynamic parameters for
  it, Android's waveform API has nothing equivalent, so the feature would be iOS-only in practice.
- **Gamepad rumble.** A different device class entirely; it would arrive as another
  `IHapticProvider` plus a routing decision the current API does not express.
- **`IsEnabled` persistence.** See above — the consuming project owns its settings storage.

Also deliberately absent: the legacy pre-Oreo Android `vibrate(long)` path. The project's minimum
SDK is 26, where `VibrationEffect`, `createOneShot` and `createWaveform` all exist.
