using System;
using UnityEngine;
using UnityEssentials.Utilities;

public class BoardCamera : MonoBehaviour
{
	private const float ReferenceHeight = 2648f;
	private const float ReferenceWidth = 1448f;
	private const int ReferenceDistance = 15;

	public void PositionCamera(int sizeX, int sizeY)
	{
		var referenceAspect = ReferenceWidth / ReferenceHeight;
		var aspect = (float) Screen.width / Screen.height;
		var scale = referenceAspect / aspect;
		scale = Math.Max(scale, 0.6f);

		var cameraPositionX = (sizeX / 2) - (TileViewMono.TileSize / 2);
		var cameraPositionY = (sizeY / 2) - (TileViewMono.TileSize / 2);
		var cameraPositionZ = - ReferenceDistance * scale;

		transform.position = new Vector3(cameraPositionX, cameraPositionY, cameraPositionZ);
	}
}

