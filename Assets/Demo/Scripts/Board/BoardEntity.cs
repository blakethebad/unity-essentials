using UnityEngine;

[RequireComponent(typeof(Rigidbody))]
public class BoardEntity : MonoBehaviour
{
	public Rigidbody Rigidbody { get; private set; }
	public float Radius { get; private set; }
	public PhysicsMaterial Material { get; private set; }

	private void Awake()
	{
		Rigidbody = GetComponent<Rigidbody>();
		var entityCollider = GetComponentInChildren<Collider>();
		var bounds = entityCollider.bounds;
		Radius = Mathf.Max(bounds.extents.x, bounds.extents.y, bounds.extents.z);
		Material = entityCollider.sharedMaterial;
	}
}
