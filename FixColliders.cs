using UnityEngine;
using UnityEditor;

public class FixColliders {
    public static void Do() {
        string[] prefabs = new string[] {
            "Assets/Prefabs/KelpLeaf.prefab",
            "Assets/Prefabs/KelpLeaf_Inventory.prefab"
        };
        
        foreach (string path in prefabs) {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (prefab != null) {
                MeshCollider[] mcs = prefab.GetComponentsInChildren<MeshCollider>(true);
                foreach (var mc in mcs) {
                    Object.DestroyImmediate(mc, true);
                }
                EditorUtility.SetDirty(prefab);
                Debug.Log("Cleaned " + path);
            }
        }
        AssetDatabase.SaveAssets();
    }
}
