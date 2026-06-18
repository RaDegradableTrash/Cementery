using UnityEngine;
using UnityEditor;

public class FixCactusMaterial
{
    [MenuItem("Tools/Fix Cactus Material")]
    public static void Fix()
    {
        string prefabPath = "Assets/Prefabs/Cactus.prefab";
        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
        if (prefab != null)
        {
            MeshRenderer[] renderers = prefab.GetComponentsInChildren<MeshRenderer>(true);
            foreach (var renderer in renderers)
            {
                Material[] mats = renderer.sharedMaterials;
                for (int i = 0; i < mats.Length; i++)
                {
                    if (mats[i] != null && mats[i].name.Contains("Material.001"))
                    {
                        Material newMat = AssetDatabase.LoadAssetAtPath<Material>("Assets/Model/Material.001.mat");
                        if (newMat != null)
                        {
                            mats[i] = newMat;
                        }
                    }
                }
                renderer.sharedMaterials = mats;
            }
            EditorUtility.SetDirty(prefab);
            PrefabUtility.SavePrefabAsset(prefab);
            Debug.Log("Successfully assigned external material to Cactus.prefab!");
        }
    }
}
