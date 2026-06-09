using UnityEngine;
using UnityEditor;

public class FixPrefab {
    public static void Do() {
        string path = "Assets/Prefabs/KelpLeaf_Inventory.prefab";
        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
        if (prefab == null) {
            Debug.LogError("Not found!");
            return;
        }
        
        GameObject inst = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
        
        // Save world matrix of children
        Transform plane = inst.transform.Find("Plane");
        Vector3 wPos = plane.position;
        Quaternion wRot = plane.rotation;
        Vector3 wScale = plane.lossyScale;
        
        // Reset root
        inst.transform.localPosition = Vector3.zero;
        inst.transform.localRotation = Quaternion.identity;
        inst.transform.localScale = Vector3.one;
        
        // Restore world matrix of children
        plane.position = wPos;
        plane.rotation = wRot;
        
        // Local scale approximation since there's no skew
        Transform parent = plane.parent;
        plane.parent = null;
        plane.localScale = wScale;
        plane.parent = parent;
        
        PrefabUtility.SaveAsPrefabAsset(inst, path);
        Object.DestroyImmediate(inst);
        Debug.Log("Fixed KelpLeaf_Inventory.prefab!");
    }
}
