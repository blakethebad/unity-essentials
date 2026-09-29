using System.Collections.Generic;
using Unity.VisualScripting;
using UnityEngine;
using UnityEssentials.Colliders;
using UnityEssentials.Utilities;

public class Board : MonoBehaviour
{
	[SerializeField] private InverseCollider inverseCollider;
	[SerializeField] private EntityCollector _entityCollector;
	[SerializeField] private Transform objectParent;
	[SerializeField] private LayerMask contentObjectLayer;
	[SerializeField] private Vector2 bottomLeftOffset = new Vector2(0.5f, 0.5f);
	[SerializeField] private Vector2 topRightOffset = new Vector2(0.5f, 0.5f);
	private const int MaxSpawnAttempts = 30;

	[SerializeField] private float perObjectSpawnRadius = 2f;
	[SerializeField] private float followStrength = 20f;
	[SerializeField] private float markDuration = 1.5f;
	[SerializeField] private float pickupLiftHeight = 1.5f;

	private List<BoardEntity> _activeEntites;
	private RaycastHit[] inputHits = new RaycastHit[10];

	private BoardEntity _selectedEntity;
	private float _selectedEntityScreenDepth;

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
					//Lift is applied as depth toward the camera so the object stays under the mouse
					_selectedEntityScreenDepth = Camera.main.WorldToScreenPoint(boardEntity.transform.position).z - pickupLiftHeight;
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

			var mouseScreenPosition = Input.mousePosition;
			mouseScreenPosition.z = _selectedEntityScreenDepth;
			var targetPosition = Camera.main.ScreenToWorldPoint(mouseScreenPosition);
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
			//Radius comes from the prefab so the entity can spawn directly at its final position;
			//moving an interpolated rigidbody after Instantiate gets overwritten by its physics pose
			var objectRadius = entityPrefabs[prefabIndex].ComputeBoundingRadius();

			for(var i = 0; i < 24; i++)
			{
				var objectPosition = GenerateRandomObjectPosition(objectRadius);
				var objectRotation = GenerateRandomObjectRotation();

				var spawnedEntity = Object.Instantiate(entityPrefabs[prefabIndex], objectPosition, objectRotation, objectParent);
				//using the prefab index as the id for now, until the object specification system exists
				spawnedEntity.Initialize(prefabIndex);
				_activeEntites.Add(spawnedEntity);
			}
		}
	}

	public void ClearBoard()
	{
		foreach(var entity in _activeEntites)
		{
			Object.Destroy(entity.gameObject);
		}
	}

	//Generates a random point inside the inverse collider volume while considering other spawned objects.
	//The sampling area is inset by the object's radius so the whole object fits inside the walls.
	private Vector3 GenerateRandomObjectPosition(float objectRadius)
	{
		var bestCandidate = GenerateRandomPointInsideArea(objectRadius);
		var bestDistance = DistanceToClosestEntity(bestCandidate);

		for(var attempt = 1; attempt < MaxSpawnAttempts && bestDistance < perObjectSpawnRadius; attempt++)
		{
			var candidate = GenerateRandomPointInsideArea(objectRadius);
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

			Vector3 half = InsetHalfExtents(objectRadius);
			Vector3 local = inverseCollider.transform.InverseTransformPoint(unclampedPosition) - inverseCollider.Center;
			local.x = Mathf.Clamp(local.x, -half.x, half.x);
			local.y = Mathf.Clamp(local.y, -half.y, half.y);
			local.z = Mathf.Clamp(local.z, -half.z, half.z);

			//The nudge may only replace the candidate if it does not push the point into the collector
			var nudgedCandidate = inverseCollider.transform.TransformPoint(local + inverseCollider.Center);
			if(!_entityCollector.ContainsPoint(nudgedCandidate))
				bestCandidate = nudgedCandidate;
		}
		return bestCandidate;


	}

	//Adjusts the board collider to the bounds of the screen so that we can show the same view in every screen
	public void FitBoardToScreen()
	{
		_fittedScreenWidth = Screen.width;
		_fittedScreenHeight = Screen.height;

		var camera = Camera.main;
		var floorPlane = new Plane(
			inverseCollider.transform.up,
			inverseCollider.transform.TransformPoint(inverseCollider.Center + Vector3.down * (inverseCollider.Size.y * 0.5f)));

		
		var ray = camera.ViewportPointToRay(new Vector2(1f, 1f));
		floorPlane.Raycast(ray, out var distance);
		var cornerOnFloor = ray.GetPoint(distance); 
		var localCorner = inverseCollider.transform.InverseTransformPoint(cornerOnFloor);
		var halfExtents = new Vector2(Mathf.Abs(localCorner.x), Mathf.Abs(localCorner.z));

		//Each corner offset pulls its own corner inward, so the two together set all four walls
		var minCorner = new Vector2(-halfExtents.x, -halfExtents.y) + bottomLeftOffset;
		var maxCorner = new Vector2(halfExtents.x, halfExtents.y) - topRightOffset;

		var center = inverseCollider.Center;
		var size = inverseCollider.Size;
		center.x = (minCorner.x + maxCorner.x) * 0.5f;
		center.z = (minCorner.y + maxCorner.y) * 0.5f;
		size.x = maxCorner.x - minCorner.x;
		size.z = maxCorner.y - minCorner.y;

		inverseCollider.SetBoxBounds(center, size);

		//Place collector on to the bottom of the screen 
		var collectorLocal = new Vector3(center.x, center.y - size.y * 0.5f, center.z - size.z * 0.5f);
		_entityCollector.Collider.transform.position = inverseCollider.transform.TransformPoint(collectorLocal);
	}

	private Quaternion GenerateRandomObjectRotation()
	{
		return Random.rotationUniform;
	}

	//Board half extents pulled in by the object's radius so its bounds cannot cross the walls
	private Vector3 InsetHalfExtents(float objectRadius)
	{
		Vector3 half = inverseCollider.Size * 0.5f - Vector3.one * objectRadius;
		half.x = Mathf.Max(half.x, 0f);
		half.y = Mathf.Max(half.y, 0f);
		half.z = Mathf.Max(half.z, 0f);
		return half;
	}

	//TODO: Really don't like the do while logic here. Find a better solution.
	private Vector3 GenerateRandomPointInsideArea(float objectRadius)
	{
		Vector3 half = InsetHalfExtents(objectRadius);
		Vector3 point;
		do
		{
			var local = inverseCollider.Center + new Vector3(
				Random.Range(-half.x, half.x),
				Random.Range(-half.y, half.y),
				Random.Range(-half.z, half.z));
			point = inverseCollider.transform.TransformPoint(local);
		} while(_entityCollector.ContainsPoint(point));
		return point;
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


