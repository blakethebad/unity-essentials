using System;
using UnityEngine;

namespace UnityEssentials.Utilities
{
    /// <summary>
    /// Statically dispatched publish/subscribe, one channel per event type. Handlers live in a
    /// per-event copy-on-write array, which keeps the warm publish path allocation free.
    /// </summary>
    /// <remarks>
    /// Publish iterates a snapshot of that array: a handler unsubscribed while a publish is in flight
    /// still receives the in-flight event, and one subscribed during it starts with the next publish.
    /// Subscribing the same delegate twice registers it twice, so it is invoked twice per publish;
    /// each Unsubscribe removes one registration. A handler that throws is reported through
    /// <see cref="Debug.LogException(Exception)"/> and the handlers behind it still run.
    /// </remarks>
    public static class EventBus
    {
        /// <summary>
        /// Registers <paramref name="handler"/> for <typeparamref name="TEvent"/>, behind every
        /// handler already subscribed. Subscribing twice makes it fire twice.
        /// </summary>
        public static void Subscribe<TEvent>(Action<TEvent> handler) where TEvent : struct, IEvent
        {
            StaticResetRegistry.AssertMainThread();

            if (handler == null)
            {
                throw new ArgumentNullException(nameof(handler));
            }

            var current = Channel<TEvent>.Handlers;
            var updated = new Action<TEvent>[current.Length + 1];
            Array.Copy(current, updated, current.Length);
            updated[current.Length] = handler;
            Channel<TEvent>.Handlers = updated;
        }

        /// <summary>
        /// Removes the first registration of <paramref name="handler"/> for
        /// <typeparamref name="TEvent"/>. A handler that is not subscribed is a no-op.
        /// </summary>
        public static void Unsubscribe<TEvent>(Action<TEvent> handler) where TEvent : struct, IEvent
        {
            StaticResetRegistry.AssertMainThread();

            if (handler == null)
            {
                throw new ArgumentNullException(nameof(handler));
            }

            var current = Channel<TEvent>.Handlers;
            var index = Array.IndexOf(current, handler);
            if (index < 0)
            {
                return;
            }

            if (current.Length == 1)
            {
                Channel<TEvent>.Handlers = Array.Empty<Action<TEvent>>();
                return;
            }

            var updated = new Action<TEvent>[current.Length - 1];
            Array.Copy(current, 0, updated, 0, index);
            Array.Copy(current, index + 1, updated, index, current.Length - index - 1);
            Channel<TEvent>.Handlers = updated;
        }

		//TODO: Maybe rename to raise instead of publish?? 
        /// <summary>
        /// Invokes every handler subscribed to <typeparamref name="TEvent"/> in subscription order,
        /// on the snapshot of handlers taken when the call started.
        /// </summary>
        public static void Publish<TEvent>(in TEvent evt) where TEvent : struct, IEvent
        {
            StaticResetRegistry.AssertMainThread();

            // Captured once: Subscribe and Unsubscribe swap the field for a fresh array rather than
            // mutating it, so this local keeps a consistent view even if a handler resubscribes.
            var handlers = Channel<TEvent>.Handlers;
            for (var i = 0; i < handlers.Length; i++)
            {
                try
                {
                    handlers[i].Invoke(evt);
                }
                catch (Exception exception)
                {
                    // One bad handler must not swallow the event for the handlers queued behind it.
                    Debug.LogException(exception);
                }
            }
        }

        /// <summary>Removes every handler subscribed to <typeparamref name="TEvent"/>.</summary>
        public static void Clear<TEvent>() where TEvent : struct, IEvent
        {
            StaticResetRegistry.AssertMainThread();
            Channel<TEvent>.Handlers = Array.Empty<Action<TEvent>>();
        }

        /// <summary>
        /// Backing store for one event type. Each closed generic gets its own statics, which is what
        /// makes the channels independent without a dictionary lookup on the hot path.
        /// </summary>
        internal static class Channel<TEvent> where TEvent : struct, IEvent
        {
            // Never null and never mutated in place: Subscribe/Unsubscribe publish a replacement
            // array, which is the whole reason an in-flight Publish can ignore concurrent mutation.
            internal static Action<TEvent>[] Handlers = Array.Empty<Action<TEvent>>();

            static Channel()
            {
                // RuntimeInitializeOnLoadMethod never fires on a generic type and closed generics
                // cannot be enumerated, so each channel hands its own reset over on first touch.
                StaticResetRegistry.Register(() => Handlers = Array.Empty<Action<TEvent>>());
            }
        }
    }
}
