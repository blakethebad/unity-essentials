// UnityEssentialsHaptics.mm
//
// Native half of the iOS haptic provider for the UnityEssentials Haptics package. Unity compiles any
// .mm found under a Plugins/iOS folder — including one nested inside a package, as here — into the
// generated Xcode project's UnityFramework target, so this file needs no prebuilt binary and no
// manual Xcode step. The managed half is IOSHapticProvider.cs, which reaches these symbols through
// [DllImport("__Internal")].
//
// The C ABI is fixed and mirrored exactly on the managed side. Booleans cross the boundary as int
// (1 or 0) rather than as BOOL, because BOOL's width is a portability trap:
//
//     int  UnityEssentialsHaptics_IsSupported(void);
//     int  UnityEssentialsHaptics_SupportsPatterns(void);
//     void UnityEssentialsHaptics_Initialize(void);
//     void UnityEssentialsHaptics_PlayPreset(int presetId);            // 0 Light, 1 Medium, 2 Heavy
//     void UnityEssentialsHaptics_PlayPattern(const float* times, const float* intensities,
//                                             const float* sharpnesses, const float* durations,
//                                             int count);
//     void UnityEssentialsHaptics_Stop(void);
//     void UnityEssentialsHaptics_OnApplicationPause(int paused);
//
// Two Apple APIs are used. UIImpactFeedbackGenerator (UIKit, iOS 10+) backs the three presets: one
// generator is cached per style and prepare()d after creation and after every impact so the Taptic
// Engine stays warm. CHHapticEngine (CoreHaptics, iOS 13+) backs custom patterns; it is created
// lazily on the first pattern, not at initialization, because an app that only plays presets should
// never pay for it.
//
// DEGRADE RULE — deliberate and documented, never silent. When patterns cannot be rendered, either
// because the OS predates iOS 13 or because CHHapticEngine.capabilitiesForHardware().supportsHaptics
// is false (iPads, older iPhones), UnityEssentialsHaptics_PlayPattern does not go quiet: it plays a
// single impact scaled to the PEAK intensity of the pattern. On iOS 13 and later that scaling is
// exact, through impactOccurredWithIntensity:; below iOS 13 there is no intensity parameter, so the
// peak is bucketed into the Light, Medium or Heavy style. The same fallback covers a CoreHaptics
// failure at play time. Callers who need to know the difference read SupportsPatterns.
//
// EXCEPTIONS. An Objective-C exception unwinding into Unity's managed runtime is undefined behaviour,
// so every entry point wraps its whole body in @try/@catch and logs with an "[UnityEssentialsHaptics]"
// prefix. A haptic is cosmetic: nothing in this file may ever take down the frame that asked for it.
//
// AVAILABILITY. CoreHaptics.framework is weak-linked by HapticsIOSBuildPostprocessor, so on an OS
// older than iOS 13 the CHHapticEngine class is simply absent at runtime. Every use of it therefore
// sits behind @available(iOS 13.0, *) — an unguarded message to a weak-linked class is a crash, not a
// no-op.
//
// MEMORY. Unity's generated Xcode project does not enable ARC for UnityFramework by default, and a
// consumer may switch it on per file, so this file compiles correctly under both models: explicit
// retain/release is illegal under ARC and its absence leaks under manual retain/release, hence the
// UEH_RETAIN/UEH_RELEASE macros below. Internal statics and helpers use the short UEH_ prefix; only
// the seven exported entry points use the full UnityEssentialsHaptics_ name.

#import <UIKit/UIKit.h>
#import <CoreHaptics/CoreHaptics.h>

#pragma mark - Compatibility macros

#if __has_feature(objc_arc)
    #define UEH_RETAIN(object) (object)
    #define UEH_RELEASE(object) do { } while (0)
#else
    #define UEH_RETAIN(object) [(object) retain]
    #define UEH_RELEASE(object) [(object) release]
#endif

#define UEH_LOG(format, ...) NSLog(@"[UnityEssentialsHaptics] " format, ##__VA_ARGS__)

#pragma mark - Exported C ABI

extern "C"
{
    int  UnityEssentialsHaptics_IsSupported(void);
    int  UnityEssentialsHaptics_SupportsPatterns(void);
    void UnityEssentialsHaptics_Initialize(void);
    void UnityEssentialsHaptics_PlayPreset(int presetId);
    void UnityEssentialsHaptics_PlayPattern(const float* times,
                                            const float* intensities,
                                            const float* sharpnesses,
                                            const float* durations,
                                            int count);
    void UnityEssentialsHaptics_Stop(void);
    void UnityEssentialsHaptics_OnApplicationPause(int paused);
}

#pragma mark - Preset identifiers

// These must stay in lock step with the managed HapticPresetType enum, whose numeric values are a
// wire contract with this file.
static const int kUEHPresetLight  = 0;
static const int kUEHPresetMedium = 1;
static const int kUEHPresetHeavy  = 2;

// Thresholds used only by the pre-iOS-13 degrade path, where an impact has no intensity parameter and
// the pattern's peak intensity can only be expressed by choosing a style.
static const float kUEHLightIntensityCeiling  = 0.34f;
static const float kUEHMediumIntensityCeiling = 0.67f;

#pragma mark - State

// Cached generators, one per style. They are created on first use and kept for the lifetime of the
// process: creating one per impact is exactly the mistake that makes iOS haptics feel late.
static UIImpactFeedbackGenerator* gLightGenerator  = nil;
static UIImpactFeedbackGenerator* gMediumGenerator = nil;
static UIImpactFeedbackGenerator* gHeavyGenerator  = nil;

// The pattern engine and the player of the pattern currently running, if any. Both are declared as
// available from iOS 13 so that merely naming the types on an older deployment target is legal.
API_AVAILABLE(ios(13.0))
static CHHapticEngine* gEngine = nil;

API_AVAILABLE(ios(13.0))
static id<CHHapticPatternPlayer> gPlayer = nil;

// Whether we believe the engine is started. CoreHaptics stops the engine behind our back (audio
// session interruptions, backgrounding), which is what stoppedHandler and resetHandler correct.
static BOOL gEngineStarted = NO;

// Pause bookkeeping. gApplicationPaused makes the transition idempotent, because OnApplicationPause
// may legitimately arrive repeatedly with the same value, and gEngineStartedBeforePause records
// whether resuming should bring the engine back up.
static BOOL gApplicationPaused = NO;
static BOOL gEngineStartedBeforePause = NO;

#pragma mark - Helpers

/// Clamps a value from the managed side into the 0 to 1 range CoreHaptics parameters require, mapping
/// NaN to 0. The managed layer already clamps, but a CoreHaptics parameter built from an out-of-range
/// value raises, and raising here would mean logging a failure instead of playing a haptic.
static float UEH_Clamp01(float value)
{
    if (value != value)
    {
        return 0.0f;
    }

    if (value < 0.0f)
    {
        return 0.0f;
    }

    if (value > 1.0f)
    {
        return 1.0f;
    }

    return value;
}

/// Whether the hardware can render CoreHaptics patterns. False on every OS older than iOS 13, and on
/// iOS 13 and later for devices without a Taptic Engine.
static BOOL UEH_HardwareSupportsHaptics(void)
{
    if (@available(iOS 13.0, *))
    {
        id<CHHapticDeviceCapability> capabilities = [CHHapticEngine capabilitiesForHardware];
        return (capabilities != nil) && capabilities.supportsHaptics;
    }

    return NO;
}

/// Returns the cached generator for one of the three styles this package uses, creating and preparing
/// it on first request. Returns nil for any other style — the caller decides what that means rather
/// than getting a silently mismatched generator.
static UIImpactFeedbackGenerator* UEH_GeneratorForStyle(UIImpactFeedbackStyle style)
{
    switch (style)
    {
        case UIImpactFeedbackStyleLight:
            if (gLightGenerator == nil)
            {
                gLightGenerator = [[UIImpactFeedbackGenerator alloc] initWithStyle:UIImpactFeedbackStyleLight];
                [gLightGenerator prepare];
            }
            return gLightGenerator;

        case UIImpactFeedbackStyleMedium:
            if (gMediumGenerator == nil)
            {
                gMediumGenerator = [[UIImpactFeedbackGenerator alloc] initWithStyle:UIImpactFeedbackStyleMedium];
                [gMediumGenerator prepare];
            }
            return gMediumGenerator;

        case UIImpactFeedbackStyleHeavy:
            if (gHeavyGenerator == nil)
            {
                gHeavyGenerator = [[UIImpactFeedbackGenerator alloc] initWithStyle:UIImpactFeedbackStyleHeavy];
                [gHeavyGenerator prepare];
            }
            return gHeavyGenerator;

        default:
            UEH_LOG(@"No cached generator exists for impact style %ld.", (long)style);
            return nil;
    }
}

/// Fires one impact and immediately re-prepares the generator, so the next impact in a burst is as
/// prompt as the first.
static void UEH_PlayImpactWithStyle(UIImpactFeedbackStyle style)
{
    UIImpactFeedbackGenerator* generator = UEH_GeneratorForStyle(style);
    if (generator == nil)
    {
        return;
    }

    [generator impactOccurred];
    [generator prepare];
}

/// Plays the single impact that stands in for a pattern this device cannot render, scaled to the
/// pattern's peak intensity. See the degrade rule in the file header.
static void UEH_PlayScaledImpact(float intensity)
{
    float scaled = UEH_Clamp01(intensity);

    if (@available(iOS 13.0, *))
    {
        // iOS 13 added an intensity parameter to impacts, so the peak translates exactly. Medium is
        // the neutral style to scale from.
        UIImpactFeedbackGenerator* generator = UEH_GeneratorForStyle(UIImpactFeedbackStyleMedium);
        if (generator == nil)
        {
            return;
        }

        [generator impactOccurredWithIntensity:scaled];
        [generator prepare];
        return;
    }

    // iOS 10 to 12: intensity can only be approximated by picking a style.
    if (scaled < kUEHLightIntensityCeiling)
    {
        UEH_PlayImpactWithStyle(UIImpactFeedbackStyleLight);
    }
    else if (scaled < kUEHMediumIntensityCeiling)
    {
        UEH_PlayImpactWithStyle(UIImpactFeedbackStyleMedium);
    }
    else
    {
        UEH_PlayImpactWithStyle(UIImpactFeedbackStyleHeavy);
    }
}

/// Stops and forgets the running pattern player, if any. A player is single-use in practice and is
/// always invalid after an engine reset, so it is discarded rather than reused.
API_AVAILABLE(ios(13.0))
static void UEH_DiscardPlayer(void)
{
    if (gPlayer == nil)
    {
        return;
    }

    NSError* error = nil;
    if (![gPlayer stopAtTime:CHHapticTimeImmediate error:&error])
    {
        UEH_LOG(@"Pattern player failed to stop: %@", error);
    }

    UEH_RELEASE(gPlayer);
    gPlayer = nil;
}

/// Returns the engine, creating it on first use, or nil when the hardware cannot render patterns or
/// creation failed. The reset and stopped handlers are installed here, once per engine.
API_AVAILABLE(ios(13.0))
static CHHapticEngine* UEH_EnsureEngine(void)
{
    if (gEngine != nil)
    {
        return gEngine;
    }

    if (!UEH_HardwareSupportsHaptics())
    {
        return nil;
    }

    NSError* error = nil;
    CHHapticEngine* engine = [[CHHapticEngine alloc] initAndReturnError:&error];
    if (engine == nil)
    {
        UEH_LOG(@"Haptic engine could not be created: %@", error);
        return nil;
    }

    // This package never plays audio through CoreHaptics, and saying so keeps the engine out of the
    // app's audio session.
    engine.playsHapticsOnly = YES;

    // The handlers intentionally reference the globals rather than capturing `engine`: capturing it
    // in a block the engine itself retains would be a retain cycle under ARC.
    engine.resetHandler = ^{
        if (@available(iOS 13.0, *))
        {
            @try
            {
                UEH_LOG(@"Haptic engine was reset by the system; restarting it.");

                // Players do not survive a reset.
                UEH_RELEASE(gPlayer);
                gPlayer = nil;
                gEngineStarted = NO;

                if (gApplicationPaused)
                {
                    // Restarting while suspended would only be undone by the system; the resume path
                    // brings the engine back instead.
                    return;
                }

                NSError* restartError = nil;
                if ([gEngine startAndReturnError:&restartError])
                {
                    gEngineStarted = YES;
                }
                else
                {
                    UEH_LOG(@"Haptic engine failed to restart after a reset: %@", restartError);
                }
            }
            @catch (NSException* exception)
            {
                UEH_LOG(@"Haptic engine reset handler failed: %@", exception);
            }
        }
    };

    engine.stoppedHandler = ^(CHHapticEngineStoppedReason reason) {
        // Apple's guidance is not to restart from here — the next play restarts the engine on demand.
        UEH_LOG(@"Haptic engine stopped (reason %ld).", (long)reason);
        gEngineStarted = NO;
    };

    gEngine = engine;
    return gEngine;
}

/// Starts the engine if it is not already running. Returns whether the engine is usable afterwards.
API_AVAILABLE(ios(13.0))
static BOOL UEH_StartEngine(void)
{
    CHHapticEngine* engine = UEH_EnsureEngine();
    if (engine == nil)
    {
        return NO;
    }

    if (gEngineStarted)
    {
        return YES;
    }

    NSError* error = nil;
    if (![engine startAndReturnError:&error])
    {
        UEH_LOG(@"Haptic engine failed to start: %@", error);
        return NO;
    }

    gEngineStarted = YES;
    return YES;
}

/// Stops the engine, releasing the Taptic Engine while the app is backgrounded.
API_AVAILABLE(ios(13.0))
static void UEH_StopEngine(void)
{
    if (gEngine == nil || !gEngineStarted)
    {
        gEngineStarted = NO;
        return;
    }

    gEngineStarted = NO;
    [gEngine stopWithCompletionHandler:^(NSError* error) {
        if (error != nil)
        {
            UEH_LOG(@"Haptic engine reported an error while stopping: %@", error);
        }
    }];
}

/// Builds a CHHapticPattern from the four parallel arrays. Events with a duration of 0 become
/// transient events and everything else becomes a continuous event of that length; both carry the
/// intensity and sharpness parameters at their relative time. Returns nil on failure, having logged
/// why. Ownership follows the alloc/init convention: the caller releases the returned pattern.
API_AVAILABLE(ios(13.0))
static CHHapticPattern* UEH_BuildPattern(const float* times,
                                         const float* intensities,
                                         const float* sharpnesses,
                                         const float* durations,
                                         int count)
{
    NSMutableArray<CHHapticEvent*>* events = [[NSMutableArray alloc] initWithCapacity:(NSUInteger)count];

    for (int i = 0; i < count; i++)
    {
        float intensity = UEH_Clamp01(intensities[i]);
        float sharpness = UEH_Clamp01(sharpnesses[i]);

        // A negative relative time or duration would raise inside CoreHaptics.
        NSTimeInterval relativeTime = (times[i] > 0.0f) ? (NSTimeInterval)times[i] : 0.0;
        NSTimeInterval duration = (durations[i] > 0.0f) ? (NSTimeInterval)durations[i] : 0.0;

        CHHapticEventParameter* intensityParameter =
            [[CHHapticEventParameter alloc] initWithParameterID:CHHapticEventParameterIDHapticIntensity
                                                          value:intensity];
        CHHapticEventParameter* sharpnessParameter =
            [[CHHapticEventParameter alloc] initWithParameterID:CHHapticEventParameterIDHapticSharpness
                                                          value:sharpness];
        NSArray<CHHapticEventParameter*>* parameters = @[ intensityParameter, sharpnessParameter ];

        CHHapticEvent* event = nil;
        if (duration <= 0.0)
        {
            event = [[CHHapticEvent alloc] initWithEventType:CHHapticEventTypeHapticTransient
                                                  parameters:parameters
                                                relativeTime:relativeTime];
        }
        else
        {
            event = [[CHHapticEvent alloc] initWithEventType:CHHapticEventTypeHapticContinuous
                                                  parameters:parameters
                                                relativeTime:relativeTime
                                                    duration:duration];
        }

        if (event != nil)
        {
            [events addObject:event];
            UEH_RELEASE(event);
        }

        UEH_RELEASE(intensityParameter);
        UEH_RELEASE(sharpnessParameter);
    }

    if (events.count == 0)
    {
        UEH_LOG(@"Pattern contained no playable events.");
        UEH_RELEASE(events);
        return nil;
    }

    NSError* error = nil;
    CHHapticPattern* pattern = [[CHHapticPattern alloc] initWithEvents:events parameters:@[] error:&error];
    UEH_RELEASE(events);

    if (pattern == nil)
    {
        UEH_LOG(@"Pattern could not be built: %@", error);
    }

    return pattern;
}

/// Plays the pattern through CoreHaptics, replacing whatever was playing. Returns NO if anything went
/// wrong, which is the caller's cue to fall back to the scaled impact.
API_AVAILABLE(ios(13.0))
static BOOL UEH_PlayPatternWithEngine(const float* times,
                                      const float* intensities,
                                      const float* sharpnesses,
                                      const float* durations,
                                      int count)
{
    if (!UEH_StartEngine())
    {
        return NO;
    }

    CHHapticPattern* pattern = UEH_BuildPattern(times, intensities, sharpnesses, durations, count);
    if (pattern == nil)
    {
        return NO;
    }

    NSError* error = nil;
    id<CHHapticPatternPlayer> player = [gEngine createPlayerWithPattern:pattern error:&error];
    UEH_RELEASE(pattern);

    if (player == nil)
    {
        UEH_LOG(@"Pattern player could not be created: %@", error);
        return NO;
    }

    // A new pattern replaces the running one.
    UEH_DiscardPlayer();

    // The player is autoreleased, and playback outlives this call, so it is retained until it is
    // replaced, stopped or invalidated by an engine reset.
    gPlayer = UEH_RETAIN(player);

    if (![gPlayer startAtTime:CHHapticTimeImmediate error:&error])
    {
        UEH_LOG(@"Pattern failed to start: %@", error);
        UEH_DiscardPlayer();
        return NO;
    }

    return YES;
}

#pragma mark - Entry points

/// Whether the device can play anything at all. On iOS 13 and later this is the CoreHaptics hardware
/// capability; below that it is true, because UIImpactFeedbackGenerator has been available since
/// iOS 10 and offers no capability query.
extern "C" int UnityEssentialsHaptics_IsSupported(void)
{
    @try
    {
        if (@available(iOS 13.0, *))
        {
            return UEH_HardwareSupportsHaptics() ? 1 : 0;
        }

        return 1;
    }
    @catch (NSException* exception)
    {
        UEH_LOG(@"IsSupported failed: %@", exception);
    }

    return 0;
}

/// Whether the device can render a full custom pattern, which needs both iOS 13 and CoreHaptics
/// hardware support. When this returns 0, PlayPattern degrades to a scaled impact.
extern "C" int UnityEssentialsHaptics_SupportsPatterns(void)
{
    @try
    {
        return UEH_HardwareSupportsHaptics() ? 1 : 0;
    }
    @catch (NSException* exception)
    {
        UEH_LOG(@"SupportsPatterns failed: %@", exception);
    }

    return 0;
}

/// Pre-warms the three impact generators. The CoreHaptics engine is deliberately left uncreated: it
/// is the expensive resource, and an app that only plays presets never needs it.
extern "C" void UnityEssentialsHaptics_Initialize(void)
{
    @try
    {
        UEH_GeneratorForStyle(UIImpactFeedbackStyleLight);
        UEH_GeneratorForStyle(UIImpactFeedbackStyleMedium);
        UEH_GeneratorForStyle(UIImpactFeedbackStyleHeavy);
    }
    @catch (NSException* exception)
    {
        UEH_LOG(@"Initialize failed: %@", exception);
    }
}

/// Plays one of the three built-in impacts. The switch is explicit on purpose: an unrecognized id is
/// logged and ignored rather than cast into UIImpactFeedbackStyle, where it would select an
/// arbitrary style or a style that does not exist on this OS.
extern "C" void UnityEssentialsHaptics_PlayPreset(int presetId)
{
    @try
    {
        switch (presetId)
        {
            case kUEHPresetLight:
                UEH_PlayImpactWithStyle(UIImpactFeedbackStyleLight);
                break;

            case kUEHPresetMedium:
                UEH_PlayImpactWithStyle(UIImpactFeedbackStyleMedium);
                break;

            case kUEHPresetHeavy:
                UEH_PlayImpactWithStyle(UIImpactFeedbackStyleHeavy);
                break;

            default:
                UEH_LOG(@"PlayPreset ignored the unknown preset id %d.", presetId);
                break;
        }
    }
    @catch (NSException* exception)
    {
        UEH_LOG(@"PlayPreset failed: %@", exception);
    }
}

/// Plays a custom pattern described by four parallel arrays, replacing anything already playing.
/// Falls back to a single impact scaled to the pattern's peak intensity when patterns cannot be
/// rendered or CoreHaptics refuses the pattern — see the degrade rule in the file header.
extern "C" void UnityEssentialsHaptics_PlayPattern(const float* times,
                                                   const float* intensities,
                                                   const float* sharpnesses,
                                                   const float* durations,
                                                   int count)
{
    @try
    {
        if (times == NULL || intensities == NULL || sharpnesses == NULL || durations == NULL || count <= 0)
        {
            UEH_LOG(@"PlayPattern ignored a null or empty pattern.");
            return;
        }

        float peakIntensity = 0.0f;
        for (int i = 0; i < count; i++)
        {
            float intensity = UEH_Clamp01(intensities[i]);
            if (intensity > peakIntensity)
            {
                peakIntensity = intensity;
            }
        }

        if (@available(iOS 13.0, *))
        {
            if (UEH_HardwareSupportsHaptics() &&
                UEH_PlayPatternWithEngine(times, intensities, sharpnesses, durations, count))
            {
                return;
            }
        }

        UEH_PlayScaledImpact(peakIntensity);
    }
    @catch (NSException* exception)
    {
        UEH_LOG(@"PlayPattern failed: %@", exception);
    }
}

/// Stops pattern playback. Presets are fire-and-forget impacts that cannot be cancelled once
/// triggered, so there is nothing else to stop.
extern "C" void UnityEssentialsHaptics_Stop(void)
{
    @try
    {
        if (@available(iOS 13.0, *))
        {
            UEH_DiscardPlayer();
        }
    }
    @catch (NSException* exception)
    {
        UEH_LOG(@"Stop failed: %@", exception);
    }
}

/// Releases the haptic engine while the app is in the background and brings it back on resume if it
/// had been running. Only transitions act, because this may arrive repeatedly with the same value.
extern "C" void UnityEssentialsHaptics_OnApplicationPause(int paused)
{
    @try
    {
        if (@available(iOS 13.0, *))
        {
            if (paused != 0)
            {
                if (gApplicationPaused)
                {
                    return;
                }

                gApplicationPaused = YES;
                gEngineStartedBeforePause = gEngineStarted;

                UEH_DiscardPlayer();
                UEH_StopEngine();
            }
            else
            {
                if (!gApplicationPaused)
                {
                    return;
                }

                gApplicationPaused = NO;

                if (gEngineStartedBeforePause)
                {
                    gEngineStartedBeforePause = NO;
                    UEH_StartEngine();
                }
            }
        }
        else
        {
            // Below iOS 13 there is no engine to suspend; impacts are stateless.
            gApplicationPaused = (paused != 0);
        }
    }
    @catch (NSException* exception)
    {
        UEH_LOG(@"OnApplicationPause failed: %@", exception);
    }
}
