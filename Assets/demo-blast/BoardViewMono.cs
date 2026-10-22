using System.Collections.Generic;
using UnityEngine;
public interface IBoardListener
{
	void OnBoardGenerated(in Tile[] tiles);
	void OnEntityCreated(int posX, int posY, ITileEntity entity, TileEntityData entityData);
	void OnEntitiesMatched(List<ITileEntity> matchedEntities);
	void OnEntityMoved(ITileEntity tileEntity, Tile newTile);

}

public class BoardViewMono : MonoBehaviour, IBoardListener
{
	private Board _board;
	private ITileView[,] _tileViews;
	private Dictionary<ITileEntity, ITileEntityView> _entityViewMap;

	private ITileViewFactory _tileViewFactory;
	private ITileEntityFactory _tileEntityFactory;

	public void SetupView(Board board, ITileViewFactory tileViewFactory, ITileEntityFactory entityFactory)
	{
		_board = board;
		_tileViewFactory = tileViewFactory;
		_tileEntityFactory = entityFactory;
		_entityViewMap = new Dictionary<ITileEntity, ITileEntityView>();
	}

	public void OnBoardGenerated(in Tile[] tiles)
	{
		for(int i = 0; i < tiles.Length; i++)
		{
			var tile = tiles[i];
			var tileView = _tileViewFactory.GetTileView();
			tileView.Bind(tile);
			tileView.SetTilePosition(tile.X, tile.Y);
			tileView.SetListener(_board);
		}
	}

	public void OnEntityCreated(int posX, int posY, ITileEntity entity, TileEntityData entityData)
	{
		var entityView = _tileEntityFactory.CreateView(in entityData);
		_entityViewMap.Add(entity, entityView);
		entityView.Bind(entity);
		entityView.SetEntityPosition(posX, posY);
	}

	public void OnEntitiesMatched(List<ITileEntity> matchedEntities)
	{
		foreach(var entity in matchedEntities)
		{
			if(_entityViewMap.TryGetValue(entity, out var view))
			{
				_tileEntityFactory.ReleaseView(view);
				_entityViewMap.Remove(entity);
			}
		}
	}

    public void OnEntityMoved(ITileEntity tileEntity, Tile newTile)
    {
		if(!_entityViewMap.TryGetValue(tileEntity, out var view))
			return;

		view.MoveToTile(newTile);
    }
}
