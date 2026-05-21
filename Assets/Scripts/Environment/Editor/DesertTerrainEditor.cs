using System.IO;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine.SceneManagement;

namespace EnvironmentSystem
{
    /// <summary>
    /// Professional Editor Window for batch generating seamless desert terrain chunks,
    /// serializing generated meshes to disk to prevent data loss, and auto-registering chunks in Build Settings.
    /// Features default parameter cloning from active scene and a scrollable layout.
    /// </summary>
    public class DesertTerrainEditor : EditorWindow
    {
        [MenuItem("Tools/Desert Terrain Generator")]
        public static void ShowWindow()
        {
            GetWindow<DesertTerrainEditor>("Desert Generator");
        }

        [Header("Grid Range (烘焙区块范围)")]
        private int minChunkX = -1;
        private int maxChunkX = 1;
        private int minChunkZ = -1;
        private int maxChunkZ = 1;

        [Header("Save Folder (场景保存目录)")]
        private string scenesPath = "Assets/Scenes/DesertChunks";

        // Local instance to easily adjust settings in the editor window
        private DesertTerrainChunk settings = null;
        private Vector2 scrollPosition = Vector2.zero; // Scroll view vector for the UI

        private void OnEnable()
        {
            // Create a temporary dummy object to hold parameters and show nicely in GUI
            GameObject tempGo = new GameObject("TempDesertSettingsHolder");
            tempGo.hideFlags = HideFlags.DontSave;
            settings = tempGo.AddComponent<DesertTerrainChunk>();

            // 🌟 Proactively scan active scene for any existing DesertTerrainChunk to clone settings!
            DesertTerrainChunk activeSceneChunk = FindObjectOfType<DesertTerrainChunk>();
            if (activeSceneChunk != null)
            {
                CopyChunkSettings(activeSceneChunk, settings);
                Debug.Log($"[DesertTerrainEditor] Successfully loaded configurations from active scene chunk: '{activeSceneChunk.name}'!");
            }
        }

        private void OnDisable()
        {
            if (settings != null)
            {
                DestroyImmediate(settings.gameObject);
            }
        }

        private void OnGUI()
        {
            // 🌟 1. Wrap the entire window contents inside a smooth scroll view! 🌟
            scrollPosition = EditorGUILayout.BeginScrollView(scrollPosition);

            GUILayout.Label("Desert Terrain Generator", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox("This tool generates multiple seamless terrain scenes, creates persisted mesh assets, and adds them to Unity Build Settings for the additive WorldStreamer.", MessageType.Info);

            EditorGUILayout.Space();
            GUILayout.Label("1. Choose Chunk Grid Range", EditorStyles.boldLabel);
            EditorGUILayout.BeginHorizontal();
            minChunkX = EditorGUILayout.IntField("Min Chunk X", minChunkX);
            maxChunkX = EditorGUILayout.IntField("Max Chunk X", maxChunkX);
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.BeginHorizontal();
            minChunkZ = EditorGUILayout.IntField("Min Chunk Z", minChunkZ);
            maxChunkZ = EditorGUILayout.IntField("Max Chunk Z", maxChunkZ);
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.Space();
            scenesPath = EditorGUILayout.TextField("Save Scenes Path", scenesPath);

            EditorGUILayout.Space();
            GUILayout.Label("2. Configure Terrain Shape Parameters", EditorStyles.boldLabel);
            
            if (settings != null)
            {
                // Create a serialized object to use standard layout fields nicely
                SerializedObject serializedObj = new SerializedObject(settings);
                serializedObj.Update();

                SerializedProperty prop = serializedObj.GetIterator();
                if (prop.NextVisible(true))
                {
                    do
                    {
                        // Skip built-in script component property
                        if (prop.name == "m_Script") continue;
                        EditorGUILayout.PropertyField(prop, true);
                    }
                    while (prop.NextVisible(false));
                }
                serializedObj.ApplyModifiedProperties();
            }

            EditorGUILayout.Space();
            GUILayout.Label("3. Generate World Chunks", EditorStyles.boldLabel);
            if (GUILayout.Button("Batch Generate & Bake Chunks", GUILayout.Height(40)))
            {
                if (EditorUtility.DisplayDialog("Bake Desert Terrain Chunks?", 
                    $"This will create {(maxChunkX - minChunkX + 1) * (maxChunkZ - minChunkZ + 1)} scenes in '{scenesPath}'. Are you sure?", "Yes, Bake Chunks!", "Cancel"))
                {
                    BakeChunks();
                }
            }

            EditorGUILayout.Space();
            
            // 🌟 2. End the scroll view area! 🌟
            EditorGUILayout.EndScrollView();
        }

        private void BakeChunks()
        {
            if (!Directory.Exists(scenesPath))
            {
                Directory.CreateDirectory(scenesPath);
            }

            string meshesPath = Path.Combine(scenesPath, "Meshes");
            if (!Directory.Exists(meshesPath))
            {
                Directory.CreateDirectory(meshesPath);
            }

            AssetDatabase.Refresh();

            List<string> generatedScenePaths = new List<string>();

            // Save active scene before baking
            string originalScenePath = SceneManager.GetActiveScene().path;
            if (!string.IsNullOrEmpty(originalScenePath))
            {
                EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo();
            }

            int totalChunks = (maxChunkX - minChunkX + 1) * (maxChunkZ - minChunkZ + 1);
            int currentCount = 0;

            try
            {
                for (int x = minChunkX; x <= maxChunkX; x++)
                {
                    for (int z = minChunkZ; z <= maxChunkZ; z++)
                    {
                        currentCount++;
                        string chunkName = $"Desert_Chunk_{x}_{z}";
                        EditorUtility.DisplayProgressBar("Baking Desert Chunks", $"Generating {chunkName} ({currentCount}/{totalChunks})", (float)currentCount / totalChunks);

                        try
                        {
                            // 1. Create a fresh new additive single scene
                            Scene chunkScene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

                            // 2. Spawn the chunk GameObject
                            GameObject chunkGo = new GameObject(chunkName);
                            DesertTerrainChunk chunk = chunkGo.AddComponent<DesertTerrainChunk>();

                            // 3. Copy settings from the editor window
                            CopyChunkSettings(settings, chunk);

                            // Position offset in the grid
                            float worldOffsetX = x * chunk.width * chunk.cellSize;
                            float worldOffsetZ = z * chunk.depth * chunk.cellSize;
                            chunkGo.transform.position = new Vector3(worldOffsetX, 0, worldOffsetZ);

                            // 4. Generate seamless terrain mesh
                            Mesh mesh = chunk.GenerateMesh();
                            mesh.name = $"Mesh_{chunkName}";

                            // 5. Serialize and save mesh asset
                            string meshAssetPath = Path.Combine(meshesPath, $"Mesh_{chunkName}.asset");
                            AssetDatabase.CreateAsset(mesh, meshAssetPath);
                            AssetDatabase.SaveAssets();

                            // Bind mesh asset to components
                            if (chunkGo.TryGetComponent<MeshFilter>(out var filter))
                            {
                                filter.sharedMesh = mesh;
                            }

                            if (chunkGo.TryGetComponent<MeshCollider>(out var col))
                            {
                                col.sharedMesh = mesh;
                            }

                            if (chunkGo.TryGetComponent<MeshRenderer>(out var mr))
                            {
                                if (chunk.terrainMaterial != null)
                                {
                                    mr.sharedMaterial = chunk.terrainMaterial;
                                }
                                else
                                {
                                    mr.sharedMaterial = new Material(Shader.Find("Universal Render Pipeline/Lit"));
                                }
                            }

                            // Save the generated chunk scene file
                            string sceneFileName = $"{chunkName}.unity";
                            string sceneFileFullPath = Path.Combine(scenesPath, sceneFileName);
                            EditorSceneManager.SaveScene(chunkScene, sceneFileFullPath);
                            
                            generatedScenePaths.Add(sceneFileFullPath);
                        }
                        catch (System.Exception ex)
                        {
                            Debug.LogError($"[DesertTerrainEditor] Fatal exception baking chunk '{chunkName}' at grid ({x}, {z}):\n{ex}");
                            EditorUtility.DisplayDialog("Bake Chunk Exception!", 
                                $"An error occurred while baking chunk '{chunkName}' at coordinate ({x},{z}).\n\nError details:\n{ex.Message}\n\nPlease check console for stack trace.", "Understood");
                            throw; // Re-throw to halt the progress bar and abort the batch safely
                        }
                    }
                }
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            // Register scenes in Build Settings automatically
            RegisterScenesInBuildSettings(generatedScenePaths);

            // Re-open the original active scene
            if (!string.IsNullOrEmpty(originalScenePath))
            {
                EditorSceneManager.OpenScene(originalScenePath, OpenSceneMode.Single);
            }
            else
            {
                EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);
            }

            EditorUtility.DisplayDialog("Bake Finished Successfully!", 
                $"Baked {totalChunks} independent seamless desert chunks successfully!\n" +
                $"Meshes are saved as assets under: {meshesPath}\n" +
                $"Scenes have been automatically added to your Build Settings!", "Fantastic!");
        }

        private void CopyChunkSettings(DesertTerrainChunk source, DesertTerrainChunk target)
        {
            target.width = source.width;
            target.depth = source.depth;
            target.cellSize = source.cellSize;
            target.seed = source.seed;

            target.enableMinecraftHills = source.enableMinecraftHills;
            target.hillMaxHeight = source.hillMaxHeight;
            target.hillNoiseScale = source.hillNoiseScale;
            target.octaves = source.octaves;
            target.lacunarity = source.lacunarity;
            target.gain = source.gain;

            target.enableTerracing = source.enableTerracing;
            target.terraceStep = source.terraceStep;
            target.terraceFlatness = source.terraceFlatness;

            target.baseScale = source.baseScale;
            target.baseHeight = source.baseHeight;
            target.baseNoiseHeight = source.baseNoiseHeight;
            target.duneSpacing = source.duneSpacing;
            target.duneHeight = source.duneHeight;
            target.duneDirection = source.duneDirection;
            target.duneWarpScale = source.duneWarpScale;
            target.duneWarpStrength = source.duneWarpStrength;
            target.crestPosition = source.crestPosition;
            target.windwardExponent = source.windwardExponent;

            target.rippleSpacing = source.rippleSpacing;
            target.rippleHeight = source.rippleHeight;
            target.rippleDirection = source.rippleDirection;

            target.detailScale = source.detailScale;
            target.detailHeight = source.detailHeight;
            target.blendWidth = source.blendWidth;

            target.terrainMaterial = source.terrainMaterial;
        }

        private void RegisterScenesInBuildSettings(List<string> newScenePaths)
        {
            List<EditorBuildSettingsScene> scenesList = new List<EditorBuildSettingsScene>(EditorBuildSettings.scenes);

            // Create a set of existing paths to avoid duplicates
            HashSet<string> existingPaths = new HashSet<string>();
            foreach (var sc in scenesList)
            {
                existingPaths.Add(sc.path.Replace('\\', '/'));
            }

            bool changed = false;
            foreach (var newPath in newScenePaths)
            {
                string normalizedPath = newPath.Replace('\\', '/');
                if (!existingPaths.Contains(normalizedPath))
                {
                    scenesList.Add(new EditorBuildSettingsScene(normalizedPath, true));
                    changed = true;
                }
            }

            if (changed)
            {
                EditorBuildSettings.scenes = scenesList.ToArray();
                Debug.Log($"[DesertTerrainEditor] Registered {newScenePaths.Count} new desert chunk scenes in Build Settings.");
            }
        }

        [MenuItem("Tools/Desert Terrain/Scan Scene Chunks")]
        public static void ScanSceneChunks()
        {
            DesertTerrainChunk[] allChunks = FindObjectsOfType<DesertTerrainChunk>();
            Debug.Log($"=== Desert Terrain Chunk Scan ({allChunks.Length} found) ===");
            for (int i = 0; i < allChunks.Length; i++)
            {
                var c = allChunks[i];
                var filter = c.GetComponent<MeshFilter>();
                var meshName = filter != null && filter.sharedMesh != null ? filter.sharedMesh.name : "NULL";
                Debug.Log($"[{i}] Name: '{c.name}', Path: '{GetGameObjectPath(c.gameObject)}', baseHeight: {c.baseHeight}, baseNoiseHeight: {c.baseNoiseHeight}, pos: {c.transform.position}, mesh: '{meshName}'");
            }
        }

        private static string GetGameObjectPath(GameObject obj)
        {
            string path = "/" + obj.name;
            while (obj.transform.parent != null)
            {
                obj = obj.transform.parent.gameObject;
                path = "/" + obj.name + path;
            }
            return path;
        }
    }
}
