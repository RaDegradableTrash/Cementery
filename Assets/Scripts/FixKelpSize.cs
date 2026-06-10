using UnityEngine;
using UnityEditor;

public class FixKelpSize {
    public static void Do() {
        string path = "Assets/Prefabs/KelpLeaf.prefab";
        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
        if (prefab != null) {
            Transform plane = prefab.transform.Find("Plane");
            if (plane != null) {
                // Set the visual child to the huge size (e.g. 28)
                plane.localScale = new Vector3(28f, 28f, 28f);
                EditorUtility.SetDirty(prefab);
                AssetDatabase.SaveAssets();
                Debug.Log("Scaled KelpLeaf.prefab Plane to 28.");
            }
        }
    }
}
