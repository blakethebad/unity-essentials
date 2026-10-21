public enum EntityType
{
	None = 0,
	Stone = 1,
}

public struct TileEntityData
{
	public EntityType entityType;
	public StoneColor stoneColor;
}
