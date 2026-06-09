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

        // We no longer use a generic BoxCollider on the root, because the leaf scales visually 
        // and we want the collider to match exactly.
        BoxCollider oldBox = GetComponent<BoxCollider>();
        if (oldBox != null)
        {
            Destroy(oldBox);
        }

        // Add a MeshCollider to the visual child so it inherits the correct rotation and scale
        Transform plane = transform.Find("Plane");
        if (plane != null)
        {
            MeshCollider mc = plane.GetComponent<MeshCollider>();
            if (mc == null)
            {
                mc = plane.gameObject.AddComponent<MeshCollider>();
            }
            mc.convex = true;

            MeshFilter mf = plane.GetComponent<MeshFilter>();
            if (mf != null && mf.sharedMesh != null)
            {
                mc.sharedMesh = mf.sharedMesh;
            }
            
            // We let Unity automatically calculate the center of mass from the Convex MeshCollider.
            // Setting it to Vector3.zero caused it to act like a tumbler toy and stand on its base
            // because the origin of the mesh is at the very tip/base of the leaf!
            if (_rb != null)
            {
                _rb.ResetCenterOfMass();
            }
        }
    }
}
