using UnityEngine;
using UnityEditor;
using System.Collections.Generic;
using EnvironmentSystem;
using UnityEngine.SceneManagement;
using UnityEditor.SceneManagement;

public class DesertPropPlacementTool : EditorWindow
{
    [MenuItem("Tools/Cemetery/Desert Prop Placement Tool")]
    public static void ShowWindow()
    {
        GetWindow<DesertPropPlacementTool>("Prop Placement");
    }

    public enum TerrainTrendMode
    {
        ValleysAndLowlands, // 洼地与低谷（水分充足，生长茂密）
        RidgesAndPeaks,     // 山脊与高地（风口，稀疏高耸）
        GentleSlopes,       // 平缓开阔地（坡度小的地方密集）
        NoiseClustered      // 纯柏林噪声聚类（疏密随机成簇）
    }

    // Prefab to spawn (Clean, prominent drag-and-drop slot)
    private GameObject prefabToSpawn;

    // Density and Placement Settings
    private TerrainTrendMode trendMode = TerrainTrendMode.ValleysAndLowlands;
    private int spawnAttemptsPerChunk = 150;
    private float minDistance = 3.0f;
    private float minScale = 0.5f;
    private float maxScale = 2.5f;
    private float heightOffset = 0.35f; // Relative height offset based on scale to control embedding depth
    private int seed = 1337;

    // Visibility LOD Settings
    private float visibilityRadius = 120f;
    private float visibilityHysteresis = 15f;
    private float visibilityCheckInterval = 0.4f;

    // Static stack to record spawned GameObjects for immediate undo
    private static Stack<List<GameObject>> spawnedObjectsHistory = new Stack<List<GameObject>>();

    private void OnGUI()
    {
        GUILayout.Label("Desert Prop Placement Tool", EditorStyles.boldLabel);
        EditorGUILayout.Space();

        // 1. CLEAR & PROMINENT PREFAB SLOT (仙人掌/物体挂载槽)
        EditorGUILayout.BeginVertical("box");
        GUILayout.Label("Object Placement Target (预制体挂载)", EditorStyles.boldLabel);
        prefabToSpawn = (GameObject)EditorGUILayout.ObjectField("Prefab (拖入物体)", prefabToSpawn, typeof(GameObject), false);
        EditorGUILayout.EndVertical();

        EditorGUILayout.Space();

        // 2. TERRAIN TREND SETTINGS (地形趋势设置)
        EditorGUILayout.BeginVertical("box");
        GUILayout.Label("Distribution Settings (地形趋势与疏密)", EditorStyles.boldLabel);
        trendMode = (TerrainTrendMode)EditorGUILayout.EnumPopup("Terrain Trend (地形趋势)", trendMode);
        spawnAttemptsPerChunk = EditorGUILayout.IntField("Density (密度/每区块尝试次数)", spawnAttemptsPerChunk);
        minDistance = EditorGUILayout.FloatField("Min Distance (防穿模最小距离)", minDistance);
        EditorGUILayout.EndVertical();

        EditorGUILayout.Space();

        // 3. SCALE & ROTATION SETTINGS
        EditorGUILayout.BeginVertical("box");
        GUILayout.Label("Variation Settings (大小与随机变化)", EditorStyles.boldLabel);
        minScale = EditorGUILayout.FloatField("Min Scale Multiplier", minScale);
        maxScale = EditorGUILayout.FloatField("Max Scale Multiplier", maxScale);
        heightOffset = EditorGUILayout.FloatField("Height Offset (Y轴向上偏移比例)", heightOffset);
        seed = EditorGUILayout.IntField("Random Seed (随机种子)", seed);
        EditorGUILayout.EndVertical();

        EditorGUILayout.Space();

        // 4. VISIBILITY LOD SETTINGS
        EditorGUILayout.BeginVertical("box");
        GUILayout.Label("Visibility LOD Settings (距离可见性优化)", EditorStyles.boldLabel);
        visibilityRadius = EditorGUILayout.FloatField("Visibility Radius (可见距离)", visibilityRadius);
        visibilityHysteresis = EditorGUILayout.FloatField("Hysteresis Band (抖动防护宽度)", visibilityHysteresis);
        visibilityCheckInterval = EditorGUILayout.FloatField("Check Interval (检测间隔秒)", visibilityCheckInterval);
        EditorGUILayout.HelpBox("在运行时，离玩家超出可见距离的仙人掌将自动隐藏，重新靠近后恢复显示。", MessageType.None);
        EditorGUILayout.EndVertical();

        EditorGUILayout.Space();

        // HELP BOX
        string trendHelp = "";
        switch (trendMode)
        {
            case TerrainTrendMode.ValleysAndLowlands:
                trendHelp = "洼地与低谷模式：低海拔处水分较多，仙人掌明显多且密；高处非常少。";
                break;
            case TerrainTrendMode.RidgesAndPeaks:
                trendHelp = "山脊与高地模式：高山顶部和沙丘山脊生长较多；低谷平原较少。";
                break;
            case TerrainTrendMode.GentleSlopes:
                trendHelp = "平缓开阔地模式：地面越平坦生长的越密集；陡坡区域几乎不生长。";
                break;
            case TerrainTrendMode.NoiseClustered:
                trendHelp = "纯柏林噪声模式：不受地形起伏影响，随机呈岛屿状成簇分布，有些地方极密，有些地方全无。";
                break;
        }
        EditorGUILayout.HelpBox(trendHelp + "\n物体将自动放置在所属区块 scene 内的 'Spawned_Props' 节点中。", MessageType.Info);

        EditorGUILayout.Space();

        // 4. ACTION BUTTONS
        if (GUILayout.Button("Place Props on ALL Loaded Chunks (开始放置)", GUILayout.Height(35)))
        {
            PlaceProps();
        }

        EditorGUILayout.Space();

        // 5. UNDO BUTTONS (撤回选项)
        EditorGUILayout.BeginHorizontal();
        if (GUILayout.Button("Undo Last Placement (撤回上次放置)", GUILayout.Height(25)))
        {
            UndoLastPlacement();
        }
        if (GUILayout.Button("Clear ALL Spawned Props (清除所有放置)", GUILayout.Height(25)))
        {
            ClearAllProps();
        }
        EditorGUILayout.EndHorizontal();
    }

    private void PlaceProps()
    {
        if (prefabToSpawn == null)
        {
            EditorUtility.DisplayDialog("Error", "Please drag a valid Prefab into the 'Prefab to Spawn' field first.", "OK");
            return;
        }

        DesertTerrainChunk[] chunks = FindObjectsOfType<DesertTerrainChunk>();
        if (chunks.Length == 0)
        {
            EditorUtility.DisplayDialog("Warning", "No loaded DesertTerrainChunk found in the current scene.", "OK");
            return;
        }

        // Randomize seed for this placement to ensure a different pattern each time
        seed = Random.Range(1, 100000);

        // Setup deterministic random states
        Random.InitState(seed);
        Vector2 seedOffset = new Vector2(Random.Range(-10000f, 10000f), Random.Range(-10000f, 10000f));
        int totalSpawned = 0;

        List<GameObject> currentSpawns = new List<GameObject>();

        foreach (var chunk in chunks)
        {
            Transform chunkTransform = chunk.transform;
            float chunkWidthWorld = chunk.width * chunk.cellSize;
            float chunkDepthWorld = chunk.depth * chunk.cellSize;
            Vector3 chunkPos = chunkTransform.position;

            // Ensure the scene is loaded and ready
            Scene chunkScene = chunk.gameObject.scene;

            // Create chunk-specific props holder inside the chunk's hierarchy (and scene!)
            // Reuse existing holder if it exists to avoid overwriting previously placed props
            Transform existingHolder = chunkTransform.Find("Spawned_Props");
            GameObject holder;
            if (existingHolder != null)
            {
                holder = existingHolder.gameObject;
            }
            else
            {
                holder = new GameObject("Spawned_Props");
                holder.transform.SetParent(chunkTransform);
                holder.transform.localPosition = Vector3.zero;
                holder.transform.localRotation = Quaternion.identity;
                holder.transform.localScale = Vector3.one;
                Undo.RegisterCreatedObjectUndo(holder, "Spawn Chunk Props");

                // Attach visibility LOD manager
                PropVisibilityManager visLOD = holder.AddComponent<PropVisibilityManager>();
                visLOD.visibilityRadius = visibilityRadius;
                visLOD.hysteresis = visibilityHysteresis;
                visLOD.checkInterval = visibilityCheckInterval;
            }

            // Fetch mesh data directly if it exists, to support sculpted/deformed meshes in the editor
            MeshFilter filter = chunk.GetComponent<MeshFilter>();
            Vector3[] vertices = null;
            Vector3[] normals = null;
            if (filter != null && filter.sharedMesh != null)
            {
                vertices = filter.sharedMesh.vertices;
                normals = filter.sharedMesh.normals;
            }

            // Calculate chunk height limits to determine trend
            float minChunkH = float.MaxValue;
            float maxChunkH = float.MinValue;
            if (vertices != null && vertices.Length > 0)
            {
                foreach (var v in vertices)
                {
                    if (v.y < minChunkH) minChunkH = v.y;
                    if (v.y > maxChunkH) maxChunkH = v.y;
                }
            }
            else
            {
                float h1 = chunk.SampleHeight(chunkPos.x, chunkPos.z);
                float h2 = chunk.SampleHeight(chunkPos.x + chunkWidthWorld, chunkPos.z);
                float h3 = chunk.SampleHeight(chunkPos.x, chunkPos.z + chunkDepthWorld);
                float h4 = chunk.SampleHeight(chunkPos.x + chunkWidthWorld, chunkPos.z + chunkDepthWorld);
                float h5 = chunk.SampleHeight(chunkPos.x + chunkWidthWorld * 0.5f, chunkPos.z + chunkDepthWorld * 0.5f);
                minChunkH = Mathf.Min(h1, Mathf.Min(h2, Mathf.Min(h3, Mathf.Min(h4, h5))));
                maxChunkH = Mathf.Max(h1, Mathf.Max(h2, Mathf.Max(h3, Mathf.Max(h4, h5))));
            }
            float heightRange = maxChunkH - minChunkH + 0.001f;

            List<Vector3> spawnedPositions = new List<Vector3>();
            if (existingHolder != null)
            {
                foreach (Transform child in existingHolder)
                {
                    spawnedPositions.Add(child.position);
                }
            }

            for (int i = 0; i < spawnAttemptsPerChunk; i++)
            {
                float localX = Random.Range(0f, chunkWidthWorld);
                float localZ = Random.Range(0f, chunkDepthWorld);
                Vector3 worldPos2D = new Vector3(chunkPos.x + localX, 0f, chunkPos.z + localZ);
                Vector3 localPos = chunkTransform.InverseTransformPoint(new Vector3(worldPos2D.x, 0f, worldPos2D.z));

                float height = 0f;
                Vector3 normal = Vector3.up;
                float relativeHeight = 0f;

                if (vertices != null && vertices.Length > 0)
                {
                    float gridX = localPos.x / chunk.cellSize;
                    float gridZ = localPos.z / chunk.cellSize;

                    int x0 = Mathf.Clamp(Mathf.FloorToInt(gridX), 0, chunk.width);
                    int x1 = Mathf.Clamp(x0 + 1, 0, chunk.width);
                    int z0 = Mathf.Clamp(Mathf.FloorToInt(gridZ), 0, chunk.depth);
                    int z1 = Mathf.Clamp(z0 + 1, 0, chunk.depth);

                    float tx = gridX - x0;
                    float tz = gridZ - z0;

                    int numVertsWidth = chunk.width + 1;
                    if (z1 * numVertsWidth + x1 < vertices.Length)
                    {
                        float h00 = vertices[z0 * numVertsWidth + x0].y;
                        float h10 = vertices[z0 * numVertsWidth + x1].y;
                        float h01 = vertices[z1 * numVertsWidth + x0].y;
                        float h11 = vertices[z1 * numVertsWidth + x1].y;

                        float h_interpolated = Mathf.Lerp(Mathf.Lerp(h00, h10, tx), Mathf.Lerp(h01, h11, tx), tz);
                        relativeHeight = h_interpolated;
                        height = chunkTransform.TransformPoint(new Vector3(localPos.x, h_interpolated, localPos.z)).y;

                        if (normals != null && normals.Length == vertices.Length)
                        {
                            Vector3 n00 = normals[z0 * numVertsWidth + x0];
                            Vector3 n10 = normals[z0 * numVertsWidth + x1];
                            Vector3 n01 = normals[z1 * numVertsWidth + x0];
                            Vector3 n11 = normals[z1 * numVertsWidth + x1];

                            Vector3 normal_interpolated = Vector3.Lerp(Vector3.Lerp(n00, n10, tx), Vector3.Lerp(n01, n11, tx), tz).normalized;
                            normal = chunkTransform.TransformDirection(normal_interpolated);
                        }
                    }
                    else
                    {
                        relativeHeight = chunk.SampleHeight(worldPos2D.x, worldPos2D.z);
                        height = chunkPos.y + relativeHeight;
                    }
                }
                else
                {
                    relativeHeight = chunk.SampleHeight(worldPos2D.x, worldPos2D.z);
                    height = chunkPos.y + relativeHeight;

                    float cellSize = chunk.cellSize;
                    float hL = chunk.SampleHeight(worldPos2D.x - cellSize, worldPos2D.z);
                    float hR = chunk.SampleHeight(worldPos2D.x + cellSize, worldPos2D.z);
                    float hD = chunk.SampleHeight(worldPos2D.x, worldPos2D.z - cellSize);
                    float hU = chunk.SampleHeight(worldPos2D.x, worldPos2D.z + cellSize);
                    Vector3 tangentX = new Vector3(cellSize * 2f, hR - hL, 0);
                    Vector3 tangentZ = new Vector3(0, hU - hD, cellSize * 2f);
                    normal = Vector3.Cross(tangentZ, tangentX).normalized;
                }

                Vector3 spawnPos = new Vector3(worldPos2D.x, height, worldPos2D.z);

                // Sample normal for mathematical fallback
                if (vertices == null || vertices.Length == 0)
                {
                    float cellSize = chunk.cellSize;
                    float hL = chunk.SampleHeight(worldPos2D.x - cellSize, worldPos2D.z);
                    float hR = chunk.SampleHeight(worldPos2D.x + cellSize, worldPos2D.z);
                    float hD = chunk.SampleHeight(worldPos2D.x, worldPos2D.z - cellSize);
                    float hU = chunk.SampleHeight(worldPos2D.x, worldPos2D.z + cellSize);
                    Vector3 tangentX = new Vector3(cellSize * 2f, hR - hL, 0);
                    Vector3 tangentZ = new Vector3(0, hU - hD, cellSize * 2f);
                    normal = Vector3.Cross(tangentZ, tangentX).normalized;
                }

                float slopeAngle = Vector3.Angle(normal, Vector3.up);

                // Calculate trend weight [0f, 1f]
                float probability = 1f;
                float normalizedHeight = Mathf.Clamp01((relativeHeight - minChunkH) / heightRange);

                switch (trendMode)
                {
                    case TerrainTrendMode.ValleysAndLowlands:
                        probability = Mathf.Pow(1f - normalizedHeight, 1.8f); // Valley bias
                        break;
                    case TerrainTrendMode.RidgesAndPeaks:
                        probability = Mathf.Pow(normalizedHeight, 1.8f); // Peak bias
                        break;
                    case TerrainTrendMode.GentleSlopes:
                        probability = Mathf.Clamp01(1f - (slopeAngle / 22f)); // Flat ground bias
                        break;
                    case TerrainTrendMode.NoiseClustered:
                        // Pure Perlin noise clustering
                        float noise = Mathf.PerlinNoise((worldPos2D.x + seedOffset.x) / 70f, (worldPos2D.z + seedOffset.y) / 70f);
                        probability = noise > 0.52f ? (noise - 0.52f) / 0.48f : 0f;
                        break;
                }

                // Random evaluation against trend probability
                if (Random.value > probability)
                    continue;

                // Prevent overlaps
                bool tooClose = false;
                foreach (var pos in spawnedPositions)
                {
                    if (Vector3.Distance(spawnPos, pos) < minDistance)
                    {
                        tooClose = true;
                        break;
                    }
                }
                if (tooClose) continue;

                // Final raycast fallback to drop precisely onto the collider surface if mesh was not loaded
                if (vertices == null || vertices.Length == 0)
                {
                    if (chunk.TryGetComponent<Collider>(out var col))
                    {
                        Ray ray = new Ray(spawnPos + Vector3.up * 50f, Vector3.down);
                        if (col.Raycast(ray, out RaycastHit hit, 100f))
                        {
                            spawnPos = hit.point;
                            normal = hit.normal;
                        }
                    }
                }

                // Instantiate under prefab utility (preserves prefab connections in editor)
                GameObject spawnedObj = (GameObject)PrefabUtility.InstantiatePrefab(prefabToSpawn, holder.scene);
                if (spawnedObj != null)
                {
                    Undo.RegisterCreatedObjectUndo(spawnedObj, "Desert Prop Placement");
                    currentSpawns.Add(spawnedObj);

                    // Apply scale variation first
                    float scaleFactor = Random.Range(minScale, maxScale);
                    spawnedObj.transform.localScale = prefabToSpawn.transform.localScale * scaleFactor;

                    spawnedObj.transform.SetParent(holder.transform);
                    // Push the object upward along the terrain normal relative to its scale to avoid sinking too deep
                    spawnedObj.transform.position = spawnPos + normal * (heightOffset * scaleFactor);

                    // Aligned to normal + random Y spin
                    Quaternion rot = Quaternion.FromToRotation(Vector3.up, normal);
                    rot = rot * Quaternion.Euler(0f, Random.Range(0f, 360f), 0f);
                    spawnedObj.transform.rotation = rot;

                    spawnedPositions.Add(spawnPos);
                    totalSpawned++;
                }
            }

            // Mark Scene Dirty
            EditorSceneManager.MarkSceneDirty(chunkScene);
        }

        // Store execution group in stack for Undo operation
        spawnedObjectsHistory.Push(currentSpawns);
        Debug.Log($"[PropPlacementTool] Spawned {totalSpawned} props on {chunks.Length} chunks based on '{trendMode}' terrain trend.");
    }

    private void UndoLastPlacement()
    {
        if (spawnedObjectsHistory.Count > 0)
        {
            List<GameObject> lastSpawns = spawnedObjectsHistory.Pop();
            int count = 0;
            foreach (var obj in lastSpawns)
            {
                if (obj != null)
                {
                    Undo.DestroyObjectImmediate(obj);
                    count++;
                }
            }
            // Mark affected scenes as dirty
            DesertTerrainChunk[] chunks = FindObjectsOfType<DesertTerrainChunk>();
            foreach (var chunk in chunks)
            {
                EditorSceneManager.MarkSceneDirty(chunk.gameObject.scene);
            }
            Debug.Log($"[PropPlacementTool] Undone placement of {count} props.");
        }
        else
        {
            // Fallback: search chunks and clear if stack is empty
            ClearAllProps();
        }
    }

    private void ClearAllProps()
    {
        DesertTerrainChunk[] chunks = FindObjectsOfType<DesertTerrainChunk>();
        int count = 0;
        foreach (var chunk in chunks)
        {
            Transform t = chunk.transform.Find("Spawned_Props");
            if (t != null)
            {
                Undo.DestroyObjectImmediate(t.gameObject);
                EditorSceneManager.MarkSceneDirty(chunk.gameObject.scene);
                count++;
            }
        }
        spawnedObjectsHistory.Clear();
        Debug.Log($"[PropPlacementTool] Cleared all spawned props across {count} chunks.");
    }
}
