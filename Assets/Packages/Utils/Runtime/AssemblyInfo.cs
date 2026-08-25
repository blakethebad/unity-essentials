using System.Runtime.CompilerServices;

// The EditMode test assembly exercises internal state (StaticResetRegistry, the Timer tick seam,
// log formatting) that is deliberately not part of the public contract.
[assembly: InternalsVisibleTo("UnityEssentials.Utilities.Tests")]
