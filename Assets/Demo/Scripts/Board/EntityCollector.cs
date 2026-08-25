using System.Collections.Generic;
using UnityEngine;

[RequireComponent(typeof(CapsuleCollider))]
public class EntityCollector : MonoBehaviour
{
	private const int Capacity = 2;

	private readonly List<BoardEntity> _storedEntities = new List<BoardEntity>();

	private CapsuleCollider _collider;

	private void Awake()
	{
		_collider = GetComponent<CapsuleCollider>();
	}


	private void OnCollisionEnter(Collision collision)
	{
		if(!collision.gameObject.TryGetComponent<BoardEntity>(out var boardEntity))
			return;

		if(!boardEntity.IsMarked || boardEntity.IsStored)
			return;

		if(_storedEntities.Count >= Capacity)
			return;

		// mismatched entities just bounce off
		if(_storedEntities.Count > 0 && _storedEntities[0].Id != boardEntity.Id)
			return;

		boardEntity.Store();
		boardEntity.transform.position = GetSlotPosition(_storedEntities.Count);
		_storedEntities.Add(boardEntity);
		
		if(_storedEntities.Count == Capacity)
		{
			Destroy(_storedEntities[0].gameObject);
			Destroy(_storedEntities[1].gameObject);
			_storedEntities.Clear();
		}
	}

	private Vector3 GetSlotPosition(int slotIndex)
	{
		var axisOffset = (_collider.height * 0.5f - _collider.radius) * (slotIndex == 0 ? -0.5f : 0.5f);
		return transform.TransformPoint(_collider.center + Vector3.up * axisOffset);
	}
}
