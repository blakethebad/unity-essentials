using System.Collections.Generic;
using System.Text;
using UnityEngine;

public class Main : MonoBehaviour
{
	[SerializeField] private int boardSizeX;
	[SerializeField] private int boardSizeY;
	[SerializeField] private BoardCamera gridCamera;
	[SerializeField] private TileViewMono tileViewPrefab;
	[SerializeField] private StoneViewMono stoneViewPrefab;

	private Board _board;

	private void Awake()
	{
		Install();
	}

	private void Install()
	{
		var sizeX = boardSizeX;
		var sizeY = boardSizeY;

		var tileViewFactory = new TileViewFactory(tileViewPrefab, transform);
		var tileEntityFactory = new TileEntityFactory(stoneViewPrefab);
		_board = new Board(tileViewFactory, tileEntityFactory);

		gridCamera.PositionCamera(sizeX, sizeY);
		_board.GenerateBoard(sizeX, sizeY);
	}
}
