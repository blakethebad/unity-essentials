using System.Collections.Generic;
using UnityEngine;

//This, in and of itself, is opposed to a few rules of OOP i guess but i want to see how this turns out other than a gazillion interfaces and concretes
public class TileEntityFactory : ITileEntityFactory
{
	private Transform _entityParent;
	private StoneViewMono _stoneViewPrefab;

	private Dictionary<ITileEntityView, GameObject> _spawnedViews;

	public TileEntityFactory(StoneViewMono stoneViewPrefab)
	{
		_stoneViewPrefab = stoneViewPrefab;
		_spawnedViews = new Dictionary<ITileEntityView, GameObject>();
	}

    public ITileEntity Create(in TileEntityData entityData)
    {
        return entityData.entityType switch
        {
            EntityType.Stone => new Stone(entityData.stoneColor),
            _ => null,
        };
    }

    public ITileEntityView CreateView(in TileEntityData entityData)
    {
        GameObject a = entityData.entityType switch
        {
            EntityType.Stone => Object.Instantiate<StoneViewMono>(_stoneViewPrefab, _entityParent).gameObject,
            _ => null,
        };

		var view = a.GetComponent<ITileEntityView>();
		_spawnedViews.Add(view, a.gameObject);
		return view;
    }

    public void ReleaseView(ITileEntityView tileEntityView)
    {
		if(!_spawnedViews.TryGetValue(tileEntityView, out var gameObject))
			return;

		_spawnedViews.Remove(tileEntityView);
		Object.Destroy(gameObject);
    }
}
