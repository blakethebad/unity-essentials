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
    /// duplicate subscriptions, bus isolation, handler exception containment and the reset action.
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
            EventBus<TestBusA>.Clear<DamageEvent>();
            EventBus<TestBusA>.Clear<ScoreEvent>();
            EventBus<TestBusB>.Clear<DamageEvent>();
            EventBus<TestBusB>.Clear<ScoreEvent>();
        }

        [Test]
        public void Subscribe_NullHandler_ThrowsArgumentNullException()
        {
            Assert.Throws<ArgumentNullException>(() => EventBus<TestBusA>.Subscribe<DamageEvent>(null));
        }

        [Test]
        public void Unsubscribe_NullHandler_ThrowsArgumentNullException()
        {
            Assert.Throws<ArgumentNullException>(() => EventBus<TestBusA>.Unsubscribe<DamageEvent>(null));
        }

        [Test]
        public void Publish_DeliversPayloadToSubscriber()
        {
            var received = default(DamageEvent);
            var count = 0;
            EventBus<TestBusA>.Subscribe<DamageEvent>(evt =>
            {
                received = evt;
                count++;
            });

            EventBus<TestBusA>.Publish(new DamageEvent { Amount = 42, Source = "trap" });

            Assert.AreEqual(1, count);
            Assert.AreEqual(42, received.Amount);
            Assert.AreEqual("trap", received.Source);
        }

        [Test]
        public void Publish_WithNoSubscribers_DoesNothing()
        {
            Assert.DoesNotThrow(() => EventBus<TestBusA>.Publish(new DamageEvent { Amount = 1 }));
        }

        [Test]
        public void Publish_InvokesHandlersInSubscriptionOrder()
        {
            EventBus<TestBusA>.Subscribe<DamageEvent>(_ => _calls.Add("first"));
            EventBus<TestBusA>.Subscribe<DamageEvent>(_ => _calls.Add("second"));
            EventBus<TestBusA>.Subscribe<DamageEvent>(_ => _calls.Add("third"));

            EventBus<TestBusA>.Publish(new DamageEvent());

            CollectionAssert.AreEqual(new[] { "first", "second", "third" }, _calls);
        }

        [Test]
        public void Unsubscribe_StopsDelivery()
        {
            Action<DamageEvent> handler = _ => _calls.Add("handler");
            EventBus<TestBusA>.Subscribe(handler);

            EventBus<TestBusA>.Publish(new DamageEvent());
            EventBus<TestBusA>.Unsubscribe(handler);
            EventBus<TestBusA>.Publish(new DamageEvent());

            CollectionAssert.AreEqual(new[] { "handler" }, _calls);
        }

        [Test]
        public void Unsubscribe_KeepsTheHandlersAroundTheRemovedOne()
        {
            Action<DamageEvent> middle = _ => _calls.Add("middle");
            EventBus<TestBusA>.Subscribe<DamageEvent>(_ => _calls.Add("first"));
            EventBus<TestBusA>.Subscribe(middle);
            EventBus<TestBusA>.Subscribe<DamageEvent>(_ => _calls.Add("last"));

            EventBus<TestBusA>.Unsubscribe(middle);
            EventBus<TestBusA>.Publish(new DamageEvent());

            CollectionAssert.AreEqual(new[] { "first", "last" }, _calls);
        }

        [Test]
        public void Unsubscribe_HandlerThatWasNeverSubscribed_IsNoOp()
        {
            EventBus<TestBusA>.Subscribe<DamageEvent>(_ => _calls.Add("subscribed"));

            Assert.DoesNotThrow(() => EventBus<TestBusA>.Unsubscribe<DamageEvent>(_ => _calls.Add("stranger")));
            EventBus<TestBusA>.Publish(new DamageEvent());

            CollectionAssert.AreEqual(new[] { "subscribed" }, _calls);
        }

        [Test]
        public void Unsubscribe_OnEmptyChannel_IsNoOp()
        {
            Assert.DoesNotThrow(() => EventBus<TestBusA>.Unsubscribe<DamageEvent>(_ => _calls.Add("stranger")));
            Assert.AreEqual(0, EventBus<TestBusA>.Channel<DamageEvent>.Handlers.Length);
        }

        [Test]
        public void Subscribe_SameDelegateTwice_InvokesItTwicePerPublish()
        {
            Action<DamageEvent> handler = _ => _calls.Add("handler");
            EventBus<TestBusA>.Subscribe(handler);
            EventBus<TestBusA>.Subscribe(handler);

            EventBus<TestBusA>.Publish(new DamageEvent());

            CollectionAssert.AreEqual(new[] { "handler", "handler" }, _calls);
        }

        [Test]
        public void Unsubscribe_AfterDuplicateSubscribe_RemovesOneRegistration()
        {
            Action<DamageEvent> handler = _ => _calls.Add("handler");
            EventBus<TestBusA>.Subscribe(handler);
            EventBus<TestBusA>.Subscribe(handler);

            EventBus<TestBusA>.Unsubscribe(handler);
            EventBus<TestBusA>.Publish(new DamageEvent());

            CollectionAssert.AreEqual(new[] { "handler" }, _calls);
        }

        [Test]
        public void Clear_RemovesEveryHandlerForThatEvent()
        {
            EventBus<TestBusA>.Subscribe<DamageEvent>(_ => _calls.Add("damage"));
            EventBus<TestBusA>.Subscribe<DamageEvent>(_ => _calls.Add("damage"));
            EventBus<TestBusA>.Subscribe<ScoreEvent>(_ => _calls.Add("score"));

            EventBus<TestBusA>.Clear<DamageEvent>();
            EventBus<TestBusA>.Publish(new DamageEvent());
            EventBus<TestBusA>.Publish(new ScoreEvent());

            Assert.AreEqual(0, EventBus<TestBusA>.Channel<DamageEvent>.Handlers.Length);
            CollectionAssert.AreEqual(new[] { "score" }, _calls);
        }

        [Test]
        public void Clear_OnEmptyChannel_IsNoOp()
        {
            Assert.DoesNotThrow(() => EventBus<TestBusA>.Clear<DamageEvent>());
        }

        [Test]
        public void Publish_OnOneBus_DoesNotReachTheSameEventOnAnother()
        {
            EventBus<TestBusA>.Subscribe<DamageEvent>(evt => _calls.Add($"A:{evt.Amount}"));
            EventBus<TestBusB>.Subscribe<DamageEvent>(evt => _calls.Add($"B:{evt.Amount}"));

            EventBus<TestBusA>.Publish(new DamageEvent { Amount = 1 });
            EventBus<TestBusB>.Publish(new DamageEvent { Amount = 2 });

            CollectionAssert.AreEqual(new[] { "A:1", "B:2" }, _calls);
        }

        [Test]
        public void Clear_OnOneBus_LeavesTheOtherBusSubscribed()
        {
            EventBus<TestBusA>.Subscribe<DamageEvent>(_ => _calls.Add("A"));
            EventBus<TestBusB>.Subscribe<DamageEvent>(_ => _calls.Add("B"));

            EventBus<TestBusA>.Clear<DamageEvent>();
            EventBus<TestBusA>.Publish(new DamageEvent());
            EventBus<TestBusB>.Publish(new DamageEvent());

            CollectionAssert.AreEqual(new[] { "B" }, _calls);
        }

        [Test]
        public void Publish_DeliversOnlyToTheMatchingEventType()
        {
            EventBus<TestBusA>.Subscribe<DamageEvent>(_ => _calls.Add("damage"));
            EventBus<TestBusA>.Subscribe<ScoreEvent>(_ => _calls.Add("score"));

            EventBus<TestBusA>.Publish(new ScoreEvent { Points = 3 });

            CollectionAssert.AreEqual(new[] { "score" }, _calls);
        }

        [Test]
        public void Publish_HandlerThrows_LogsItAndKeepsRunningTheRest()
        {
            LogAssert.Expect(LogType.Exception, new Regex(HandlerFailureMessage));

            EventBus<TestBusA>.Subscribe<DamageEvent>(_ => _calls.Add("before"));
            EventBus<TestBusA>.Subscribe<DamageEvent>(_ => throw new InvalidOperationException(HandlerFailureMessage));
            EventBus<TestBusA>.Subscribe<DamageEvent>(_ => _calls.Add("after"));

            Assert.DoesNotThrow(() => EventBus<TestBusA>.Publish(new DamageEvent()));

            CollectionAssert.AreEqual(new[] { "before", "after" }, _calls);
        }

        [Test]
        public void Publish_HandlerThrows_StaysSubscribedForTheNextPublish()
        {
            LogAssert.Expect(LogType.Exception, new Regex(HandlerFailureMessage));
            LogAssert.Expect(LogType.Exception, new Regex(HandlerFailureMessage));

            EventBus<TestBusA>.Subscribe<DamageEvent>(_ =>
            {
                _calls.Add("thrower");
                throw new InvalidOperationException(HandlerFailureMessage);
            });

            EventBus<TestBusA>.Publish(new DamageEvent());
            EventBus<TestBusA>.Publish(new DamageEvent());

            CollectionAssert.AreEqual(new[] { "thrower", "thrower" }, _calls);
        }

        [Test]
        public void ResetStatics_RestoresEveryTouchedChannelToEmpty()
        {
            EventBus<TestBusA>.Subscribe<DamageEvent>(_ => _calls.Add("A damage"));
            EventBus<TestBusA>.Subscribe<ScoreEvent>(_ => _calls.Add("A score"));
            EventBus<TestBusB>.Subscribe<DamageEvent>(_ => _calls.Add("B damage"));

            StaticResetRegistry.ResetStatics();

            Assert.AreEqual(0, EventBus<TestBusA>.Channel<DamageEvent>.Handlers.Length);
            Assert.AreEqual(0, EventBus<TestBusA>.Channel<ScoreEvent>.Handlers.Length);
            Assert.AreEqual(0, EventBus<TestBusB>.Channel<DamageEvent>.Handlers.Length);

            EventBus<TestBusA>.Publish(new DamageEvent());
            EventBus<TestBusA>.Publish(new ScoreEvent());
            EventBus<TestBusB>.Publish(new DamageEvent());

            CollectionAssert.IsEmpty(_calls);
        }

        [Test]
        public void Subscribe_AfterReset_WorksAgain()
        {
            EventBus<TestBusA>.Subscribe<DamageEvent>(_ => _calls.Add("stale"));
            StaticResetRegistry.ResetStatics();

            EventBus<TestBusA>.Subscribe<DamageEvent>(_ => _calls.Add("fresh"));
            EventBus<TestBusA>.Publish(new DamageEvent());

            CollectionAssert.AreEqual(new[] { "fresh" }, _calls);
        }
    }
}
