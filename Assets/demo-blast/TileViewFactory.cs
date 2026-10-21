using UnityEngine;

public class TileViewFactory : ITileViewFactory
{
	//TODO: Later would be asset reference
	private TileViewMono _tileViewPrefab;
	private Transform _tileViewParent;

	public TileViewFactory(TileViewMono tileViewPrefab, Transform tileViewParent)
	{
		_tileViewPrefab = tileViewPrefab;
		_tileViewParent = tileViewParent;
	}

    public ITileView GetTileView()
    {
		return Object.Instantiate<TileViewMono>(_tileViewPrefab, _tileViewParent) as ITileView;
    }
}
