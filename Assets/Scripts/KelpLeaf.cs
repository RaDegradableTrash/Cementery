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
    }
}
