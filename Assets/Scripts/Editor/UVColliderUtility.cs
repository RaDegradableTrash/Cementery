using UnityEngine;
using UnityEditor;
using System.Collections.Generic;

namespace RVSystem.Editor
{
    public class UVColliderUtility : EditorWindow
    {
        private GameObject targetRoot;
        private bool useConvexMesh = true;
        private bool useFittedBoxes = false;
        private enum TargetMode { BodyOnly, WheelsOnly, All }
        private TargetMode targetMode = TargetMode.BodyOnly;
        private bool removeExisting = true;

        [MenuItem("Tools/RV System/UV Collider Utility")]
        public static void ShowWindow()
        {
            GetWindow<UVColliderUtility>("UV Collider Utility");
        }

        private void OnGUI()
        {
            GUILayout.Label("UV System Collider Generator", EditorStyles.boldLabel);
            
            targetRoot = (GameObject)EditorGUILayout.ObjectField("Target Root", targetRoot, typeof(GameObject), true);
            
            EditorGUILayout.Space();
            GUILayout.Label("Target Selection:", EditorStyles.label);
            targetMode = (TargetMode)EditorGUILayout.EnumPopup("Target Mode", targetMode);

            EditorGUILayout.Space();
            GUILayout.Label("Generation Mode:", EditorStyles.label);
            useConvexMesh = EditorGUILayout.Toggle("Use Convex MeshColliders", useConvexMesh);
            if (useConvexMesh) useFittedBoxes = false;
            
            useFittedBoxes = EditorGUILayout.Toggle("Use Fitted BoxColliders", useFittedBoxes);
            if (useFittedBoxes) useConvexMesh = false;

            EditorGUILayout.Space();
            removeExisting = EditorGUILayout.Toggle("Remove Existing Colliders", removeExisting);

            if (GUILayout.Button("Generate Colliders"))
            {
                if (targetRoot == null)
                {
                    EditorUtility.DisplayDialog("Error", "Please select a target root object.", "OK");
                    return;
                }
                Generate();
            }

            EditorGUILayout.Space();
            EditorGUILayout.HelpBox("Note: For vehicle wheels, WheelColliders are still required for driving physics (suspension/torque). MeshColliders on wheels are usually for physical impact detection and should be set to a different layer to avoid fighting the ground physics.", MessageType.Info);
        }

        private void Generate()
        {
            Undo.RegisterFullObjectHierarchyUndo(targetRoot, "Generate UV Colliders");

            MeshFilter[] meshFilters = targetRoot.GetComponentsInChildren<MeshFilter>(true);
            int count = 0;

            foreach (var mf in meshFilters)
            {
                bool isWheel = mf.name.ToLower().Contains("wheel");
                
                if (targetMode == TargetMode.BodyOnly && isWheel) continue;
                if (targetMode == TargetMode.WheelsOnly && !isWheel) continue;

                if (mf.sharedMesh == null) continue;

                GameObject go = mf.gameObject;

                if (removeExisting)
                {
                    Collider[] existing = go.GetComponents<Collider>();
                    foreach (var c in existing) 
                    {
                        // Don't remove WheelColliders, they are precious!
                        if (c is WheelCollider) continue;
                        DestroyImmediate(c);
                    }
                }

                if (useConvexMesh)
                {
                    MeshCollider mc = go.AddComponent<MeshCollider>();
                    mc.sharedMesh = mf.sharedMesh;
                    mc.convex = true;
                    count++;
                }
                else if (useFittedBoxes)
                {
                    BoxCollider bc = go.AddComponent<BoxCollider>();
                    bc.center = mf.sharedMesh.bounds.center;
                    bc.size = mf.sharedMesh.bounds.size;
                    count++;
                }
            }

            Debug.Log($"Successfully generated {count} colliders for {targetRoot.name} ({targetMode})");
            EditorUtility.DisplayDialog("Success", $"Generated {count} colliders.", "OK");
        }
    }
}
