namespace UnityEssentials.Utilities.Tests
{
    /// <summary>Payload-carrying event used by the EventBus fixtures.</summary>
    public struct DamageEvent : IEvent
    {
        public int Amount;
        public string Source;
    }

    /// <summary>Second event type, so fixtures can prove channels do not bleed into each other.</summary>
    public struct ScoreEvent : IEvent
    {
        public int Points;
    }

    /// <summary>Bus discriminator; pairs with <see cref="TestBusB"/> to prove bus isolation.</summary>
    public struct TestBusA : IEventBus
    {
    }

    /// <summary>Bus discriminator; pairs with <see cref="TestBusA"/> to prove bus isolation.</summary>
    public struct TestBusB : IEventBus
    {
    }
}
