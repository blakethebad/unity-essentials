public class Stone : ITileEntity
{
	public StoneColor Color => _stoneColor;
	private StoneColor _stoneColor;

	public Stone(StoneColor stoneColor)
	{
		_stoneColor = stoneColor;
	}
}
