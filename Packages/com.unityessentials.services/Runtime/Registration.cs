using System;

namespace UnityEssentials.Services
{
    /// <summary>
    /// The committed state of a single <c>Register</c> call. One registration can be published
    /// under several keys (its own type plus up to <see cref="MaxInterfaceKeys"/> interfaces);
    /// every key resolves to this one object, so a shared instance is shared across all of them.
    /// </summary>
    internal sealed class Registration
    {
        /// <summary>Maximum number of <c>.As&lt;T&gt;()</c> keys a single registration may publish.</summary>
        internal const int MaxInterfaceKeys = 4;

        /// <summary>The concrete type that was registered; also the "self" key.</summary>
        internal readonly Type ConcreteType;

        /// <summary>Shared or transient. Instance registrations are always <see cref="Lifetime.Shared"/>.</summary>
        internal readonly Lifetime Lifetime;

        /// <summary>The scope that owns this registration and disposes its instance on release.</summary>
        internal readonly ServiceScope Owner;

        /// <summary>Creates the service. Null for instance registrations.</summary>
        internal readonly Func<object> Creator;

        /// <summary>True when the caller supplied a ready-made instance instead of a creator.</summary>
        internal readonly bool IsInstanceRegistration;

        /// <summary>The cached shared instance, or null while a lazy shared service is uncreated.</summary>
        internal object Instance;

        /// <summary>When true the locator never disposes <see cref="Instance"/>.</summary>
        internal bool ExternallyOwned;

        /// <summary>Reentrancy flag used to turn circular resolution into an exception.</summary>
        internal bool IsCreating;

        /// <summary>True once <c>.AsSelf()</c> was chained, which keeps the self key published.</summary>
        internal bool SelfExplicit;

        /// <summary>True while the self key is unpublished because of an access lock from <c>.As&lt;T&gt;()</c>.</summary>
        internal bool SelfLocked;

        /// <summary>How many interface keys have been published so far.</summary>
        internal int InterfaceCount;

        /// <summary>Creates a lazily/transiently created registration backed by a creator delegate.</summary>
        internal Registration(Type concreteType, Lifetime lifetime, ServiceScope owner, Func<object> creator)
        {
            ConcreteType = concreteType;
            Lifetime = lifetime;
            Owner = owner;
            Creator = creator;
            IsInstanceRegistration = false;
        }

        /// <summary>Creates a registration for an instance the caller already owns. Always shared.</summary>
        internal Registration(Type concreteType, ServiceScope owner, object instance)
        {
            ConcreteType = concreteType;
            Lifetime = Lifetime.Shared;
            Owner = owner;
            Creator = null;
            IsInstanceRegistration = true;
            Instance = instance;
        }
    }

    /// <summary>
    /// One node of the intrusive per-key shadow stack stored in <see cref="ServiceRegistry.Bindings"/>.
    /// The dictionary always holds the head (most recently published registration for that key);
    /// <see cref="Next"/> is whatever that registration shadowed and is restored when it is unlinked.
    /// </summary>
    internal sealed class Binding
    {
        /// <summary>The registration this binding resolves to.</summary>
        internal readonly Registration Registration;

        /// <summary>The binding shadowed by this one, or null if this is the oldest for the key.</summary>
        internal Binding Next;

        internal Binding(Registration registration, Binding next)
        {
            Registration = registration;
            Next = next;
        }
    }
}
