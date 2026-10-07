using System.Runtime.CompilerServices;

// The EditMode test assembly exercises internal state (ServiceRegistry.Bindings, ResetStatics)
// that is deliberately not part of the public contract.
[assembly: InternalsVisibleTo("UnityEssentials.Services.Tests")]
