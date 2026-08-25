namespace UnityEssentials.Services
{
    /// <summary>
    /// Controls how many instances of a registered service the locator creates.
    /// </summary>
    public enum Lifetime : byte
    {
        /// <summary>
        /// One instance per registration, created lazily on the first resolution (or eagerly when
        /// <see cref="RegistrationBuilder{TConcrete}.NonLazy"/> is chained) and cached for every
        /// subsequent resolution. Shared instances that implement <see cref="System.IDisposable"/>
        /// are disposed when their owning scope is released, unless the registration is marked
        /// <see cref="RegistrationBuilder{TConcrete}.ExternallyOwned"/>.
        /// </summary>
        Shared = 0,

        /// <summary>
        /// A new instance per resolution. Transient instances are never cached, never tracked and
        /// never disposed by the locator — the caller owns whatever it resolves.
        /// </summary>
        Transient = 1
    }
}
