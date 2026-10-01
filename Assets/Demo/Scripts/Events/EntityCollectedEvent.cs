using UnityEssentials.Utilities;

public struct EntityCollectedEvent : IEvent
{
	public int CurrentScore;
}

public struct EntityGrabbedEvent : IEvent
{
	public string EntityName;
}
