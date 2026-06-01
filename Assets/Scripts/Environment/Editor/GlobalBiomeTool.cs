using UnityEngine;
using UnityEditor;
using System.Linq;

namespace EnvironmentSystem
{
    public class GlobalBiomeTool : EditorWindow
    {
        private GlobalBiomeSettings settings;

        [MenuItem("Tools/Environment/Global Biome Tool")]
        public static void ShowWindow()
        {
            GetWindow<GlobalBiomeTool>("Global Biome Tool");
        }

        private void OnEnable()
        {
            LoadOrCreateSettings();
        }

        private void LoadOrCreateSettings()
        {
            string[] guids = AssetDatabase.FindAssets("t:GlobalBiomeSettings");
            if (guids.Length > 0)
            {
                string path = AssetDatabase.GUIDToAssetPath(guids[0]);
                settings = AssetDatabase.LoadAssetAtPath<GlobalBiomeSettings>(path);
            }
            else
            {
                if (!System.IO.Directory.Exists("Assets/Settings"))
                {
                    System.IO.Directory.CreateDirectory("Assets/Settings");
                }
                settings = ScriptableObject.CreateInstance<GlobalBiomeSettings>();
                settings.ResetToDefault();
                AssetDatabase.CreateAsset(settings, "Assets/Settings/GlobalBiomeSettings.asset");
                AssetDatabase.SaveAssets();
            }
        }

        private void OnGUI()
        {
            GUILayout.Label("Global Biome Generator", EditorStyles.boldLabel);

            if (settings == null)
            {
                LoadOrCreateSettings();
            }

            if (settings != null)
            {
                SerializedObject serializedSettings = new SerializedObject(settings);
                serializedSettings.Update();

                EditorGUILayout.PropertyField(serializedSettings.FindProperty("globalSeed"));
                EditorGUILayout.PropertyField(serializedSettings.FindProperty("noiseScale"));
                EditorGUILayout.PropertyField(serializedSettings.FindProperty("biomeGradient"));

                serializedSettings.ApplyModifiedProperties();

                if (GUILayout.Button("Reset Gradient to Defaults"))
                {
                    Undo.RecordObject(settings, "Reset Biome Gradient");
                    settings.ResetToDefault();
                    EditorUtility.SetDirty(settings);
                }
            }

            EditorGUILayout.Space();

            GUI.backgroundColor = new Color(0.4f, 0.9f, 0.6f);
            if (GUILayout.Button("🌍 Apply Biomes to All Terrain Chunks", GUILayout.Height(50)))
            {
                ApplyBiomesToAllChunks();
            }
            GUI.backgroundColor = Color.white;

            EditorGUILayout.HelpBox("This tool uses Vertex Colors to permanently bake biome colors onto the meshes. It does not alter terrain height.", MessageType.Info);
        }

        private void ApplyBiomesToAllChunks()
        {
            if (settings == null) return;

            var chunks = FindObjectsOfType<DesertTerrainChunk>();
            if (chunks.Length == 0)
            {
                Debug.LogWarning("[GlobalBiomeTool] No DesertTerrainChunks found in the scene.");
                return;
            }

            int count = 0;
            foreach (var chunk in chunks)
            {
                MeshFilter filter = chunk.GetComponent<MeshFilter>();
                if (filter == null || filter.sharedMesh == null) continue;

                Mesh mesh = filter.sharedMesh;
                Vector3[] vertices = mesh.vertices;
                Color[] colors = new Color[vertices.Length];

                int vw = chunk.width + 1;
                int vd = chunk.depth + 1;

                if (vertices.Length != vw * vd) continue;

                for (int z = 0; z < vd; z++)
                {
                    for (int x = 0; x < vw; x++)
                    {
                        int index = z * vw + x;
                        float worldX = chunk.transform.position.x + vertices[index].x;
                        float worldZ = chunk.transform.position.z + vertices[index].z;

                        float bx = (worldX + settings.globalSeed * 99.1f) / settings.noiseScale;
                        float bz = (worldZ + settings.globalSeed * 77.3f) / settings.noiseScale;

                        float noise = Mathf.PerlinNoise(bx, bz) * 0.5f
                                    + Mathf.PerlinNoise(bx * 2f, bz * 2f) * 0.25f
                                    + Mathf.PerlinNoise(bx * 4f, bz * 4f) * 0.125f;
                        noise = Mathf.Clamp01(noise * 1.15f);

                        colors[index] = settings.biomeGradient.Evaluate(noise);
                        colors[index].a = 1.0f; // Alpha > 0.05 enables biome
                    }
                }

                Undo.RecordObject(mesh, "Apply Global Biome");
                mesh.colors = colors;
                EditorUtility.SetDirty(mesh);
                count++;
            }

            AssetDatabase.SaveAssets();
            Debug.Log($"[GlobalBiomeTool] Successfully baked global biomes to {count} chunks!");
        }
    }
}
