using UnityEngine;
using UnityEditor;

public class FixKelpLeafPrefab {
    public static void Do() {
        string path = "Assets/Prefabs/KelpLeaf.prefab";
        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
        if (prefab != null) {
            // Remove colliders from root
            foreach (var col in prefab.GetComponents<Collider>()) {
                Object.DestroyImmediate(col, true);
            }
            
            // Get the child Plane
            Transform plane = prefab.transform.Find("Plane");
            if (plane != null) {
                // Ensure there is a MeshCollider
                MeshCollider mc = plane.GetComponent<MeshCollider>();
                if (mc == null) {
                    mc = plane.gameObject.AddComponent<MeshCollider>();
                }
                mc.convex = true;
                
                // If it doesn't have a mesh, assign the Seaweed2 mesh
                // Actually the MeshFilter has the mesh
                MeshFilter mf = plane.GetComponent<MeshFilter>();
                if (mf != null && mf.sharedMesh != null) {
                    mc.sharedMesh = mf.sharedMesh;
                }
            }
            
            EditorUtility.SetDirty(prefab);
            AssetDatabase.SaveAssets();
            Debug.Log("[FixKelpLeafPrefab] Successfully updated prefab.");
        } else {
            Debug.LogError("[FixKelpLeafPrefab] Prefab not found.");
        }
    }
}
