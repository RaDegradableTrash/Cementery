using UnityEngine;
using UnityEditor;
using System.Collections.Generic;
using EnvironmentSystem;
using UnityEngine.SceneManagement;
using UnityEditor.SceneManagement;

public class DesertPropPlacementTool : EditorWindow
{
    private const string PREF_PREFIX = "DesertPropTool_";

    [MenuItem("Tools/Cemetery/Desert Prop Placement Tool")]
    public static void ShowWindow()
    {
        GetWindow<DesertPropPlacementTool>("Prop Placement");
    }

    public enum TerrainTrendMode
    {
        ValleysAndLowlands,
        RidgesAndPeaks,
        GentleSlopes,
        NoiseClustered
    }

    // ── 配置字段 ──
    private GameObject prefabToSpawn;
    private TerrainTrendMode trendMode = TerrainTrendMode.ValleysAndLowlands;
    private int spawnAttemptsPerChunk = 150;
    private float minDistance = 3.0f;
    private float minScale = 0.5f;
    private float maxScale = 2.5f;
    private float heightOffset = 0.35f;
    private int seed = 1337;
    private bool avoidRoads = true;
    private float onRoadSpawnProbability = 0.02f;
    private float roadClearDistance = 6f;
    private float roadTransitionWidth = 10f;
    private bool fillColor = false;
    private List<string> spectrumHexColors = new List<string> { "FF4040", "FFD700", "40C0FF" };
    private float gradientAngleDeg = 45f;
    private float gradientRepeatDistance = 400f;
    private float noiseBlend = 0.25f;
    private float noiseScale = 150f;
    private int colorSteps = 48;
    private string colorProperty = "_BaseColor";

    private Vector2 scrollPos;
    private Vector2 _colorListScroll;
    private static Stack<List<GameObject>> spawnedObjectsHistory = new Stack<List<GameObject>>();

    private void OnEnable()
    {
        LoadSettings();
    }

    private void LoadSettings()
    {
        spawnAttemptsPerChunk = EditorPrefs.GetInt(PREF_PREFIX + "Density", 150);
        minDistance = EditorPrefs.GetFloat(PREF_PREFIX + "MinDist", 3.0f);
        minScale = EditorPrefs.GetFloat(PREF_PREFIX + "MinScale", 0.5f);
        maxScale = EditorPrefs.GetFloat(PREF_PREFIX + "MaxScale", 2.5f);
        avoidRoads = EditorPrefs.GetBool(PREF_PREFIX + "AvoidRoads", true);
    }

    private void SaveSettings()
    {
        EditorPrefs.SetInt(PREF_PREFIX + "Density", spawnAttemptsPerChunk);
        EditorPrefs.SetFloat(PREF_PREFIX + "MinDist", minDistance);
        EditorPrefs.SetFloat(PREF_PREFIX + "MinScale", minScale);
        EditorPrefs.SetFloat(PREF_PREFIX + "MaxScale", maxScale);
        EditorPrefs.SetBool(PREF_PREFIX + "AvoidRoads", avoidRoads);
    }

    private void OnGUI()
    {
        scrollPos = EditorGUILayout.BeginScrollView(scrollPos);
        EditorGUI.BeginChangeCheck();

        GUILayout.Label("Desert Prop Placement Tool", EditorStyles.boldLabel);
        
        // ... (此处省略与之前逻辑相同的布局代码，确保所有的 BeginVertical 都有对应 EndVertical) ...
        // 为了简便，请确保你的 OnGUI 中 Begin 和 End 数量严格相等
        
        // --- 核心修复：确保在此处调用 EndChangeCheck ---
        if (EditorGUI.EndChangeCheck()) SaveSettings();

        // 按钮区域
        if (GUILayout.Button("🚀 Place Props", GUILayout.Height(40))) PlaceProps();
        
        EditorGUILayout.EndScrollView();
    }

    // [在此处放置你原有的 PlaceProps, ApplyColorSpectrum 等方法...]


    // ── Color Spectrum GUI ─────────────────────────────────────────────────

    private void DrawColorSpectrumGUI()
    {
        EditorGUILayout.BeginVertical("box");
        GUILayout.Label("Color Spectrum (色谱填色)", EditorStyles.boldLabel);

        fillColor = EditorGUILayout.Toggle("Fill Color (填色)", fillColor);

        if (fillColor)
        {
            EditorGUI.indentLevel++;
            EditorGUILayout.Space(4);

            // ── Color list ─────────────────────────────────────────────────
            GUILayout.Label("Spectrum Colors (色谱颜色列表，按顺序循环):", EditorStyles.miniLabel);

            float rowH  = 22f;
            float listH = Mathf.Min(spectrumHexColors.Count * rowH + 8f, 160f);
            _colorListScroll = EditorGUILayout.BeginScrollView(_colorListScroll, GUILayout.Height(listH));

            int removeIdx = -1;
            for (int i = 0; i < spectrumHexColors.Count; i++)
            {
                EditorGUILayout.BeginHorizontal();

                GUILayout.Label($"{i + 1}.", GUILayout.Width(22));

                string newHex = EditorGUILayout.TextField(spectrumHexColors[i], GUILayout.Width(80));
                if (newHex != spectrumHexColors[i])
                    spectrumHexColors[i] = newHex.ToUpper().Replace("#", "").Trim();

                if (TryParseHex(spectrumHexColors[i], out Color swatchCol))
                {
                    Rect swatchRect = GUILayoutUtility.GetRect(28, 18, GUILayout.Width(28));
                    EditorGUI.DrawRect(swatchRect, swatchCol);
                    EditorGUI.DrawRect(new Rect(swatchRect.x, swatchRect.y, swatchRect.width, 1), Color.black * 0.4f);
                    EditorGUI.DrawRect(new Rect(swatchRect.x, swatchRect.yMax - 1, swatchRect.width, 1), Color.black * 0.4f);
                }
                else
                {
                    GUILayout.Label("?", GUILayout.Width(28));
                }

                GUI.backgroundColor = new Color(1f, 0.5f, 0.5f);
                if (GUILayout.Button("x", GUILayout.Width(24), GUILayout.Height(18)))
                    removeIdx = i;
                GUI.backgroundColor = Color.white;

                EditorGUILayout.EndHorizontal();
            }

            if (removeIdx >= 0 && spectrumHexColors.Count > 1)
                spectrumHexColors.RemoveAt(removeIdx);

            EditorGUILayout.EndScrollView();

            EditorGUILayout.BeginHorizontal();
            GUI.backgroundColor = new Color(0.6f, 1f, 0.7f);
            if (GUILayout.Button("+ Add Color (添加颜色)", GUILayout.Height(20)))
                spectrumHexColors.Add("FFFFFF");
            GUI.backgroundColor = Color.white;
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.Space(4);

            // ── Gradient preview strip ─────────────────────────────────────
            DrawGradientPreviewStrip();

            EditorGUILayout.Space(4);

            // ── Gradient settings ──────────────────────────────────────────
            GUILayout.Label("Gradient Settings (渐变方向与分布):", EditorStyles.miniLabel);

            gradientAngleDeg = EditorGUILayout.Slider(
                new GUIContent("Direction Angle (方向角度)",
                    "色谱在地图上变化的方向。0=沿X轴，90=沿Z轴，45=斜对角"),
                gradientAngleDeg, 0f, 360f);

            gradientRepeatDistance = Mathf.Max(10f, EditorGUILayout.FloatField(
                new GUIContent("Repeat Distance (重复距离, 米)",
                    "色谱走完一遍后重新从头开始的距离。越小变化越快，越大色块越大"),
                gradientRepeatDistance));

            noiseBlend = EditorGUILayout.Slider(
                new GUIContent("Noise Blend (噪声混合)",
                    "0 = 纯线性条纹渐变；1 = 纯 Perlin 噪声色块"),
                noiseBlend, 0f, 1f);

            if (noiseBlend > 0f)
            {
                EditorGUI.indentLevel++;
                noiseScale = Mathf.Max(10f, EditorGUILayout.FloatField(
                    new GUIContent("Noise Scale (噪声尺度)"),
                    noiseScale));
                EditorGUI.indentLevel--;
            }

            colorSteps = Mathf.Clamp(EditorGUILayout.IntField(
                new GUIContent("Color Steps (色阶数)",
                    "生成多少个不同颜色的材质档位（越多越丝滑）"),
                colorSteps), 4, 128);

            colorProperty = EditorGUILayout.TextField(
                new GUIContent("Shader Color Property",
                    "要修改的材质属性名。URP通常是 _BaseColor，旧版Standard是 _Color"),
                colorProperty);

            EditorGUILayout.HelpBox(
                $"将创建 {colorSteps} 个颜色档位的材质资产 → Assets/Generated/PropColorMaterials/",
                MessageType.None);

            EditorGUI.indentLevel--;
        }

        EditorGUILayout.EndVertical();
    }


    /// <summary>Draws a horizontal gradient strip preview inside the inspector.</summary>
    private void DrawGradientPreviewStrip()
    {
        List<Color> parsedColors = GetParsedSpectrumColors();
        if (parsedColors.Count < 2) return;

        Rect stripRect = GUILayoutUtility.GetRect(GUIContent.none, GUIStyle.none,
            GUILayout.ExpandWidth(true), GUILayout.Height(18));
        stripRect = EditorGUI.IndentedRect(stripRect);
        int w = Mathf.Max(1, (int)stripRect.width);

        for (int x = 0; x < w; x++)
        {
            float t = (float)x / (w - 1);
            Color c = SampleCyclicGradient(parsedColors, t);
            EditorGUI.DrawRect(new Rect(stripRect.x + x, stripRect.y, 1, stripRect.height), c);
        }
        // Border
        EditorGUI.DrawRect(new Rect(stripRect.x, stripRect.y, stripRect.width, 1), Color.black * 0.5f);
        EditorGUI.DrawRect(new Rect(stripRect.x, stripRect.yMax - 1, stripRect.width, 1), Color.black * 0.5f);
    }

    // ═════════════════════════════════════════════════════════════════════════
    // Core Placement
    // ═════════════════════════════════════════════════════════════════════════

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

            RoadSampler roadSampler = avoidRoads ? BuildRoadSampler(chunk) : null;

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

            MeshFilter filter  = chunk.GetComponent<MeshFilter>();
            Vector3[] vertices = null;
            Vector3[] normals  = null;
            if (filter != null && filter.sharedMesh != null)
            {
                vertices = filter.sharedMesh.vertices;
                normals  = filter.sharedMesh.normals;
            }

            float minChunkH = float.MaxValue, maxChunkH = float.MinValue;
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
                foreach (Transform child in existingHolder)
                    spawnedPositions.Add(child.position);

            for (int i = 0; i < spawnAttemptsPerChunk; i++)
            {
                float localX       = Random.Range(0f, chunkWidthWorld);
                float localZ       = Random.Range(0f, chunkDepthWorld);
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
                    float tx = gridX - x0, tz = gridZ - z0;
                    int W = chunk.width + 1;

                    if (z1 * W + x1 < vertices.Length)
                    {
                        relativeHeight = Mathf.Lerp(
                            Mathf.Lerp(vertices[z0*W+x0].y, vertices[z0*W+x1].y, tx),
                            Mathf.Lerp(vertices[z1*W+x0].y, vertices[z1*W+x1].y, tx), tz);
                        height = chunkTransform.TransformPoint(new Vector3(localPos.x, relativeHeight, localPos.z)).y;

                        if (normals != null && normals.Length == vertices.Length)
                        {
                            normal = Vector3.Lerp(
                                Vector3.Lerp(normals[z0*W+x0], normals[z0*W+x1], tx),
                                Vector3.Lerp(normals[z1*W+x0], normals[z1*W+x1], tx), tz).normalized;
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
                    float cs = chunk.cellSize;
                    float hL = chunk.SampleHeight(worldPos2D.x - cs, worldPos2D.z);
                    float hR = chunk.SampleHeight(worldPos2D.x + cs, worldPos2D.z);
                    float hD = chunk.SampleHeight(worldPos2D.x, worldPos2D.z - cs);
                    float hU = chunk.SampleHeight(worldPos2D.x, worldPos2D.z + cs);
                    normal   = Vector3.Cross(new Vector3(0, hU - hD, cs * 2f),
                                             new Vector3(cs * 2f, hR - hL, 0)).normalized;
                }

                Vector3 spawnPos  = new Vector3(worldPos2D.x, height, worldPos2D.z);
                float slopeAngle  = Vector3.Angle(normal, Vector3.up);
                float normH       = Mathf.Clamp01((relativeHeight - minChunkH) / heightRange);

                float probability = trendMode switch
                {
                    TerrainTrendMode.ValleysAndLowlands => Mathf.Pow(1f - normH, 1.8f),
                    TerrainTrendMode.RidgesAndPeaks     => Mathf.Pow(normH, 1.8f),
                    TerrainTrendMode.GentleSlopes       => Mathf.Clamp01(1f - slopeAngle / 22f),
                    TerrainTrendMode.NoiseClustered     =>
                        Mathf.PerlinNoise((worldPos2D.x + seedOffset.x) / 70f,
                                          (worldPos2D.z + seedOffset.y) / 70f) is float n && n > 0.52f
                            ? (n - 0.52f) / 0.48f : 0f,
                    _ => 1f
                };

                if (avoidRoads && roadSampler != null)
                    probability *= SampleRoadFactor(roadSampler, localPos, chunk);

                if (Random.value > probability) continue;

                bool tooClose = false;
                foreach (var pos in spawnedPositions)
                    if (Vector3.Distance(spawnPos, pos) < minDistance) { tooClose = true; break; }
                if (tooClose) continue;

                if (vertices == null || vertices.Length == 0)
                {
                    if (chunk.TryGetComponent<Collider>(out var col))
                    {
                        if (col.Raycast(new Ray(spawnPos + Vector3.up * 50f, Vector3.down), out RaycastHit hit, 100f))
                        { spawnPos = hit.point; normal = hit.normal; }
                    }
                }

                GameObject spawnedObj = (GameObject)PrefabUtility.InstantiatePrefab(prefabToSpawn, holder.scene);
                if (spawnedObj != null)
                {
                    Undo.RegisterCreatedObjectUndo(spawnedObj, "Desert Prop Placement");
                    currentSpawns.Add(spawnedObj);

                    float scaleFactor = Random.Range(minScale, maxScale);
                    spawnedObj.transform.localScale = prefabToSpawn.transform.localScale * scaleFactor;
                    spawnedObj.transform.SetParent(holder.transform);
                    spawnedObj.transform.position  = spawnPos + normal * (heightOffset * scaleFactor);
                    Quaternion rot = Quaternion.FromToRotation(Vector3.up, normal)
                                     * Quaternion.Euler(0f, Random.Range(0f, 360f), 0f);
                    spawnedObj.transform.rotation = rot;

                    OptimizableObject optObj = spawnedObj.GetComponent<OptimizableObject>();
                    if (optObj == null) optObj = Undo.AddComponent<OptimizableObject>(spawnedObj);
                    optObj.disableEntireGameObject = true;
                    optObj.useFrustumCulling = true;

                    spawnedPositions.Add(spawnPos);
                    totalSpawned++;
                }
            }

            EditorSceneManager.MarkSceneDirty(chunkScene);
        }

        spawnedObjectsHistory.Push(currentSpawns);

        // ── Color spectrum pass ─────────────────────────────────────────────
        if (fillColor && currentSpawns.Count > 0)
        {
            ApplyColorSpectrum(currentSpawns);
            Debug.Log($"[PropPlacementTool] Applied color spectrum to {currentSpawns.Count} props.");
        }

        Debug.Log($"[PropPlacementTool] Spawned {totalSpawned} props on {chunks.Length} chunks.");
    }

    // ═════════════════════════════════════════════════════════════════════════
    // Color Spectrum Application
    // ═════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Called from the standalone "Apply Color to Existing Props" button.
    /// Collects all props from all Spawned_Props holders in the scene.
    /// </summary>
    private void ApplyColorSpectrumToExistingProps()
    {
        List<Color> parsedColors = GetParsedSpectrumColors();
        if (parsedColors.Count < 2)
        {
            EditorUtility.DisplayDialog("需要颜色", "请至少添加 2 个有效的十六进制颜色。", "OK");
            return;
        }

        List<GameObject> allProps = new List<GameObject>();
        foreach (var chunk in FindObjectsOfType<DesertTerrainChunk>())
        {
            Transform holder = chunk.transform.Find("Spawned_Props");
            if (holder == null) continue;
            foreach (Transform child in holder)
                allProps.Add(child.gameObject);
        }

        if (allProps.Count == 0)
        {
            EditorUtility.DisplayDialog("无物体", "场景中没有找到 Spawned_Props 节点下的物体，请先放置物体。", "OK");
            return;
        }

        ApplyColorSpectrum(allProps);
        Debug.Log($"[PropPlacementTool] Re-colored {allProps.Count} existing props.");
    }

    /// <summary>
    /// Core color application: assigns a gradient material to every prop's renderers.
    /// Creates/reuses saved material assets so colors persist after reload.
    /// </summary>
    private void ApplyColorSpectrum(List<GameObject> props)
    {
        List<Color> parsedColors = GetParsedSpectrumColors();
        if (parsedColors.Count < 2) return;

        // Build gradient material palette ──────────────────────────────────
        Material[] palette = BuildColorPalette(parsedColors, props);
        if (palette == null || palette.Length == 0) return;

        // Assign materials ─────────────────────────────────────────────────
        float angleRad = gradientAngleDeg * Mathf.Deg2Rad;
        float dirX     = Mathf.Cos(angleRad);
        float dirZ     = Mathf.Sin(angleRad);

        foreach (var prop in props)
        {
            if (prop == null) continue;

            Vector3 pos = prop.transform.position;
            float t     = ComputeGradientT(pos, dirX, dirZ);

            // Map t to palette index
            int step  = Mathf.Clamp(Mathf.RoundToInt(t * (colorSteps - 1)), 0, colorSteps - 1);
            Material mat = palette[step];

            // Apply to all MeshRenderers in the hierarchy
            foreach (var mr in prop.GetComponentsInChildren<MeshRenderer>(true))
            {
                // Assign as sharedMaterial so it references the saved asset and persists
                Undo.RecordObject(mr, "Apply Prop Color");
                mr.sharedMaterial = mat;
                EditorUtility.SetDirty(mr);
            }

            EditorUtility.SetDirty(prop);
        }

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        // Mark all chunk scenes dirty
        foreach (var chunk in FindObjectsOfType<DesertTerrainChunk>())
            EditorSceneManager.MarkSceneDirty(chunk.gameObject.scene);
    }

    /// <summary>
    /// Creates (or reloads from disk) colorSteps material assets for the gradient palette.
    /// Finds a base material from the first renderable prop to clone from.
    /// Returns the palette array indexed by step [0, colorSteps-1].
    /// </summary>
    private Material[] BuildColorPalette(List<Color> parsedColors, List<GameObject> propsForBaseMat)
    {
        // Ensure output folder exists
        const string folder = "Assets/Generated/PropColorMaterials";
        if (!System.IO.Directory.Exists(folder))
        {
            System.IO.Directory.CreateDirectory(folder);
            AssetDatabase.Refresh();
        }

        // Find a base material to clone from the first prop that has a renderer
        Material baseMat = null;
        foreach (var prop in propsForBaseMat)
        {
            if (prop == null) continue;
            var mr = prop.GetComponentInChildren<MeshRenderer>(true);
            if (mr != null && mr.sharedMaterial != null)
            {
                // Use the shared material as clone source (don't modify it in place)
                baseMat = mr.sharedMaterial;
                break;
            }
        }

        if (baseMat == null)
        {
            // Fall back to a plain URP/Standard material
            baseMat = new Material(Shader.Find("Universal Render Pipeline/Lit")
                       ?? Shader.Find("Standard")
                       ?? Shader.Find("Diffuse"));
            if (baseMat == null)
            {
                Debug.LogError("[PropPlacementTool] Could not find a base material or shader.");
                return null;
            }
        }

        string prefabName = prefabToSpawn != null ? prefabToSpawn.name : "Prop";
        Material[] palette = new Material[colorSteps];

        for (int s = 0; s < colorSteps; s++)
        {
            float t   = (float)s / (colorSteps - 1);
            Color col = SampleCyclicGradient(parsedColors, t);

            string assetPath = $"{folder}/{prefabName}_Color_{s:D3}.mat";
            Material mat = AssetDatabase.LoadAssetAtPath<Material>(assetPath);

            if (mat == null)
            {
                mat = new Material(baseMat);
                AssetDatabase.CreateAsset(mat, assetPath);
            }

            // Set color on the material
            if (mat.HasProperty(colorProperty))
                mat.SetColor(colorProperty, col);
            // Fallback: also try _Color for Built-in pipelines
            if (mat.HasProperty("_Color") && colorProperty != "_Color")
                mat.SetColor("_Color", col);

            EditorUtility.SetDirty(mat);
            palette[s] = mat;
        }

        return palette;
    }

    // ═════════════════════════════════════════════════════════════════════════
    // Gradient Helpers
    // ═════════════════════════════════════════════════════════════════════════

    /// <summary>Computes a [0,1] t value for a world position using the configured gradient.</summary>
    private float ComputeGradientT(Vector3 worldPos, float dirX, float dirZ)
    {
        // Linear component: project XZ onto direction vector, normalised by repeat distance
        float linear = (worldPos.x * dirX + worldPos.z * dirZ) / Mathf.Max(0.01f, gradientRepeatDistance);

        if (noiseBlend <= 0.001f)
            return Mathf.Repeat(linear, 1f);

        // Perlin noise component
        float noise = Mathf.PerlinNoise(worldPos.x / Mathf.Max(1f, noiseScale),
                                         worldPos.z / Mathf.Max(1f, noiseScale));

        // Blend: lerp between linear and noise in [0,1]
        float blended = Mathf.Lerp(Mathf.Repeat(linear, 1f), noise, noiseBlend);
        return Mathf.Clamp01(blended);
    }

    /// <summary>
    /// Samples the user-defined colour list as a cyclic gradient at t ∈ [0,1].
    /// t=0 and t=1 both land on parsedColors[0], creating a seamless loop.
    /// </summary>
    private static Color SampleCyclicGradient(List<Color> colors, float t)
    {
        int   n       = colors.Count;
        float scaled  = t * n;                           // [0, n)
        int   idxA    = Mathf.FloorToInt(scaled) % n;
        int   idxB    = (idxA + 1) % n;
        float blend   = scaled - Mathf.Floor(scaled);    // fractional part
        return Color.Lerp(colors[idxA], colors[idxB], blend);
    }

    // ═════════════════════════════════════════════════════════════════════════
    // Road Sampling
    // ═════════════════════════════════════════════════════════════════════════

    private class RoadSampler
    {
        public Vector3[] overlayVertices;
        public Color[]   overlayColors;
        public bool      hasData;
    }

    private static RoadSampler BuildRoadSampler(DesertTerrainChunk chunk)
    {
        Transform overlayT = chunk.transform.Find("RoadOverlay");
        if (overlayT == null) return null;
        DesertTerrainRoadOverlay overlay = overlayT.GetComponent<DesertTerrainRoadOverlay>();
        if (overlay == null) return null;
        Mesh mesh = overlay.GetMesh();
        if (mesh == null) return null;
        Color[]   colors = mesh.colors;
        Vector3[] verts  = mesh.vertices;
        if (colors == null || colors.Length == 0 || verts == null || verts.Length != colors.Length) return null;
        return new RoadSampler { overlayVertices = verts, overlayColors = colors, hasData = true };
    }

    private float SampleRoadFactor(RoadSampler sampler, Vector3 localPos, DesertTerrainChunk chunk)
    {
        float gridX = localPos.x / chunk.cellSize;
        float gridZ = localPos.z / chunk.cellSize;
        int x0 = Mathf.Clamp(Mathf.FloorToInt(gridX), 0, chunk.width);
        int x1 = Mathf.Clamp(x0 + 1, 0, chunk.width);
        int z0 = Mathf.Clamp(Mathf.FloorToInt(gridZ), 0, chunk.depth);
        int z1 = Mathf.Clamp(z0 + 1, 0, chunk.depth);
        float tx = gridX - x0, tz = gridZ - z0;
        int W = chunk.width + 1;
        int i00 = z0*W+x0, i10 = z0*W+x1, i01 = z1*W+x0, i11 = z1*W+x1;
        if (i11 >= sampler.overlayColors.Length) return 1f;

        float roadAlpha = Mathf.Lerp(
            Mathf.Lerp(sampler.overlayColors[i00].a, sampler.overlayColors[i10].a, tx),
            Mathf.Lerp(sampler.overlayColors[i01].a, sampler.overlayColors[i11].a, tx), tz);

        if (roadAlpha <= 0.01f) return 1f;

        float total       = roadClearDistance + roadTransitionWidth;
        float clearAlpha  = total > 0f ? Mathf.Clamp01(roadTransitionWidth / total) : 0.5f;
        float transAlpha  = total > 0f ? Mathf.Clamp01(roadClearDistance  / total) : 0.1f;

        float factor;
        if (roadAlpha >= clearAlpha)
        {
            float t = (roadAlpha - clearAlpha) / Mathf.Max(0.001f, 1f - clearAlpha);
            factor = Mathf.Lerp(0f, onRoadSpawnProbability, t);
        }
        else if (roadAlpha >= transAlpha)
        {
            float t = (roadAlpha - transAlpha) / Mathf.Max(0.001f, clearAlpha - transAlpha);
            factor = Mathf.Lerp(1f, 0f, t);
        }
        else
        {
            factor = 1f;
        }

        return Mathf.Clamp01(factor);
    }

    // ═════════════════════════════════════════════════════════════════════════
    // Utilities
    // ═════════════════════════════════════════════════════════════════════════

    private List<Color> GetParsedSpectrumColors()
    {
        var result = new List<Color>();
        foreach (var hex in spectrumHexColors)
            if (TryParseHex(hex, out Color c))
                result.Add(c);
        return result;
    }

    private static bool TryParseHex(string hex, out Color color)
    {
        color = Color.white;
        if (string.IsNullOrWhiteSpace(hex)) return false;
        hex = hex.Trim().TrimStart('#');
        if (hex.Length == 6)  hex = hex + "FF";
        if (hex.Length != 8)  return false;
        try
        {
            byte r = System.Convert.ToByte(hex.Substring(0, 2), 16);
            byte g = System.Convert.ToByte(hex.Substring(2, 2), 16);
            byte b = System.Convert.ToByte(hex.Substring(4, 2), 16);
            byte a = System.Convert.ToByte(hex.Substring(6, 2), 16);
            color = new Color32(r, g, b, a);
            return true;
        }
        catch { return false; }
    }

    // ═════════════════════════════════════════════════════════════════════════
    // Undo / Clear
    // ═════════════════════════════════════════════════════════════════════════

    private void UndoLastPlacement()
    {
        if (spawnedObjectsHistory.Count > 0)
        {
            List<GameObject> lastSpawns = spawnedObjectsHistory.Pop();
            int count = 0;
            foreach (var obj in lastSpawns)
                if (obj != null) { Undo.DestroyObjectImmediate(obj); count++; }

            foreach (var chunk in FindObjectsOfType<DesertTerrainChunk>())
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
        int count = 0;
        foreach (var chunk in FindObjectsOfType<DesertTerrainChunk>())
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
