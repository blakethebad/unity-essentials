using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;
using UnityEngine;

namespace UnityEssentials.Services
{
    /// <summary>
    /// All mutable locator state plus the publish/unpublish and resolution machinery.
    /// Internal by design: <see cref="ServiceLocator"/> and <see cref="ServiceScope"/> are the API.
    /// </summary>
    /// <remarks>
    /// Every collection here uses an inline field initializer so the registry is usable in EditMode,
    /// where <see cref="RuntimeInitializeOnLoadMethodAttribute"/> never fires. The implicit static
    /// constructor also captures <see cref="MainThreadId"/> from the thread that first touches the
    /// registry, which in Unity is the main thread; <see cref="ResetStatics"/> recaptures it.
    /// No locks are taken anywhere — the locator is main-thread only.
    /// </remarks>
    internal static class ServiceRegistry
    {
        /// <summary>Managed id of the thread that owns the locator.</summary>
        internal static int MainThreadId = Environment.CurrentManagedThreadId;

        /// <summary>
        /// The unified lookup: service key to the head of that key's shadow stack. This is the only
        /// dictionary touched on the hot path.
        /// </summary>
        internal static readonly Dictionary<Type, Binding> Bindings = new Dictionary<Type, Binding>(64);

        /// <summary>User-created scopes in creation order. The root scope is not in this list.</summary>
        internal static readonly List<ServiceScope> Scopes = new List<ServiceScope>();

        /// <summary>
        /// The implicit scope backing <see cref="ServiceLocator"/>'s own registrations. Users never
        /// see it and can never release it; <see cref="ReleaseAll"/> replaces it with a fresh one.
        /// </summary>
        internal static ServiceScope Root = ServiceScope.CreateRoot();

        /// <summary>
        /// Hard-resets every static without disposing anything, so a domain reload starts clean.
        /// Objects from the previous session are already gone, which is why nothing is disposed;
        /// call <see cref="ServiceLocator.ReleaseAll"/> yourself if disposal matters.
        /// </summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        internal static void ResetStatics()
        {
            MainThreadId = Environment.CurrentManagedThreadId;

            // Scope handles retained from a previous session (possible when domain reload is
            // disabled) must throw on use rather than silently serve destroyed objects, so mark
            // them released before dropping them.
            for (var i = Scopes.Count - 1; i >= 0; i--)
            {
                Scopes[i].HardReset();
            }

            Root.HardReset();
            Bindings.Clear();
            Scopes.Clear();
            Root = ServiceScope.CreateRoot();
        }

        /// <summary>
        /// Throws if the caller is not on the thread that owns the locator. Compiled out of release
        /// builds — it exists only in the Editor and development builds.
        /// </summary>
        [Conditional("UNITY_EDITOR")]
        [Conditional("DEVELOPMENT_BUILD")]
        internal static void AssertMainThread()
        {
            if (Environment.CurrentManagedThreadId != MainThreadId)
            {
                throw new ServiceLocatorException(
                    "ServiceLocator may only be used from the Unity main thread. " +
                    "Resolve the services you need on the main thread and hand them to the worker.");
            }
        }

        // ---------------------------------------------------------------------------------------
        // Resolution
        // ---------------------------------------------------------------------------------------

        /// <summary>Resolves <typeparamref name="T"/> or throws <see cref="ServiceNotFoundException"/>.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static T Resolve<T>() where T : class
        {
            if (Bindings.TryGetValue(typeof(T), out var binding))
            {
                var registration = binding.Registration;
                var instance = registration.Instance;
                return instance != null ? (T)instance : (T)ResolveSlow(registration);
            }

            throw CreateNotFound(typeof(T));
        }

        /// <summary>
        /// Resolves <typeparamref name="T"/>, returning false only when the key is not registered.
        /// Resolution failures (null factory product, circular resolution) still throw.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static bool TryResolve<T>(out T service) where T : class
        {
            if (Bindings.TryGetValue(typeof(T), out var binding))
            {
                var registration = binding.Registration;
                var instance = registration.Instance;
                service = instance != null ? (T)instance : (T)ResolveSlow(registration);
                return true;
            }

            service = null;
            return false;
        }

        /// <summary>
        /// The cold half of resolution: creates a lazy shared instance (caching it) or a transient
        /// one (per call), guarding against circular resolution and null factory products.
        /// </summary>
        internal static object ResolveSlow(Registration registration)
        {
            var creator = registration.Creator;
            if (creator == null)
            {
                throw new ServiceLocatorException(
                    $"The registered instance of '{registration.ConcreteType.FullName}' is no longer available " +
                    "because its scope was released.");
            }

            if (registration.IsCreating)
            {
                throw new ServiceLocatorException(
                    $"Circular resolution detected while creating '{registration.ConcreteType.FullName}'. " +
                    "Its constructor or factory resolved the same service again, directly or through another service. " +
                    "Break the cycle by resolving one side lazily.");
            }

            object created;
            registration.IsCreating = true;
            try
            {
                created = creator();
            }
            catch (TargetInvocationException exception) when (exception.InnerException != null)
            {
                // `new TConcrete()` under a new() constraint compiles to Activator.CreateInstance,
                // which wraps whatever the constructor threw. Unwrap it so callers see the real
                // error — the circular-resolution guard above, for instance — with its stack intact.
                ExceptionDispatchInfo.Capture(exception.InnerException).Throw();
                throw; // Unreachable: Throw() always rethrows. Keeps definite assignment happy.
            }
            finally
            {
                registration.IsCreating = false;
            }

            if (created == null)
            {
                throw new ServiceLocatorException(
                    $"The factory registered for '{registration.ConcreteType.FullName}' returned null. " +
                    "A factory must always produce an instance.");
            }

            if (registration.Lifetime == Lifetime.Shared)
            {
                registration.Instance = created;
            }

            return created;
        }

        /// <summary>Builds the enriched "not registered" message used by the cold miss path.</summary>
        internal static ServiceNotFoundException CreateNotFound(Type key)
        {
            var locked = FindLockedSelfRegistration(key);
            if (locked != null)
            {
                return new ServiceNotFoundException(
                    $"No service is registered for type '{key.FullName}'. It is registered in scope " +
                    $"'{locked.Owner.Name}' but locked by .As<...>(); chain .AsSelf() or resolve via its interface.");
            }

            return new ServiceNotFoundException(
                $"No service is registered for type '{key.FullName}'. " +
                $"Register one with ServiceLocator.Register<{key.Name}>() before resolving it.");
        }

        private static Registration FindLockedSelfRegistration(Type key)
        {
            for (var i = Scopes.Count - 1; i >= 0; i--)
            {
                var match = FindLockedSelf(Scopes[i], key);
                if (match != null)
                {
                    return match;
                }
            }

            return FindLockedSelf(Root, key);
        }

        private static Registration FindLockedSelf(ServiceScope scope, Type key)
        {
            var registrations = scope.Registrations;
            for (var i = registrations.Count - 1; i >= 0; i--)
            {
                var registration = registrations[i];
                if (registration.SelfLocked && registration.ConcreteType == key)
                {
                    return registration;
                }
            }

            return null;
        }

        // ---------------------------------------------------------------------------------------
        // Registration
        // ---------------------------------------------------------------------------------------

        /// <summary>Publishes the self key, tracks the registration on its scope and wraps it in a builder.</summary>
        internal static RegistrationBuilder<TConcrete> Commit<TConcrete>(Registration registration)
            where TConcrete : class
        {
            Publish(typeof(TConcrete), registration);
            registration.Owner.TrackRegistration(registration);
            return new RegistrationBuilder<TConcrete>(registration);
        }

        /// <summary>
        /// Pushes <paramref name="registration"/> onto the shadow stack for <paramref name="key"/>,
        /// making it the winner. Registering the same key twice in one scope is a programming error;
        /// across scopes it shadows instead.
        /// </summary>
        internal static void Publish(Type key, Registration registration)
        {
            var owner = registration.Owner;
            ThrowIfKeyTaken(key, owner);

            Bindings.TryGetValue(key, out var head);
            var binding = new Binding(registration, head);
            Bindings[key] = binding;
            owner.TrackPublished(key, binding);
        }

        /// <summary>
        /// Throws when <paramref name="owner"/> already publishes <paramref name="key"/>. Called
        /// before any mutation so a failing registration leaves prior state untouched.
        /// </summary>
        private static void ThrowIfKeyTaken(Type key, ServiceScope owner)
        {
            if (!Bindings.TryGetValue(key, out var head))
            {
                return;
            }

            for (var node = head; node != null; node = node.Next)
            {
                if (node.Registration.Owner == owner)
                {
                    throw new ServiceRegistrationException(
                        $"'{key.FullName}' is already registered in scope '{owner.Name}'. " +
                        "Register a key once per scope; use a separate scope to shadow it.");
                }
            }
        }

        /// <summary>
        /// Removes <paramref name="target"/> from its key's shadow stack, restoring whatever it
        /// shadowed. Handles both head pops and mid-stack unlinks, and drops the dictionary entry
        /// entirely when the stack empties so a locked or released key leaves no residue.
        /// </summary>
        internal static void Unlink(Type key, Binding target)
        {
            if (!Bindings.TryGetValue(key, out var head))
            {
                return;
            }

            if (head == target)
            {
                if (target.Next == null)
                {
                    Bindings.Remove(key);
                }
                else
                {
                    Bindings[key] = target.Next;
                }

                target.Next = null;
                return;
            }

            for (var node = head; node.Next != null; node = node.Next)
            {
                if (node.Next == target)
                {
                    node.Next = target.Next;
                    target.Next = null;
                    return;
                }
            }
        }

        /// <summary>Unpublishes the binding <paramref name="registration"/> owns for <paramref name="key"/>.</summary>
        private static void UnpublishKey(Registration registration, Type key)
        {
            if (!Bindings.TryGetValue(key, out var head))
            {
                return;
            }

            Binding target = null;
            if (head.Registration == registration)
            {
                target = head;
            }
            else
            {
                for (var node = head; node.Next != null; node = node.Next)
                {
                    if (node.Next.Registration == registration)
                    {
                        target = node.Next;
                        break;
                    }
                }
            }

            if (target == null)
            {
                return;
            }

            Unlink(key, target);
            registration.Owner.UntrackPublished(key, target);
        }

        /// <summary>
        /// Backs <c>.As&lt;TInterface&gt;()</c>: validates assignability and the four-key cap, locks
        /// the self key on the first call unless <c>.AsSelf()</c> was requested, then publishes the
        /// interface key.
        /// </summary>
        internal static void AddInterface(Registration registration, Type interfaceType)
        {
            AssertMainThread();
            ThrowIfOwnerReleased(registration);

            // .As<TConcrete>() with the concrete type itself just means "keep me resolvable by
            // my own type" — that is .AsSelf(), and it should not consume an interface slot.
            if (interfaceType == registration.ConcreteType)
            {
                EnableSelf(registration);
                return;
            }

            if (!interfaceType.IsAssignableFrom(registration.ConcreteType))
            {
                throw new ServiceRegistrationException(
                    $"'{registration.ConcreteType.FullName}' cannot be registered as '{interfaceType.FullName}' " +
                    "because it does not implement or derive from it.");
            }

            if (registration.InterfaceCount >= Registration.MaxInterfaceKeys)
            {
                throw new ServiceRegistrationException(
                    $"'{registration.ConcreteType.FullName}' already exposes the maximum of " +
                    $"{Registration.MaxInterfaceKeys} interface keys. Split the type or register it twice.");
            }

            // Validate the interface key BEFORE locking the self key, so a duplicate-key failure
            // leaves the registration exactly as it was (self key still published and resolvable).
            ThrowIfKeyTaken(interfaceType, registration.Owner);

            if (!registration.SelfExplicit && !registration.SelfLocked)
            {
                UnpublishKey(registration, registration.ConcreteType);
                registration.SelfLocked = true;
            }

            Publish(interfaceType, registration);
            registration.InterfaceCount++;
        }

        /// <summary>
        /// Backs <c>.AsSelf()</c>: keeps (or restores) the self key regardless of whether it is
        /// chained before or after <c>.As&lt;TInterface&gt;()</c>.
        /// </summary>
        internal static void EnableSelf(Registration registration)
        {
            AssertMainThread();
            ThrowIfOwnerReleased(registration);

            if (registration.SelfExplicit)
            {
                return;
            }

            registration.SelfExplicit = true;
            if (registration.SelfLocked)
            {
                Publish(registration.ConcreteType, registration);
                registration.SelfLocked = false;
            }
        }

        /// <summary>
        /// Backs <c>.NonLazy()</c>: creates the shared instance immediately. Transient registrations
        /// have nothing to pre-create and instance registrations already hold theirs.
        /// </summary>
        internal static void ForceCreate(Registration registration)
        {
            AssertMainThread();
            ThrowIfOwnerReleased(registration);

            if (registration.IsInstanceRegistration)
            {
                return;
            }

            if (registration.Lifetime == Lifetime.Transient)
            {
                throw new ServiceRegistrationException(
                    $"NonLazy() is not valid for the transient registration of " +
                    $"'{registration.ConcreteType.FullName}' — transient services are created per resolution.");
            }

            if (registration.Instance == null)
            {
                ResolveSlow(registration);
            }
        }

        /// <summary>Backs <c>.ExternallyOwned()</c>: opts the instance out of disposal on release.</summary>
        internal static void MarkExternallyOwned(Registration registration)
        {
            AssertMainThread();
            ThrowIfOwnerReleased(registration);
            registration.ExternallyOwned = true;
        }

        private static void ThrowIfOwnerReleased(Registration registration)
        {
            if (registration.Owner.IsReleased)
            {
                throw new ServiceLocatorException(
                    $"Scope '{registration.Owner.Name}' has been released; its registrations can no longer be configured.");
            }
        }

        // ---------------------------------------------------------------------------------------
        // Scopes
        // ---------------------------------------------------------------------------------------

        /// <summary>Creates and tracks a named scope.</summary>
        internal static ServiceScope CreateScope(string name)
        {
            if (name == null)
            {
                throw new ArgumentNullException(nameof(name));
            }

            if (name.Length == 0)
            {
                throw new ArgumentException("Scope name must not be empty.", nameof(name));
            }

            if (string.Equals(name, ServiceScope.RootName, StringComparison.Ordinal))
            {
                throw new ServiceRegistrationException(
                    $"Scope name '{ServiceScope.RootName}' is reserved for the internal global scope.");
            }

            for (var i = 0; i < Scopes.Count; i++)
            {
                if (string.Equals(Scopes[i].Name, name, StringComparison.Ordinal))
                {
                    throw new ServiceRegistrationException(
                        $"A scope named '{name}' already exists. Scope names must be unique while the scope is alive.");
                }
            }

            var scope = new ServiceScope(name, false);
            Scopes.Add(scope);
            return scope;
        }

        /// <summary>Stops tracking a scope that has released itself.</summary>
        internal static void RemoveScope(ServiceScope scope)
        {
            Scopes.Remove(scope);
        }

        /// <summary>
        /// Releases every user scope in reverse creation order, then the root's registrations, then
        /// hard-clears the lookup and installs a fresh root so the locator is immediately reusable.
        /// </summary>
        internal static void ReleaseAll()
        {
            for (var i = Scopes.Count - 1; i >= 0; i--)
            {
                Scopes[i].ReleaseInternal();
            }

            Scopes.Clear();
            Root.ReleaseInternal();
            Bindings.Clear();
            Root = ServiceScope.CreateRoot();
        }
    }
}
