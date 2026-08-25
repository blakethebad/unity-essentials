using System.Runtime.CompilerServices;

// The EditMode test assembly exercises internals that are deliberately outside the public contract:
// TransitionTable<TState>, BaseState<TState>.Attach and BaseStateManager<TState>.SetAttachOwner.
[assembly: InternalsVisibleTo("UnityEssentials.States.Tests")]

// The debug window reads machines through the internal editor-only registry in this assembly.
[assembly: InternalsVisibleTo("UnityEssentials.States.Editor")]
