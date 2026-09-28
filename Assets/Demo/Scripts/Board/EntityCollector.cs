using System.Collections.Generic;
using UnityEngine;
using UnityEssentials.Utilities;

[RequireComponent(typeof(CapsuleCollider))]
public class EntityCollector : MonoBehaviour
{
	public CapsuleCollider Collider => _collider;
	[SerializeField] private CapsuleCollider _collider;

	private const int Capacity = 2;
	private readonly List<BoardEntity> _storedEntities = new List<BoardEntity>();

	private void OnCollisionEnter(Collision collision)
	{
		TryCollect(collision);
	}

	//Entities can get marked while already resting on the collector, so keep checking ongoing contacts
	private void OnCollisionStay(Collision collision)
	{
		//TODO: This gets run a lot of times because many objects usually stay on the collider. Better solution is needed
		TryCollect(collision);
	}

	private void TryCollect(Collision collision)
	{
		if(!collision.gameObject.TryGetComponent<BoardEntity>(out var boardEntity))
			return;

		if(!boardEntity.IsMarked || boardEntity.IsStored)
			return;

		//TODO: We might need to handle entity mismatch
		if(_storedEntities.Count >= Capacity || (_storedEntities.Count > 0 && _storedEntities[0].Id != boardEntity.Id))
			return;

		boardEntity.Store();
		boardEntity.transform.position = GetSlotPosition(_storedEntities.Count); // Later place with tween in 0.6 seconds
		boardEntity.transform.rotation = Quaternion.identity; // Later rotate with tween in 0.6 seconds
		_storedEntities.Add(boardEntity);
		
		if(_storedEntities.Count == Capacity)
		{
			Object.Destroy(_storedEntities[0].gameObject);
			Object.Destroy(_storedEntities[1].gameObject);
			_storedEntities.Clear();

			EventBus.Publish<EntityCollectedEvent>(new EntityCollectedEvent());
		}
	}

	public bool ContainsPoint(Vector3 point)
	{
		return (_collider.ClosestPoint(point) - point).sqrMagnitude < 0.0001f;
	}

	private Vector3 GetSlotPosition(int slotIndex)
	{
		var axisOffset = (_collider.height * 0.5f - _collider.radius) * (slotIndex == 0 ? -0.5f : 0.5f);
		return _collider.transform.TransformPoint(_collider.center + Vector3.up * axisOffset);
	}
}
