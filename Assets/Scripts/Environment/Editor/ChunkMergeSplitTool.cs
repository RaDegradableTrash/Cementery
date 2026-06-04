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

            _brushRadius = EditorGUILayout.Slider("Brush Radius (Size)", _brushRadius, 1f, 500f);
            _brushStrength = EditorGUILayout.Slider("Brush Strength", _brushStrength, 0.05f, 50f);

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

            GUI.backgroundColor = new Color(0.1f, 0.6f, 0.9f);
            if (GUILayout.Button("🌍 一键缝合全图所有区块接缝 (Batch Fuse ALL Chunks in Project)", GUILayout.Height(35)))
            {
                FuseAllMapChunksBatch();
            }
            GUI.backgroundColor = Color.white;

            GUILayout.Space(5);

            GUI.backgroundColor = new Color(0.4f, 0.7f, 0.9f);
            if (GUILayout.Button("✨ 一键微调所有地形细节 (Refine All Chunks)", GUILayout.Height(35)))
            {
                RefineAllChunks();
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
            CacheSceneChunks();
            var allChunks = _cachedSceneChunks;
            int savedMeshesCount = 0;

            foreach (var chunk in allChunks)
            {
                if (chunk != null && chunk.useSavedMeshAsset)
                {
                    SaveMeshAsset(chunk);
                    savedMeshesCount++;

                    EditorUtility.SetDirty(chunk);
                    if (chunk.gameObject.scene != null && chunk.gameObject.scene.name != null)
                    {
                        UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(chunk.gameObject.scene);
                    }
                }
            }

            // Save open scenes
            UnityEditor.SceneManagement.EditorSceneManager.SaveOpenScenes();

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            EditorUtility.DisplayDialog("Saved Successfully", $"Successfully saved {savedMeshesCount} sculpt modifications, serialized mesh assets, and updated scene chunk settings to disk!", "Excellent!");
        }

        private void SaveMeshAsset(DesertTerrainChunk targetChunk)
        {
            if (targetChunk.TryGetComponent<MeshFilter>(out var filter) && filter.sharedMesh != null)
            {
                string assetPath = AssetDatabase.GetAssetPath(filter.sharedMesh);
                bool isWriteableCustomAsset = !string.IsNullOrEmpty(assetPath) && assetPath.EndsWith(".asset") && assetPath.Contains("/Meshes/");

                if (isWriteableCustomAsset)
                {
                    EditorUtility.SetDirty(filter.sharedMesh);
                }
                else
                {
                    string folderPath = "Assets/Scenes/Chunks/Meshes";
                    if (!System.IO.Directory.Exists(folderPath))
                    {
                        System.IO.Directory.CreateDirectory(folderPath);
                        AssetDatabase.Refresh();
                    }

                    string meshPath = $"{folderPath}/Mesh_{targetChunk.name}.asset";
                    
                    Mesh standaloneMesh = Instantiate(filter.sharedMesh);
                    standaloneMesh.name = $"Mesh_{targetChunk.name}";

                    AssetDatabase.CreateAsset(standaloneMesh, meshPath);
                    filter.sharedMesh = standaloneMesh;
                    
                    if (targetChunk.TryGetComponent<MeshCollider>(out var col))
                    {
                        col.sharedMesh = standaloneMesh;
                    }

                    Debug.Log($"[ChunkMergeSplitTool] Saved new stitched mesh to disk asset: {meshPath}");
                }
            }
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

                    chunk.useSavedMeshAsset = true; // 🚀 Prevent noise regeneration!

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

            // Pre-cache all chunk grids in the registry
            ChunkRegistry.Clear();
            foreach (var chunk in allChunks)
            {
                ChunkRegistry.Register(chunk);
            }

            var chunkData = new Dictionary<DesertTerrainChunk, Vector3[]>();
            foreach (var chunk in allChunks)
            {
                MeshFilter mf = chunk.GetComponent<MeshFilter>();
                if (mf == null || mf.sharedMesh == null) continue;
                chunkData[chunk] = mf.sharedMesh.vertices;
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
                Vector2Int gc = chunk.GridCoord;

                int vw = chunk.width + 1;
                int vd = chunk.depth + 1;
                int mainVerts = vw * vd;
                float cellSize = chunk.cellSize;

                var leftChunk = ChunkRegistry.Get(gc.x - 1, gc.y);
                var rightChunk = ChunkRegistry.Get(gc.x + 1, gc.y);
                var bottomChunk = ChunkRegistry.Get(gc.x, gc.y - 1);
                var topChunk = ChunkRegistry.Get(gc.x, gc.y + 1);

                var leftVerts = leftChunk != null && chunkData.TryGetValue(leftChunk, out var lv) ? lv : null;
                var rightVerts = rightChunk != null && chunkData.TryGetValue(rightChunk, out var rv) ? rv : null;
                var bottomVerts = bottomChunk != null && chunkData.TryGetValue(bottomChunk, out var bv) ? bv : null;
                var topVerts = topChunk != null && chunkData.TryGetValue(topChunk, out var tv) ? tv : null;

                for (int z = 0; z < vd; z++)
                {
                    for (int x = 0; x < vw; x++)
                    {
                        int index = z * vw + x;
                        float currentY = vertices[index].y + chunkPos.y;

                        // Query Left Height
                        float hL = currentY;
                        if (x > 0)
                        {
                            hL = vertices[z * vw + (x - 1)].y + chunkPos.y;
                        }
                        else if (leftVerts != null)
                        {
                            hL = leftVerts[z * vw + (chunk.width - 1)].y + leftChunk.transform.position.y;
                        }

                        // Query Right Height
                        float hR = currentY;
                        if (x < chunk.width)
                        {
                            hR = vertices[z * vw + (x + 1)].y + chunkPos.y;
                        }
                        else if (rightVerts != null)
                        {
                            hR = rightVerts[z * vw + 1].y + rightChunk.transform.position.y;
                        }

                        // Query Bottom Height
                        float hD = currentY;
                        if (z > 0)
                        {
                            hD = vertices[(z - 1) * vw + x].y + chunkPos.y;
                        }
                        else if (bottomVerts != null)
                        {
                            hD = bottomVerts[(chunk.depth - 1) * vw + x].y + bottomChunk.transform.position.y;
                        }

                        // Query Top Height
                        float hU = currentY;
                        if (z < chunk.depth)
                        {
                            hU = vertices[(z + 1) * vw + x].y + chunkPos.y;
                        }
                        else if (topVerts != null)
                        {
                            hU = topVerts[1 * vw + x].y + topChunk.transform.position.y;
                        }

                        Vector3 tangentX = new Vector3(cellSize * 2f, hR - hL, 0);
                        Vector3 tangentZ = new Vector3(0, hU - hD, cellSize * 2f);
                        normals[index] = Vector3.Cross(tangentZ, tangentX).normalized;
                    }
                }

                // Properly calculate and set outward-facing normals for the skirt vertices
                int leftSB = mainVerts;
                int rightSB = mainVerts + vd;
                int bottomSB = mainVerts + 2 * vd;
                int topSB = mainVerts + 2 * vd + vw;

                if (vertices.Length == mainVerts + 2 * vd + 2 * vw)
                {
                    for (int z = 0; z < vd; z++) normals[leftSB + z] = Vector3.left;
                    for (int z = 0; z < vd; z++) normals[rightSB + z] = Vector3.right;
                    for (int x = 0; x < vw; x++) normals[bottomSB + x] = Vector3.back;
                    for (int x = 0; x < vw; x++) normals[topSB + x] = Vector3.forward;
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

            // Pre-cache all chunk grids in the registry
            ChunkRegistry.Clear();
            foreach (var chunk in allChunks)
            {
                ChunkRegistry.Register(chunk);
            }

            var chunkData = new Dictionary<DesertTerrainChunk, (Mesh mesh, Vector3[] verts)>();
            foreach (var chunk in allChunks)
            {
                MeshFilter mf = chunk.GetComponent<MeshFilter>();
                if (mf == null || mf.sharedMesh == null) continue;
                chunkData[chunk] = (mf.sharedMesh, mf.sharedMesh.vertices);
            }

            HashSet<DesertTerrainChunk> modified = new HashSet<DesertTerrainChunk>();

            // Phase 1: Sew boundaries exactly using grid-aligned vertices (Run 2 times for corner convergence)
            for (int pass = 0; pass < 2; pass++)
            {
                foreach (var chunk in allChunks)
                {
                    if (!chunkData.TryGetValue(chunk, out var data)) continue;
                    Vector2Int gc = chunk.GridCoord;
                    int vw = chunk.width + 1;
                    int vd = chunk.depth + 1;

                    // 1. Align Left border with Right border of Left chunk
                    var leftChunk = ChunkRegistry.Get(gc.x - 1, gc.y);
                    if (leftChunk != null && chunkData.TryGetValue(leftChunk, out var leftData))
                    {
                        for (int z = 0; z < vd; z++)
                        {
                            int myIdx = z * vw; // x = 0
                            int otherIdx = z * vw + chunk.width; // x = width
                            
                            float myWorldY = data.verts[myIdx].y + chunk.transform.position.y;
                            float otherWorldY = leftData.verts[otherIdx].y + leftChunk.transform.position.y;
                            
                            float avgWorldY = (myWorldY + otherWorldY) * 0.5f;
                            
                            data.verts[myIdx].y = avgWorldY - chunk.transform.position.y;
                            leftData.verts[otherIdx].y = avgWorldY - leftChunk.transform.position.y;
                            
                            modified.Add(chunk);
                            modified.Add(leftChunk);
                        }
                    }

                    // 2. Align Bottom border with Top border of Bottom chunk
                    var bottomChunk = ChunkRegistry.Get(gc.x, gc.y - 1);
                    if (bottomChunk != null && chunkData.TryGetValue(bottomChunk, out var bottomData))
                    {
                        for (int x = 0; x < vw; x++)
                        {
                            int myIdx = x; // z = 0
                            int otherIdx = chunk.depth * vw + x; // z = depth
                            
                            float myWorldY = data.verts[myIdx].y + chunk.transform.position.y;
                            float otherWorldY = bottomData.verts[otherIdx].y + bottomChunk.transform.position.y;
                            
                            float avgWorldY = (myWorldY + otherWorldY) * 0.5f;
                            
                            data.verts[myIdx].y = avgWorldY - chunk.transform.position.y;
                            bottomData.verts[otherIdx].y = avgWorldY - bottomChunk.transform.position.y;
                            
                            modified.Add(chunk);
                            modified.Add(bottomChunk);
                        }
                    }
                }
            }

            // Phase 2: Smooth 1-ring boundary vertices
            var smoothedHeights = new Dictionary<(DesertTerrainChunk chunk, int index), float>();
            foreach (var chunk in allChunks)
            {
                if (!chunkData.TryGetValue(chunk, out var data)) continue;
                Vector2Int gc = chunk.GridCoord;
                int vw = chunk.width + 1;
                int vd = chunk.depth + 1;

                var leftChunk = ChunkRegistry.Get(gc.x - 1, gc.y);
                var rightChunk = ChunkRegistry.Get(gc.x + 1, gc.y);
                var bottomChunk = ChunkRegistry.Get(gc.x, gc.y - 1);
                var topChunk = ChunkRegistry.Get(gc.x, gc.y + 1);

                var leftData = leftChunk != null && chunkData.TryGetValue(leftChunk, out var ld) ? ld.verts : null;
                var rightData = rightChunk != null && chunkData.TryGetValue(rightChunk, out var rd) ? rd.verts : null;
                var bottomData = bottomChunk != null && chunkData.TryGetValue(bottomChunk, out var bd) ? bd.verts : null;
                var topData = topChunk != null && chunkData.TryGetValue(topChunk, out var td) ? td.verts : null;

                for (int z = 0; z < vd; z++)
                {
                    for (int x = 0; x < vw; x++)
                    {
                        bool isNearBoundary = (x <= 1 || x >= chunk.width - 1 || z <= 1 || z >= chunk.depth - 1);
                        if (!isNearBoundary) continue;

                        int myIdx = z * vw + x;
                        float currentWorldY = data.verts[myIdx].y + chunk.transform.position.y;

                        float sumNeighborY = 0f;
                        int neighborCount = 0;

                        // Query Left
                        if (x > 0)
                        {
                            sumNeighborY += data.verts[z * vw + (x - 1)].y + chunk.transform.position.y;
                            neighborCount++;
                        }
                        else if (leftData != null)
                        {
                            sumNeighborY += leftData[z * vw + (chunk.width - 1)].y + leftChunk.transform.position.y;
                            neighborCount++;
                        }

                        // Query Right
                        if (x < chunk.width)
                        {
                            sumNeighborY += data.verts[z * vw + (x + 1)].y + chunk.transform.position.y;
                            neighborCount++;
                        }
                        else if (rightData != null)
                        {
                            sumNeighborY += rightData[z * vw + 1].y + rightChunk.transform.position.y;
                            neighborCount++;
                        }

                        // Query Bottom
                        if (z > 0)
                        {
                            sumNeighborY += data.verts[(z - 1) * vw + x].y + chunk.transform.position.y;
                            neighborCount++;
                        }
                        else if (bottomData != null)
                        {
                            sumNeighborY += bottomData[(chunk.depth - 1) * vw + x].y + bottomChunk.transform.position.y;
                            neighborCount++;
                        }

                        // Query Top
                        if (z < chunk.depth)
                        {
                            sumNeighborY += data.verts[(z + 1) * vw + x].y + chunk.transform.position.y;
                            neighborCount++;
                        }
                        else if (topData != null)
                        {
                            sumNeighborY += topData[1 * vw + x].y + topChunk.transform.position.y;
                            neighborCount++;
                        }

                        if (neighborCount > 0)
                        {
                            float avgNeighborY = sumNeighborY / neighborCount;
                            float smoothedWorldY = Mathf.Lerp(currentWorldY, avgNeighborY, 0.5f);
                            smoothedHeights[(chunk, myIdx)] = smoothedWorldY - chunk.transform.position.y;
                        }
                    }
                }
            }

            // Apply smoothed heights
            foreach (var kvp in smoothedHeights)
            {
                var chunk = kvp.Key.chunk;
                int idx = kvp.Key.index;
                float targetLocalY = kvp.Value;
                
                var data = chunkData[chunk];
                if (!Mathf.Approximately(data.verts[idx].y, targetLocalY))
                {
                    data.verts[idx].y = targetLocalY;
                    modified.Add(chunk);
                }
            }

            // Phase 3: Rebuild/update skirt vertices for all modified chunks
            foreach (var chunk in modified)
            {
                var data = chunkData[chunk];
                int vw = chunk.width + 1;
                int vd = chunk.depth + 1;
                int mainVerts = vw * vd;
                const float SkirtDepth = 40f;

                int leftSB = mainVerts;
                int rightSB = mainVerts + vd;
                int bottomSB = mainVerts + 2 * vd;
                int topSB = mainVerts + 2 * vd + vw;

                if (data.verts.Length == mainVerts + 2 * vd + 2 * vw)
                {
                    // Left skirt (x=0 column)
                    for (int z = 0; z < vd; z++)
                    {
                        int mi = z * vw;
                        data.verts[leftSB + z] = data.verts[mi] + new Vector3(0f, -SkirtDepth, 0f);
                    }
                    // Right skirt (x=width column)
                    for (int z = 0; z < vd; z++)
                    {
                        int mi = z * vw + chunk.width;
                        data.verts[rightSB + z] = data.verts[mi] + new Vector3(0f, -SkirtDepth, 0f);
                    }
                    // Bottom skirt (z=0 row)
                    for (int x = 0; x < vw; x++)
                    {
                        int mi = x;
                        data.verts[bottomSB + x] = data.verts[mi] + new Vector3(0f, -SkirtDepth, 0f);
                    }
                    // Top skirt (z=depth row)
                    for (int x = 0; x < vw; x++)
                    {
                        int mi = chunk.depth * vw + x;
                        data.verts[topSB + x] = data.verts[mi] + new Vector3(0f, -SkirtDepth, 0f);
                    }
                }
            }

            // Write modified vertices back and update bounds/colliders
            foreach (var chunk in modified)
            {
                var data = chunkData[chunk];
                Undo.RecordObject(data.mesh, "Fuse Seams");
                Undo.RecordObject(chunk, "Fuse Seams");

                chunk.useSavedMeshAsset = true;

                data.mesh.vertices = data.verts;
                data.mesh.RecalculateBounds();
                data.mesh.UploadMeshData(false);

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

                chunk.SyncSnowLayer();

            }

            // Force useSavedMeshAsset = true for ALL chunks in the project so they never rebuild from noise at runtime
            foreach (var chunk in allChunks)
            {
                if (chunk != null)
                {
                    if (!chunk.useSavedMeshAsset)
                    {
                        Undo.RecordObject(chunk, "Enable useSavedMeshAsset");
                        chunk.useSavedMeshAsset = true;
                        EditorUtility.SetDirty(chunk);
                        if (chunk.gameObject.scene != null && chunk.gameObject.scene.name != null)
                        {
                            UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(chunk.gameObject.scene);
                        }
                    }
                }
            }

            // Recalculate seamless normals for all affected chunks
            _modifiedChunks.Clear();
            foreach (var chunk in modified)
            {
                _modifiedChunks.Add(chunk);
            }
            RecalculateNormalsForModifiedChunks();

            EditorUtility.DisplayDialog("Fusion & Smooth Complete", $"Successfully aligned, smoothed, and fused seam coordinates across {modified.Count} chunks. No more cracks!", "Fantastic!");
        }

        private void RefineAllChunks()
        {
            CacheSceneChunks();
            var allChunks = _cachedSceneChunks;
            
            int refinedCount = 0;
            foreach (var chunk in allChunks)
            {
                if (chunk == null) continue;
                MeshFilter mf = chunk.GetComponent<MeshFilter>();
                if (mf == null || mf.sharedMesh == null) continue;

                chunk.useSavedMeshAsset = true;
                chunk.RefineExistingTerrain();
                refinedCount++;
            }

            // Recalculate normals seamlessly
            _modifiedChunks.Clear();
            foreach (var chunk in allChunks)
            {
                if (chunk != null) _modifiedChunks.Add(chunk);
            }
            RecalculateNormalsForModifiedChunks();

            EditorUtility.DisplayDialog("Refinement Complete", $"Successfully refined details for {refinedCount} chunks in the scene!", "Excellent!");
        }

        private void FuseAllMapChunksBatch()
        {
            if (!EditorUtility.DisplayDialog("一键缝合全图所有区块？", 
                "该操作将自动在编辑器中以叠加方式加载项目内所有的地形区块场景，执行无缝对齐与平滑抚平，然后自动存盘并恢复初始场景。该操作由于需要读取保存所有网格资产，会消耗约10-30秒，是否继续？", "确认缝合全图", "取消"))
            {
                return;
            }

            // Save current scene layout
            string originalScenePath = UnityEngine.SceneManagement.SceneManager.GetActiveScene().path;
            if (!string.IsNullOrEmpty(originalScenePath))
            {
                UnityEditor.SceneManagement.EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo();
            }

            // Find all chunk scenes
            string chunksFolder = "Assets/Scenes/Chunks";
            if (!System.IO.Directory.Exists(chunksFolder))
            {
                EditorUtility.DisplayDialog("错误", $"找不到地形区块目录: {chunksFolder}", "OK");
                return;
            }

            string[] files = System.IO.Directory.GetFiles(chunksFolder, "*.unity", System.IO.SearchOption.TopDirectoryOnly);
            if (files.Length == 0)
            {
                EditorUtility.DisplayDialog("提示", "未找到任何地形区块场景文件 (*.unity)", "OK");
                return;
            }

            try
            {
                int count = 0;
                foreach (var file in files)
                {
                    count++;
                    string normalizedPath = file.Replace('\\', '/');
                    EditorUtility.DisplayProgressBar("正在加载全图区块", $"加载 {System.IO.Path.GetFileName(file)} ({count}/{files.Length})", (float)count / files.Length);
                    
                    // Open scene additively
                    UnityEditor.SceneManagement.EditorSceneManager.OpenScene(normalizedPath, UnityEditor.SceneManagement.OpenSceneMode.Additive);
                }
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }

            // Run the fusion on all loaded chunks!
            Debug.Log("[ChunkMergeSplitTool] Loaded all map scenes. Starting batch fusion...");
            FuseAllSeams();

            // Save everything
            SaveSculptChangesToDisk();

            // Re-open original scene to clean up hierarchy
            if (!string.IsNullOrEmpty(originalScenePath))
            {
                UnityEditor.SceneManagement.EditorSceneManager.OpenScene(originalScenePath, UnityEditor.SceneManagement.OpenSceneMode.Single);
            }
            
            EditorUtility.DisplayDialog("全图缝合完成", $"成功加载并一键对齐、平滑、保存了全图 {files.Length} 个关卡场景的所有接缝！", "太棒了！");
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
