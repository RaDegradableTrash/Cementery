using UnityEngine;
using UnityEditor;

[InitializeOnLoad]
public class CreateKelpLeafPrefab
{
    static CreateKelpLeafPrefab()
    {
        EditorApplication.delayCall += CheckAndCreateKelpLeaf;
    }

    private static void CheckAndCreateKelpLeaf()
    {
        string path = "Assets/Prefabs/KelpLeaf.prefab";
        GameObject existing = AssetDatabase.LoadAssetAtPath<GameObject>(path);
        if (existing != null)
        {
            // Verify components
            bool modified = false;
            if (existing.GetComponent<KelpLeaf>() == null)
            {
                // We'll let the initial creation handle it or we can recreate if missing
            }
            return;
        }

        // Create a new leaf prefab
        GameObject leafObj = GameObject.CreatePrimitive(PrimitiveType.Quad);
        leafObj.name = "KelpLeaf";

        // Remove default mesh collider
        Collider col = leafObj.GetComponent<Collider>();
        if (col != null) Object.DestroyImmediate(col);

        // Add components
        Rigidbody rb = leafObj.AddComponent<Rigidbody>();
        rb.isKinematic = false;
        rb.useGravity = true;

        WorldObject wo = leafObj.AddComponent<WorldObject>();
        wo.carryable = true;
        wo.interactable = false;
        wo.collectable = false;
        wo.canBePlacedOnFloor = true;

        leafObj.AddComponent<KelpLeaf>();

        // Add BoxCollider for physics
        BoxCollider box = leafObj.AddComponent<BoxCollider>();
        box.size = new Vector3(0.5f, 0.1f, 0.5f);

        // Rotate slightly to look like a leaf flat on the ground
        leafObj.transform.rotation = Quaternion.Euler(90f, 0f, 0f);

        // Setup a default green-ish material if possible, or leave it default
        Renderer r = leafObj.GetComponent<Renderer>();
        if (r != null)
        {
            Material mat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            if (mat.shader == null) mat = new Material(Shader.Find("Standard"));
            mat.color = new Color(0.12f, 0.75f, 0.12f, 1f);
            AssetDatabase.CreateAsset(mat, "Assets/Materials/KelpLeafMaterial.mat");
            r.sharedMaterial = mat;
        }

        // Save as prefab
        PrefabUtility.SaveAsPrefabAsset(leafObj, path);
        Object.DestroyImmediate(leafObj);

        Debug.Log("Successfully created KelpLeaf prefab at " + path);
    }
}
