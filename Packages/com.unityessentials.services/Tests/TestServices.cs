using System;
using System.Collections.Generic;

namespace UnityEssentials.Services.Tests
{
    // ---------------------------------------------------------------------
    // Marker interfaces. Five of them so the 5th ".As<>()" cap can be tested.
    // ---------------------------------------------------------------------

    public interface IWeaponA { string Id { get; } }

    public interface IWeaponB { string Id { get; } }

    public interface IWeaponC { string Id { get; } }

    public interface IWeaponD { string Id { get; } }

    public interface IWeaponE { string Id { get; } }

    /// <summary>Implements every weapon interface (A-E).</summary>
    public class Sword : IWeaponA, IWeaponB, IWeaponC, IWeaponD, IWeaponE
    {
        public string Id => "Sword";
    }

    /// <summary>Second implementor, used for shadowing tests.</summary>
    public class Axe : IWeaponA, IWeaponB
    {
        public string Id => "Axe";
    }

    /// <summary>Third implementor, used for shadowing / fallback tests.</summary>
    public class Bow : IWeaponA
    {
        public string Id => "Bow";
    }

    /// <summary>Implements none of the weapon interfaces (unimplemented-interface test).</summary>
    public class Shield
    {
        public string Id => "Shield";
    }

    /// <summary>Never registered anywhere; used for miss tests.</summary>
    public class UnregisteredService
    {
    }

    // ---------------------------------------------------------------------
    // Construction counting (lazy vs NonLazy vs Transient).
    // ---------------------------------------------------------------------

    /// <summary>Counts how many times it has been constructed. Reset in SetUp.</summary>
    public class ConstructionCounter
    {
        public static int Count;

        public ConstructionCounter()
        {
            Count++;
        }

        public static void Reset()
        {
            Count = 0;
        }
    }

    /// <summary>Second counter so two lifetimes can be observed independently.</summary>
    public class OtherConstructionCounter
    {
        public static int Count;

        public OtherConstructionCounter()
        {
            Count++;
        }

        public static void Reset()
        {
            Count = 0;
        }
    }

    // ---------------------------------------------------------------------
    // Disposal order recording.
    // ---------------------------------------------------------------------

    /// <summary>Shared sink recording disposal order across all fake disposables.</summary>
    public static class DisposalLog
    {
        public static readonly List<string> Entries = new List<string>();

        public static void Clear()
        {
            Entries.Clear();
        }

        public static void Record(string name)
        {
            Entries.Add(name);
        }
    }

    public class DisposableA : IDisposable
    {
        public bool WasDisposed;

        public void Dispose()
        {
            WasDisposed = true;
            DisposalLog.Record("A");
        }
    }

    public class DisposableB : IDisposable
    {
        public bool WasDisposed;

        public void Dispose()
        {
            WasDisposed = true;
            DisposalLog.Record("B");
        }
    }

    public class DisposableC : IDisposable
    {
        public bool WasDisposed;

        public void Dispose()
        {
            WasDisposed = true;
            DisposalLog.Record("C");
        }
    }

    /// <summary>Disposable that also implements an interface, for interface-key disposal tests.</summary>
    public class DisposableWeapon : IWeaponA, IDisposable
    {
        public bool WasDisposed;

        public string Id => "DisposableWeapon";

        public void Dispose()
        {
            WasDisposed = true;
            DisposalLog.Record("Weapon");
        }
    }

    /// <summary>Records that it ran, then throws out of Dispose.</summary>
    public class ThrowingDisposable : IDisposable
    {
        public const string FailureMessage = "ThrowingDisposable failed on purpose";

        public void Dispose()
        {
            DisposalLog.Record("Throwing");
            throw new InvalidOperationException(FailureMessage);
        }
    }

    // ---------------------------------------------------------------------
    // Circular resolution.
    // ---------------------------------------------------------------------

    /// <summary>Resolves itself from its own constructor; registered via a factory.</summary>
    public class CircularService
    {
        public CircularService()
        {
            ServiceLocator.Get<CircularService>();
        }
    }
}
