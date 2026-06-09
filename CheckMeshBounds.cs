using UnityEngine;
using UnityEditor;

public class CheckMeshBounds {
    public static void Do() {
        string path = "Assets/Models/Seaweed2.fbx";
        GameObject go = AssetDatabase.LoadAssetAtPath<GameObject>(path);
        if (go != null) {
            MeshFilter[] mfs = go.GetComponentsInChildren<MeshFilter>();
            foreach (var mf in mfs) {
                if (mf.sharedMesh != null) {
                    Debug.Log(">>> MESH BOUNDS: " + mf.sharedMesh.bounds.ToString() + " SIZE: " + mf.sharedMesh.bounds.size);
                }
            }
        } else {
            Debug.Log(">>> MESH NOT FOUND");
        }
    }
}
