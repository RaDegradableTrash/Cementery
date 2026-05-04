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

            GameObject colliderContainer = null;
            if (useFittedBoxes)
            {
                // Find or create a dedicated container for the compound colliders
                Transform existingContainer = targetRoot.transform.Find("Generated_Colliders");
                if (existingContainer != null)
                {
                    if (removeExisting)
                    {
                        Undo.DestroyObjectImmediate(existingContainer.gameObject);
                        existingContainer = null;
                    }
                }
                
                if (existingContainer == null)
                {
                    colliderContainer = new GameObject("Generated_Colliders");
                    colliderContainer.transform.SetParent(targetRoot.transform, false);
                    colliderContainer.transform.localPosition = Vector3.zero;
                    colliderContainer.transform.localRotation = Quaternion.identity;
                    Undo.RegisterCreatedObjectUndo(colliderContainer, "Create Collider Container");
                }
                else
                {
                    colliderContainer = existingContainer.gameObject;
                }
            }

            foreach (var mf in meshFilters)
            {
                bool isWheel = mf.name.ToLower().Contains("wheel");
                
                if (targetMode == TargetMode.BodyOnly && isWheel) continue;
                if (targetMode == TargetMode.WheelsOnly && !isWheel) continue;
                if (mf.sharedMesh == null) continue;

                GameObject targetGo = mf.gameObject;

                if (removeExisting && !useFittedBoxes)
                {
                    // Clean up direct colliders if not in compound mode
                    Collider[] existing = targetGo.GetComponents<Collider>();
                    foreach (var c in existing) 
                    {
                        if (c is WheelCollider) continue;
                        Undo.DestroyObjectImmediate(c);
                    }
                }

                if (useConvexMesh)
                {
                    MeshCollider mc = Undo.AddComponent<MeshCollider>(targetGo);
                    mc.sharedMesh = mf.sharedMesh;
                    mc.convex = true;
                    count++;
                }
                else if (useFittedBoxes)
                {
                    // Create a separate object for this box to "piece together" the complex shape
                    GameObject boxObj = new GameObject(mf.name + "_Collider");
                    boxObj.transform.SetParent(colliderContainer.transform, false);
                    
                    // Sync transform with the original mesh
                    boxObj.transform.position = mf.transform.position;
                    boxObj.transform.rotation = mf.transform.rotation;
                    
                    // Adjust local scale to match world lossy scale, accounting for the container's hierarchy
                    Vector3 lossy = mf.transform.lossyScale;
                    Vector3 containerLossy = colliderContainer.transform.lossyScale;
                    boxObj.transform.localScale = new Vector3(
                        containerLossy.x != 0 ? lossy.x / containerLossy.x : lossy.x,
                        containerLossy.y != 0 ? lossy.y / containerLossy.y : lossy.y,
                        containerLossy.z != 0 ? lossy.z / containerLossy.z : lossy.z
                    );

                    BoxCollider bc = boxObj.AddComponent<BoxCollider>();
                    bc.center = mf.sharedMesh.bounds.center;
                    bc.size = mf.sharedMesh.bounds.size;
                    
                    Undo.RegisterCreatedObjectUndo(boxObj, "Create Box Collider Piece");
                    count++;
                }
            }

            Debug.Log($"Successfully generated {count} colliders for {targetRoot.name} ({targetMode})");
            EditorUtility.DisplayDialog("Success", $"Generated {count} colliders.", "OK");
        }
    }
}
