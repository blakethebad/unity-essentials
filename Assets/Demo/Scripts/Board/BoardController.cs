using System.Collections.Generic;
using UnityEngine;
using UnityEssentials.Colliders;

public class BoardController : MonoBehaviour
{
	[SerializeField] private InverseCollider inverseCollider;
	[SerializeField] private Transform objectParent;
	[SerializeField] private LayerMask contentObjectLayer;
	[SerializeField] private float verticalOffset = 0.5f;
	[SerializeField] private float horizontalOffset = 0.5f;
	private const int MaxSpawnAttempts = 30;

	public float perObjectSpawnRadius = 2f;

	[SerializeField] private float followStrength = 20f;
	[SerializeField] private float markDuration = 1.5f;

	private List<BoardEntity> _activeEntites;
	private RaycastHit[] inputHits = new RaycastHit[10];

	private BoardEntity _selectedEntity;
	private float _selectedEntityDistance;

	private int _fittedScreenWidth;
	private int _fittedScreenHeight;

	//Going to keep the input logic here for now but later should move into the new unity input system
	private void Update()
	{
		//Refit the board whenever the screen size changes; escape recovery pulls back anything left outside the new walls
		if(Screen.width != _fittedScreenWidth || Screen.height != _fittedScreenHeight)
			FitBoardToScreen();

		//If we find an object on initial click grab it as selected
		if(Input.GetMouseButtonDown(0))
		{
			if(Physics.RaycastNonAlloc(Camera.main.ScreenPointToRay(Input.mousePosition), inputHits, 500, contentObjectLayer) > 0)
			{
				var entity = inputHits[0];
				if(entity.transform.TryGetComponent<BoardEntity>(out var boardEntity))
				{
					_selectedEntity = boardEntity;
					boardEntity.OnObjectSelectedWithInput();
					_selectedEntityDistance = entity.distance;
				}
			}
		}

		//If we already had a selected object when we release the mouse we should deselect it.
		if(Input.GetMouseButtonUp(0))
		{
			if(_selectedEntity != null)
			{
				_selectedEntity.OnObjectReleasedWithInput(markDuration);
				_selectedEntity = null;
			}
		}

	}

	//Handling the selected entities movement in fixed update because we use rigidbodies
	private void FixedUpdate()
	{
		//If there is a selected entity we should move it toward the point under the mouse at the grab distance
		if(_selectedEntity != null)
		{
			if(_selectedEntity.IsStored)
			{
				_selectedEntity = null;
				return;
			}

			var mouseRay = Camera.main.ScreenPointToRay(Input.mousePosition);
			var targetPosition = mouseRay.GetPoint(_selectedEntityDistance);
			_selectedEntity.Rigidbody.linearVelocity = (targetPosition - _selectedEntity.Rigidbody.position) * followStrength;
		}
	}

	//For now lets use just a single pool of objects. Later we will have different count of objects for levels
	public void GenerateObjects(List<BoardEntity> entityPrefabs)
	{
		FitBoardToScreen();

		_activeEntites = new List<BoardEntity>();

		for(var prefabIndex = 0; prefabIndex < entityPrefabs.Count; prefabIndex++)
		{
			for(var i = 0; i < 24; i++)
			{
				var objectPosition = GenerateRandomObjectPosition();
				var objectRotation = GenerateRandomObjectRotation();

				var spawnedEntity = Object.Instantiate(entityPrefabs[prefabIndex], objectPosition, objectRotation, objectParent);
				//using the prefab index as the id for now, until the object specification system exists
				spawnedEntity.Initialize(prefabIndex);
				_activeEntites.Add(spawnedEntity);
			}
		}
	}

	//Generates a random point inside the inverse collider volume while considering other spawned objects.
	//Keeping objects contained is the inverse collider's job — its walls are solid from the inside.
	private Vector3 GenerateRandomObjectPosition()
	{
		var bestCandidate = GenerateRandomPointInsideArea();
		var bestDistance = DistanceToClosestEntity(bestCandidate);

		for(var attempt = 1; attempt < MaxSpawnAttempts && bestDistance < perObjectSpawnRadius; attempt++)
		{
			var candidate = GenerateRandomPointInsideArea();
			var distance = DistanceToClosestEntity(candidate);
			if(distance > bestDistance)
			{
				bestDistance = distance;
				bestCandidate = candidate;
			}
		}

		if(bestDistance < perObjectSpawnRadius)
		{
			var unclampedPosition = bestCandidate + Random.onUnitSphere * (perObjectSpawnRadius * 0.5f);

			Vector3 half = inverseCollider.Size * 0.5f;
			Vector3 local = inverseCollider.transform.InverseTransformPoint(unclampedPosition) - inverseCollider.Center;
			local.x = Mathf.Clamp(local.x, -half.x, half.x);
			local.y = Mathf.Clamp(local.y, -half.y, half.y);
			local.z = Mathf.Clamp(local.z, -half.z, half.z);

			bestCandidate = inverseCollider.transform.TransformPoint(local + inverseCollider.Center);
		}
		return bestCandidate;


	}

	//TODO: Using 4 raycasts just to position the bounding box feels a bit overkill. Lets find a better option.
	//TODO: Send 1 raycast to the corner of the screen to find the corner position. find the vector from center to top corner. Rest is easy.
	private void FitBoardToScreen()
	{
		_fittedScreenWidth = Screen.width;
		_fittedScreenHeight = Screen.height;

		var camera = Camera.main;
		var floorPlane = new Plane(
			inverseCollider.transform.up,
			inverseCollider.transform.TransformPoint(inverseCollider.Center + Vector3.down * (inverseCollider.Size.y * 0.5f)));

		var bottomEdge = ViewportPointOnFloor(camera, new Vector2(0.5f, 0f), floorPlane);
		var topEdge = ViewportPointOnFloor(camera, new Vector2(0.5f, 1f), floorPlane);
		var leftEdge = ViewportPointOnFloor(camera, new Vector2(0f, 0.5f), floorPlane);
		var rightEdge = ViewportPointOnFloor(camera, new Vector2(1f, 0.5f), floorPlane);

		//Pull the screen edges inward by the fixed offsets before measuring
		var screenUpOnFloor = (topEdge - bottomEdge).normalized;
		topEdge -= screenUpOnFloor * verticalOffset;
		bottomEdge += screenUpOnFloor * verticalOffset;

		var screenRightOnFloor = (rightEdge - leftEdge).normalized;
		rightEdge -= screenRightOnFloor * horizontalOffset;
		leftEdge += screenRightOnFloor * horizontalOffset;

		//Screen right runs along the collider's local x and screen up along its local z
		var localBottom = inverseCollider.transform.InverseTransformPoint(bottomEdge);
		var localTop = inverseCollider.transform.InverseTransformPoint(topEdge);
		var localLeft = inverseCollider.transform.InverseTransformPoint(leftEdge);
		var localRight = inverseCollider.transform.InverseTransformPoint(rightEdge);

		var center = inverseCollider.Center;
		var size = inverseCollider.Size;
		center.x = (localLeft.x + localRight.x) * 0.5f;
		center.z = (localBottom.z + localTop.z) * 0.5f;
		size.x = Mathf.Abs(localRight.x - localLeft.x);
		size.z = Mathf.Abs(localTop.z - localBottom.z);

		inverseCollider.SetBoxBounds(center, size);
	}

	private Vector3 ViewportPointOnFloor(Camera camera, Vector2 viewportPoint, Plane floorPlane)
	{
		var ray = camera.ViewportPointToRay(viewportPoint);
		floorPlane.Raycast(ray, out var distance);
		return ray.GetPoint(distance);
	}

	private Quaternion GenerateRandomObjectRotation()
	{
		return Random.rotationUniform;
	}

	private Vector3 GenerateRandomPointInsideArea()
	{
		Vector3 half = inverseCollider.Size * 0.5f;
		var local = inverseCollider.Center + new Vector3(
			Random.Range(-half.x, half.x),
			Random.Range(-half.y, half.y),
			Random.Range(-half.z, half.z));
		return inverseCollider.transform.TransformPoint(local);
	}

	private float DistanceToClosestEntity(Vector3 position)
	{
		var closest = float.MaxValue;
		foreach(var entity in _activeEntites)
		{
			closest = Mathf.Min(closest, Vector3.Distance(position, entity.transform.position));
		}
		return closest;
	}
}


