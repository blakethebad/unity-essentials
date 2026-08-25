using System;
using System.Collections.Generic;
using NUnit.Framework;

namespace UnityEssentials.Utilities.Tests
{
    /// <summary>
    /// Pins the snapshot semantics of Publish: mutations made from inside a handler apply to the
    /// next publish, never to the one in flight. Also covers recursive publishing.
    /// </summary>
    [TestFixture]
    public class EventBusMutationDuringPublishTests
    {
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
        }

        [Test]
        public void HandlerUnsubscribingItself_FinishesThisPublishAndIsGoneFromTheNext()
        {
            Action<DamageEvent> selfRemoving = null;
            selfRemoving = _ =>
            {
                _calls.Add("self");
                EventBus<TestBusA>.Unsubscribe(selfRemoving);
            };

            EventBus<TestBusA>.Subscribe(selfRemoving);
            EventBus<TestBusA>.Subscribe<DamageEvent>(_ => _calls.Add("other"));

            EventBus<TestBusA>.Publish(new DamageEvent());
            CollectionAssert.AreEqual(new[] { "self", "other" }, _calls);

            EventBus<TestBusA>.Publish(new DamageEvent());
            CollectionAssert.AreEqual(new[] { "self", "other", "other" }, _calls);
        }

        [Test]
        public void HandlerUnsubscribingALaterHandler_StillDeliversToItThisPublishOnly()
        {
            Action<DamageEvent> victim = _ => _calls.Add("victim");
            EventBus<TestBusA>.Subscribe<DamageEvent>(_ =>
            {
                _calls.Add("remover");
                EventBus<TestBusA>.Unsubscribe(victim);
            });
            EventBus<TestBusA>.Subscribe(victim);

            EventBus<TestBusA>.Publish(new DamageEvent());
            CollectionAssert.AreEqual(new[] { "remover", "victim" }, _calls);

            EventBus<TestBusA>.Publish(new DamageEvent());
            CollectionAssert.AreEqual(new[] { "remover", "victim", "remover" }, _calls);
        }

        [Test]
        public void HandlerSubscribingANewHandler_SkipsThisPublishAndRunsOnTheNext()
        {
            var added = false;
            EventBus<TestBusA>.Subscribe<DamageEvent>(_ =>
            {
                _calls.Add("adder");
                if (added)
                {
                    return;
                }

                added = true;
                EventBus<TestBusA>.Subscribe<DamageEvent>(__ => _calls.Add("newcomer"));
            });

            EventBus<TestBusA>.Publish(new DamageEvent());
            CollectionAssert.AreEqual(new[] { "adder" }, _calls);

            EventBus<TestBusA>.Publish(new DamageEvent());
            CollectionAssert.AreEqual(new[] { "adder", "adder", "newcomer" }, _calls);
        }

        [Test]
        public void HandlerClearingTheChannel_StillDeliversToTheRestOfThisPublish()
        {
            EventBus<TestBusA>.Subscribe<DamageEvent>(_ =>
            {
                _calls.Add("clearer");
                EventBus<TestBusA>.Clear<DamageEvent>();
            });
            EventBus<TestBusA>.Subscribe<DamageEvent>(_ => _calls.Add("survivor"));

            EventBus<TestBusA>.Publish(new DamageEvent());
            CollectionAssert.AreEqual(new[] { "clearer", "survivor" }, _calls);

            EventBus<TestBusA>.Publish(new DamageEvent());
            CollectionAssert.AreEqual(new[] { "clearer", "survivor" }, _calls);
        }

        [Test]
        public void RecursivePublishOfAnotherEvent_CompletesBeforeTheOuterPublishResumes()
        {
            EventBus<TestBusA>.Subscribe<ScoreEvent>(evt => _calls.Add($"score:{evt.Points}"));
            EventBus<TestBusA>.Subscribe<DamageEvent>(_ =>
            {
                _calls.Add("damage-first");
                EventBus<TestBusA>.Publish(new ScoreEvent { Points = 7 });
            });
            EventBus<TestBusA>.Subscribe<DamageEvent>(_ => _calls.Add("damage-second"));

            EventBus<TestBusA>.Publish(new DamageEvent());

            CollectionAssert.AreEqual(new[] { "damage-first", "score:7", "damage-second" }, _calls);
        }

        [Test]
        public void RecursivePublishOfTheSameEvent_ReentersTheSameSnapshotWithoutLooping()
        {
            var depth = 0;
            EventBus<TestBusA>.Subscribe<DamageEvent>(_ =>
            {
                depth++;
                _calls.Add($"recursive:{depth}");
                if (depth == 1)
                {
                    EventBus<TestBusA>.Publish(new DamageEvent());
                }

                depth--;
            });
            EventBus<TestBusA>.Subscribe<DamageEvent>(_ => _calls.Add("tail"));

            EventBus<TestBusA>.Publish(new DamageEvent());

            CollectionAssert.AreEqual(new[] { "recursive:1", "recursive:2", "tail", "tail" }, _calls);
        }

        [Test]
        public void UnsubscribeDuringPublish_TakesEffectForAPublishStartedAfterwards()
        {
            var reentered = false;
            Action<DamageEvent> victim = _ => _calls.Add("victim");
            EventBus<TestBusA>.Subscribe<DamageEvent>(_ =>
            {
                _calls.Add("remover");
                if (reentered)
                {
                    return;
                }

                reentered = true;
                EventBus<TestBusA>.Unsubscribe(victim);

                // This publish starts after the mutation, so it takes the shortened array as its
                // snapshot while the outer publish keeps running the one it captured earlier.
                EventBus<TestBusA>.Publish(new DamageEvent());
            });
            EventBus<TestBusA>.Subscribe(victim);

            EventBus<TestBusA>.Publish(new DamageEvent());

            CollectionAssert.AreEqual(new[] { "remover", "remover", "victim" }, _calls);
        }
    }
}
