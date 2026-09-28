using UnityEssentials.Utilities;

public struct EntityCollectedEvent : IEvent
{
	
}

public struct EntityGrabbedEvent : IEvent
{
	public string EntityName;
}
