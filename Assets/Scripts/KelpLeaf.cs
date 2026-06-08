using UnityEngine;

[RequireComponent(typeof(WorldObject))]
[RequireComponent(typeof(Rigidbody))]
public class KelpLeaf : MonoBehaviour
{
    private Rigidbody _rb;
    private WorldObject _worldObject;

    void Awake()
    {
        _rb = GetComponent<Rigidbody>();
        if (_rb == null)
        {
            _rb = gameObject.AddComponent<Rigidbody>();
        }

        _worldObject = GetComponent<WorldObject>();
        if (_worldObject == null)
        {
            _worldObject = gameObject.AddComponent<WorldObject>();
        }

        // Leaf is carryable, not wild-extractable
        _worldObject.interactable = false;
        _worldObject.carryable = true;

        // Ensure collider exists and offset its center downwards so the visual leaf sits above the snow/ground
        BoxCollider box = GetComponent<BoxCollider>();
        if (box == null)
        {
            box = gameObject.AddComponent<BoxCollider>();
        }
        if (box != null)
        {
            // Center is zero to keep center of mass aligned, avoiding physics torque/sliding instability.
            // Increased Y size lets the flat visual float ~17cm above the collision contact plane (above snow).
            box.size = new Vector3(0.5f, 0.35f, 0.5f);
            box.center = Vector3.zero;
        }
    }
}
