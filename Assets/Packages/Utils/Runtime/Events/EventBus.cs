using System;
using UnityEngine;

namespace UnityEssentials.Utilities
{
    public static class EventBus
    {
        public static void Subscribe<TEvent>(Action<TEvent> handler) where TEvent : struct, IEvent
        {
            MainThreadGuard.AssertMainThread();

            if (handler == null)
                throw new ArgumentNullException(nameof(handler));

            var current = Channel<TEvent>.Handlers;
            var updated = new Action<TEvent>[current.Length + 1];
            Array.Copy(current, updated, current.Length);
            updated[current.Length] = handler;
            Channel<TEvent>.Handlers = updated;
        }

        public static void Unsubscribe<TEvent>(Action<TEvent> handler) where TEvent : struct, IEvent
        {
            MainThreadGuard.AssertMainThread();

            if (handler == null)
                throw new ArgumentNullException(nameof(handler));

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

        public static void Publish<TEvent>(in TEvent evt) where TEvent : struct, IEvent
        {
            MainThreadGuard.AssertMainThread();

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

        public static void Clear<TEvent>() where TEvent : struct, IEvent
        {
            MainThreadGuard.AssertMainThread();
            Channel<TEvent>.Handlers = Array.Empty<Action<TEvent>>();
        }

        internal static class Channel<TEvent> where TEvent : struct, IEvent
        {
            internal static Action<TEvent>[] Handlers = Array.Empty<Action<TEvent>>();
        }
    }
}
