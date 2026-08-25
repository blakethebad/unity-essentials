namespace UnityEssentials.Utilities
{
    /// <summary>
    /// Marker for a bus discriminator: an empty struct used as the <c>TBus</c> argument of
    /// <see cref="EventBus{TBus}"/>. Each discriminator closes the generic over its own statics,
    /// giving the consuming system a channel space no other system can publish into.
    /// </summary>
    public interface IEventBus
    {
    }
}
