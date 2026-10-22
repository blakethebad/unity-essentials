public interface ITileEntityView
{
	void Bind(ITileEntity tileEntity);

	public void SetEntityPosition(int x, int y);

	public void MoveToTile(Tile tile);
}
