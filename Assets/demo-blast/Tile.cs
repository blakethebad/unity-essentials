public class Tile
{
	public int X { get; private set; }
	public int Y { get; private set; }

	public ITileEntity Entity;

	public void Initialize( int x, int y)
	{
		X = x;
		Y = y;
	}

	public void BindEntity(ITileEntity tileEntity)
	{
		Entity = tileEntity;
	}
}

