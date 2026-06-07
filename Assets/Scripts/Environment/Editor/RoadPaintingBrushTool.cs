using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using System.Collections.Generic;

namespace EnvironmentSystem
{
    public class RoadPaintingBrushTool : EditorWindow
    {
        public enum PaintAction { Paint, Erase }

        [Header("Brush Settings")]
        private bool _paintModeActive = false;
        private PaintAction _paintAction = PaintAction.Paint;
        private float _brushRadius = 15f;
        private float _brushStrength = 1f;

        private List<DesertTerrainRoadOverlay> _modifiedOverlays = new List<DesertTerrainRoadOverlay>();
        private List<DesertTerrainChunk> _cachedChunks = new List<DesertTerrainChunk>();

        // Custom Undo System
        private struct RoadUndoState
        {
            public DesertTerrainRoadOverlay overlay;
            public Color[] originalColors;
        }

        private struct UndoStroke
        {
            public List<RoadUndoState> states;
        }

        private static Stack<UndoStroke> _customUndoStack = new Stack<UndoStroke>();
        private List<RoadUndoState> _currentStrokeStates = new List<RoadUndoState>();
        private HashSet<DesertTerrainRoadOverlay> _recordedOverlays = new HashSet<DesertTerrainRoadOverlay>();

        [MenuItem("Tools/Environment/Road Painting Brush")]
        public static void ShowWindow()
        {
            GetWindow<RoadPaintingBrushTool>("Road Paint Brush");
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

            GUILayout.Label("Road Overlay Painting Tool", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox("This tool lets you paint roads onto the terrain overlay layer. It does not modify the terrain mesh, but drapes a custom mesh overlay that moves with it.", MessageType.Info);

            GUILayout.Space(8);

            // Toggle brush mode
            GUI.backgroundColor = _paintModeActive ? new Color(0.2f, 0.8f, 0.4f) : Color.white;
            if (GUILayout.Button(_paintModeActive ? "🎨 Road Brush Active (Click/Drag to Paint)" : "🖌️ Activate Road Brush", GUILayout.Height(35)))
            {
                _paintModeActive = !_paintModeActive;
                if (_paintModeActive)
                {
                    if (SceneView.lastActiveSceneView != null)
                        SceneView.lastActiveSceneView.Focus();
                }
            }
            GUI.backgroundColor = Color.white;

            if (_paintModeActive)
            {
                EditorGUILayout.HelpBox("Hold [Left-Click & Drag] to paint roads.\nHold [Shift] while dragging to erase roads.\nPress [Cmd+Z / Ctrl+Z] to undo painting strokes.", MessageType.Warning);
            }

            GUILayout.Space(12);
            GUILayout.Label("Brush Controls", EditorStyles.boldLabel);

            _paintAction = (PaintAction)EditorGUILayout.EnumPopup("Action Mode", _paintAction);
            _brushRadius = EditorGUILayout.Slider("Brush Radius (Size)", _brushRadius, 1f, 100f);
            _brushStrength = EditorGUILayout.Slider("Brush Strength/Flow", _brushStrength, 0.05f, 10f);

            GUILayout.Space(20);
            GUILayout.Label("Serialization & Saving", EditorStyles.boldLabel);

            GUI.backgroundColor = new Color(0.2f, 0.8f, 0.5f);
            if (GUILayout.Button("💾 一键保存道路数据 (Save Road Overlay Meshes)", GUILayout.Height(35)))
            {
                SaveRoadMeshesToDisk();
            }
            GUI.backgroundColor = Color.white;

            GUILayout.Space(5);

            if (GUILayout.Button("🛠️ 初始化所有地表道路层 (Initialize Overlays on All Loaded Chunks)"))
            {
                InitializeOverlaysOnAllChunks();
            }
        }

        private void OnSceneGUI(SceneView sceneView)
        {
            Event current = Event.current;

            // Listen for Cmd+Z / Ctrl+Z undo hotkeys inside the Scene View
            if (current.type == EventType.KeyDown && current.keyCode == KeyCode.Z && (current.command || current.control))
            {
                PerformCustomUndo();
                current.Use();
                return;
            }

            if (!_paintModeActive) return;

            // Block default Unity scene view clicks
            int controlID = GUIUtility.GetControlID(FocusType.Passive);
            HandleUtility.AddDefaultControl(controlID);

            Ray ray = HandleUtility.GUIPointToWorldRay(current.mousePosition);

            if (Physics.Raycast(ray, out RaycastHit hit, 20000f))
            {
                // Draw brush preview in Scene View
                Handles.color = (_paintAction == PaintAction.Paint) ? new Color(0f, 1f, 0.5f, 0.5f) : new Color(1f, 0.2f, 0.2f, 0.5f);
                Handles.DrawWireDisc(hit.point, Vector3.up, _brushRadius);
                Handles.color = (_paintAction == PaintAction.Paint) ? new Color(0f, 1f, 0.5f, 0.04f) : new Color(1f, 0.2f, 0.2f, 0.04f);
                Handles.DrawSolidDisc(hit.point, Vector3.up, _brushRadius);

                // Mouse interaction
                if (current.type == EventType.MouseDown && current.button == 0 && !current.alt)
                {
                    CacheChunks();
                    _recordedOverlays.Clear();
                    _currentStrokeStates.Clear();

                    PaintAction activeAction = current.shift ? PaintAction.Erase : _paintAction;
                    ApplyRoadPaint(hit.point, activeAction);
                    current.Use();
                }
                else if (current.type == EventType.MouseDrag && current.button == 0 && !current.alt)
                {
                    PaintAction activeAction = current.shift ? PaintAction.Erase : _paintAction;
                    ApplyRoadPaint(hit.point, activeAction);
                    current.Use();
                }

                if (current.type == EventType.MouseUp && current.button == 0)
                {
                    // Push current stroke undo states to our custom stack
                    if (_currentStrokeStates.Count > 0)
                    {
                        _customUndoStack.Push(new UndoStroke { states = new List<RoadUndoState>(_currentStrokeStates) });
                        Debug.Log($"[Road Brush] Stroke finished. Recorded undo state for {_currentStrokeStates.Count} chunks.");
                    }

                    _cachedChunks.Clear();
                    _modifiedOverlays.Clear();
                    _recordedOverlays.Clear();
                    _currentStrokeStates.Clear();
                    SceneView.RepaintAll();
                }
            }

            if (current.type == EventType.MouseMove || current.type == EventType.MouseDrag)
            {
                sceneView.Repaint();
            }
        }

        private void PerformCustomUndo()
        {
            if (_customUndoStack.Count == 0)
            {
                Debug.LogWarning("[Road Brush] No more undo actions available in the stack.");
                return;
            }

            UndoStroke stroke = _customUndoStack.Pop();
            foreach (var state in stroke.states)
            {
                if (state.overlay == null || state.originalColors == null) continue;
                Mesh mesh = state.overlay.GetMesh();
                if (mesh == null) continue;

                mesh.colors = state.originalColors;
                mesh.UploadMeshData(false);
                state.overlay.SetMesh(mesh);

                EditorUtility.SetDirty(mesh);
                EditorUtility.SetDirty(state.overlay);
                if (state.overlay.gameObject.scene != null && state.overlay.gameObject.scene.name != null)
                {
                    EditorSceneManager.MarkSceneDirty(state.overlay.gameObject.scene);
                }
            }

            SceneView.RepaintAll();
            Debug.Log("<color=#ff8000><b>[Road Brush]</b></color> Undo successful! Reverted road paint stroke.");
        }

        private void CacheChunks()
        {
            if (_cachedChunks.Count > 0) return;

            var all = Resources.FindObjectsOfTypeAll<DesertTerrainChunk>();
            foreach (var chunk in all)
            {
                if (chunk != null && !EditorUtility.IsPersistent(chunk.gameObject))
                {
                    _cachedChunks.Add(chunk);
                }
            }
        }

        private void ApplyRoadPaint(Vector3 centerPoint, PaintAction action)
        {
            foreach (var chunk in _cachedChunks)
            {
                MeshFilter terrainFilter = chunk.GetComponent<MeshFilter>();
                if (terrainFilter == null || terrainFilter.sharedMesh == null) continue;

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

                // Get or create RoadOverlay child
                Transform overlayTransform = chunk.transform.Find("RoadOverlay");
                DesertTerrainRoadOverlay overlay = null;

                if (overlayTransform == null)
                {
                    GameObject go = new GameObject("RoadOverlay");
                    go.transform.SetParent(chunk.transform, false);
                    go.transform.localPosition = Vector3.zero;
                    go.transform.localRotation = Quaternion.identity;
                    go.transform.localScale = Vector3.one;
                    overlay = go.AddComponent<DesertTerrainRoadOverlay>();
                    overlay.SyncWithTerrain(terrainFilter.sharedMesh);
                }
                else
                {
                    overlay = overlayTransform.GetComponent<DesertTerrainRoadOverlay>();
                    if (overlay == null) overlay = overlayTransform.gameObject.AddComponent<DesertTerrainRoadOverlay>();
                }

                Mesh overlayMesh = overlay.GetMesh();
                if (overlayMesh == null)
                {
                    overlay.SyncWithTerrain(terrainFilter.sharedMesh);
                    overlayMesh = overlay.GetMesh();
                }

                if (overlayMesh == null) continue;

                Vector3[] vertices = overlayMesh.vertices;
                Color[] colors = overlayMesh.colors;

                if (colors == null || colors.Length != vertices.Length)
                {
                    colors = new Color[vertices.Length];
                    for (int i = 0; i < colors.Length; i++) colors[i] = new Color(0, 0, 0, 0);
                }

                // 🚀 Record undo state BEFORE modifying colors for this stroke
                if (!_recordedOverlays.Contains(overlay))
                {
                    _currentStrokeStates.Add(new RoadUndoState
                    {
                        overlay = overlay,
                        originalColors = (Color[])colors.Clone()
                    });
                    _recordedOverlays.Add(overlay);
                }

                bool modified = false;

                for (int i = 0; i < vertices.Length; i++)
                {
                    Vector3 worldPos = chunkPos + vertices[i];
                    float dist = Vector2.Distance(new Vector2(worldPos.x, worldPos.z), new Vector2(centerPoint.x, centerPoint.z));

                    if (dist <= _brushRadius)
                    {
                        float t = dist / _brushRadius;
                        // Cosine smooth curve falloff
                        float falloff = 0.5f * (1f + Mathf.Cos(t * Mathf.PI));
                        float delta = _brushStrength * falloff * 0.1f;

                        if (action == PaintAction.Paint)
                        {
                            colors[i].a = Mathf.Clamp01(colors[i].a + delta);
                        }
                        else
                        {
                            colors[i].a = Mathf.Clamp01(colors[i].a - delta);
                        }
                        modified = true;
                    }
                }

                if (modified)
                {
                    overlayMesh.colors = colors;
                    overlayMesh.UploadMeshData(false);
                    overlay.SetMesh(overlayMesh);

                    EditorUtility.SetDirty(overlayMesh);
                    EditorUtility.SetDirty(overlay);
                    if (overlay.gameObject.scene != null && overlay.gameObject.scene.name != null)
                    {
                        EditorSceneManager.MarkSceneDirty(overlay.gameObject.scene);
                    }

                    if (!_modifiedOverlays.Contains(overlay))
                    {
                        _modifiedOverlays.Add(overlay);
                    }
                }
            }
        }

        private void InitializeOverlaysOnAllChunks()
        {
            CacheChunks();
            int count = 0;
            foreach (var chunk in _cachedChunks)
            {
                MeshFilter terrainFilter = chunk.GetComponent<MeshFilter>();
                if (terrainFilter == null || terrainFilter.sharedMesh == null) continue;

                Transform overlayTransform = chunk.transform.Find("RoadOverlay");
                if (overlayTransform == null)
                {
                    GameObject go = new GameObject("RoadOverlay");
                    go.transform.SetParent(chunk.transform, false);
                    go.transform.localPosition = Vector3.zero;
                    go.transform.localRotation = Quaternion.identity;
                    go.transform.localScale = Vector3.one;
                    var overlay = go.AddComponent<DesertTerrainRoadOverlay>();
                    overlay.SyncWithTerrain(terrainFilter.sharedMesh);
                    count++;

                    EditorUtility.SetDirty(go);
                    EditorSceneManager.MarkSceneDirty(chunk.gameObject.scene);
                }
            }
            Debug.Log($"[Road Brush] Initialized {count} road overlays on loaded terrain chunks.");
            EditorUtility.DisplayDialog("Initialization Complete", $"Initialized {count} road overlays on loaded terrain chunks successfully!", "OK");
        }

        private void SaveRoadMeshesToDisk()
        {
            var allOverlays = Resources.FindObjectsOfTypeAll<DesertTerrainRoadOverlay>();
            int savedCount = 0;

            string folderPath = "Assets/Scenes/Chunks/Meshes";
            if (!System.IO.Directory.Exists(folderPath))
            {
                System.IO.Directory.CreateDirectory(folderPath);
                AssetDatabase.Refresh();
            }

            foreach (var overlay in allOverlays)
            {
                if (overlay == null || EditorUtility.IsPersistent(overlay.gameObject)) continue;

                Mesh mesh = overlay.GetMesh();
                if (mesh == null) continue;

                string assetPath = AssetDatabase.GetAssetPath(mesh);
                bool isWriteableCustomAsset = !string.IsNullOrEmpty(assetPath) && assetPath.EndsWith(".asset") && assetPath.Contains("/Meshes/");

                if (isWriteableCustomAsset)
                {
                    EditorUtility.SetDirty(mesh);
                }
                else
                {
                    string meshPath = $"{folderPath}/RoadMesh_{overlay.transform.parent.name}.asset";
                    
                    Mesh standaloneMesh = Instantiate(mesh);
                    standaloneMesh.name = "RoadMesh_" + overlay.transform.parent.name;

                    AssetDatabase.CreateAsset(standaloneMesh, meshPath);
                    overlay.SetMesh(standaloneMesh);

                    Debug.Log($"[Road Brush] Saved new road mesh asset: {meshPath}");
                }

                EditorUtility.SetDirty(overlay);
                if (overlay.gameObject.scene != null && overlay.gameObject.scene.name != null)
                {
                    EditorSceneManager.MarkSceneDirty(overlay.gameObject.scene);
                }
                savedCount++;
            }

            EditorSceneManager.SaveOpenScenes();
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            EditorUtility.DisplayDialog("Saved Successfully", $"Successfully saved {savedCount} road overlay modifications and serialized meshes to disk!", "Excellent!");
        }
    }
}
