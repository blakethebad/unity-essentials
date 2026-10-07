using System.Runtime.CompilerServices;

// The EditMode test assembly exercises internal state (the Timer tick seam, the singleton caches,
// log formatting) that is deliberately not part of the public contract.
[assembly: InternalsVisibleTo("UnityEssentials.Utilities.Tests")]
