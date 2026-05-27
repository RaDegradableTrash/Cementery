using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace EnvironmentSystem
{
    public class WorldStreamer : MonoBehaviour
    {
        public static WorldStreamer Instance { get; private set; }

        [Header("General Settings")]
        [Tooltip("Delay in seconds before unloading a chunk to prevent thrashing at boundaries.")]
        public float unloadDelay = 5f;
        [Tooltip("Set to true to use automatic 7x7 grid coordinate-based loading (Option B). Set to false to use legacy ChunkTriggers.")]
        public bool useGridStreaming = true;

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

        private HashSet<string> _requestedChunks = new HashSet<string>();
        private HashSet<string> _loadedChunks = new HashSet<string>();
        private Dictionary<string, Coroutine> _unloadRoutines = new Dictionary<string, Coroutine>();
        // Pending scene load queue for throttling: max 2 concurrent loads to avoid stutter
        private Queue<string> _loadQueue = new Queue<string>();
        private int _activeLoads = 0;
        private const int MaxConcurrentLoads = 2;

        // Cache DesertTerrainChunk size so FindObjectOfType is not called every 0.5 s
        private float _chunkSizeCacheTime = -99f;
        private const float ChunkSizeCacheInterval = 5f;

        private float _nextCheckTime;
        private int _lastGridX = int.MinValue;
        private int _lastGridZ = int.MinValue;

        private void Awake()
        {
            if (Instance == null) Instance = this;
            else Destroy(gameObject);
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

            // 1. Auto-detect target if none assigned
            if (trackingTarget == null)
            {
                GameObject player = GameObject.FindGameObjectWithTag("Player");
                if (player != null)
                {
                    trackingTarget = player.transform;
                }
                else
                {
                    var rv = FindObjectOfType<RVSystem.RVController>();
                    if (rv != null)
                    {
                        trackingTarget = rv.transform;
                    }
                    else if (Camera.main != null)
                    {
                        trackingTarget = Camera.main.transform;
                    }
                }
            }

            // 2. Perform grid projection and load 7x7 surrounding chunks
            if (trackingTarget != null)
            {
                // Refresh chunk size from active chunk every ChunkSizeCacheInterval (not every frame!)
                if (Time.time - _chunkSizeCacheTime > ChunkSizeCacheInterval)
                {
                    _chunkSizeCacheTime = Time.time;
                    var activeChunk = FindObjectOfType<DesertTerrainChunk>();
                    if (activeChunk != null)
                    {
                        chunkSizeX = activeChunk.width * activeChunk.cellSize;
                        chunkSizeZ = activeChunk.depth * activeChunk.cellSize;
                    }
                }

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

        private void UpdateGridChunks(int centerGridX, int centerGridZ)
        {
            List<string> requiredList = new List<string>();

            // 5x5 grid (25 chunks) instead of 7x7 (49) for much better performance
            for (int dx = -2; dx <= 2; dx++)
            {
                for (int dz = -2; dz <= 2; dz++)
                {
                    int gx = centerGridX + dx;
                    int gz = centerGridZ + dz;
                    string sceneName = $"{sceneNamePrefix}_{gx}_{gz}";
                    requiredList.Add(sceneName);
                }
            }

            Debug.Log($"<color=#38bdf8><b>[WorldStreamer]</b></color> Grid updated! Center ({centerGridX}, {centerGridZ}). Loading 5x5 grid.");
            RequestChunks(requiredList);
        }

        public void RequestChunks(List<string> chunkSceneNames)
        {
            _requestedChunks.Clear();
            foreach (var chunk in chunkSceneNames)
            {
                _requestedChunks.Add(chunk);
                LoadChunk(chunk);
            }

            // Unload chunks that are no longer requested
            List<string> chunksToUnload = new List<string>();
            foreach (var loaded in _loadedChunks)
            {
                if (!_requestedChunks.Contains(loaded))
                {
                    chunksToUnload.Add(loaded);
                }
            }

            foreach (var chunk in chunksToUnload)
            {
                UnloadChunk(chunk);
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

            if (!_loadedChunks.Contains(chunkName) && !_loadQueue.Contains(chunkName))
            {
                _loadedChunks.Add(chunkName);
                // Throttle: enqueue, then drain up to MaxConcurrentLoads
                _loadQueue.Enqueue(chunkName);
                DrainLoadQueue();
            }
        }

        private void DrainLoadQueue()
        {
            while (_activeLoads < MaxConcurrentLoads && _loadQueue.Count > 0)
            {
                string next = _loadQueue.Dequeue();
                _activeLoads++;
                StartCoroutine(LoadSceneAsync(next));
            }
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
            AsyncOperation asyncLoad = SceneManager.LoadSceneAsync(sceneName, LoadSceneMode.Additive);
            if (asyncLoad == null)
            {
                Debug.LogWarning($"[WorldStreamer] Failed to load chunk {sceneName}. Check Build Settings!");
                _loadedChunks.Remove(sceneName);
                _activeLoads = Mathf.Max(0, _activeLoads - 1);
                DrainLoadQueue();
                yield break;
            }

            asyncLoad.allowSceneActivation = true;
            while (!asyncLoad.isDone)
            {
                yield return null;
            }
            Debug.Log($"[WorldStreamer] Loaded chunk: {sceneName}");
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
            if (asyncUnload == null) yield break;

            while (!asyncUnload.isDone)
            {
                yield return null;
            }

            _loadedChunks.Remove(sceneName);
            _unloadRoutines.Remove(sceneName);
            Debug.Log($"[WorldStreamer] Unloaded chunk: {sceneName}");
        }
    }
}
