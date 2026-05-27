using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;

namespace EnvironmentSystem
{
    /// <summary>
    /// Custom Inspector Editor for DesertTerrainChunk.
    /// Exposes intuitive designer buttons with full Undo support, seed randomizer,
    /// and recursive neighbor dirty marking and mesh asset serialization.
    /// </summary>
    [CustomEditor(typeof(DesertTerrainChunk))]
    public class DesertTerrainChunkEditor : Editor
    {
        public override void OnInspectorGUI()
        {
            // Draw default fields first
            DrawDefaultInspector();

            EditorGUILayout.Space();
            GUILayout.Label("🎨 Designer Live Operations (交互式设计工具)", EditorStyles.boldLabel);

            DesertTerrainChunk chunk = (DesertTerrainChunk)target;

            EditorGUILayout.HelpBox(
                "【按钮 1】8方向动态边界缝合：\n扫描并协商周围 8 个方向（包括对角线）相邻的地形高度，强制实现物理与光影咬合缝合，并将缝合效果反向传播通知给它们，让整个世界同步对齐！\n\n" +
                "【按钮 2】基于已有地形微调：\n在不破坏大体轮廓的前提下，在表面叠加一层极细小的沙纹风蚀噪声，支持多次点击层层打磨！",
                MessageType.Info
            );

            Color defaultBg = GUI.backgroundColor;

            // Draw Randomize Seed Button (Pastel Pink)
            GUI.backgroundColor = new Color(0.95f, 0.5f, 0.7f);
            if (GUILayout.Button("🎲 随机化种子并直接生成地形", GUILayout.Height(38)))
            {
                Undo.RecordObject(chunk, "Randomize Terrain Seed");
                if (chunk.TryGetComponent<MeshFilter>(out var filter) && filter.sharedMesh != null)
                {
                    Undo.RecordObject(filter.sharedMesh, "Randomize Terrain Seed Mesh");
                }

                // Inject a high variety random seed
                chunk.seed = Random.Range(-99999, 99999);
                Debug.Log($"[DesertTerrainChunkEditor] Seed randomized to: {chunk.seed}");

                // Re-bake seamless mesh with 8-direction propagation!
                chunk.BuildSeamlessWithNeighbors(propagate: true);

                // Auto-save both this chunk AND all modified neighbors to disk assets
                MarkDirtyAndSave(chunk);
            }

            EditorGUILayout.Space();

            // Draw Button 1 (Pastel Green)
            GUI.backgroundColor = new Color(0.4f, 0.8f, 0.6f);
            if (GUILayout.Button("按钮 1：缝合邻近边界并生成地形", GUILayout.Height(36)))
            {
                Undo.RecordObject(chunk, "Generate Seamless Terrain");
                if (chunk.TryGetComponent<MeshFilter>(out var filter) && filter.sharedMesh != null)
                {
                    Undo.RecordObject(filter.sharedMesh, "Generate Seamless Terrain Mesh");
                }

                chunk.BuildSeamlessWithNeighbors(propagate: true);

                MarkDirtyAndSave(chunk);
            }

            // Draw Button 2 (Pastel Blue)
            GUI.backgroundColor = new Color(0.4f, 0.7f, 0.9f);
            if (GUILayout.Button("按钮 2：基于已有地形微调细化", GUILayout.Height(36)))
            {
                Undo.RecordObject(chunk, "Refine Existing Terrain");
                if (chunk.TryGetComponent<MeshFilter>(out var filter) && filter.sharedMesh != null)
                {
                    Undo.RecordObject(filter.sharedMesh, "Refine Existing Terrain Mesh");
                }

                chunk.RefineExistingTerrain();

                MarkDirtyAndSave(chunk);
            }

            EditorGUILayout.Space();

            // Draw Biome Button (Pastel Yellow/Orange)
            GUI.backgroundColor = new Color(0.9f, 0.75f, 0.4f);
            if (GUILayout.Button("🌍 仅重新生成群系颜色 (Regenerate Biome Colormap)", GUILayout.Height(36)))
            {
                Undo.RecordObject(chunk, "Regenerate Biome Colors");
                if (chunk.TryGetComponent<MeshFilter>(out var filter) && filter.sharedMesh != null)
                {
                    Undo.RecordObject(filter.sharedMesh, "Regenerate Biome Colors Mesh");
                }

                chunk.RegenerateBiomeColors();

                MarkDirtyAndSave(chunk);
            }

            // Restore GUI color
            GUI.backgroundColor = defaultBg;
        }

        private void MarkDirtyAndSave(DesertTerrainChunk chunk)
        {
            // 1. Mark current chunk scene dirty & save mesh asset
            EditorUtility.SetDirty(chunk);
            if (chunk.gameObject.scene != null && chunk.gameObject.scene.name != null)
            {
                EditorSceneManager.MarkSceneDirty(chunk.gameObject.scene);
            }
            SaveMeshAsset(chunk);

            // 🌟 2. Scan and auto-save all active neighbors whose meshes were updated via propagation!
            DesertTerrainChunk[] allChunks = FindObjectsOfType<DesertTerrainChunk>();
            foreach (var c in allChunks)
            {
                if (c == chunk) continue;

                // Save their modified meshes to disk asset files automatically
                SaveMeshAsset(c);
                EditorUtility.SetDirty(c);

                if (c.gameObject.scene != null && c.gameObject.scene.name != null)
                {
                    EditorSceneManager.MarkSceneDirty(c.gameObject.scene);
                }
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
        }

        private void SaveMeshAsset(DesertTerrainChunk targetChunk)
        {
            if (targetChunk.TryGetComponent<MeshFilter>(out var filter) && filter.sharedMesh != null)
            {
                string assetPath = AssetDatabase.GetAssetPath(filter.sharedMesh);
                bool isWriteableCustomAsset = !string.IsNullOrEmpty(assetPath) && assetPath.EndsWith(".asset") && assetPath.Contains("/Meshes/");

                if (isWriteableCustomAsset)
                {
                    // Existing custom writeable mesh asset, save it immediately
                    EditorUtility.SetDirty(filter.sharedMesh);
                }
                else
                {
                    // Brand new or read-only/FBX mesh, save to meshes directory as a new writeable asset!
                    string folderPath = "Assets/Scenes/Chunks/Meshes";
                    if (!System.IO.Directory.Exists(folderPath))
                    {
                        System.IO.Directory.CreateDirectory(folderPath);
                        AssetDatabase.Refresh();
                    }

                    string meshPath = $"{folderPath}/Mesh_{targetChunk.name}.asset";
                    
                    // Instantiate the mesh to make it a standalone writeable instance before saving as asset
                    Mesh standaloneMesh = Instantiate(filter.sharedMesh);
                    standaloneMesh.name = $"Mesh_{targetChunk.name}";

                    AssetDatabase.CreateAsset(standaloneMesh, meshPath);
                    filter.sharedMesh = standaloneMesh;
                    
                    if (targetChunk.TryGetComponent<MeshCollider>(out var col))
                    {
                        col.sharedMesh = standaloneMesh;
                    }

                    Debug.Log($"[DesertTerrainChunkEditor] Saved new stitched mesh to disk asset: {meshPath}");
                }
            }
        }
    }
}
