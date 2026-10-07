using System;
using System.Collections.Generic;

namespace UnityEssentials.Services
{
    /// <summary>
    /// A named lifetime boundary for registrations. Services registered on a scope resolve through
    /// the global <see cref="ServiceLocator.Get{T}"/> just like global ones (resolution is unified),
    /// but they disappear — and their created shared instances are disposed — when the scope is
    /// released. Registering a key another scope already published shadows it; releasing restores
    /// what was shadowed.
    /// </summary>
    /// <remarks>
    /// Scopes are main-thread only, exactly like <see cref="ServiceLocator"/>. Wrap one in a
    /// <c>using</c> block to release it deterministically.
    /// </remarks>
    public sealed class ServiceScope : IDisposable
    {
        /// <summary>Name of the internal root scope. Angle brackets keep it out of user name space.</summary>
        internal const string RootName = "<global>";

        private readonly List<Registration> _registrations = new List<Registration>();
        private readonly List<PublishedKey> _published = new List<PublishedKey>();
        private readonly Dictionary<Type, Registration> _local = new Dictionary<Type, Registration>();
        private readonly bool _isRoot;
        private bool _released;

        internal ServiceScope(string name, bool isRoot)
        {
            Name = name;
            _isRoot = isRoot;
        }

        /// <summary>Creates the internal root scope that backs <see cref="ServiceLocator"/>.</summary>
        internal static ServiceScope CreateRoot()
        {
            return new ServiceScope(RootName, true);
        }

        /// <summary>The name this scope was created with. Unique among live scopes.</summary>
        public string Name { get; }

        /// <summary>True once <see cref="Release"/> (or <see cref="Dispose"/>) has run. A released scope cannot be used again.</summary>
        public bool IsReleased => _released;

        /// <summary>This scope's registrations in commit order; disposal walks it in reverse.</summary>
        internal List<Registration> Registrations => _registrations;

        // -------------------------------------------------------------------------------------------
        // Registration
        // -------------------------------------------------------------------------------------------

        /// <summary>
        /// Registers <typeparamref name="TConcrete"/>, constructed by the locator through its public
        /// parameterless constructor. Shared registrations are created lazily on first resolution.
        /// </summary>
        /// <typeparam name="TConcrete">Concrete service type to register and, by default, resolve by.</typeparam>
        /// <param name="lifetime">Shared (one cached instance) or transient (one per resolution).</param>
        /// <returns>A builder for chaining <c>.As&lt;T&gt;()</c>, <c>.AsSelf()</c>, <c>.NonLazy()</c> and <c>.ExternallyOwned()</c>.</returns>
        /// <exception cref="ServiceRegistrationException">The key is already registered in this scope.</exception>
        /// <exception cref="ServiceLocatorException">The scope has been released.</exception>
        public RegistrationBuilder<TConcrete> Register<TConcrete>(Lifetime lifetime = Lifetime.Shared)
            where TConcrete : class, new()
        {
            ServiceRegistry.AssertMainThread();
            ThrowIfReleased();
            var registration = new Registration(typeof(TConcrete), lifetime, this, () => new TConcrete());
            return ServiceRegistry.Commit<TConcrete>(registration);
        }

        /// <summary>
        /// Registers an instance you already created. Instance registrations are inherently shared —
        /// there is no lifetime to choose — and are disposed with the scope unless marked
        /// <see cref="RegistrationBuilder{TConcrete}.ExternallyOwned"/>.
        /// </summary>
        /// <typeparam name="TConcrete">Type the instance is registered (and resolved) as.</typeparam>
        /// <param name="instance">The instance to hand out. Must not be null.</param>
        /// <returns>A builder for chaining further configuration.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="instance"/> is null.</exception>
        /// <exception cref="ServiceRegistrationException">The key is already registered in this scope.</exception>
        /// <exception cref="ServiceLocatorException">The scope has been released.</exception>
        public RegistrationBuilder<TConcrete> Register<TConcrete>(TConcrete instance)
            where TConcrete : class
        {
            ServiceRegistry.AssertMainThread();
            ThrowIfReleased();
            if (instance == null)
            {
                throw new ArgumentNullException(nameof(instance));
            }

            var registration = new Registration(typeof(TConcrete), this, instance);
            return ServiceRegistry.Commit<TConcrete>(registration);
        }

        /// <summary>
        /// Registers a factory. Shared registrations invoke it once, on first resolution (or at
        /// registration when <c>.NonLazy()</c> is chained); transient registrations invoke it per
        /// resolution. A factory that returns null throws at creation time.
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
        /// <exception cref="ServiceRegistrationException">The key is already registered in this scope.</exception>
        /// <exception cref="ServiceLocatorException">The scope has been released.</exception>
        public RegistrationBuilder<TConcrete> Register<TConcrete>(Func<TConcrete> factory, Lifetime lifetime = Lifetime.Shared)
            where TConcrete : class
        {
            ServiceRegistry.AssertMainThread();
            ThrowIfReleased();
            if (factory == null)
            {
                throw new ArgumentNullException(nameof(factory));
            }

            // Func<out TResult> is covariant and TConcrete is a reference type, so this is a free
            // reference conversion rather than an allocating wrapper delegate.
            var registration = new Registration(typeof(TConcrete), lifetime, this, factory);
            return ServiceRegistry.Commit<TConcrete>(registration);
        }

        // -------------------------------------------------------------------------------------------
        // Resolution
        // -------------------------------------------------------------------------------------------

        /// <summary>
        /// Resolves <typeparamref name="T"/>, preferring this scope's own registration and falling
        /// back to the unified lookup (other scopes and the global registrations).
        /// </summary>
        /// <typeparam name="T">The registered key: a concrete type or one of its <c>.As&lt;T&gt;()</c> interfaces.</typeparam>
        /// <returns>The shared instance, or a fresh one for transient registrations.</returns>
        /// <exception cref="ServiceNotFoundException">Nothing is registered for <typeparamref name="T"/>.</exception>
        /// <exception cref="ServiceLocatorException">The scope has been released, or creation failed (null product or circular resolution).</exception>
        public T Get<T>() where T : class
        {
            ServiceRegistry.AssertMainThread();
            ThrowIfReleased();

            if (_local.TryGetValue(typeof(T), out var registration))
            {
                var instance = registration.Instance;
                return instance != null ? (T)instance : (T)ServiceRegistry.ResolveSlow(registration);
            }

            return ServiceRegistry.Resolve<T>();
        }

        /// <summary>
        /// Tries to resolve <typeparamref name="T"/>, preferring this scope's own registration.
        /// Returns false only when nothing is registered for the key; a registration that fails to
        /// create (null factory product, circular resolution) still throws, because that is a bug
        /// rather than a miss.
        /// </summary>
        /// <typeparam name="T">The registered key.</typeparam>
        /// <param name="service">The resolved service, or null on a miss.</param>
        /// <returns>True if a registration was found.</returns>
        /// <exception cref="ServiceLocatorException">The scope has been released, or creation failed.</exception>
        public bool TryGet<T>(out T service) where T : class
        {
            ServiceRegistry.AssertMainThread();
            ThrowIfReleased();

            if (_local.TryGetValue(typeof(T), out var registration))
            {
                var instance = registration.Instance;
                service = instance != null ? (T)instance : (T)ServiceRegistry.ResolveSlow(registration);
                return true;
            }

            return ServiceRegistry.TryResolve(out service);
        }

        // -------------------------------------------------------------------------------------------
        // Teardown
        // -------------------------------------------------------------------------------------------

        /// <summary>
        /// Removes every binding this scope published — restoring anything it shadowed — and then
        /// disposes the shared instances it created, in reverse registration order. Never-created
        /// lazy services, transients and externally owned instances are not disposed. A
        /// <see cref="IDisposable.Dispose"/> that throws is logged and teardown continues.
        /// Calling this twice is a no-op.
        /// </summary>
        /// <exception cref="ServiceLocatorException">Called on the internal root scope.</exception>
        public void Release()
        {
            ServiceRegistry.AssertMainThread();

            if (_isRoot)
            {
                throw new ServiceLocatorException(
                    "The global scope cannot be released directly. Call ServiceLocator.ReleaseAll() instead.");
            }

            if (_released)
            {
                return;
            }

            ReleaseInternal();
            ServiceRegistry.RemoveScope(this);
        }

        /// <summary>Releases the scope. Identical to <see cref="Release"/>, so <c>using</c> works.</summary>
        public void Dispose()
        {
            Release();
        }

        /// <summary>
        /// The teardown itself, usable on the root scope and without touching the registry's scope
        /// list (<see cref="ServiceRegistry.ReleaseAll"/> clears that in one go).
        /// </summary>
        internal void ReleaseInternal()
        {
            if (_released)
            {
                return;
            }

            _released = true;

            for (var i = _published.Count - 1; i >= 0; i--)
            {
                ServiceRegistry.Unlink(_published[i].Key, _published[i].Binding);
            }

            _published.Clear();
            _local.Clear();

            for (var i = _registrations.Count - 1; i >= 0; i--)
            {
                var registration = _registrations[i];
                var instance = registration.Instance;
                registration.Instance = null;

                if (instance == null || registration.ExternallyOwned)
                {
                    continue;
                }

                if (instance is IDisposable disposable)
                {
                    try
                    {
                        disposable.Dispose();
                    }
                    catch (Exception exception)
                    {
                        UnityEngine.Debug.LogException(exception);
                    }
                }
            }

            _registrations.Clear();
        }

        /// <summary>
        /// Marks the scope released and drops all bookkeeping without unlinking or disposing
        /// anything. Used only by the domain-reload reset, where the registry is cleared wholesale
        /// and instances from the previous session must not be touched — this makes stale handles
        /// throw instead of resolving dead objects.
        /// </summary>
        internal void HardReset()
        {
            _released = true;
            _published.Clear();
            _local.Clear();
            _registrations.Clear();
        }

        // -------------------------------------------------------------------------------------------
        // Bookkeeping used by ServiceRegistry
        // -------------------------------------------------------------------------------------------

        /// <summary>Records a committed registration so release can dispose it in reverse order.</summary>
        internal void TrackRegistration(Registration registration)
        {
            _registrations.Add(registration);
        }

        /// <summary>Records a published key so release can unlink it and scope-first Get can find it.</summary>
        internal void TrackPublished(Type key, Binding binding)
        {
            _published.Add(new PublishedKey(key, binding));
            _local[key] = binding.Registration;
        }

        /// <summary>Forgets a key that was unpublished before release (the <c>.As&lt;T&gt;()</c> access lock).</summary>
        internal void UntrackPublished(Type key, Binding binding)
        {
            for (var i = _published.Count - 1; i >= 0; i--)
            {
                if (_published[i].Binding == binding)
                {
                    _published.RemoveAt(i);
                    break;
                }
            }

            if (_local.TryGetValue(key, out var registration) && registration == binding.Registration)
            {
                _local.Remove(key);
            }
        }

        private void ThrowIfReleased()
        {
            if (_released)
            {
                throw new ServiceLocatorException(
                    $"Scope '{Name}' has been released and can no longer be used.");
            }
        }

        /// <summary>A key this scope published together with the shadow-stack node it pushed.</summary>
        private readonly struct PublishedKey
        {
            internal readonly Type Key;
            internal readonly Binding Binding;

            internal PublishedKey(Type key, Binding binding)
            {
                Key = key;
                Binding = binding;
            }
        }
    }
}
