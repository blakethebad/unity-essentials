using System.Collections.Generic;
using UnityEngine;
using UnityEssentials.Utilities;

public class Board : ITileListener
{
	private Tile[,] _tiles;
	private Tile[] _tilesOrdered;

	private List<ITileEntity> _entities;
	private ITileEntityFactory _tileEntityFactory;
	private IBoardListener _listener;
	private StoneColor[] _tempColors = new StoneColor[] { StoneColor.Red, StoneColor.Blue, StoneColor.Green, StoneColor.Yellow, StoneColor.Purple };

	public Board(ITileEntityFactory tileEntityFactory, IBoardListener listener)
	{
		_tileEntityFactory = tileEntityFactory;
		_listener = listener;
	}

	public void GenerateBoard(int sizeX, int sizeY)
	{
		_tiles = new Tile[sizeX, sizeY];
		_tilesOrdered = new Tile[sizeX * sizeY];
		_entities = new List<ITileEntity>();

		var orderedIndex = 0;
		for(var y = 0; y < sizeY; y++)
			for(var x = 0; x < sizeX; x++)
			{
				var tile = SetupTile(x,y);
				_tilesOrdered[orderedIndex] = tile;
				orderedIndex++;
			}

		_listener.OnBoardGenerated(in _tilesOrdered);
	}

	private Tile SetupTile(int x, int y)
	{
		var tile = new Tile();
		tile.Initialize(x, y);
		_tiles[x, y] = tile;

		var randomColorIndex = Random.Range(0, _tempColors.Length - 1);
		var randomColor = _tempColors[randomColorIndex];
		var entityData = new TileEntityData()
		{
			entityType = EntityType.Stone,
			stoneColor = randomColor
		};

		var tileEntity = _tileEntityFactory.Create(in entityData);
		tile.BindEntity(tileEntity);
		_entities.Add(tileEntity);
		_listener.OnEntityCreated(x, y, tileEntity, entityData);

		return tile;
	}

	public void OnTileInputRequested(int x, int y)
	{
		var inputTile = _tiles[x,y];

		if(inputTile.Entity == null || inputTile.Entity is not Stone stone)
			return;

		var matchedTiles = new List<Tile>();
		var checkedTiles = new List<Tile>();
		CheckColorMatchAroundTile(x, y, stone.Color, matchedTiles, checkedTiles);

		if(matchedTiles.Count < 2)
			return;

		MatchTiles(matchedTiles);
		DropEntities();
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
		var matchedEntities = new List<ITileEntity>();
		foreach(var tile in matchedTiles)
		{
			var entity = tile.Entity;
			tile.Entity = null;
			_entities.Remove(entity);
			matchedEntities.Add(entity);
		}

		_listener.OnEntitiesMatched(matchedEntities);
	}

	private void DropEntities()
	{
		var emptyTiles = new List<Tile>();

		for(var i = 0; i < _tilesOrdered.Length; i++)
		{
			var tile = _tilesOrdered[i];
			if (tile.Entity == null)
				emptyTiles.Add(tile);
		}

		if(emptyTiles.Count <= 0)
			return;

		var processedTiles = new List<Tile>();
		foreach(var emptyTile in emptyTiles)
		{
			GetTileFromAbove(emptyTile, processedTiles);
		}

		DropEntities();
	}

	private void GetTileFromAbove(Tile emptyTile, List<Tile> processedTiles)
	{
		if(processedTiles.Contains(emptyTile))
			return;

		var x = emptyTile.X;
		var y = emptyTile.Y + 1;
		if (y >= _tiles.GetLength(1) || y < 0)
		{
			y = emptyTile.Y;
			var randomColorIndex = Random.Range(0, _tempColors.Length - 1);
			var randomColor = _tempColors[randomColorIndex];
			var entityData = new TileEntityData()
			{
				entityType = EntityType.Stone,
				stoneColor = randomColor
			};
			var tileEntity = _tileEntityFactory.Create(in entityData);
			emptyTile.BindEntity(tileEntity);
			_listener.OnEntityCreated(x, y, tileEntity, entityData);

			_entities.Add(tileEntity);
		}
		else
		{
			var topTile = _tiles[x, y];
			if (topTile.Entity != null)
			{
				var entity = topTile.Entity;
				topTile.Entity = null;
				emptyTile.Entity = entity;

				_listener.OnEntityMoved(entity, emptyTile);
			}
			
			GetTileFromAbove(topTile, processedTiles);
		}

		processedTiles.Add(emptyTile);
	}
}
