using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace EnvironmentSystem
{
    public class WorldStreamer : MonoBehaviour
    {
        public static WorldStreamer Instance { get; private set; }
        public bool HasLoadedAnyChunks => _loadedChunks.Count > 0;

        [Header("General Settings")]
        [Tooltip("Delay in seconds before unloading a chunk to prevent thrashing at boundaries.")]
        public float unloadDelay = 5f;
        [Tooltip("Set to true to use automatic grid coordinate-based loading. Set to false to use legacy ChunkTriggers.")]
        public bool useGridStreaming = true;
        [Tooltip("Enable detailed chunk load/unload logs. Keep off during normal gameplay to avoid console overhead.")]
        public bool verboseLogging = false;

        [Header("Grid Auto Streamer (Option B)")]
        [Tooltip("The Transform to track. If left empty, it will automatically search for the Player, the RV, or the Main Camera.")]
        public Transform trackingTarget;
        [Tooltip("Time interval in seconds between grid coordinate checks.")]
        public float checkInterval = 0.5f;
        [Tooltip("The width of each chunk mesh in world units (width * cellSize).")]
        public float chunkSizeX = 256f;
        [Tooltip("The depth of each chunk mesh in world units (depth * cellSize).")]
        public float chunkSizeZ = 256f;
        [Tooltip("The prefix of the baked chunk scenes, e.g. Desert_Chunk_X_Z")]
        public string sceneNamePrefix = "Desert_Chunk";

        [Tooltip("The grid range of chunks to load around the player (2 is a 5x5 grid, 3 is a 7x7 grid).")]
        public int loadingRange = 2;
        [Tooltip("Maximum additive chunk scene loads running at once. Lower values reduce frame spikes.")]
        [Min(1)] public int maxConcurrentLoads = 1;
        [Tooltip("Minimum time between starting additive chunk scene loads. Small spacing smooths activation spikes while moving fast.")]
        [Min(0f)] public float loadStartInterval = 0.1f;
        [Tooltip("Minimum time between allowing loaded chunk scenes to activate. This smooths the heavier activation frame.")]
        [Min(0f)] public float loadActivationInterval = 0.12f;

        private HashSet<string> _requestedChunks = new HashSet<string>();
        private HashSet<string> _loadedChunks = new HashSet<string>();
        private Dictionary<string, Coroutine> _unloadRoutines = new Dictionary<string, Coroutine>();
        // Pending scene load queue for throttling.
        private Queue<string> _loadQueue = new Queue<string>();
        private HashSet<string> _queuedChunks = new HashSet<string>();
        private readonly List<string> _requiredChunks = new List<string>(49);
        private readonly List<string> _chunksToUnload = new List<string>(49);
        private readonly List<Vector2Int> _streamingOffsets = new List<Vector2Int>(49);
        private int _activeLoads = 0;
        private int _cachedOffsetRange = int.MinValue;
        private Coroutine _drainLoadQueueRoutine;
        private float _nextLoadStartTime;
        private float _nextLoadActivationTime;

        // Cache DesertTerrainChunk size so it is not recalculated every streaming check.
        private float _chunkSizeCacheTime = -99f;
        private const float ChunkSizeCacheInterval = 5f;

        private float _nextCheckTime;
        private int _lastGridX = int.MinValue;
        private int _lastGridZ = int.MinValue;
        private float _nextTargetRefreshTime = -1f;
        private const float TargetRefreshInterval = 0.75f;
        private GameObject _cachedPlayer;
        private Renderer _cachedPlayerRenderer;
        private RVSystem.RVController _cachedRv;
        private RVSystem.RVStateMachine _cachedRvStateMachine;
        private Camera _cachedCamera;

        private void Awake()
        {
            if (Instance == null) Instance = this;
            else
            {
                if (Application.isPlaying) Destroy(gameObject);
                else UnityEngine.Object.DestroyImmediate(gameObject);
            }
        }

        private void Start()
        {
            // Initial trigger will be fired automatically in the first Update frame
            _nextCheckTime = 0f;
        }

        private void Update()
        {
            if (Time.time < _nextCheckTime) return;
            _nextCheckTime = Time.time + checkInterval;

            if (!useGridStreaming) return;

            RefreshTrackingTarget();

            // 2. Perform grid projection and load surrounding chunks based on loadingRange
            if (trackingTarget != null)
            {
                // Refresh chunk size from the active chunk registry without a scene-wide search.
                if (Time.time - _chunkSizeCacheTime > ChunkSizeCacheInterval)
                {
                    _chunkSizeCacheTime = Time.time;
                    TryRefreshChunkSizeFromLoadedChunk();
                }

                if (chunkSizeX <= 0.1f) chunkSizeX = 256f;
                if (chunkSizeZ <= 0.1f) chunkSizeZ = 256f;

                Vector3 pos = trackingTarget.position;
                int gridX = Mathf.RoundToInt(pos.x / chunkSizeX);
                int gridZ = Mathf.RoundToInt(pos.z / chunkSizeZ);

                if (gridX != _lastGridX || gridZ != _lastGridZ)
                {
                    _lastGridX = gridX;
                    _lastGridZ = gridZ;
                    UpdateGridChunks(gridX, gridZ);
                }
            }

        }

        private void RefreshTrackingTarget()
        {
            if (Time.time >= _nextTargetRefreshTime)
            {
                _nextTargetRefreshTime = Time.time + TargetRefreshInterval;

                if (_cachedPlayer == null || !_cachedPlayer.activeInHierarchy)
                {
                    _cachedPlayer = GameObject.FindGameObjectWithTag("Player");
                    _cachedPlayerRenderer = _cachedPlayer != null
                        ? _cachedPlayer.GetComponentInChildren<Renderer>()
                        : null;
                }

                if (_cachedRv == null || !_cachedRv.gameObject.activeInHierarchy)
                {
                    _cachedRv = FindObjectOfType<RVSystem.RVController>();
                }

                if (_cachedRvStateMachine == null || !_cachedRvStateMachine.gameObject.activeInHierarchy)
                {
                    _cachedRvStateMachine = FindObjectOfType<RVSystem.RVStateMachine>();
                }

                if (_cachedCamera == null || !_cachedCamera.gameObject.activeInHierarchy)
                {
                    _cachedCamera = Camera.main;
                }
            }

            trackingTarget = ResolveTrackingTarget();
        }

        private Transform ResolveTrackingTarget()
        {
            if (_cachedRvStateMachine != null &&
                _cachedRvStateMachine.gameObject.activeInHierarchy &&
                _cachedRvStateMachine.currentState == RVSystem.RVState.Active)
            {
                return _cachedRvStateMachine.transform;
            }

            if (_cachedPlayer != null && _cachedPlayer.activeInHierarchy)
            {
                if (_cachedRv != null &&
                    _cachedRv.gameObject.activeInHierarchy &&
                    _cachedPlayerRenderer != null &&
                    !_cachedPlayerRenderer.enabled)
                {
                    return _cachedRv.transform;
                }

                return _cachedPlayer.transform;
            }

            if (_cachedRv != null && _cachedRv.gameObject.activeInHierarchy)
            {
                return _cachedRv.transform;
            }

            return _cachedCamera != null ? _cachedCamera.transform : null;
        }

        private void TryRefreshChunkSizeFromLoadedChunk()
        {
            foreach (var kv in ChunkRegistry.All)
            {
                DesertTerrainChunk activeChunk = kv.Value;
                if (activeChunk == null || !activeChunk.gameObject.activeInHierarchy) continue;

                chunkSizeX = activeChunk.width * activeChunk.cellSize;
                chunkSizeZ = activeChunk.depth * activeChunk.cellSize;
                return;
            }
        }

        private void UpdateGridChunks(int centerGridX, int centerGridZ)
        {
            int range = loadingRange;
            if (Application.platform == RuntimePlatform.WebGLPlayer)
            {
                range = Mathf.Max(1, loadingRange - 1);
            }

            EnsureStreamingOffsets(range);

            _requiredChunks.Clear();
            for (int i = 0; i < _streamingOffsets.Count; i++)
            {
                Vector2Int offset = _streamingOffsets[i];
                int gx = centerGridX + offset.x;
                int gz = centerGridZ + offset.y;
                string sceneName = $"{sceneNamePrefix}_{gx}_{gz}";
                _requiredChunks.Add(sceneName);
            }

            int gridSizeDim = range * 2 + 1;
            if (verboseLogging)
            {
                Debug.Log($"<color=#38bdf8><b>[WorldStreamer]</b></color> Grid updated. Center ({centerGridX}, {centerGridZ}). Loading {gridSizeDim}x{gridSizeDim} grid.");
            }
            RequestChunks(_requiredChunks);
        }

        private void EnsureStreamingOffsets(int range)
        {
            if (_cachedOffsetRange == range)
                return;

            _cachedOffsetRange = range;
            _streamingOffsets.Clear();

            for (int dx = -range; dx <= range; dx++)
            {
                for (int dz = -range; dz <= range; dz++)
                {
                    _streamingOffsets.Add(new Vector2Int(dx, dz));
                }
            }

            // Closest chunks are queued first so the playable area fills in before far edges.
            _streamingOffsets.Sort((a, b) => (a.x * a.x + a.y * a.y).CompareTo(b.x * b.x + b.y * b.y));
        }

        public void RequestChunks(List<string> chunkSceneNames)
        {
            _requestedChunks.Clear();
            foreach (var chunk in chunkSceneNames)
            {
                _requestedChunks.Add(chunk);
                LoadChunk(chunk);
            }

            // Unload chunks that are no longer requested.
            _chunksToUnload.Clear();
            foreach (var loaded in _loadedChunks)
            {
                if (!_requestedChunks.Contains(loaded))
                {
                    _chunksToUnload.Add(loaded);
                }
            }

            for (int i = 0; i < _chunksToUnload.Count; i++)
            {
                UnloadChunk(_chunksToUnload[i]);
            }
        }

        private void LoadChunk(string chunkName)
        {
            if (string.IsNullOrEmpty(chunkName)) return;

            // If it was queued for unloading, cancel the unload
            if (_unloadRoutines.TryGetValue(chunkName, out Coroutine routine))
            {
                if (routine != null) StopCoroutine(routine);
                _unloadRoutines.Remove(chunkName);
            }

            if (!_loadedChunks.Contains(chunkName) && !_queuedChunks.Contains(chunkName))
            {
                _loadedChunks.Add(chunkName);
                // Throttle: enqueue, then drain up to MaxConcurrentLoads
                _loadQueue.Enqueue(chunkName);
                _queuedChunks.Add(chunkName);
                DrainLoadQueue();
            }
        }

        private void DrainLoadQueue()
        {
            if (_drainLoadQueueRoutine != null)
                return;

            _drainLoadQueueRoutine = StartCoroutine(DrainLoadQueueAsync());
        }

        private IEnumerator DrainLoadQueueAsync()
        {
            while (_loadQueue.Count > 0)
            {
                int loadLimit = Mathf.Max(1, maxConcurrentLoads);
                if (_activeLoads >= loadLimit)
                    break;

                if (Time.time < _nextLoadStartTime)
                {
                    yield return new WaitForSeconds(_nextLoadStartTime - Time.time);
                    continue;
                }

                string next = _loadQueue.Dequeue();
                _queuedChunks.Remove(next);
                if (!_requestedChunks.Contains(next))
                {
                    _loadedChunks.Remove(next);
                    continue;
                }

                _activeLoads++;
                _nextLoadStartTime = Time.time + loadStartInterval;
                StartCoroutine(LoadSceneAsync(next));

                if (loadStartInterval > 0f)
                    yield return new WaitForSeconds(loadStartInterval);
                else
                    yield return null;
            }

            _drainLoadQueueRoutine = null;
        }

        private void UnloadChunk(string chunkName)
        {
            if (!_unloadRoutines.ContainsKey(chunkName) && _loadedChunks.Contains(chunkName))
            {
                _unloadRoutines[chunkName] = StartCoroutine(UnloadSceneAsync(chunkName));
            }
        }

        private IEnumerator LoadSceneAsync(string sceneName)
        {
            if (!_requestedChunks.Contains(sceneName))
            {
                _loadedChunks.Remove(sceneName);
                _activeLoads = Mathf.Max(0, _activeLoads - 1);
                DrainLoadQueue();
                yield break;
            }

            AsyncOperation asyncLoad = SceneManager.LoadSceneAsync(sceneName, LoadSceneMode.Additive);
            if (asyncLoad == null)
            {
                Debug.LogWarning($"[WorldStreamer] Failed to load chunk {sceneName}. Check Build Settings!");
                _loadedChunks.Remove(sceneName);
                _activeLoads = Mathf.Max(0, _activeLoads - 1);
                DrainLoadQueue();
                yield break;
            }

            asyncLoad.allowSceneActivation = false;
            while (asyncLoad.progress < 0.9f)
            {
                yield return null;
            }

            if (loadActivationInterval > 0f && Time.time < _nextLoadActivationTime)
            {
                yield return new WaitForSeconds(_nextLoadActivationTime - Time.time);
            }

            _nextLoadActivationTime = Time.time + loadActivationInterval;
            asyncLoad.allowSceneActivation = true;
            while (!asyncLoad.isDone)
            {
                yield return null;
            }

            if (verboseLogging)
            {
                Debug.Log($"[WorldStreamer] Loaded chunk: {sceneName}");
            }

            if (!_requestedChunks.Contains(sceneName))
            {
                UnloadChunk(sceneName);
            }

            _activeLoads = Mathf.Max(0, _activeLoads - 1);
            DrainLoadQueue();
        }

        private IEnumerator UnloadSceneAsync(string sceneName)
        {
            // Wait a few seconds before actual unload to avoid thrashing
            yield return new WaitForSeconds(unloadDelay);

            // Double check if it got re-requested during the delay
            if (_requestedChunks.Contains(sceneName))
            {
                _unloadRoutines.Remove(sceneName);
                yield break;
            }

            AsyncOperation asyncUnload = SceneManager.UnloadSceneAsync(sceneName);
            if (asyncUnload == null)
            {
                _loadedChunks.Remove(sceneName);
                _unloadRoutines.Remove(sceneName);
                DrainLoadQueue();
                yield break;
            }

            while (!asyncUnload.isDone)
            {
                yield return null;
            }

            _loadedChunks.Remove(sceneName);
            _unloadRoutines.Remove(sceneName);
            if (verboseLogging)
            {
                Debug.Log($"[WorldStreamer] Unloaded chunk: {sceneName}");
            }
        }
    }
}
