using System.Runtime.CompilerServices;

// The EditMode test assembly drives internal surface (WindowService.WindowParent, UIHistory<TEntry>,
// element binding, the WindowService registration hooks) that is deliberately not part of the
// public contract.
[assembly: InternalsVisibleTo("UnityEssentials.UI.Tests")]
