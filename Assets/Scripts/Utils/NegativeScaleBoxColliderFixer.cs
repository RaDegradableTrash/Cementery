using UnityEngine;

/// <summary>
/// Fixes BoxColliders on GameObjects with negative lossy scale at runtime.
/// Unity's BoxCollider does not support negative scale and produces warnings +
/// incorrect collision geometry. This component replaces such BoxColliders with
/// convex MeshColliders automatically on Awake.
///
/// Attach to a root GameObject (e.g. RV_Root) to fix all children.
/// </summary>
public class NegativeScaleBoxColliderFixer : MonoBehaviour
{
    void Awake()
    {
        FixNegativeScaleBoxColliders(transform);
    }

    void FixNegativeScaleBoxColliders(Transform root)
    {
        BoxCollider[] boxes = root.GetComponentsInChildren<BoxCollider>(true);
        for (int i = 0; i < boxes.Length; i++)
        {
            BoxCollider box = boxes[i];
            if (box == null) continue;

            Vector3 ls = box.transform.lossyScale;
            if (ls.x < 0f || ls.y < 0f || ls.z < 0f)
            {
                // Try to get the MeshFilter for a convex MeshCollider replacement
                MeshFilter mf = box.GetComponent<MeshFilter>();
                if (mf != null && mf.sharedMesh != null)
                {
                    bool wasTrigger = box.isTrigger;
                    PhysicMaterial mat = box.sharedMaterial;
                    GameObject go = box.gameObject;
                    
                    DestroyImmediate(box);
                    
                    MeshCollider mc = go.AddComponent<MeshCollider>();
                    mc.sharedMesh = mf.sharedMesh;
                    mc.convex = true;
                    mc.isTrigger = wasTrigger;
                    mc.sharedMaterial = mat;
                }
                else
                {
                    // No mesh available: absorb negative scale into size (flip size to positive)
                    Vector3 size = box.size;
                    Vector3 center = box.center;
                    
                    if (ls.x < 0f) { size.x = Mathf.Abs(size.x); center.x = -center.x; }
                    if (ls.y < 0f) { size.y = Mathf.Abs(size.y); center.y = -center.y; }
                    if (ls.z < 0f) { size.z = Mathf.Abs(size.z); center.z = -center.z; }
                    
                    box.size = size;
                    box.center = center;
                }
            }
        }
    }
}
