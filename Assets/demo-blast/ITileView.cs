public interface ITileView
{
	void Bind(Tile tile);
	void SetTilePosition(int x, int y);
	void SetListener(ITileListener tileListener);
}

