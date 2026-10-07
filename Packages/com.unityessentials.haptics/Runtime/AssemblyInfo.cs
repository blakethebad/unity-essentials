using System.Runtime.CompilerServices;

// The EditMode test assembly exercises internal state and pure helpers that are deliberately not
// part of the public contract: HapticManager.ProviderOverride, HapticManager.ResetStatics and
// HapticManager.CreateProvider (the platform-selection seam), HapticPattern.Events and
// HapticPattern.ThrowIfInvalid, and the AndroidWaveformConverter / IOSPatternFlattener converters.
[assembly: InternalsVisibleTo("UnityEssentials.Haptics.Tests")]
