using UnityEngine;

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
		var bounds = _collider.bounds;
		Radius = Mathf.Max(bounds.extents.x, bounds.extents.y, bounds.extents.z);
		Material = _collider.sharedMaterial;
	}

	public void Initialize(int id)
	{
		Id = id;
	}

	public void OnObjectSelectedWithInput()
	{
		_isHeld = true;
	}

	public void OnObjectReleasedWithInput(float markDuration)
	{
		_isHeld = false;
		_markedUntil = Time.time + markDuration;
	}

	public void Store()
	{
		IsStored = true;
		_collider.enabled = false;
		Rigidbody.isKinematic = true;
	}
}
