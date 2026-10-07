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
            EventBus.Clear<DamageEvent>();
            EventBus.Clear<ScoreEvent>();
        }

        [Test]
        public void HandlerUnsubscribingItself_FinishesThisPublishAndIsGoneFromTheNext()
        {
            Action<DamageEvent> selfRemoving = null;
            selfRemoving = _ =>
            {
                _calls.Add("self");
                EventBus.Unsubscribe(selfRemoving);
            };

            EventBus.Subscribe(selfRemoving);
            EventBus.Subscribe<DamageEvent>(_ => _calls.Add("other"));

            EventBus.Publish(new DamageEvent());
            CollectionAssert.AreEqual(new[] { "self", "other" }, _calls);

            EventBus.Publish(new DamageEvent());
            CollectionAssert.AreEqual(new[] { "self", "other", "other" }, _calls);
        }

        [Test]
        public void HandlerUnsubscribingALaterHandler_StillDeliversToItThisPublishOnly()
        {
            Action<DamageEvent> victim = _ => _calls.Add("victim");
            EventBus.Subscribe<DamageEvent>(_ =>
            {
                _calls.Add("remover");
                EventBus.Unsubscribe(victim);
            });
            EventBus.Subscribe(victim);

            EventBus.Publish(new DamageEvent());
            CollectionAssert.AreEqual(new[] { "remover", "victim" }, _calls);

            EventBus.Publish(new DamageEvent());
            CollectionAssert.AreEqual(new[] { "remover", "victim", "remover" }, _calls);
        }

        [Test]
        public void HandlerSubscribingANewHandler_SkipsThisPublishAndRunsOnTheNext()
        {
            var added = false;
            EventBus.Subscribe<DamageEvent>(_ =>
            {
                _calls.Add("adder");
                if (added)
                {
                    return;
                }

                added = true;
                EventBus.Subscribe<DamageEvent>(__ => _calls.Add("newcomer"));
            });

            EventBus.Publish(new DamageEvent());
            CollectionAssert.AreEqual(new[] { "adder" }, _calls);

            EventBus.Publish(new DamageEvent());
            CollectionAssert.AreEqual(new[] { "adder", "adder", "newcomer" }, _calls);
        }

        [Test]
        public void HandlerClearingTheChannel_StillDeliversToTheRestOfThisPublish()
        {
            EventBus.Subscribe<DamageEvent>(_ =>
            {
                _calls.Add("clearer");
                EventBus.Clear<DamageEvent>();
            });
            EventBus.Subscribe<DamageEvent>(_ => _calls.Add("survivor"));

            EventBus.Publish(new DamageEvent());
            CollectionAssert.AreEqual(new[] { "clearer", "survivor" }, _calls);

            EventBus.Publish(new DamageEvent());
            CollectionAssert.AreEqual(new[] { "clearer", "survivor" }, _calls);
        }

        [Test]
        public void RecursivePublishOfAnotherEvent_CompletesBeforeTheOuterPublishResumes()
        {
            EventBus.Subscribe<ScoreEvent>(evt => _calls.Add($"score:{evt.Points}"));
            EventBus.Subscribe<DamageEvent>(_ =>
            {
                _calls.Add("damage-first");
                EventBus.Publish(new ScoreEvent { Points = 7 });
            });
            EventBus.Subscribe<DamageEvent>(_ => _calls.Add("damage-second"));

            EventBus.Publish(new DamageEvent());

            CollectionAssert.AreEqual(new[] { "damage-first", "score:7", "damage-second" }, _calls);
        }

        [Test]
        public void RecursivePublishOfTheSameEvent_ReentersTheSameSnapshotWithoutLooping()
        {
            var depth = 0;
            EventBus.Subscribe<DamageEvent>(_ =>
            {
                depth++;
                _calls.Add($"recursive:{depth}");
                if (depth == 1)
                {
                    EventBus.Publish(new DamageEvent());
                }

                depth--;
            });
            EventBus.Subscribe<DamageEvent>(_ => _calls.Add("tail"));

            EventBus.Publish(new DamageEvent());

            CollectionAssert.AreEqual(new[] { "recursive:1", "recursive:2", "tail", "tail" }, _calls);
        }

        [Test]
        public void UnsubscribeDuringPublish_TakesEffectForAPublishStartedAfterwards()
        {
            var reentered = false;
            Action<DamageEvent> victim = _ => _calls.Add("victim");
            EventBus.Subscribe<DamageEvent>(_ =>
            {
                _calls.Add("remover");
                if (reentered)
                {
                    return;
                }

                reentered = true;
                EventBus.Unsubscribe(victim);

                // This publish starts after the mutation, so it takes the shortened array as its
                // snapshot while the outer publish keeps running the one it captured earlier.
                EventBus.Publish(new DamageEvent());
            });
            EventBus.Subscribe(victim);

            EventBus.Publish(new DamageEvent());

            CollectionAssert.AreEqual(new[] { "remover", "remover", "victim" }, _calls);
        }
    }
}
