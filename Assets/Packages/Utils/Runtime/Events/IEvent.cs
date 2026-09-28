namespace UnityEssentials.Utilities
{
    /// <summary>
    /// Marker for a value type carried by <see cref="EventBus"/>. Events are structs so that
    /// publishing never allocates and handlers cannot mutate the publisher's copy.
    /// </summary>
    public interface IEvent
    {
    }
}
