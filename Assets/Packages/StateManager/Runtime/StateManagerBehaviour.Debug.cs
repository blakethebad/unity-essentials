// Editor-only half of StateManagerBehaviour<TState>: the two lines that tie a behaviour's Unity
// lifecycle to the debug window's registry. Wrapped entirely in #if UNITY_EDITOR, so in a player
// build DebugAttachOwner() and DebugUnregister() have no implementing part and the compiler erases
// both the methods and the calls to them — Awake and OnDestroy end up with nothing in them.
#if UNITY_EDITOR
using System;

namespace UnityEssentials.States
{
    /// <summary>
    /// Editor-only debug wiring for the behaviour: names the hosted machine after this component and
    /// releases it from the registry when the component is destroyed.
    /// </summary>
    public abstract partial class StateManagerBehaviour<TState> where TState : struct, Enum
    {
        partial void DebugAttachOwner()
        {
            // Must run before Initialize(), because registration reads the owner once and never
            // revisits it — hence Awake calling this while Start is what initializes.
            Machine.DebugOwner = this;
        }

        partial void DebugUnregister()
        {
            // Belt-and-braces: an entry whose owner is destroyed is pruned anyway, which is what
            // covers subclasses that declare their own OnDestroy without calling base.
            StateMachineDebugRegistry.Unregister(Machine);
        }
    }
}
#endif
