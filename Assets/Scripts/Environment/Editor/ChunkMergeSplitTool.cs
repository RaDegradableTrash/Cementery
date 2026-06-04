using UnityEngine;
using UnityEditor;
using System.Collections.Generic;

namespace EnvironmentSystem
{
    public class ChunkMergeSplitTool : EditorWindow
    {
        public enum BrushMode { RaiseLower, Smooth, Flatten }
        public enum FalloffStyle { Smooth, Linear, Sharp, Flat }

        [Header("Brush Settings")]
        private bool _paintModeActive = false;
        private BrushMode _brushMode = BrushMode.RaiseLower;
        private FalloffStyle _falloffStyle = FalloffStyle.Smooth;
        private float _brushRadius = 15f;
        private float _brushStrength = 1f;
        private float _flattenTargetHeight = 0f;
        private bool _hasFlattenTarget = false;

        private HashSet<DesertTerrainChunk> _modifiedChunks = new HashSet<DesertTerrainChunk>();

        [MenuItem("Tools/Environment/Chunk Terrain Sculpting Brush")]
        public static void ShowWindow()
        {
            GetWindow<ChunkMergeSplitTool>("Terrain Sculpt Brush");
        }

        private void OnEnable()
        {
            SceneView.duringSceneGui += OnSceneGUI;
        }

        private void OnDisable()
        {
            SceneView.duringSceneGui -= OnSceneGUI;
        }

        private void OnGUI()
        {
            // Listen for Cmd+Z / Ctrl+Z undo hotkeys inside the Tool Window
            Event current = Event.current;
            if (current.type == EventType.KeyDown && current.keyCode == KeyCode.Z && (current.command || current.control))
            {
                PerformCustomUndo();
                current.Use();
                return;
            }

            GUILayout.Label("Terrain Sculpting Brush Tool", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox("This tool lets you sculpt multiple DesertTerrainChunks directly in the Scene view with 0 lag. Handles seamless borders and matches terrain refinement settings.", MessageType.Info);

            GUILayout.Space(8);

            // Toggle brush mode
            GUI.backgroundColor = _paintModeActive ? new Color(0.2f, 0.8f, 0.4f) : Color.white;
            if (GUILayout.Button(_paintModeActive ? "🎨 Brush Active (Click/Drag in Scene to Sculpt)" : "🖌️ Activate Sculpting Brush", GUILayout.Height(35)))
            {
                _paintModeActive = !_paintModeActive;
                if (_paintModeActive)
                {
                    // Focus scene view
                    if (SceneView.lastActiveSceneView != null)
                        SceneView.lastActiveSceneView.Focus();
                }
            }
            GUI.backgroundColor = Color.white;

            if (_paintModeActive)
            {
                EditorGUILayout.HelpBox("Hold [L-Click & Drag] to sculpt. Press [Shift] to invert (Lower instead of Raise). Make sure you have colliders enabled on chunks.", MessageType.Warning);
            }

            GUILayout.Space(12);
            GUILayout.Label("Brush Controls", EditorStyles.boldLabel);

            _brushMode = (BrushMode)EditorGUILayout.EnumPopup("Sculpt Mode", _brushMode);
            _falloffStyle = (FalloffStyle)EditorGUILayout.EnumPopup("Falloff (Cliff Style)", _falloffStyle);

            _brushRadius = EditorGUILayout.Slider("Brush Radius (Size)", _brushRadius, 1f, 100f);
            _brushStrength = EditorGUILayout.Slider("Brush Strength", _brushStrength, 0.05f, 5f);

            if (_brushMode == BrushMode.Flatten)
            {
                EditorGUILayout.BeginHorizontal();
                _flattenTargetHeight = EditorGUILayout.FloatField("Flatten Target Height (Y)", _flattenTargetHeight);
                if (GUILayout.Button("Get Height Under Mouse", GUILayout.Width(150)))
                {
                    _hasFlattenTarget = false; // Reset to sample next click
                }
                EditorGUILayout.EndHorizontal();
            }

            GUILayout.Space(15);
            GUILayout.Label("Verification & Maintenance", EditorStyles.boldLabel);
            
            GUI.backgroundColor = new Color(0.2f, 0.7f, 1f);
            if (GUILayout.Button("🔗 一键融合与抚平地表裂隙 (Auto-Fuse Seams & Cracks)", GUILayout.Height(35)))
            {
                FuseAllSeams();
            }
            GUI.backgroundColor = Color.white;

            GUILayout.Space(5);

            if (GUILayout.Button("Force Recalculate All Scene Chunk Normals (Seamless)"))
            {
                ForceRecalculateAllNormals();
            }

            GUILayout.Space(12);
            GUILayout.Label("Scene Clean Up & Saving", EditorStyles.boldLabel);

            GUI.backgroundColor = new Color(0.2f, 0.8f, 0.5f);
            if (GUILayout.Button("💾 一键保存地形修改至磁盘 (Save Sculpt Changes)", GUILayout.Height(35)))
            {
                SaveSculptChangesToDisk();
            }
            GUI.backgroundColor = Color.white;

            GUILayout.Space(5);

            GUI.backgroundColor = new Color(0.9f, 0.6f, 0.2f);
            if (GUILayout.Button("🛠️ 强制清理场景并恢复所有地形区块 (Clean & Reactivate Chunks)"))
            {
                ReactivateAllChunks();
            }
            GUI.backgroundColor = Color.white;
        }

        private void SaveSculptChangesToDisk()
        {
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            EditorUtility.DisplayDialog("Saved Successfully", "All sculpt modifications have been successfully written to your local .asset mesh files!", "Excellent!");
        }

        private void ReactivateAllChunks()
        {
            var allChunks = Resources.FindObjectsOfTypeAll<DesertTerrainChunk>();
            int reactivatedCount = 0;
            foreach (var chunk in allChunks)
            {
                if (chunk != null && !EditorUtility.IsPersistent(chunk.gameObject))
                {
                    // Traverse up and make all parents active as well
                    Transform p = chunk.transform;
                    bool activatedThis = false;
                    while (p != null)
                    {
                        if (!p.gameObject.activeSelf)
                        {
                            Undo.RecordObject(p.gameObject, "Reactivate Chunk Hierarchy");
                            p.gameObject.SetActive(true);
                            activatedThis = true;
                        }
                        p = p.parent;
                    }
                    if (activatedThis)
                    {
                        reactivatedCount++;
                    }
                }
            }

            // Clean up any remaining temporary [Combined_Terrain_Sculpt] gameobjects
            var allObjects = Resources.FindObjectsOfTypeAll<GameObject>();
            int destroyedCount = 0;
            foreach (var go in allObjects)
            {
                if (go != null && go.name == "[Combined_Terrain_Sculpt]" && !EditorUtility.IsPersistent(go))
                {
                    Undo.DestroyObjectImmediate(go);
                    destroyedCount++;
                }
            }

            Debug.Log($"[Terrain Brush] Reactivated {reactivatedCount} chunks hierarchy and cleaned up {destroyedCount} combined sculpt meshes.");
            EditorUtility.DisplayDialog("Scene Cleaned", $"Successfully reactivated {reactivatedCount} hidden chunk hierarchies and removed {destroyedCount} temporary merged objects.", "Great!");
        }

        private List<DesertTerrainChunk> _cachedSceneChunks = new List<DesertTerrainChunk>();

        private void CacheSceneChunks()
        {
            _cachedSceneChunks.Clear();
            var all = Resources.FindObjectsOfTypeAll<DesertTerrainChunk>();
            foreach (var chunk in all)
            {
                if (chunk != null && !EditorUtility.IsPersistent(chunk.gameObject))
                {
                    _cachedSceneChunks.Add(chunk);
                }
            }
        }

        private struct ChunkUndoState
        {
            public DesertTerrainChunk chunk;
            public Vector3[] originalVertices;
            public Vector3[] originalNormals;
        }

        private struct UndoStroke
        {
            public List<ChunkUndoState> states;
        }

        private static Stack<UndoStroke> _customUndoStack = new Stack<UndoStroke>();
        private List<ChunkUndoState> _currentStrokeStates = new List<ChunkUndoState>();
        private HashSet<DesertTerrainChunk> _undoneChunks = new HashSet<DesertTerrainChunk>();

        private void OnSceneGUI(SceneView sceneView)
        {
            // Listen for Cmd+Z / Ctrl+Z undo hotkeys inside the Scene View
            Event current = Event.current;
            if (current.type == EventType.KeyDown && current.keyCode == KeyCode.Z && (current.command || current.control))
            {
                PerformCustomUndo();
                current.Use();
                return;
            }

            if (!_paintModeActive) return;

            // Block standard selection selection box in Scene view
            int controlID = GUIUtility.GetControlID(FocusType.Passive);
            HandleUtility.AddDefaultControl(controlID);

            Ray ray = HandleUtility.GUIPointToWorldRay(current.mousePosition);

            if (Physics.Raycast(ray, out RaycastHit hit, 20000f))
            {
                // Only interact if hitting a DesertTerrainChunk
                var hitChunk = hit.collider.GetComponentInParent<DesertTerrainChunk>();
                if (hitChunk != null)
                {
                    // Draw brush preview in Scene View
                    Handles.color = new Color(0f, 0.8f, 1f, 0.5f);
                    Handles.DrawWireDisc(hit.point, Vector3.up, _brushRadius);
                    Handles.color = new Color(0f, 0.8f, 1f, 0.04f);
                    Handles.DrawSolidDisc(hit.point, Vector3.up, _brushRadius);

                    // If Flatten mode has no target yet, sample it on the first hover/click
                    if (_brushMode == BrushMode.Flatten && !_hasFlattenTarget && current.type == EventType.MouseDown && current.button == 0)
                    {
                        _flattenTargetHeight = hit.point.y;
                        _hasFlattenTarget = true;
                    }

                    // Mouse Interaction
                    if (current.type == EventType.MouseDown && current.button == 0 && !current.alt)
                    {
                        CacheSceneChunks(); // 🚀 仅在鼠标点下的瞬间缓存一次场景中所有区块，彻底解决卡死
                        _undoneChunks.Clear(); // 🚀 清空本次涂抹的撤销缓存记录
                        _currentStrokeStates.Clear();

                        ApplySculpt(hit.point, current.shift ? -1f : 1f);
                        current.Use(); // Prevent Unity default click behavior
                    }
                    else if (current.type == EventType.MouseDrag && current.button == 0 && !current.alt)
                    {
                        ApplySculpt(hit.point, current.shift ? -1f : 1f);
                        current.Use(); // Prevent Unity default click behavior
                    }

                    // Trigger normal recalculations and asset dirtying on mouse release
                    if (current.type == EventType.MouseUp && current.button == 0)
                    {
                        int modifiedCount = _modifiedChunks.Count;

                        // 🚀 核心性能优化：在松开鼠标的瞬间，统一重新烘焙受影响区块的物理碰撞体
                        foreach (var chunk in _modifiedChunks)
                        {
                            if (chunk != null)
                            {
                                MeshFilter mf = chunk.GetComponent<MeshFilter>();
                                if (mf != null && mf.sharedMesh != null)
                                {
                                    if (chunk.TryGetComponent<MeshCollider>(out var col))
                                    {
                                        col.sharedMesh = null;
                                        col.sharedMesh = mf.sharedMesh;
                                    }
                                }
                            }
                        }

                        RecalculateNormalsForModifiedChunks();

                        // 🚀 Push current stroke undo states to our custom stack
                        if (_currentStrokeStates.Count > 0)
                        {
                            _customUndoStack.Push(new UndoStroke { states = new List<ChunkUndoState>(_currentStrokeStates) });
                        }

                        _hasFlattenTarget = false; // Reset flatten target on release
                        _cachedSceneChunks.Clear(); // Clear cache to free reference
                        _undoneChunks.Clear(); // Clear undo cache
                        _currentStrokeStates.Clear();
                        
                        Debug.Log($"<color=#00ff80><b>[Terrain Brush]</b></color> Sculpt stroke finished. Updated <b>{modifiedCount}</b> chunks in memory! Fast response.");
                        SceneView.RepaintAll();
                    }
                }
            }

            // Keep scene view updated
            if (current.type == EventType.MouseMove || current.type == EventType.MouseDrag)
            {
                sceneView.Repaint();
            }
        }

        private void PerformCustomUndo()
        {
            if (_customUndoStack.Count == 0)
            {
                Debug.LogWarning("[Terrain Brush] No more undo actions available in the stack.");
                return;
            }

            UndoStroke stroke = _customUndoStack.Pop();
            foreach (var state in stroke.states)
            {
                if (state.chunk == null || state.originalVertices == null) continue;
                MeshFilter mf = state.chunk.GetComponent<MeshFilter>();
                if (mf == null || mf.sharedMesh == null) continue;

                Mesh mesh = mf.sharedMesh;
                
                // Restore height & normals in memory
                mesh.vertices = state.originalVertices;
                mesh.normals = state.originalNormals;
                mesh.RecalculateBounds();
                mesh.UploadMeshData(false);

                // Force GPU update
                mf.sharedMesh = null;
                mf.sharedMesh = mesh;

                // Re-cook physics collider
                if (state.chunk.TryGetComponent<MeshCollider>(out var col))
                {
                    col.sharedMesh = null;
                    col.sharedMesh = mesh;
                }

                EditorUtility.SetDirty(mesh);
                EditorUtility.SetDirty(state.chunk);
            }

            SceneView.RepaintAll();
            Debug.Log("<color=#ff8000><b>[Terrain Brush]</b></color> Undo successful! Reverted vertices and normals in memory.");
        }

        private void ApplySculpt(Vector3 centerPoint, float direction)
        {
            // Use the cached chunks populated on MouseDown
            if (_cachedSceneChunks.Count == 0)
            {
                CacheSceneChunks();
            }
            var allChunks = _cachedSceneChunks;
            
            // Collect heights within brush radius for Average calculation (needed for Smooth mode)
            float averageHeightSum = 0f;
            int averageCount = 0;

            if (_brushMode == BrushMode.Smooth)
            {
                foreach (var chunk in allChunks)
                {
                    MeshFilter mf = chunk.GetComponent<MeshFilter>();
                    if (mf == null || mf.sharedMesh == null) continue;

                    Vector3 chunkPos = chunk.transform.position;
                    float chunkHalfW = chunk.width * chunk.cellSize * 0.5f;
                    float chunkHalfD = chunk.depth * chunk.cellSize * 0.5f;
                    Vector3 chunkCenter = chunkPos + new Vector3(chunkHalfW, 0, chunkHalfD);

                    // Fast bound sphere check
                    float maxChunkRadius = Mathf.Sqrt(chunkHalfW * chunkHalfW + chunkHalfD * chunkHalfD);
                    if (Vector3.Distance(new Vector3(chunkCenter.x, 0, chunkCenter.z), new Vector3(centerPoint.x, 0, centerPoint.z)) > maxChunkRadius + _brushRadius)
                    {
                        continue;
                    }

                    Vector3[] verts = mf.sharedMesh.vertices;
                    for (int i = 0; i < verts.Length; i++)
                    {
                        Vector3 worldPos = chunkPos + verts[i];
                        float dist = Vector2.Distance(new Vector2(worldPos.x, worldPos.z), new Vector2(centerPoint.x, centerPoint.z));
                        if (dist <= _brushRadius)
                        {
                            averageHeightSum += worldPos.y;
                            averageCount++;
                        }
                    }
                }
            }

            float averageHeight = averageCount > 0 ? averageHeightSum / averageCount : centerPoint.y;

            // Apply modification to meshes
            foreach (var chunk in allChunks)
            {
                MeshFilter mf = chunk.GetComponent<MeshFilter>();
                if (mf == null || mf.sharedMesh == null) continue;

                Vector3 chunkPos = chunk.transform.position;
                float chunkHalfW = chunk.width * chunk.cellSize * 0.5f;
                float chunkHalfD = chunk.depth * chunk.cellSize * 0.5f;
                Vector3 chunkCenter = chunkPos + new Vector3(chunkHalfW, 0, chunkHalfD);

                // Check overlap
                float maxChunkRadius = Mathf.Sqrt(chunkHalfW * chunkHalfW + chunkHalfD * chunkHalfD);
                if (Vector3.Distance(new Vector3(chunkCenter.x, 0, chunkCenter.z), new Vector3(centerPoint.x, 0, centerPoint.z)) > maxChunkRadius + _brushRadius)
                {
                    continue;
                }

                Mesh mesh = mf.sharedMesh;
                Vector3[] vertices = mesh.vertices;
                bool modified = false;

                for (int i = 0; i < vertices.Length; i++)
                {
                    Vector3 worldPos = chunkPos + vertices[i];
                    float dist = Vector2.Distance(new Vector2(worldPos.x, worldPos.z), new Vector2(centerPoint.x, centerPoint.z));
                    if (dist <= _brushRadius)
                    {
                        float falloff = GetFalloff(dist / _brushRadius, _falloffStyle);
                        float stepAmount = _brushStrength * falloff * 0.4f;

                        if (_brushMode == BrushMode.RaiseLower)
                        {
                            vertices[i].y += stepAmount * direction;
                        }
                        else if (_brushMode == BrushMode.Smooth)
                        {
                            vertices[i].y = Mathf.Lerp(vertices[i].y + chunkPos.y, averageHeight, stepAmount) - chunkPos.y;
                        }
                        else if (_brushMode == BrushMode.Flatten)
                        {
                            vertices[i].y = Mathf.Lerp(vertices[i].y + chunkPos.y, _flattenTargetHeight, stepAmount) - chunkPos.y;
                        }
                        modified = true;
                    }
                }

                if (modified)
                {
                    // 🚀 Custom In-Memory Undo: Record once per chunk when first modified in this stroke
                    if (!_undoneChunks.Contains(chunk))
                    {
                        _currentStrokeStates.Add(new ChunkUndoState
                        {
                            chunk = chunk,
                            originalVertices = (Vector3[])mesh.vertices.Clone(),
                            originalNormals = (Vector3[])mesh.normals.Clone()
                        });
                        _undoneChunks.Add(chunk);
                    }

                    // Auto-wake up chunk if it was hidden/inactive
                    if (!chunk.gameObject.activeSelf)
                    {
                        chunk.gameObject.SetActive(true);
                    }

                    mesh.vertices = vertices;
                    mesh.RecalculateBounds();
                    mesh.UploadMeshData(false);

                    // Force Unity renderer to update GPU vertex buffers and redraw immediately
                    mf.sharedMesh = null;
                    mf.sharedMesh = mesh;

                    // 🚀 核心优化：拖拽时完全关闭物理碰撞体的重构，仅在松开鼠标时一次性烘焙
                    _modifiedChunks.Add(chunk);
                    EditorUtility.SetDirty(mesh);
                    EditorUtility.SetDirty(chunk);
                }
            }
        }

        private float GetFalloff(float t, FalloffStyle style)
        {
            t = Mathf.Clamp01(t);
            switch (style)
            {
                case FalloffStyle.Linear:
                    return 1f - t;
                case FalloffStyle.Sharp:
                    return Mathf.SmoothStep(1f, 0f, t * t);
                case FalloffStyle.Flat:
                    return 1f; // Full strength everywhere within radius (perfect for vertical cliffs)
                case FalloffStyle.Smooth:
                default:
                    // Cosine smooth curve
                    return 0.5f * (1f + Mathf.Cos(t * Mathf.PI));
            }
        }

        private void RecalculateNormalsForModifiedChunks()
        {
            if (_modifiedChunks.Count == 0) return;

            // Collect heights from all chunks in the scene to build a global spatial lookup table (seams mapping)
            if (_cachedSceneChunks.Count == 0)
            {
                CacheSceneChunks();
            }
            var allChunks = _cachedSceneChunks;
            float epsilon = 0.01f;
            Dictionary<Vector2Int, float> worldHeightMap = new Dictionary<Vector2Int, float>();

            foreach (var chunk in allChunks)
            {
                MeshFilter mf = chunk.GetComponent<MeshFilter>();
                if (mf == null || mf.sharedMesh == null) continue;

                Vector3[] verts = mf.sharedMesh.vertices;
                Vector3 chunkPos = chunk.transform.position;

                foreach (var v in verts)
                {
                    Vector3 wPos = chunkPos + v;
                    Vector2Int key = new Vector2Int(
                        Mathf.RoundToInt(wPos.x / epsilon),
                        Mathf.RoundToInt(wPos.z / epsilon)
                    );
                    worldHeightMap[key] = wPos.y;
                }
            }

            // Recalculate seamless normals for modified chunks
            foreach (var chunk in _modifiedChunks)
            {
                if (chunk == null) continue;
                MeshFilter mf = chunk.GetComponent<MeshFilter>();
                if (mf == null || mf.sharedMesh == null) continue;

                Mesh mesh = mf.sharedMesh;
                Vector3[] vertices = mesh.vertices;
                Vector3[] normals = new Vector3[vertices.Length];
                Vector3 chunkPos = chunk.transform.position;

                int vw = chunk.width + 1;
                int vd = chunk.depth + 1;
                float cellSize = chunk.cellSize;

                for (int z = 0; z < vd; z++)
                {
                    for (int x = 0; x < vw; x++)
                    {
                        int index = z * vw + x;
                        float localX = x * cellSize;
                        float localZ = z * cellSize;

                        float worldX = chunkPos.x + localX;
                        float worldZ = chunkPos.z + localZ;

                        float currentY = vertices[index].y + chunkPos.y;

                        float hL = GetHeightFromMap(worldX - cellSize, worldZ, worldHeightMap, epsilon, currentY);
                        float hR = GetHeightFromMap(worldX + cellSize, worldZ, worldHeightMap, epsilon, currentY);
                        float hD = GetHeightFromMap(worldX, worldZ - cellSize, worldHeightMap, epsilon, currentY);
                        float hU = GetHeightFromMap(worldX, worldZ + cellSize, worldHeightMap, epsilon, currentY);

                        Vector3 tangentX = new Vector3(cellSize * 2f, hR - hL, 0);
                        Vector3 tangentZ = new Vector3(0, hU - hD, cellSize * 2f);
                        normals[index] = Vector3.Cross(tangentZ, tangentX).normalized;
                    }
                }

                mesh.normals = normals;
                EditorUtility.SetDirty(mesh);
                EditorUtility.SetDirty(chunk);

                string assetPath = AssetDatabase.GetAssetPath(mesh);
                if (!string.IsNullOrEmpty(assetPath))
                {
                    AssetDatabase.SaveAssetIfDirty(mesh);
                }
            }

            AssetDatabase.SaveAssets();
            Debug.Log($"[ChunkMergeSplitTool] Seamlessly recalculated normals for {_modifiedChunks.Count} sculpted chunks.");
            _modifiedChunks.Clear();
        }

        private static float GetHeightFromMap(float wX, float wZ, Dictionary<Vector2Int, float> map, float epsilon, float defaultY)
        {
            Vector2Int key = new Vector2Int(
                Mathf.RoundToInt(wX / epsilon),
                Mathf.RoundToInt(wZ / epsilon)
            );
            if (map.TryGetValue(key, out float y))
                return y;
            return defaultY;
        }

        private struct VertexRef
        {
            public DesertTerrainChunk chunk;
            public Mesh mesh;
            public Vector3[] vertices;
            public int index;
        }

        private void FuseAllSeams()
        {
            CacheSceneChunks();
            var allChunks = _cachedSceneChunks;
            float epsilon = 0.01f;

            // Key: rounded grid coordinate in world space (X, Z)
            // Value: List of vertex references that lie at this X/Z coordinate
            var vertexGroups = new Dictionary<Vector2Int, List<VertexRef>>();

            // Read vertices from all chunks
            var chunkData = new Dictionary<DesertTerrainChunk, (Mesh mesh, Vector3[] verts)>();
            foreach (var chunk in allChunks)
            {
                MeshFilter mf = chunk.GetComponent<MeshFilter>();
                if (mf == null || mf.sharedMesh == null) continue;

                Mesh mesh = mf.sharedMesh;
                Vector3[] verts = mesh.vertices;
                chunkData[chunk] = (mesh, verts);

                Vector3 chunkPos = chunk.transform.position;

                for (int i = 0; i < verts.Length; i++)
                {
                    Vector3 wPos = chunkPos + verts[i];
                    Vector2Int key = new Vector2Int(
                        Mathf.RoundToInt(wPos.x / epsilon),
                        Mathf.RoundToInt(wPos.z / epsilon)
                    );

                    if (!vertexGroups.TryGetValue(key, out var list))
                    {
                        list = new List<VertexRef>();
                        vertexGroups[key] = list;
                    }

                    list.Add(new VertexRef
                    {
                        chunk = chunk,
                        mesh = mesh,
                        vertices = verts,
                        index = i
                    });
                }
            }

            // Align heights of duplicate vertices (seams) using Y-clustering (separates surface from skirt)
            int fusedCount = 0;
            HashSet<DesertTerrainChunk> modified = new HashSet<DesertTerrainChunk>();

            foreach (var kvp in vertexGroups)
            {
                var list = kvp.Value;
                if (list.Count > 1)
                {
                    // Sort by world Y height to cluster surface and skirt vertices separately
                    list.Sort((a, b) => {
                        float ay = a.chunk.transform.position.y + a.vertices[a.index].y;
                        float by = b.chunk.transform.position.y + b.vertices[b.index].y;
                        return ay.CompareTo(by);
                    });

                    // Cluster vertices where Y distance is less than 3.0 meters
                    int startIdx = 0;
                    for (int i = 1; i <= list.Count; i++)
                    {
                        bool endOfCluster = (i == list.Count);
                        if (!endOfCluster)
                        {
                            float yCurrent = list[i].chunk.transform.position.y + list[i].vertices[list[i].index].y;
                            float yPrev = list[i - 1].chunk.transform.position.y + list[i - 1].vertices[list[i - 1].index].y;
                            if (Mathf.Abs(yCurrent - yPrev) > 3.0f) // 3 meters distance threshold
                            {
                                endOfCluster = true;
                            }
                        }

                        if (endOfCluster)
                        {
                            int clusterSize = i - startIdx;
                            if (clusterSize > 1)
                            {
                                // Calculate average world Y of this specific cluster
                                float sumY = 0f;
                                for (int c = startIdx; c < i; c++)
                                {
                                    sumY += list[c].chunk.transform.position.y + list[c].vertices[list[c].index].y;
                                }
                                float avgY = sumY / clusterSize;

                                // Set height for all vertices in this cluster
                                for (int c = startIdx; c < i; c++)
                                {
                                    var vr = list[c];
                                    float localY = avgY - vr.chunk.transform.position.y;
                                    if (!Mathf.Approximately(vr.vertices[vr.index].y, localY))
                                    {
                                        vr.vertices[vr.index].y = localY;
                                        modified.Add(vr.chunk);
                                    }
                                }
                                fusedCount++;
                            }
                            startIdx = i;
                        }
                    }
                }
            }

            // Write modified vertices back and update bounds/colliders
            foreach (var chunk in modified)
            {
                var data = chunkData[chunk];
                Undo.RecordObject(data.mesh, "Fuse Seams");
                Undo.RecordObject(chunk, "Fuse Seams");

                data.mesh.vertices = data.verts;
                data.mesh.RecalculateBounds();
                data.mesh.UploadMeshData(false);

                // Force update GPU vertex buffer
                var mf = chunk.GetComponent<MeshFilter>();
                if (mf != null)
                {
                    mf.sharedMesh = null;
                    mf.sharedMesh = data.mesh;
                }

                if (chunk.TryGetComponent<MeshCollider>(out var col))
                {
                    col.sharedMesh = null;
                    col.sharedMesh = data.mesh;
                }

                EditorUtility.SetDirty(data.mesh);
                EditorUtility.SetDirty(chunk);
            }

            // Recalculate seamless normals for all affected chunks
            _modifiedChunks.Clear();
            foreach (var chunk in modified)
            {
                _modifiedChunks.Add(chunk);
            }
            RecalculateNormalsForModifiedChunks();

            EditorUtility.DisplayDialog("Fusion Complete", $"Successfully aligned and fused {fusedCount} seam coordinates across {modified.Count} chunks. No more cracks!", "Fantastic!");
        }

        private void ForceRecalculateAllNormals()
        {
            _modifiedChunks.Clear();
            var allChunks = FindObjectsOfType<DesertTerrainChunk>();
            foreach (var chunk in allChunks)
            {
                _modifiedChunks.Add(chunk);
            }
            RecalculateNormalsForModifiedChunks();
            EditorUtility.DisplayDialog("Success", "Recalculated seamless normals for all terrain chunks successfully!", "OK");
        }
    }
}
