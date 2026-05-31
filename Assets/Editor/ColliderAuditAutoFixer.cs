using UnityEditor;
using UnityEngine;
using UnityEditor.SceneManagement;
using UnityEngine.SceneManagement;
using System.Collections.Generic;

public static class ColliderAuditAutoFixer
{
    [MenuItem("Tools/Collider Audit/Auto Fix All (make convex)")]
    public static void AutoFixAllMakeConvexMenu() => AutoFixAllMakeConvex();

    // Public method so it can be invoked with -executeMethod
    public static void AutoFixAllMakeConvex()
    {
        int fixedCount = 0;

        // Scan prefabs
        string[] guids = AssetDatabase.FindAssets("t:Prefab");
        for (int i = 0; i < guids.Length; i++)
        {
            string path = AssetDatabase.GUIDToAssetPath(guids[i]);
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (prefab == null) continue;

            var mcs = prefab.GetComponentsInChildren<UnityEngine.MeshCollider>(true);
            if (mcs == null || mcs.Length == 0) continue;

            bool changed = false;
            foreach (var mc in mcs)
            {
                if (mc == null) continue;
                // Only change if convex == false and there's a non-kinematic rb in parents
                if (!mc.convex)
                {
                    var rb = mc.GetComponentInParent<Rigidbody>();
                    if (rb != null && !rb.isKinematic)
                    {
                        mc.convex = true;
                        changed = true;
                        fixedCount++;
                    }
                }
            }

            if (changed)
            {
                PrefabUtility.SavePrefabAsset(prefab);
                Debug.Log($"ColliderAuditAutoFixer: Fixed prefab {path}");
            }
        }

        // Scan open scenes
        for (int si = 0; si < EditorSceneManager.sceneCount; si++)
        {
            Scene scene = EditorSceneManager.GetSceneAt(si);
            if (!scene.isLoaded) continue;

            var roots = scene.GetRootGameObjects();
            bool sceneChanged = false;
            foreach (var root in roots)
            {
                var mcs = root.GetComponentsInChildren<UnityEngine.MeshCollider>(true);
                foreach (var mc in mcs)
                {
                    if (mc == null) continue;
                    if (!mc.convex)
                    {
                        var rb = mc.GetComponentInParent<Rigidbody>();
                        if (rb != null && !rb.isKinematic)
                        {
                            Undo.RecordObject(mc, "Set MeshCollider.convex");
                            mc.convex = true;
                            EditorUtility.SetDirty(mc);
                            sceneChanged = true;
                            fixedCount++;
                        }
                    }
                }
            }

            if (sceneChanged)
            {
                EditorSceneManager.MarkSceneDirty(scene);
                EditorSceneManager.SaveScene(scene);
                Debug.Log($"ColliderAuditAutoFixer: Fixed scene {scene.path}");
            }
        }

        Debug.Log($"ColliderAuditAutoFixer: Completed. MeshCollider.convex set true on {fixedCount} colliders.");
    }
}
