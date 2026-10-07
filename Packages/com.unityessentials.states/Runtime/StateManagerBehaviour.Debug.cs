#if UNITY_EDITOR
using System;

namespace UnityEssentials.States
{
    public abstract partial class StateManagerBehaviour<TState> where TState : struct, Enum
    {
        partial void DebugAttachOwner()
        {
            Machine.DebugOwner = this;
        }

        partial void DebugUnregister()
        {
            StateMachineDebugRegistry.Unregister(Machine);
        }
    }
}
#endif
