using System.Runtime.CompilerServices;

// The EditMode test assembly drives internal surface (UIService.WindowParent, UIBase.Bind,
// UIService.RaiseShown/RaiseHidden, WindowDataValidator) that is deliberately not part of the
// public contract.
[assembly: InternalsVisibleTo("UnityEssentials.UI.Tests")]
