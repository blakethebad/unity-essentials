using System.Collections.Generic;
using UnityEngine;
using UnityEssentials.Utilities;

public class StoneViewMono : MonoBehaviour, ITileEntityView
{
	private static readonly Dictionary<StoneColor, Color> StoneColors = new()
	{
		{ StoneColor.None, Color.clear },
		{ StoneColor.Blue, new Color32(0x2F, 0x80, 0xED, 0xFF) },
		{ StoneColor.Green, new Color32(0x27, 0xAE, 0x60, 0xFF) },
		{ StoneColor.Yellow, new Color32(0xF2, 0xC9, 0x4C, 0xFF) },
		{ StoneColor.Purple, new Color32(0x9B, 0x51, 0xE0, 0xFF) },
		{ StoneColor.Red, new Color32(0xEB, 0x57, 0x57, 0xFF) },
	};

	[SerializeField] private SpriteRenderer spriteRenderer;
	private Stone _stoneEntity;

	//Probably the casting here should not supposed to happen but lets see
	public void Bind(ITileEntity tileEntity)
	{
		_stoneEntity = (Stone) tileEntity;
		var color = StoneColors.GetValueOrDefault(_stoneEntity.Color);
		spriteRenderer.color = color;
	}

    public void MoveToTile(Tile tile)
    {
		var position = new Vector2(tile.X, tile.Y);
		transform.position = position;
		Log.Error($"Moving to {position}");
	}

    public void SetEntityPosition(int x, int y)
    {
		var position = new Vector2(x, y);
		transform.position = position;
	}
}


