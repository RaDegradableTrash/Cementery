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

    // ── Prefab ─────────────────────────────────────────────────────────────────
    private GameObject prefabToSpawn;

    // ── Density & Placement ────────────────────────────────────────────────────
    private TerrainTrendMode trendMode = TerrainTrendMode.ValleysAndLowlands;
    private int   spawnAttemptsPerChunk = 150;
    private float minDistance           = 3.0f;

    // ── Scale & Variation ──────────────────────────────────────────────────────
    private float minScale    = 0.5f;
    private float maxScale    = 2.5f;
    private float heightOffset = 0.35f;
    private int   seed        = 1337;

    // ── Road Avoidance ─────────────────────────────────────────────────────────
    private bool  avoidRoads              = true;
    private float onRoadSpawnProbability  = 0.02f;  // 道路正上方的生成概率 (0~1)
    private float roadClearDistance       = 6f;     // 道路边缘内完全不生成的半径 (m)
    private float roadTransitionWidth     = 10f;    // 从清除距离到正常密度的过渡宽度 (m)

    // ── Undo History ───────────────────────────────────────────────────────────
    private static Stack<List<GameObject>> spawnedObjectsHistory = new Stack<List<GameObject>>();

    // ── GUI ────────────────────────────────────────────────────────────────────

    private void OnGUI()
    {
        GUILayout.Label("Desert Prop Placement Tool", EditorStyles.boldLabel);
        EditorGUILayout.Space();

        // 1. PREFAB SLOT
        EditorGUILayout.BeginVertical("box");
        GUILayout.Label("Object Placement Target (预制体挂载)", EditorStyles.boldLabel);
        prefabToSpawn = (GameObject)EditorGUILayout.ObjectField("Prefab (拖入物体)", prefabToSpawn, typeof(GameObject), false);
        EditorGUILayout.EndVertical();

        EditorGUILayout.Space();

        // 2. TERRAIN TREND
        EditorGUILayout.BeginVertical("box");
        GUILayout.Label("Distribution Settings (地形趋势与疏密)", EditorStyles.boldLabel);
        trendMode             = (TerrainTrendMode)EditorGUILayout.EnumPopup("Terrain Trend (地形趋势)", trendMode);
        spawnAttemptsPerChunk = EditorGUILayout.IntField("Density (密度/每区块尝试次数)", spawnAttemptsPerChunk);
        minDistance           = EditorGUILayout.FloatField("Min Distance (防穿模最小距离)", minDistance);
        EditorGUILayout.EndVertical();

        EditorGUILayout.Space();

        // 3. SCALE & VARIATION
        EditorGUILayout.BeginVertical("box");
        GUILayout.Label("Variation Settings (大小与随机变化)", EditorStyles.boldLabel);
        minScale     = EditorGUILayout.FloatField("Min Scale Multiplier", minScale);
        maxScale     = EditorGUILayout.FloatField("Max Scale Multiplier", maxScale);
        heightOffset = EditorGUILayout.FloatField("Height Offset (Y轴向上偏移比例)", heightOffset);
        seed         = EditorGUILayout.IntField("Random Seed (随机种子)", seed);
        EditorGUILayout.EndVertical();

        EditorGUILayout.Space();

        // 4. ROAD AVOIDANCE
        EditorGUILayout.BeginVertical("box");
        GUILayout.Label("Road Avoidance (道路回避设置)", EditorStyles.boldLabel);

        avoidRoads = EditorGUILayout.Toggle("Avoid Roads (避开道路)", avoidRoads);

        if (avoidRoads)
        {
            EditorGUI.indentLevel++;

            onRoadSpawnProbability = EditorGUILayout.Slider(
                new GUIContent("On-Road Probability (道路上的生成概率)",
                    "道路正中心的生成概率。0 = 完全不生成，1 = 和普通地面一样"),
                onRoadSpawnProbability, 0f, 1f);

            roadClearDistance = EditorGUILayout.FloatField(
                new GUIContent("Clear Distance (硬性清除距离)",
                    "道路边缘到此距离内完全不生成物体（即使生成概率不为0）。单位：米"),
                roadClearDistance);

            roadTransitionWidth = EditorGUILayout.FloatField(
                new GUIContent("Transition Width (过渡宽度)",
                    "从清除距离到正常密度的线性渐变宽度。越大过渡越柔和。单位：米"),
                roadTransitionWidth);

            // Clamp to sensible values
            roadClearDistance   = Mathf.Max(0f, roadClearDistance);
            roadTransitionWidth = Mathf.Max(0.1f, roadTransitionWidth);

            EditorGUILayout.HelpBox(
                $"效果预览：\n" +
                $"  ≤ {roadClearDistance:F1}m 处（道路边缘）：完全不生成\n" +
                $"  {roadClearDistance:F1}m → {roadClearDistance + roadTransitionWidth:F1}m：线性渐变到正常密度\n" +
                $"  道路中心处生成概率：{onRoadSpawnProbability * 100f:F0}%\n\n" +
                "需要道路涂层（Road Overlay）已绘制并保存到场景中。",
                MessageType.None);

            EditorGUI.indentLevel--;
        }

        EditorGUILayout.EndVertical();

        EditorGUILayout.Space();

        // TERRAIN TREND HELP
        string trendHelp = "";
        switch (trendMode)
        {
            case TerrainTrendMode.ValleysAndLowlands:
                trendHelp = "洼地与低谷模式：低海拔处水分较多，仙人掌明显多且密；高处非常少。"; break;
            case TerrainTrendMode.RidgesAndPeaks:
                trendHelp = "山脊与高地模式：高山顶部和沙丘山脊生长较多；低谷平原较少。"; break;
            case TerrainTrendMode.GentleSlopes:
                trendHelp = "平缓开阔地模式：地面越平坦生长的越密集；陡坡区域几乎不生长。"; break;
            case TerrainTrendMode.NoiseClustered:
                trendHelp = "纯柏林噪声模式：不受地形起伏影响，随机呈岛屿状成簇分布，有些地方极密，有些地方全无。"; break;
        }
        EditorGUILayout.HelpBox(trendHelp + "\n物体将自动放置在所属区块 scene 内的 'Spawned_Props' 节点中。", MessageType.Info);

        EditorGUILayout.Space();

        // ACTION BUTTONS
        if (GUILayout.Button("Place Props on ALL Loaded Chunks (开始放置)", GUILayout.Height(35)))
        {
            PlaceProps();
        }

        EditorGUILayout.Space();

        // UNDO BUTTONS
        EditorGUILayout.BeginHorizontal();
        if (GUILayout.Button("Undo Last Placement (撤回上次放置)", GUILayout.Height(25)))
            UndoLastPlacement();
        if (GUILayout.Button("Clear ALL Spawned Props (清除所有放置)", GUILayout.Height(25)))
            ClearAllProps();
        EditorGUILayout.EndHorizontal();
    }

    // ── Core placement ─────────────────────────────────────────────────────────

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

        seed = Random.Range(1, 100000);
        Random.InitState(seed);
        Vector2 seedOffset = new Vector2(Random.Range(-10000f, 10000f), Random.Range(-10000f, 10000f));
        int totalSpawned = 0;

        List<GameObject> currentSpawns = new List<GameObject>();

        foreach (var chunk in chunks)
        {
            Transform chunkTransform = chunk.transform;
            float chunkWidthWorld    = chunk.width * chunk.cellSize;
            float chunkDepthWorld    = chunk.depth * chunk.cellSize;
            Vector3 chunkPos         = chunkTransform.position;
            Scene chunkScene         = chunk.gameObject.scene;

            // ── Road overlay data (cached once per chunk) ──────────────────────
            RoadSampler roadSampler = null;
            if (avoidRoads)
            {
                roadSampler = BuildRoadSampler(chunk);
            }

            // ── Props holder ───────────────────────────────────────────────────
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
                holder.transform.localScale    = Vector3.one;
                Undo.RegisterCreatedObjectUndo(holder, "Spawn Chunk Props");
            }

            // ── Terrain mesh data ──────────────────────────────────────────────
            MeshFilter filter   = chunk.GetComponent<MeshFilter>();
            Vector3[] vertices  = null;
            Vector3[] normals   = null;
            if (filter != null && filter.sharedMesh != null)
            {
                vertices = filter.sharedMesh.vertices;
                normals  = filter.sharedMesh.normals;
            }

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

            // Collect existing spawned positions to prevent overlap
            List<Vector3> spawnedPositions = new List<Vector3>();
            if (existingHolder != null)
            {
                foreach (Transform child in existingHolder)
                    spawnedPositions.Add(child.position);
            }

            // ── Spawn loop ─────────────────────────────────────────────────────
            for (int i = 0; i < spawnAttemptsPerChunk; i++)
            {
                float localX = Random.Range(0f, chunkWidthWorld);
                float localZ = Random.Range(0f, chunkDepthWorld);
                Vector3 worldPos2D = new Vector3(chunkPos.x + localX, 0f, chunkPos.z + localZ);
                Vector3 localPos   = chunkTransform.InverseTransformPoint(new Vector3(worldPos2D.x, 0f, worldPos2D.z));

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
                        relativeHeight = Mathf.Lerp(Mathf.Lerp(h00, h10, tx), Mathf.Lerp(h01, h11, tx), tz);
                        height = chunkTransform.TransformPoint(new Vector3(localPos.x, relativeHeight, localPos.z)).y;

                        if (normals != null && normals.Length == vertices.Length)
                        {
                            Vector3 n00 = normals[z0 * numVertsWidth + x0];
                            Vector3 n10 = normals[z0 * numVertsWidth + x1];
                            Vector3 n01 = normals[z1 * numVertsWidth + x0];
                            Vector3 n11 = normals[z1 * numVertsWidth + x1];
                            normal = Vector3.Lerp(Vector3.Lerp(n00, n10, tx), Vector3.Lerp(n01, n11, tx), tz).normalized;
                            normal = chunkTransform.TransformDirection(normal);
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
                    normal = Vector3.Cross(new Vector3(0, hU - hD, cellSize * 2f),
                                          new Vector3(cellSize * 2f, hR - hL, 0)).normalized;
                }

                float slopeAngle = Vector3.Angle(normal, Vector3.up);
                Vector3 spawnPos = new Vector3(worldPos2D.x, height, worldPos2D.z);

                // ── Terrain trend probability ──────────────────────────────────
                float probability = 1f;
                float normalizedHeight = Mathf.Clamp01((relativeHeight - minChunkH) / heightRange);

                switch (trendMode)
                {
                    case TerrainTrendMode.ValleysAndLowlands:
                        probability = Mathf.Pow(1f - normalizedHeight, 1.8f); break;
                    case TerrainTrendMode.RidgesAndPeaks:
                        probability = Mathf.Pow(normalizedHeight, 1.8f); break;
                    case TerrainTrendMode.GentleSlopes:
                        probability = Mathf.Clamp01(1f - (slopeAngle / 22f)); break;
                    case TerrainTrendMode.NoiseClustered:
                        float noise = Mathf.PerlinNoise((worldPos2D.x + seedOffset.x) / 70f,
                                                         (worldPos2D.z + seedOffset.y) / 70f);
                        probability = noise > 0.52f ? (noise - 0.52f) / 0.48f : 0f; break;
                }

                // ── Road avoidance modifier ────────────────────────────────────
                if (avoidRoads && roadSampler != null)
                {
                    float roadFactor = SampleRoadFactor(roadSampler, localPos, chunk);
                    probability *= roadFactor;
                }

                if (Random.value > probability) continue;

                // ── Overlap check ──────────────────────────────────────────────
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

                // ── Collider fallback ──────────────────────────────────────────
                if (vertices == null || vertices.Length == 0)
                {
                    if (chunk.TryGetComponent<Collider>(out var col))
                    {
                        Ray ray = new Ray(spawnPos + Vector3.up * 50f, Vector3.down);
                        if (col.Raycast(ray, out RaycastHit hit, 100f))
                        {
                            spawnPos = hit.point;
                            normal   = hit.normal;
                        }
                    }
                }

                // ── Instantiate ────────────────────────────────────────────────
                GameObject spawnedObj = (GameObject)PrefabUtility.InstantiatePrefab(prefabToSpawn, holder.scene);
                if (spawnedObj != null)
                {
                    Undo.RegisterCreatedObjectUndo(spawnedObj, "Desert Prop Placement");
                    currentSpawns.Add(spawnedObj);

                    float scaleFactor = Random.Range(minScale, maxScale);
                    spawnedObj.transform.localScale = prefabToSpawn.transform.localScale * scaleFactor;
                    spawnedObj.transform.SetParent(holder.transform);
                    spawnedObj.transform.position = spawnPos + normal * (heightOffset * scaleFactor);

                    Quaternion rot = Quaternion.FromToRotation(Vector3.up, normal);
                    rot = rot * Quaternion.Euler(0f, Random.Range(0f, 360f), 0f);
                    spawnedObj.transform.rotation = rot;

                    // Register with BetterGameplayManager
                    OptimizableObject optObj = spawnedObj.GetComponent<OptimizableObject>();
                    if (optObj == null)
                        optObj = Undo.AddComponent<OptimizableObject>(spawnedObj);
                    optObj.disableEntireGameObject = true;
                    optObj.useFrustumCulling = true;

                    spawnedPositions.Add(spawnPos);
                    totalSpawned++;
                }
            }

            EditorSceneManager.MarkSceneDirty(chunkScene);
        }

        spawnedObjectsHistory.Push(currentSpawns);
        Debug.Log($"[PropPlacementTool] Spawned {totalSpawned} props on {chunks.Length} chunks based on '{trendMode}' terrain trend." +
                  (avoidRoads ? " (Road avoidance active)" : ""));
    }

    // ── Road Sampling ──────────────────────────────────────────────────────────

    /// <summary>
    /// Holds the overlay vertex + color data for fast per-point sampling.
    /// </summary>
    private class RoadSampler
    {
        public Vector3[] overlayVertices;   // Local-space vertices of the road overlay mesh
        public Color[]   overlayColors;     // Vertex colors; alpha = road strength [0, 1]
        public bool      hasData;
    }

    /// <summary>
    /// Builds a RoadSampler for the given chunk by reading its RoadOverlay child.
    /// Returns null if no overlay exists.
    /// </summary>
    private static RoadSampler BuildRoadSampler(DesertTerrainChunk chunk)
    {
        Transform overlayT = chunk.transform.Find("RoadOverlay");
        if (overlayT == null) return null;

        DesertTerrainRoadOverlay overlay = overlayT.GetComponent<DesertTerrainRoadOverlay>();
        if (overlay == null) return null;

        Mesh mesh = overlay.GetMesh();
        if (mesh == null) return null;

        Color[] colors = mesh.colors;
        if (colors == null || colors.Length == 0) return null;

        Vector3[] verts = mesh.vertices;
        if (verts == null || verts.Length != colors.Length) return null;

        return new RoadSampler { overlayVertices = verts, overlayColors = colors, hasData = true };
    }

    /// <summary>
    /// Samples the road strength [0,1] at a local-space point using bilinear interpolation
    /// on the overlay's vertex grid (same grid as the terrain mesh).
    /// Then converts road strength to a spawn probability factor [0,1]:
    ///   • Inside hardClear radius from road edge → always 0
    ///   • Transition zone → linear ramp from 0 to 1
    ///   • Far from road → factor = 1 (unaffected)
    ///   • On the road center → factor = onRoadSpawnProbability
    /// </summary>
    private float SampleRoadFactor(RoadSampler sampler, Vector3 localPos, DesertTerrainChunk chunk)
    {
        // Bilinear interpolation in grid space (same as terrain height sampling)
        float gridX = localPos.x / chunk.cellSize;
        float gridZ = localPos.z / chunk.cellSize;

        int x0 = Mathf.Clamp(Mathf.FloorToInt(gridX), 0, chunk.width);
        int x1 = Mathf.Clamp(x0 + 1, 0, chunk.width);
        int z0 = Mathf.Clamp(Mathf.FloorToInt(gridZ), 0, chunk.depth);
        int z1 = Mathf.Clamp(z0 + 1, 0, chunk.depth);

        float tx = gridX - x0;
        float tz = gridZ - z0;
        int W = chunk.width + 1;

        int i00 = z0 * W + x0;
        int i10 = z0 * W + x1;
        int i01 = z1 * W + x0;
        int i11 = z1 * W + x1;

        // Safety: overlay may have skirt verts appended beyond the main grid
        int maxIdx = sampler.overlayColors.Length - 1;
        if (i11 > maxIdx) return 1f; // No data, don't affect probability

        float a00 = sampler.overlayColors[i00].a;
        float a10 = sampler.overlayColors[i10].a;
        float a01 = sampler.overlayColors[i01].a;
        float a11 = sampler.overlayColors[i11].a;
        float roadAlpha = Mathf.Lerp(Mathf.Lerp(a00, a10, tx), Mathf.Lerp(a01, a11, tx), tz);

        // If there is no road here at all, don't touch the probability
        if (roadAlpha <= 0.01f) return 1f;

        // roadAlpha tells us how strongly "on the road" this point is.
        //
        // Distance logic is approximated through alpha:
        //   • alpha ≈ 1 → road centre
        //   • alpha ≈ 0+ → road edge / fade-out zone from the brush
        //
        // We treat roadAlpha directly as a "proximity to road center" signal.
        // The brush already paints a smooth falloff from centre → edge, so:
        //   clearThreshold = alpha above which the hard clear zone applies
        //   The transition is (alpha - clearThreshold) / transitionRange

        // Convert world distances to alpha thresholds using the brush radius concept:
        // We don't store a global brush radius, so we use normalized fractions of [0,1].
        // A simpler approach: alpha above clearAlpha → fully cleared; below transAlpha → full density.
        // clearDistance & transitionWidth are in metres; convert using cellSize as proxy.
        float totalRoadRadius = roadClearDistance + roadTransitionWidth;   // outer edge of transition
        float clearAlpha      = totalRoadRadius > 0f ? Mathf.Clamp01(roadTransitionWidth / totalRoadRadius) : 0.5f;
        float transAlpha      = totalRoadRadius > 0f ? Mathf.Clamp01(roadClearDistance  / totalRoadRadius) : 0.1f;
        // So: alpha > clearAlpha → inside hard clear zone (factor = 0)
        //     transAlpha < alpha <= clearAlpha → transition (ramp 0→onRoadProb)
        //     alpha ≤ transAlpha → lightly painted edge → small probability remaining
        // Note: because the brush paints soft falloff, high alpha = close to road centre,
        // and "clearAlpha" marks the inner boundary.

        float factor;
        if (roadAlpha >= clearAlpha)
        {
            // Hard clear zone → interpolate between 0 and onRoadSpawnProbability
            // (alpha=clearAlpha → factor=0, alpha=1 → factor=onRoadSpawnProbability)
            float t = (roadAlpha - clearAlpha) / Mathf.Max(0.001f, 1f - clearAlpha);
            factor = Mathf.Lerp(0f, onRoadSpawnProbability, t);
        }
        else if (roadAlpha >= transAlpha)
        {
            // Transition zone → ramp from onRoadSpawnProbability back toward 1
            float t = (roadAlpha - transAlpha) / Mathf.Max(0.001f, clearAlpha - transAlpha);
            factor = Mathf.Lerp(1f, 0f, t);   // from far edge (1) to clear edge (0)
        }
        else
        {
            // Very lightly painted edge: barely affected
            factor = 1f;
        }

        return Mathf.Clamp01(factor);
    }

    // ── Undo / Clear ───────────────────────────────────────────────────────────

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
            DesertTerrainChunk[] chunks = FindObjectsOfType<DesertTerrainChunk>();
            foreach (var chunk in chunks)
                EditorSceneManager.MarkSceneDirty(chunk.gameObject.scene);

            Debug.Log($"[PropPlacementTool] Undone placement of {count} props.");
        }
        else
        {
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
