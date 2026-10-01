using UnityEngine;
using UnityEssentials.Utilities;

[RequireComponent(typeof(Rigidbody))]
public class BoardEntity : MonoBehaviour
{
	public Rigidbody Rigidbody { get; private set; }
	public float Radius { get; private set; }
	public PhysicsMaterial Material { get; private set; }
	public int Id { get; private set; }
	public bool IsStored { get; private set; }
	public bool IsMarked => _isHeld || Time.time < _markedUntil;

	private bool _isHeld;
	private float _markedUntil;
	private Collider _collider;

	private void Awake()
	{
		Rigidbody = GetComponent<Rigidbody>();
		_collider = GetComponentInChildren<Collider>();
		Radius = ComputeBoundingRadius();
		Material = _collider.sharedMaterial;
	}

	//Callable on prefab assets too: only the authored shape is read, never physics state
	public float ComputeBoundingRadius()
	{
		var collider = _collider != null ? _collider : GetComponentInChildren<Collider>();
		var scale = collider.transform.lossyScale;
		var maxScale = Mathf.Max(Mathf.Abs(scale.x), Mathf.Abs(scale.y), Mathf.Abs(scale.z));

		switch(collider)
		{
			case SphereCollider sphere:
				return (sphere.center.magnitude + sphere.radius) * maxScale;
			case CapsuleCollider capsule:
				return (capsule.center.magnitude + Mathf.Max(capsule.height * 0.5f, capsule.radius)) * maxScale;
			case BoxCollider box:
				return (box.center.magnitude + box.size.magnitude * 0.5f) * maxScale;
			case MeshCollider mesh:
				return (mesh.sharedMesh.bounds.center.magnitude + mesh.sharedMesh.bounds.extents.magnitude) * maxScale;
			default:
				return collider.bounds.extents.magnitude;
		}
	}

	public void Initialize(int id)
	{
		Id = id;
	}

	//Rotation is frozen while held so collisions on the way up cannot spin the object
	public void OnObjectSelectedWithInput()
	{
		_isHeld = true;
		Rigidbody.angularVelocity = Vector3.zero;
		Rigidbody.freezeRotation = true;

		EventBus.Publish<EntityGrabbedEvent>(new EntityGrabbedEvent()
		{
			EntityName = gameObject.name
		});
	}

	public void OnObjectReleasedWithInput(float markDuration)
	{
		_isHeld = false;
		_markedUntil = Time.time + markDuration;
		Rigidbody.freezeRotation = false;
	}

	public void SetStored(Vector3 storedPosition, Quaternion storedRotation)
	{
		IsStored = true;
		_collider.enabled = false;
		Rigidbody.isKinematic = true;
	}
}
