namespace UnityEssentials.Services
{
    /// <summary>
    /// Fluent configuration for a registration that is already committed. Every method applies its
    /// change immediately and returns the builder again — there is no <c>Build()</c> step, so a
    /// bare <c>Register&lt;T&gt;()</c> is already a working registration.
    /// </summary>
    /// <typeparam name="TConcrete">The concrete type that was registered.</typeparam>
    /// <remarks>
    /// The struct only wraps a reference to the registration, so copying it is harmless: every copy
    /// configures the same registration. A <c>default(RegistrationBuilder&lt;T&gt;)</c> wraps nothing
    /// and throws <see cref="ServiceRegistrationException"/> from every method.
    /// </remarks>
    public readonly struct RegistrationBuilder<TConcrete> where TConcrete : class
    {
        private readonly Registration _registration;

        internal RegistrationBuilder(Registration registration)
        {
            _registration = registration;
        }

        /// <summary>
        /// Also resolves this registration by <typeparamref name="TInterface"/> (an interface or a
        /// base class it derives from). The first call locks the concrete key — <c>Get&lt;TConcrete&gt;()</c>
        /// stops working so callers must depend on the abstraction — unless <see cref="AsSelf"/> is
        /// chained, before or after. All keys share the same instance. Up to four are allowed.
        /// </summary>
        /// <typeparam name="TInterface">The abstraction to expose the service as.</typeparam>
        /// <returns>The same builder, for chaining.</returns>
        /// <exception cref="ServiceRegistrationException">
        /// <typeparamref name="TConcrete"/> is not assignable to <typeparamref name="TInterface"/>,
        /// four interface keys are already published, the key is taken in this scope, or the builder
        /// is <c>default</c>.
        /// </exception>
        public RegistrationBuilder<TConcrete> As<TInterface>() where TInterface : class
        {
            ServiceRegistry.AddInterface(Require(), typeof(TInterface));
            return this;
        }

        /// <summary>
        /// Keeps the concrete type resolvable alongside any <see cref="As{TInterface}"/> keys. Order
        /// independent: chaining it before or after <c>.As&lt;T&gt;()</c> has the same effect. Without
        /// any <c>.As&lt;T&gt;()</c> call the self key is published anyway, so this is then a no-op.
        /// </summary>
        /// <returns>The same builder, for chaining.</returns>
        /// <exception cref="ServiceRegistrationException">The builder is <c>default</c>.</exception>
        /// <exception cref="ServiceLocatorException">The owning scope has been released.</exception>
        public RegistrationBuilder<TConcrete> AsSelf()
        {
            ServiceRegistry.EnableSelf(Require());
            return this;
        }

        /// <summary>
        /// Creates the shared instance now instead of on first resolution — useful for services that
        /// must start doing work immediately. On an instance registration this is a no-op, because
        /// the instance already exists.
        /// </summary>
        /// <returns>The same builder, for chaining.</returns>
        /// <exception cref="ServiceRegistrationException">
        /// The registration is <see cref="Lifetime.Transient"/> (nothing to pre-create), or the
        /// builder is <c>default</c>.
        /// </exception>
        /// <exception cref="ServiceLocatorException">The factory returned null or resolved circularly.</exception>
        public RegistrationBuilder<TConcrete> NonLazy()
        {
            ServiceRegistry.ForceCreate(Require());
            return this;
        }

        /// <summary>
        /// Declares that something else owns the instance's lifetime, so the locator will not call
        /// <see cref="System.IDisposable.Dispose"/> on it when the owning scope is released.
        /// </summary>
        /// <returns>The same builder, for chaining.</returns>
        /// <exception cref="ServiceRegistrationException">The builder is <c>default</c>.</exception>
        /// <exception cref="ServiceLocatorException">The owning scope has been released.</exception>
        public RegistrationBuilder<TConcrete> ExternallyOwned()
        {
            ServiceRegistry.MarkExternallyOwned(Require());
            return this;
        }

        private Registration Require()
        {
            var registration = _registration;
            if (registration == null)
            {
                throw new ServiceRegistrationException(
                    $"This RegistrationBuilder<{typeof(TConcrete).Name}> is not attached to a registration. " +
                    "Builders must come from ServiceLocator.Register(...) or ServiceScope.Register(...); " +
                    "a default(RegistrationBuilder<T>) has nothing to configure.");
            }

            return registration;
        }
    }
}
