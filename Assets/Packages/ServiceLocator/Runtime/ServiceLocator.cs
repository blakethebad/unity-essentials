using System;

namespace UnityEssentials.Services
{
    /// <summary>
    /// A static service locator: register implementations once, resolve them anywhere in O(1).
    /// Registrations made here live in an implicit global scope; <see cref="CreateScope"/> adds
    /// releasable ones. Resolution is unified — <see cref="Get{T}"/> finds services from any live
    /// scope, so callers never need to know where a service was registered.
    /// </summary>
    /// <remarks>
    /// Main-thread only, and deliberately lock-free: resolve on the main thread and pass the result
    /// to background work. State survives play-mode only until a domain reload, which hard-resets it
    /// without disposing anything — call <see cref="ReleaseAll"/> on shutdown if disposal matters.
    /// </remarks>
    public static class ServiceLocator
    {
        /// <summary>
        /// Registers <typeparamref name="TConcrete"/> globally, constructed by the locator through
        /// its public parameterless constructor. Shared registrations are created lazily on first
        /// resolution; chain <see cref="RegistrationBuilder{TConcrete}.NonLazy"/> to create now.
        /// </summary>
        /// <typeparam name="TConcrete">Concrete service type to register and, by default, resolve by.</typeparam>
        /// <param name="lifetime">Shared (one cached instance) or transient (one per resolution).</param>
        /// <returns>A builder for chaining <c>.As&lt;T&gt;()</c>, <c>.AsSelf()</c>, <c>.NonLazy()</c> and <c>.ExternallyOwned()</c>.</returns>
        /// <exception cref="ServiceRegistrationException">The key is already registered globally.</exception>
        public static RegistrationBuilder<TConcrete> Register<TConcrete>(Lifetime lifetime = Lifetime.Shared)
            where TConcrete : class, new()
        {
            ServiceRegistry.AssertMainThread();
            return ServiceRegistry.Root.Register<TConcrete>(lifetime);
        }

        /// <summary>
        /// Registers an instance you already created. Instance registrations are inherently shared —
        /// there is no lifetime to choose — and are disposed by <see cref="ReleaseAll"/> unless
        /// marked <see cref="RegistrationBuilder{TConcrete}.ExternallyOwned"/>.
        /// </summary>
        /// <typeparam name="TConcrete">Type the instance is registered (and resolved) as.</typeparam>
        /// <param name="instance">The instance to hand out. Must not be null.</param>
        /// <returns>A builder for chaining further configuration.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="instance"/> is null.</exception>
        /// <exception cref="ServiceRegistrationException">The key is already registered globally.</exception>
        public static RegistrationBuilder<TConcrete> Register<TConcrete>(TConcrete instance)
            where TConcrete : class
        {
            ServiceRegistry.AssertMainThread();
            return ServiceRegistry.Root.Register(instance);
        }

        /// <summary>
        /// Registers a factory. Shared registrations invoke it once, on first resolution (or at
        /// registration when <c>.NonLazy()</c> is chained); transient registrations invoke it per
        /// resolution. Prefer this overload over the <c>new()</c> one for high-frequency transients.
        /// </summary>
        /// <typeparam name="TConcrete">Type the product is registered (and resolved) as.</typeparam>
        /// <param name="factory">Creates the service. Must not be null and must not return null.</param>
        /// <param name="lifetime">Shared (one cached instance) or transient (one per resolution).</param>
        /// <returns>A builder for chaining further configuration.</returns>
        /// <remarks>
        /// When passing a stored <see cref="Func{TResult}"/> variable instead of a lambda, name the
        /// type argument explicitly (<c>Register&lt;Sword&gt;(factory)</c>) so it binds to this
        /// overload rather than to the instance overload.
        /// </remarks>
        /// <exception cref="ArgumentNullException"><paramref name="factory"/> is null.</exception>
        /// <exception cref="ServiceRegistrationException">The key is already registered globally.</exception>
        public static RegistrationBuilder<TConcrete> Register<TConcrete>(Func<TConcrete> factory, Lifetime lifetime = Lifetime.Shared)
            where TConcrete : class
        {
            ServiceRegistry.AssertMainThread();
            return ServiceRegistry.Root.Register(factory, lifetime);
        }

        /// <summary>
        /// Resolves <typeparamref name="T"/> from any live scope. Warm shared resolutions are a
        /// single dictionary lookup and allocate nothing.
        /// </summary>
        /// <typeparam name="T">The registered key: a concrete type, or one of its <c>.As&lt;T&gt;()</c> abstractions.</typeparam>
        /// <returns>The shared instance, or a fresh one for transient registrations.</returns>
        /// <exception cref="ServiceNotFoundException">
        /// Nothing is registered for <typeparamref name="T"/>. The message says so explicitly when the
        /// type is registered but its self key is locked by <c>.As&lt;T&gt;()</c>.
        /// </exception>
        /// <exception cref="ServiceLocatorException">Creation failed: the factory returned null, or the service resolved itself circularly.</exception>
        public static T Get<T>() where T : class
        {
            ServiceRegistry.AssertMainThread();
            return ServiceRegistry.Resolve<T>();
        }

        /// <summary>
        /// Tries to resolve <typeparamref name="T"/>. Returns false only when nothing is registered
        /// for the key; a registration that fails to create (null factory product, circular
        /// resolution) still throws, because that is a bug rather than a miss.
        /// </summary>
        /// <typeparam name="T">The registered key.</typeparam>
        /// <param name="service">The resolved service, or null on a miss.</param>
        /// <returns>True if a registration was found.</returns>
        public static bool TryGet<T>(out T service) where T : class
        {
            ServiceRegistry.AssertMainThread();
            return ServiceRegistry.TryResolve(out service);
        }

        /// <summary>
        /// Creates a named scope whose registrations resolve globally but disappear — and whose
        /// created shared instances are disposed — when it is released. A scope may shadow keys that
        /// other scopes (or the global registrations) already publish; releasing it restores them.
        /// </summary>
        /// <param name="name">Unique, non-empty name used in diagnostics.</param>
        /// <returns>The scope handle. Release it with <see cref="ServiceScope.Release"/> or a <c>using</c> block.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="name"/> is null.</exception>
        /// <exception cref="ArgumentException"><paramref name="name"/> is empty.</exception>
        /// <exception cref="ServiceRegistrationException">A live scope already uses that name.</exception>
        public static ServiceScope CreateScope(string name)
        {
            ServiceRegistry.AssertMainThread();
            return ServiceRegistry.CreateScope(name);
        }

        /// <summary>
        /// Releases everything: every scope in reverse creation order, then the global registrations,
        /// disposing created shared instances (and registered instances that are not externally
        /// owned) in reverse registration order. The locator is immediately reusable afterwards, and
        /// previously created scope handles are left released.
        /// </summary>
        public static void ReleaseAll()
        {
            ServiceRegistry.AssertMainThread();
            ServiceRegistry.ReleaseAll();
        }
    }
}
