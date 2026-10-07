using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace UnityEssentials.Utilities.Tests
{
    /// <summary>
    /// Covers the EventBus contract: delivery and ordering, subscribe/unsubscribe bookkeeping,
    /// duplicate subscriptions, channel isolation and handler exception containment.
    /// </summary>
    [TestFixture]
    public class EventBusTests
    {
        private const string HandlerFailureMessage = "event handler failure";

        private List<string> _calls;

        [SetUp]
        public void SetUp()
        {
            _calls = new List<string>();
            ClearAllChannels();
        }

        [TearDown]
        public void TearDown()
        {
            ClearAllChannels();
        }

        private static void ClearAllChannels()
        {
            EventBus.Clear<DamageEvent>();
            EventBus.Clear<ScoreEvent>();
        }

        [Test]
        public void Subscribe_NullHandler_ThrowsArgumentNullException()
        {
            Assert.Throws<ArgumentNullException>(() => EventBus.Subscribe<DamageEvent>(null));
        }

        [Test]
        public void Unsubscribe_NullHandler_ThrowsArgumentNullException()
        {
            Assert.Throws<ArgumentNullException>(() => EventBus.Unsubscribe<DamageEvent>(null));
        }

        [Test]
        public void Publish_DeliversPayloadToSubscriber()
        {
            var received = default(DamageEvent);
            var count = 0;
            EventBus.Subscribe<DamageEvent>(evt =>
            {
                received = evt;
                count++;
            });

            EventBus.Publish(new DamageEvent { Amount = 42, Source = "trap" });

            Assert.AreEqual(1, count);
            Assert.AreEqual(42, received.Amount);
            Assert.AreEqual("trap", received.Source);
        }

        [Test]
        public void Publish_WithNoSubscribers_DoesNothing()
        {
            Assert.DoesNotThrow(() => EventBus.Publish(new DamageEvent { Amount = 1 }));
        }

        [Test]
        public void Publish_InvokesHandlersInSubscriptionOrder()
        {
            EventBus.Subscribe<DamageEvent>(_ => _calls.Add("first"));
            EventBus.Subscribe<DamageEvent>(_ => _calls.Add("second"));
            EventBus.Subscribe<DamageEvent>(_ => _calls.Add("third"));

            EventBus.Publish(new DamageEvent());

            CollectionAssert.AreEqual(new[] { "first", "second", "third" }, _calls);
        }

        [Test]
        public void Unsubscribe_StopsDelivery()
        {
            Action<DamageEvent> handler = _ => _calls.Add("handler");
            EventBus.Subscribe(handler);

            EventBus.Publish(new DamageEvent());
            EventBus.Unsubscribe(handler);
            EventBus.Publish(new DamageEvent());

            CollectionAssert.AreEqual(new[] { "handler" }, _calls);
        }

        [Test]
        public void Unsubscribe_KeepsTheHandlersAroundTheRemovedOne()
        {
            Action<DamageEvent> middle = _ => _calls.Add("middle");
            EventBus.Subscribe<DamageEvent>(_ => _calls.Add("first"));
            EventBus.Subscribe(middle);
            EventBus.Subscribe<DamageEvent>(_ => _calls.Add("last"));

            EventBus.Unsubscribe(middle);
            EventBus.Publish(new DamageEvent());

            CollectionAssert.AreEqual(new[] { "first", "last" }, _calls);
        }

        [Test]
        public void Unsubscribe_HandlerThatWasNeverSubscribed_IsNoOp()
        {
            EventBus.Subscribe<DamageEvent>(_ => _calls.Add("subscribed"));

            Assert.DoesNotThrow(() => EventBus.Unsubscribe<DamageEvent>(_ => _calls.Add("stranger")));
            EventBus.Publish(new DamageEvent());

            CollectionAssert.AreEqual(new[] { "subscribed" }, _calls);
        }

        [Test]
        public void Unsubscribe_OnEmptyChannel_IsNoOp()
        {
            Assert.DoesNotThrow(() => EventBus.Unsubscribe<DamageEvent>(_ => _calls.Add("stranger")));
            Assert.AreEqual(0, EventBus.Channel<DamageEvent>.Handlers.Length);
        }

        [Test]
        public void Subscribe_SameDelegateTwice_InvokesItTwicePerPublish()
        {
            Action<DamageEvent> handler = _ => _calls.Add("handler");
            EventBus.Subscribe(handler);
            EventBus.Subscribe(handler);

            EventBus.Publish(new DamageEvent());

            CollectionAssert.AreEqual(new[] { "handler", "handler" }, _calls);
        }

        [Test]
        public void Unsubscribe_AfterDuplicateSubscribe_RemovesOneRegistration()
        {
            Action<DamageEvent> handler = _ => _calls.Add("handler");
            EventBus.Subscribe(handler);
            EventBus.Subscribe(handler);

            EventBus.Unsubscribe(handler);
            EventBus.Publish(new DamageEvent());

            CollectionAssert.AreEqual(new[] { "handler" }, _calls);
        }

        [Test]
        public void Clear_RemovesEveryHandlerForThatEvent()
        {
            EventBus.Subscribe<DamageEvent>(_ => _calls.Add("damage"));
            EventBus.Subscribe<DamageEvent>(_ => _calls.Add("damage"));
            EventBus.Subscribe<ScoreEvent>(_ => _calls.Add("score"));

            EventBus.Clear<DamageEvent>();
            EventBus.Publish(new DamageEvent());
            EventBus.Publish(new ScoreEvent());

            Assert.AreEqual(0, EventBus.Channel<DamageEvent>.Handlers.Length);
            CollectionAssert.AreEqual(new[] { "score" }, _calls);
        }

        [Test]
        public void Clear_OnEmptyChannel_IsNoOp()
        {
            Assert.DoesNotThrow(() => EventBus.Clear<DamageEvent>());
        }

        [Test]
        public void Clear_LeavesTheOtherEventTypeSubscribed()
        {
            EventBus.Subscribe<DamageEvent>(_ => _calls.Add("damage"));
            EventBus.Subscribe<ScoreEvent>(_ => _calls.Add("score"));

            EventBus.Clear<DamageEvent>();
            EventBus.Publish(new DamageEvent());
            EventBus.Publish(new ScoreEvent());

            Assert.AreEqual(1, EventBus.Channel<ScoreEvent>.Handlers.Length);
            CollectionAssert.AreEqual(new[] { "score" }, _calls);
        }

        [Test]
        public void Publish_DeliversOnlyToTheMatchingEventType()
        {
            EventBus.Subscribe<DamageEvent>(_ => _calls.Add("damage"));
            EventBus.Subscribe<ScoreEvent>(_ => _calls.Add("score"));

            EventBus.Publish(new ScoreEvent { Points = 3 });

            CollectionAssert.AreEqual(new[] { "score" }, _calls);
        }

        [Test]
        public void Publish_HandlerThrows_LogsItAndKeepsRunningTheRest()
        {
            LogAssert.Expect(LogType.Exception, new Regex(HandlerFailureMessage));

            EventBus.Subscribe<DamageEvent>(_ => _calls.Add("before"));
            EventBus.Subscribe<DamageEvent>(_ => throw new InvalidOperationException(HandlerFailureMessage));
            EventBus.Subscribe<DamageEvent>(_ => _calls.Add("after"));

            Assert.DoesNotThrow(() => EventBus.Publish(new DamageEvent()));

            CollectionAssert.AreEqual(new[] { "before", "after" }, _calls);
        }

        [Test]
        public void Publish_HandlerThrows_StaysSubscribedForTheNextPublish()
        {
            LogAssert.Expect(LogType.Exception, new Regex(HandlerFailureMessage));
            LogAssert.Expect(LogType.Exception, new Regex(HandlerFailureMessage));

            EventBus.Subscribe<DamageEvent>(_ =>
            {
                _calls.Add("thrower");
                throw new InvalidOperationException(HandlerFailureMessage);
            });

            EventBus.Publish(new DamageEvent());
            EventBus.Publish(new DamageEvent());

            CollectionAssert.AreEqual(new[] { "thrower", "thrower" }, _calls);
        }

        [Test]
        public void Subscribe_AfterClear_WorksAgain()
        {
            EventBus.Subscribe<DamageEvent>(_ => _calls.Add("stale"));
            EventBus.Clear<DamageEvent>();

            EventBus.Subscribe<DamageEvent>(_ => _calls.Add("fresh"));
            EventBus.Publish(new DamageEvent());

            CollectionAssert.AreEqual(new[] { "fresh" }, _calls);
        }
    }
}
