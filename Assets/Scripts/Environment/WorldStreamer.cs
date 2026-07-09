using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace EnvironmentSystem
{
    public class WorldStreamer : MonoBehaviour
    {
        public static WorldStreamer Instance { get; private set; }
        public bool HasLoadedAnyChunks => _loadedChunks.Count > 0;
        public bool DistantTerrainProxyActive => _distantTerrainObject != null && _distantTerrainObject.activeInHierarchy && _distantTerrainMesh != null;
        public int DistantTerrainProxyVertexCount => _distantTerrainMesh != null ? _distantTerrainMesh.vertexCount : 0;
        public float DistantTerrainProxyCoverageRadiusMeters => Mathf.Max(chunkSizeX, chunkSizeZ) * Mathf.Max(0, GetEffectiveDistantTerrainRange());
        public float DistantTerrainCameraFarClipTarget => distantTerrainCameraFarClip;
        private const int MinimumRuntimeLoadingRange = 3;
        private const int MinimumRuntimeConcurrentLoads = 2;
        private const int MinimumRuntimeForwardExtraRange = 4;
        private const int MinimumRuntimeVisualReadyRangeBonus = 2;
        private const int MinimumRuntimeHighSpeedForwardRange = 6;
        private const int MinimumRuntimeDistantTerrainRange = 32;
        private const float MinimumRuntimeDistantTerrainCoverageMeters = 8192f;
        private const int MinimumRuntimeDistantSamplesPerChunk = 2;
        private const float MinimumRuntimeDistantCameraFarClip = 12000f;

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
        public int loadingRange = 3;
        [Tooltip("Minimum grid range kept in WebGL. Higher values make distant chunks appear sooner at the cost of more loaded scenes.")]
        [Range(1, 4)] public int minimumWebGLLoadingRange = 3;
        [Tooltip("Maximum additive chunk scene loads running at once. Lower values reduce frame spikes.")]
        [Min(1)] public int maxConcurrentLoads = 2;
        [Tooltip("Minimum time between starting additive chunk scene loads. Small spacing smooths activation spikes while moving fast.")]
        [Min(0f)] public float loadStartInterval = 0.05f;
        [Tooltip("Minimum time between allowing loaded chunk scenes to activate. This smooths the heavier activation frame.")]
        [Min(0f)] public float loadActivationInterval = 0.06f;

        [Header("Vision Distance")]
        [Tooltip("Extra chunks to stream ahead of the current travel direction.")]
        [Range(0, 4)] public int forwardExtraRange = 4;
        [Tooltip("Half-width of the forward streaming band. 1 loads a 3-chunk-wide strip ahead.")]
        [Range(0, 3)] public int forwardBandHalfWidth = 1;
        [Tooltip("How strongly chunks ahead are prioritized over same-distance side/rear chunks.")]
        [Range(0f, 2f)] public float forwardPriorityBias = 0.75f;
        [Tooltip("Extra grid rings that may become visible once loaded. This lets preloaded distant chunks appear before the player reaches them.")]
        [Range(0, 2)] public int visualReadyRangeBonus = 2;

        [Header("Vehicle Predictive Streaming")]
        [Tooltip("Extra nearby radius while the tracked target is moving at vehicle speed.")]
        [Range(0, 2)] public int vehicleRangeBonus = 1;
        [Tooltip("Speed in m/s where the vehicle range bonus is applied.")]
        [Min(0f)] public float vehicleStreamingSpeed = 8f;
        [Tooltip("Forward range used once the tracked target is moving quickly enough to outrun the base radius.")]
        [Range(0, 6)] public int highSpeedForwardExtraRange = 6;
        [Tooltip("Speed in m/s where high-speed forward prediction reaches full strength.")]
        [Min(0f)] public float highSpeedStreamingSpeed = 18f;
        [Tooltip("Maximum chunk loads started per streaming refresh. This bounds bursts when the requested range expands.")]
        [Min(1)] public int maxChunkQueueAddsPerRefresh = 18;

        [Header("Distant Terrain Proxy")]
        [Tooltip("Creates a low-cost terrain mesh under streamed chunks so very distant terrain is visible immediately while real chunks load.")]
        public bool enableDistantTerrainProxy = true;
        [Tooltip("How many chunk rings the far terrain proxy covers from the tracked target.")]
        [Range(8, 96)] public int distantTerrainRange = 64;
        [Tooltip("Low-detail mesh subdivisions per chunk for the far terrain proxy.")]
        [Range(1, 6)] public int distantTerrainSamplesPerChunk = 2;
        [Tooltip("Drops the far proxy slightly under real chunks to prevent z-fighting when detailed chunks arrive.")]
        [Min(0f)] public float distantTerrainVerticalOffset = 1.25f;
        [Tooltip("Minimum camera far clip plane while distant terrain is enabled.")]
        [Min(1000f)] public float distantTerrainCameraFarClip = 12000f;

        private HashSet<string> _requestedChunks = new HashSet<string>();
        private HashSet<string> _loadedChunks = new HashSet<string>();
        private Dictionary<string, Coroutine> _unloadRoutines = new Dictionary<string, Coroutine>();
        // Pending scene load queue for throttling.
        private Queue<string> _loadQueue = new Queue<string>();
        private HashSet<string> _queuedChunks = new HashSet<string>();
        private readonly List<string> _requiredChunks = new List<string>(49);
        private readonly List<string> _chunksToUnload = new List<string>(49);
        private readonly List<string> _queuedChunkBuffer = new List<string>(49);
        private readonly HashSet<string> _queuedChunkBufferSet = new HashSet<string>();
        private readonly HashSet<string> _visuallyReadyChunks = new HashSet<string>();
        private readonly HashSet<string> _visibleRequestedChunks = new HashSet<string>();
        private readonly List<Vector2Int> _streamingOffsets = new List<Vector2Int>(49);
        private readonly HashSet<Vector2Int> _streamingOffsetSet = new HashSet<Vector2Int>();
        private int _activeLoads = 0;
        private int _cachedOffsetRange = int.MinValue;
        private int _cachedForwardExtraRange = int.MinValue;
        private int _cachedForwardBandHalfWidth = int.MinValue;
        private Vector2Int _cachedForwardGrid = new Vector2Int(int.MinValue, int.MinValue);
        private Vector2Int _lastForwardGrid = new Vector2Int(int.MinValue, int.MinValue);
        private Coroutine _drainLoadQueueRoutine;
        private float _nextLoadStartTime;
        private float _nextLoadActivationTime;
        private bool _hasDeferredChunkLoads;
        private float _cachedLoadStartWaitInterval = -1f;
        private WaitForSeconds _cachedLoadStartWait;
        private float _cachedUnloadWaitDelay = -1f;
        private WaitForSeconds _cachedUnloadWait;

        // Cache DesertTerrainChunk size so it is not recalculated every streaming check.
        private float _chunkSizeCacheTime = -99f;
        private const float ChunkSizeCacheInterval = 5f;

        private float _nextCheckTime;
        private int _lastGridX = int.MinValue;
        private int _lastGridZ = int.MinValue;
        private float _nextTargetRefreshTime = -1f;
        private const float TargetRefreshInterval = 0.75f;
        private Vector3 _lastTargetPosition;
        private bool _hasLastTargetPosition;
        private float _lastTargetSpeed;
        private GameObject _cachedPlayer;
        private Renderer _cachedPlayerRenderer;
        private RVSystem.RVController _cachedRv;
        private RVSystem.RVStateMachine _cachedRvStateMachine;
        private Camera _cachedCamera;
        private GameObject _distantTerrainObject;
        private MeshFilter _distantTerrainFilter;
        private MeshRenderer _distantTerrainRenderer;
        private Mesh _distantTerrainMesh;
        private DesertTerrainChunk _distantTerrainHeightSource;
        private int _lastDistantGridX = int.MinValue;
        private int _lastDistantGridZ = int.MinValue;
        private int _lastDistantTerrainRange = int.MinValue;
        private int _lastDistantTerrainSamplesPerChunk = int.MinValue;
        private float _lastDistantChunkSizeX = -1f;
        private float _lastDistantChunkSizeZ = -1f;
        private DesertTerrainChunk _lastDistantTerrainHeightSource;

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
            ApplyRuntimeVisibilityMinimums();
            // Initial trigger will be fired automatically in the first Update frame
            _nextCheckTime = 0f;
        }

        private void OnDestroy()
        {
            if (Instance == this)
            {
                Instance = null;
            }

            if (_distantTerrainObject != null)
            {
                if (Application.isPlaying)
                {
                    Destroy(_distantTerrainObject);
                }
                else
                {
                    DestroyImmediate(_distantTerrainObject);
                }

                _distantTerrainObject = null;
            }
        }

        private void Update()
        {
            if (!useGridStreaming) return;

            float now = Time.time;
            bool shouldRunScheduledCheck = now >= _nextCheckTime;
            if (shouldRunScheduledCheck)
            {
                _nextCheckTime = now + checkInterval;
                RefreshTrackingTarget();
            }
            else if (trackingTarget == null)
            {
                return;
            }

            // 2. Perform grid projection and load surrounding chunks based on loadingRange
            if (trackingTarget != null)
            {
                // Refresh chunk size from the active chunk registry without a scene-wide search.
                if (shouldRunScheduledCheck && now - _chunkSizeCacheTime > ChunkSizeCacheInterval)
                {
                    _chunkSizeCacheTime = now;
                    TryRefreshChunkSizeFromLoadedChunk();
                }

                if (chunkSizeX <= 0.1f) chunkSizeX = 256f;
                if (chunkSizeZ <= 0.1f) chunkSizeZ = 256f;

                Vector3 pos = trackingTarget.position;
                int gridX = Mathf.RoundToInt(pos.x / chunkSizeX);
                int gridZ = Mathf.RoundToInt(pos.z / chunkSizeZ);
                Vector2Int forwardGrid = ResolveForwardGridDirection(pos, shouldRunScheduledCheck);
                EnsureDistantTerrainProxy(gridX, gridZ);

                if (gridX != _lastGridX ||
                    gridZ != _lastGridZ ||
                    forwardGrid != _lastForwardGrid ||
                    (shouldRunScheduledCheck && _hasDeferredChunkLoads))
                {
                    _lastGridX = gridX;
                    _lastGridZ = gridZ;
                    _lastForwardGrid = forwardGrid;
                    UpdateGridChunks(gridX, gridZ, forwardGrid);
                }
            }

        }

        private void ApplyRuntimeVisibilityMinimums()
        {
            if (!useGridStreaming)
            {
                return;
            }

            loadingRange = Mathf.Max(loadingRange, MinimumRuntimeLoadingRange);
            minimumWebGLLoadingRange = Mathf.Max(minimumWebGLLoadingRange, MinimumRuntimeLoadingRange);
            maxConcurrentLoads = Mathf.Max(maxConcurrentLoads, MinimumRuntimeConcurrentLoads);
            forwardExtraRange = Mathf.Max(forwardExtraRange, MinimumRuntimeForwardExtraRange);
            visualReadyRangeBonus = Mathf.Max(visualReadyRangeBonus, MinimumRuntimeVisualReadyRangeBonus);
            highSpeedForwardExtraRange = Mathf.Max(highSpeedForwardExtraRange, MinimumRuntimeHighSpeedForwardRange);
            enableDistantTerrainProxy = true;
            distantTerrainRange = Mathf.Max(distantTerrainRange, MinimumRuntimeDistantTerrainRange);
            distantTerrainSamplesPerChunk = Mathf.Max(distantTerrainSamplesPerChunk, MinimumRuntimeDistantSamplesPerChunk);
            distantTerrainCameraFarClip = Mathf.Max(distantTerrainCameraFarClip, MinimumRuntimeDistantCameraFarClip);
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

        private Vector2Int ResolveForwardGridDirection(Vector3 currentPosition, bool updateVelocitySample)
        {
            Vector3 movement = Vector3.zero;
            if (_hasLastTargetPosition)
                movement = currentPosition - _lastTargetPosition;

            if (updateVelocitySample || !_hasLastTargetPosition)
            {
                float sampleDelta = Mathf.Max(0.001f, checkInterval);
                _lastTargetSpeed = _hasLastTargetPosition ? movement.magnitude / sampleDelta : 0f;
                _lastTargetPosition = currentPosition;
                _hasLastTargetPosition = true;
            }

            Vector3 direction = ResolveCameraForward();
            if (direction.sqrMagnitude < 0.001f && trackingTarget != null)
            {
                direction = trackingTarget.forward;
            }

            direction.y = 0f;
            if (direction.sqrMagnitude < 0.001f)
                return Vector2Int.zero;

            direction.Normalize();
            int x = Mathf.Abs(direction.x) > 0.35f ? (direction.x > 0f ? 1 : -1) : 0;
            int z = Mathf.Abs(direction.z) > 0.35f ? (direction.z > 0f ? 1 : -1) : 0;

            if (x == 0 && z == 0)
            {
                if (Mathf.Abs(direction.x) >= Mathf.Abs(direction.z))
                    x = direction.x >= 0f ? 1 : -1;
                else
                    z = direction.z >= 0f ? 1 : -1;
            }

            return new Vector2Int(x, z);
        }

        private Vector3 ResolveCameraForward()
        {
            if (_cachedCamera == null || !_cachedCamera.gameObject.activeInHierarchy)
            {
                _cachedCamera = Camera.main;
            }

            return _cachedCamera != null
                ? Vector3.ProjectOnPlane(_cachedCamera.transform.forward, Vector3.up)
                : Vector3.zero;
        }

        private void UpdateGridChunks(int centerGridX, int centerGridZ, Vector2Int forwardGrid)
        {
            int range = ResolveEffectiveLoadingRange();
            int extraRange = ResolveEffectiveForwardRange(forwardGrid);
            if (Application.platform == RuntimePlatform.WebGLPlayer)
            {
                range = Mathf.Max(1, minimumWebGLLoadingRange, loadingRange);
                extraRange = Mathf.Max(0, forwardExtraRange);
            }

            EnsureStreamingOffsets(range, extraRange, forwardGrid);

            _requiredChunks.Clear();
            _visuallyReadyChunks.Clear();
            int visualReadyRange = range + Mathf.Max(0, visualReadyRangeBonus);
            for (int i = 0; i < _streamingOffsets.Count; i++)
            {
                Vector2Int offset = _streamingOffsets[i];
                int gx = centerGridX + offset.x;
                int gz = centerGridZ + offset.y;
                string sceneName = $"{sceneNamePrefix}_{gx}_{gz}";
                _requiredChunks.Add(sceneName);

                if (Mathf.Abs(offset.x) <= visualReadyRange && Mathf.Abs(offset.y) <= visualReadyRange)
                {
                    _visuallyReadyChunks.Add(sceneName);
                }
            }

            int gridSizeDim = range * 2 + 1;
            if (verboseLogging)
            {
                Debug.Log($"<color=#38bdf8><b>[WorldStreamer]</b></color> Grid updated. Center ({centerGridX}, {centerGridZ}). Loading {gridSizeDim}x{gridSizeDim} base grid plus {extraRange} forward chunks toward {forwardGrid}.");
            }
            RequestChunks(_requiredChunks);
            ApplyVisualReadinessToLoadedChunks();
        }

        private int ResolveEffectiveLoadingRange()
        {
            int range = Mathf.Max(1, loadingRange);
            if (_lastTargetSpeed >= vehicleStreamingSpeed)
            {
                range += vehicleRangeBonus;
            }

            return range;
        }

        private int ResolveEffectiveForwardRange(Vector2Int forwardGrid)
        {
            if (forwardGrid == Vector2Int.zero)
            {
                return 0;
            }

            int extraRange = Mathf.Max(0, forwardExtraRange);
            if (_lastTargetSpeed >= vehicleStreamingSpeed)
            {
                float speed01 = Mathf.InverseLerp(vehicleStreamingSpeed, Mathf.Max(vehicleStreamingSpeed + 0.1f, highSpeedStreamingSpeed), _lastTargetSpeed);
                int speedRange = Mathf.RoundToInt(Mathf.Lerp(extraRange, highSpeedForwardExtraRange, speed01));
                extraRange = Mathf.Max(extraRange, speedRange);
            }

            return extraRange;
        }

        private void EnsureStreamingOffsets(int range, int extraRange, Vector2Int forwardGrid)
        {
            int bandHalfWidth = forwardGrid == Vector2Int.zero ? 0 : forwardBandHalfWidth;
            if (_cachedOffsetRange == range &&
                _cachedForwardExtraRange == extraRange &&
                _cachedForwardBandHalfWidth == bandHalfWidth &&
                _cachedForwardGrid == forwardGrid)
            {
                return;
            }

            _cachedOffsetRange = range;
            _cachedForwardExtraRange = extraRange;
            _cachedForwardBandHalfWidth = bandHalfWidth;
            _cachedForwardGrid = forwardGrid;
            _streamingOffsets.Clear();
            _streamingOffsetSet.Clear();

            for (int dx = -range; dx <= range; dx++)
            {
                for (int dz = -range; dz <= range; dz++)
                {
                    AddStreamingOffset(new Vector2Int(dx, dz));
                }
            }

            if (extraRange > 0 && forwardGrid != Vector2Int.zero)
            {
                Vector2Int lateral = new Vector2Int(-forwardGrid.y, forwardGrid.x);
                for (int distance = range + 1; distance <= range + extraRange; distance++)
                {
                    for (int side = -bandHalfWidth; side <= bandHalfWidth; side++)
                    {
                        AddStreamingOffset(new Vector2Int(
                            forwardGrid.x * distance + lateral.x * side,
                            forwardGrid.y * distance + lateral.y * side));
                    }
                }
            }

            _streamingOffsets.Sort((a, b) =>
                ScoreStreamingOffset(a, forwardGrid).CompareTo(ScoreStreamingOffset(b, forwardGrid)));
        }

        private void AddStreamingOffset(Vector2Int offset)
        {
            if (_streamingOffsetSet.Add(offset))
                _streamingOffsets.Add(offset);
        }

        private float ScoreStreamingOffset(Vector2Int offset, Vector2Int forwardGrid)
        {
            float distanceScore = offset.x * offset.x + offset.y * offset.y;
            if (forwardGrid == Vector2Int.zero)
                return distanceScore;

            float forwardScore = offset.x * forwardGrid.x + offset.y * forwardGrid.y;
            return distanceScore - forwardScore * forwardPriorityBias;
        }

        public void RequestChunks(List<string> chunkSceneNames)
        {
            _requestedChunks.Clear();
            int queuedThisRefresh = 0;
            int queueBudget = Mathf.Max(1, maxChunkQueueAddsPerRefresh);
            _hasDeferredChunkLoads = false;
            foreach (var chunk in chunkSceneNames)
            {
                _requestedChunks.Add(chunk);
                if (queuedThisRefresh >= queueBudget &&
                    !_loadedChunks.Contains(chunk) &&
                    !_queuedChunks.Contains(chunk))
                {
                    _hasDeferredChunkLoads = true;
                    continue;
                }

                if (LoadChunk(chunk))
                {
                    queuedThisRefresh++;
                }
            }

            ReprioritizeLoadQueue(chunkSceneNames);
            PruneVisibleRequestedChunks();

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

        private void ReprioritizeLoadQueue(List<string> priorityOrder)
        {
            if (_loadQueue.Count <= 1)
            {
                return;
            }

            _queuedChunkBuffer.Clear();
            _queuedChunkBufferSet.Clear();
            while (_loadQueue.Count > 0)
            {
                string chunkName = _loadQueue.Dequeue();
                _queuedChunkBuffer.Add(chunkName);
                _queuedChunkBufferSet.Add(chunkName);
            }

            _queuedChunks.Clear();
            for (int i = 0; i < priorityOrder.Count; i++)
            {
                string chunkName = priorityOrder[i];
                if (!_queuedChunkBufferSet.Contains(chunkName))
                {
                    continue;
                }

                _loadQueue.Enqueue(chunkName);
                _queuedChunks.Add(chunkName);
            }

            for (int i = 0; i < _queuedChunkBuffer.Count; i++)
            {
                string chunkName = _queuedChunkBuffer[i];
                if (_queuedChunks.Contains(chunkName))
                {
                    continue;
                }

                if (_requestedChunks.Contains(chunkName))
                {
                    _loadQueue.Enqueue(chunkName);
                    _queuedChunks.Add(chunkName);
                }
                else
                {
                    _loadedChunks.Remove(chunkName);
                }
            }
        }

        private bool LoadChunk(string chunkName)
        {
            if (string.IsNullOrEmpty(chunkName)) return false;

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
                return true;
            }

            return false;
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
                    while (Time.time < _nextLoadStartTime)
                    {
                        yield return null;
                    }
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
                    yield return GetLoadStartWait();
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
                while (Time.time < _nextLoadActivationTime)
                {
                    yield return null;
                }
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

            ApplyVisualReadinessToChunk(sceneName);

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
            yield return GetUnloadWait();

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

        private WaitForSeconds GetLoadStartWait()
        {
            float interval = Mathf.Max(0f, loadStartInterval);
            if (_cachedLoadStartWait == null || !Mathf.Approximately(_cachedLoadStartWaitInterval, interval))
            {
                _cachedLoadStartWaitInterval = interval;
                _cachedLoadStartWait = new WaitForSeconds(interval);
            }

            return _cachedLoadStartWait;
        }

        private WaitForSeconds GetUnloadWait()
        {
            float delay = Mathf.Max(0f, unloadDelay);
            if (_cachedUnloadWait == null || !Mathf.Approximately(_cachedUnloadWaitDelay, delay))
            {
                _cachedUnloadWaitDelay = delay;
                _cachedUnloadWait = new WaitForSeconds(delay);
            }

            return _cachedUnloadWait;
        }

        private void ApplyVisualReadinessToLoadedChunks()
        {
            foreach (var kv in ChunkRegistry.All)
            {
                DesertTerrainChunk chunk = kv.Value;
                if (chunk == null) continue;

                string sceneName = $"{sceneNamePrefix}_{kv.Key.x}_{kv.Key.y}";
                chunk.SetStreamedVisualReady(IsChunkVisuallyReady(sceneName));
            }
        }

        public void ApplyVisualReadinessToRegisteredChunk(DesertTerrainChunk chunk)
        {
            if (chunk == null)
            {
                return;
            }

            Vector2Int coord = chunk.GridCoord;
            string sceneName = $"{sceneNamePrefix}_{coord.x}_{coord.y}";
            chunk.SetStreamedVisualReady(IsChunkVisuallyReady(sceneName));
        }

        private void ApplyVisualReadinessToChunk(string sceneName)
        {
            if (!TryParseSceneGrid(sceneName, out Vector2Int coord))
            {
                return;
            }

            DesertTerrainChunk chunk = ChunkRegistry.Get(coord);
            if (chunk != null)
            {
                chunk.SetStreamedVisualReady(IsChunkVisuallyReady(sceneName));
            }
        }

        private void EnsureDistantTerrainProxy(int centerGridX, int centerGridZ)
        {
            if (!enableDistantTerrainProxy)
            {
                if (_distantTerrainObject != null)
                {
                    _distantTerrainObject.SetActive(false);
                }
                return;
            }

            EnsureCameraFarClip();
            EnsureDistantTerrainComponents();
            if (_distantTerrainObject == null || _distantTerrainFilter == null || _distantTerrainRenderer == null)
            {
                return;
            }

            _distantTerrainObject.SetActive(true);
            RefreshDistantTerrainHeightSource();
            RefreshDistantTerrainMaterial();

            int range = GetEffectiveDistantTerrainRange();
            int samplesPerChunk = Mathf.Max(1, distantTerrainSamplesPerChunk);
            bool needsRebuild =
                _distantTerrainMesh == null ||
                centerGridX != _lastDistantGridX ||
                centerGridZ != _lastDistantGridZ ||
                range != _lastDistantTerrainRange ||
                samplesPerChunk != _lastDistantTerrainSamplesPerChunk ||
                !Mathf.Approximately(chunkSizeX, _lastDistantChunkSizeX) ||
                !Mathf.Approximately(chunkSizeZ, _lastDistantChunkSizeZ) ||
                _distantTerrainHeightSource != _lastDistantTerrainHeightSource;

            if (!needsRebuild)
            {
                return;
            }

            BuildDistantTerrainMesh(centerGridX, centerGridZ, range, samplesPerChunk);
            _lastDistantGridX = centerGridX;
            _lastDistantGridZ = centerGridZ;
            _lastDistantTerrainRange = range;
            _lastDistantTerrainSamplesPerChunk = samplesPerChunk;
            _lastDistantChunkSizeX = chunkSizeX;
            _lastDistantChunkSizeZ = chunkSizeZ;
            _lastDistantTerrainHeightSource = _distantTerrainHeightSource;
        }

        private int GetEffectiveDistantTerrainRange()
        {
            int range = Mathf.Max(1, distantTerrainRange);
            float chunkSpan = Mathf.Max(chunkSizeX, chunkSizeZ);
            if (chunkSpan > 0.1f)
            {
                range = Mathf.Max(range, Mathf.CeilToInt(MinimumRuntimeDistantTerrainCoverageMeters / chunkSpan));
            }

            return range;
        }

        private void EnsureCameraFarClip()
        {
            if (_cachedCamera == null || !_cachedCamera.gameObject.activeInHierarchy)
            {
                _cachedCamera = Camera.main;
            }

            if (_cachedCamera != null && _cachedCamera.farClipPlane < distantTerrainCameraFarClip)
            {
                _cachedCamera.farClipPlane = distantTerrainCameraFarClip;
            }
        }

        private void EnsureDistantTerrainComponents()
        {
            if (_distantTerrainObject != null)
            {
                return;
            }

            _distantTerrainObject = new GameObject("DistantTerrainProxy");
            _distantTerrainObject.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            _distantTerrainObject.transform.localScale = Vector3.one;
            _distantTerrainFilter = _distantTerrainObject.AddComponent<MeshFilter>();
            _distantTerrainRenderer = _distantTerrainObject.AddComponent<MeshRenderer>();
            _distantTerrainRenderer.shadowCastingMode = ShadowCastingMode.Off;
            _distantTerrainRenderer.receiveShadows = false;
            _distantTerrainRenderer.motionVectorGenerationMode = MotionVectorGenerationMode.ForceNoMotion;
        }

        private void RefreshDistantTerrainHeightSource()
        {
            if (_distantTerrainHeightSource != null && _distantTerrainHeightSource.gameObject.activeInHierarchy)
            {
                return;
            }

            _distantTerrainHeightSource = null;
            foreach (var kv in ChunkRegistry.All)
            {
                DesertTerrainChunk chunk = kv.Value;
                if (chunk == null || !chunk.gameObject.activeInHierarchy)
                {
                    continue;
                }

                _distantTerrainHeightSource = chunk;
                return;
            }
        }

        private void RefreshDistantTerrainMaterial()
        {
            if (_distantTerrainRenderer == null)
            {
                return;
            }

            Material material = _distantTerrainHeightSource != null
                ? _distantTerrainHeightSource.terrainMaterial
                : null;
            if (material == null)
            {
                material = Resources.Load<Material>("GravelTerrain");
            }

            if (material != null && _distantTerrainRenderer.sharedMaterial != material)
            {
                _distantTerrainRenderer.sharedMaterial = material;
            }
        }

        private void BuildDistantTerrainMesh(int centerGridX, int centerGridZ, int range, int samplesPerChunk)
        {
            int chunksWide = range * 2;
            int quadsX = Mathf.Max(2, chunksWide * samplesPerChunk);
            int quadsZ = Mathf.Max(2, chunksWide * samplesPerChunk);
            int vertsX = quadsX + 1;
            int vertsZ = quadsZ + 1;
            int vertexCount = vertsX * vertsZ;

            Vector3[] vertices = new Vector3[vertexCount];
            Vector2[] uvs = new Vector2[vertexCount];
            int[] triangles = new int[quadsX * quadsZ * 6];

            float startX = (centerGridX - range) * chunkSizeX;
            float startZ = (centerGridZ - range) * chunkSizeZ;
            float stepX = chunkSizeX / samplesPerChunk;
            float stepZ = chunkSizeZ / samplesPerChunk;

            for (int z = 0; z < vertsZ; z++)
            {
                float worldZ = startZ + z * stepZ;
                for (int x = 0; x < vertsX; x++)
                {
                    float worldX = startX + x * stepX;
                    float height = SampleDistantTerrainHeight(worldX, worldZ) - distantTerrainVerticalOffset;
                    int index = z * vertsX + x;
                    vertices[index] = new Vector3(worldX, height, worldZ);
                    uvs[index] = new Vector2((float)x / quadsX, (float)z / quadsZ);
                }
            }

            int t = 0;
            for (int z = 0; z < quadsZ; z++)
            {
                for (int x = 0; x < quadsX; x++)
                {
                    int bl = z * vertsX + x;
                    int br = bl + 1;
                    int tl = bl + vertsX;
                    int tr = tl + 1;

                    triangles[t++] = bl;
                    triangles[t++] = tl;
                    triangles[t++] = tr;
                    triangles[t++] = bl;
                    triangles[t++] = tr;
                    triangles[t++] = br;
                }
            }

            if (_distantTerrainMesh == null)
            {
                _distantTerrainMesh = new Mesh { name = "DistantTerrainProxyMesh" };
            }
            else
            {
                _distantTerrainMesh.Clear();
            }

            if (vertexCount > 65535)
            {
                _distantTerrainMesh.indexFormat = IndexFormat.UInt32;
            }

            _distantTerrainMesh.vertices = vertices;
            _distantTerrainMesh.uv = uvs;
            _distantTerrainMesh.triangles = triangles;
            _distantTerrainMesh.RecalculateNormals();
            _distantTerrainMesh.RecalculateBounds();
            _distantTerrainFilter.sharedMesh = _distantTerrainMesh;
        }

        private float SampleDistantTerrainHeight(float worldX, float worldZ)
        {
            if (_distantTerrainHeightSource != null)
            {
                return _distantTerrainHeightSource.transform.position.y +
                    _distantTerrainHeightSource.SampleHeight(worldX, worldZ);
            }

            return Mathf.PerlinNoise(worldX / 220f, worldZ / 220f) * 10f;
        }

        private bool IsChunkVisuallyReady(string sceneName)
        {
            if (string.IsNullOrEmpty(sceneName))
            {
                return false;
            }

            if (_visuallyReadyChunks.Count == 0)
            {
                return true;
            }

            if (_visibleRequestedChunks.Contains(sceneName))
            {
                return true;
            }

            if (_unloadRoutines.ContainsKey(sceneName))
            {
                return true;
            }

            bool ready = _visuallyReadyChunks.Contains(sceneName);
            if (ready)
            {
                _visibleRequestedChunks.Add(sceneName);
            }

            return ready;
        }

        private void PruneVisibleRequestedChunks()
        {
            if (_visibleRequestedChunks.Count == 0)
            {
                return;
            }

            _queuedChunkBuffer.Clear();
            foreach (string sceneName in _visibleRequestedChunks)
            {
                if (!_requestedChunks.Contains(sceneName))
                {
                    _queuedChunkBuffer.Add(sceneName);
                }
            }

            for (int i = 0; i < _queuedChunkBuffer.Count; i++)
            {
                _visibleRequestedChunks.Remove(_queuedChunkBuffer[i]);
            }

            _queuedChunkBuffer.Clear();
        }

        private bool TryParseSceneGrid(string sceneName, out Vector2Int coord)
        {
            coord = Vector2Int.zero;
            if (string.IsNullOrEmpty(sceneName))
            {
                return false;
            }

            string prefix = sceneNamePrefix + "_";
            if (!sceneName.StartsWith(prefix))
            {
                return false;
            }

            string[] parts = sceneName.Substring(prefix.Length).Split('_');
            if (parts.Length != 2 ||
                !int.TryParse(parts[0], out int x) ||
                !int.TryParse(parts[1], out int z))
            {
                return false;
            }

            coord = new Vector2Int(x, z);
            return true;
        }
    }
}
