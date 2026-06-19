using UnityEngine;

[RequireComponent(typeof(WorldObject))]
[RequireComponent(typeof(Rigidbody))]
public class Fuel : MonoBehaviour
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
        
        // Let WorldObject or BetterGameplayManager handle the kinematic state.

        _worldObject = GetComponent<WorldObject>();
        if (_worldObject == null)
        {
            _worldObject = gameObject.AddComponent<WorldObject>();
        }

        // Fuel is carryable and collectable (can be put into inventory)
        _worldObject.interactable = true;
        _worldObject.interactMessage = "Add Fuel";
        _worldObject.carryable = true;
        _worldObject.collectable = true;
        _worldObject.canBePushed = true; // Ensure it's fully dynamic and falls to the ground!

    }
}
