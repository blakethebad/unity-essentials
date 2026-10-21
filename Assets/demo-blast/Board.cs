using System.Collections.Generic;
using UnityEngine;
using UnityEssentials.Utilities;

public class Board : ITileListener
{
	private Tile[,] _tiles;
	private ITileView[,] _tileViews; //TODO: Probably going to move this into something like BoardViewMono
	
	private Dictionary<ITileEntity, ITileEntityView> _entityViewMap;
	private List<ITileEntity> _entities;

	private ITileViewFactory _tileViewFactory; //TODO: Moving to a board view might aswell save us from creating factories for other elements
	private ITileEntityFactory _tileEntityFactory;

	private StoneColor[] _tempColors = new StoneColor[] { StoneColor.Red, StoneColor.Blue, StoneColor.Green, StoneColor.Yellow, StoneColor.Purple };

	public Board(ITileViewFactory tileViewFactory, ITileEntityFactory tileEntityFactory)
	{
		_tileViewFactory = tileViewFactory;
		_tileEntityFactory = tileEntityFactory;
	}

	public void GenerateBoard(int sizeX, int sizeY)
	{
		_tiles = new Tile[sizeX, sizeY];
		_entities = new List<ITileEntity>();
		_entityViewMap = new Dictionary<ITileEntity, ITileEntityView>();

		for(var x = 0; x < sizeX; x++)
			for(var y = 0; y < sizeY; y++)
				SetupTile(x,y);
	}

	public void OnTileInputRequested(int x, int y)
	{
		var inputTile = _tiles[x,y];

		if(inputTile.Entity == null || inputTile.Entity is not Stone stone)
			return;

		var matchedTiles = new List<Tile>();
		var checkedTiles = new List<Tile>();
		CheckColorMatchAroundTile(x, y, stone.Color, matchedTiles, checkedTiles);

		MatchTiles(matchedTiles);
	}

	private void SetupTile(int x, int y)
	{
		var tile = new Tile();
		tile.Initialize(x, y);
		_tiles[x, y] = tile;

		//TODO: This part can also move to the BoardView part
		var tileView = _tileViewFactory.GetTileView();
		tileView.Bind(tile);
		tileView.SetTilePosition(x,y);
		tileView.SetListener(this);

		var randomColorIndex = Random.Range(0, _tempColors.Length - 1);
		var randomColor = _tempColors[randomColorIndex];
		var entityData = new TileEntityData()
		{
			entityType = EntityType.Stone,
			stoneColor = randomColor
		};
		var tileEntity = _tileEntityFactory.Create(in entityData);
		tile.BindEntity(tileEntity);

		//TODO: Probably move this into view side
		var tileEntityView = _tileEntityFactory.CreateView(in entityData);
		tileEntityView.Bind(tileEntity);
		tileEntityView.SetEntityPosition(x,y);

		_entities.Add(tileEntity);
		_entityViewMap.Add(tileEntity, tileEntityView);
	}

	private void CheckColorMatchAroundTile(int x, int y, StoneColor checkedColor, List<Tile> matchedTiles, List<Tile> chekcedTiles)
    {
		if(x >= _tiles.GetLength(0) || x < 0 || y >= _tiles.GetLength(1) || y < 0)
			return;

		var checkedTile = _tiles[x,y];
        if (chekcedTiles.Contains(checkedTile) || checkedTile.Entity is not Stone stone || stone.Color != checkedColor)
            return;

        chekcedTiles.Add(checkedTile);
        matchedTiles.Add(checkedTile);

		CheckColorMatchAroundTile(checkedTile.X - 1, checkedTile.Y ,checkedColor, matchedTiles, chekcedTiles);
		CheckColorMatchAroundTile(checkedTile.X + 1, checkedTile.Y ,checkedColor, matchedTiles, chekcedTiles);
		CheckColorMatchAroundTile(checkedTile.X, checkedTile.Y - 1 ,checkedColor, matchedTiles, chekcedTiles);
		CheckColorMatchAroundTile(checkedTile.X, checkedTile.Y + 1 ,checkedColor, matchedTiles, chekcedTiles);
    }

	private void MatchTiles(List<Tile> matchedTiles)
	{
		foreach(var tile in matchedTiles)
		{
			var entity = tile.Entity;
			var entityView = _entityViewMap.GetValueOrDefault(entity);
			_tileEntityFactory.ReleaseView(entityView);
			tile.Entity = null;
			_entities.Remove(entity);
		}
	}
}
