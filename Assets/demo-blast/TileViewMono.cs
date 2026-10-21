using UnityEngine;
using UnityEngine.EventSystems;
using UnityEssentials.Utilities;

public class TileViewMono : MonoBehaviour , ITileView , IPointerDownHandler, IPointerUpHandler, IPointerExitHandler
{
	public const int TileSize = 1;
	private Tile _tile;
	private ITileListener _listener;

	private bool _isPressStartedHere = false;

    public void OnPointerDown(PointerEventData eventData)
    {
		Log.Error($"Pressed {new Vector2(_tile.X, _tile.Y)} with {_tile.Entity}");
		_isPressStartedHere = true;
    }

	public void OnPointerExit(PointerEventData eventData)
	{
		_isPressStartedHere = false;
	}

	public void OnPointerUp(PointerEventData eventData)
    {
		if(!_isPressStartedHere)
			return;

		Log.Error($"Trying {new Vector2(_tile.X, _tile.Y)} with {_tile.Entity}");
		_listener.OnTileInputRequested(_tile.X, _tile.Y);
    }

    public void Bind(Tile tile)
	{
		_tile = tile;
	}

	public void SetTilePosition(int x, int y)
	{
		var position = new Vector2(x, y);
		transform.position = position;
	}

	public void SetListener(ITileListener tileListener)
	{
		_listener = tileListener;
	}
}

