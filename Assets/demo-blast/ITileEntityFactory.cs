public interface ITileEntityFactory
{
	ITileEntity Create(in TileEntityData entityData);
	ITileEntityView CreateView(in TileEntityData entityData);

	void ReleaseView(ITileEntityView tileEntityView);
}
