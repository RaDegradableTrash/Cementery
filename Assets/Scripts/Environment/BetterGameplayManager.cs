using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace EnvironmentSystem
{
    /// <summary>
    /// Global always-on manager that automatically discovers and optimizes ALL GameObjects
    /// in every loaded scene, except:
    ///   1. Objects in this manager's own scene (the persistent/main scene).
    ///   2. Objects that are terrain roots: DesertTerrainChunk or Unity Terrain components.
    ///      Their children are also excluded (they are part of the ground mesh).
    ///
    /// No manual setup required. OptimizableObject is added dynamically at runtime.
    ///
    /// Place this on a GameObject in the Main_Persistent scene. It will survive scene loads
    /// because it lives in the persistent scene (no DontDestroyOnLoad needed).
    /// </summary>
    public class BetterGameplayManager : MonoBehaviour
    {
        public static BetterGameplayManager Instance { get; private set; }

        [Header("Distance Culling")]
        [Tooltip("Objects beyond this distance from the player will be hidden.")]
        public float defaultVisibilityRadius = 150f;

        [Tooltip("Hysteresis band to prevent flickering at the boundary.")]
        public float hysteresis = 15f;

        [Header("Performance")]
        [Tooltip("How many objects are checked per frame. Lower = smoother, Higher = faster response.")]
        public int objectsCheckedPerFrame = 300;

        [Tooltip("Delay (frames) after a scene loads before scanning it, to let Awake/Start settle.")]
        public int scanDelayFrames = 3;

        [Header("Debug")]
        [Tooltip("Log scan results to the Console.")]
        public bool verboseLogging = false;

        // ── Internal state ─────────────────────────────────────────────────────

        private readonly List<OptimizableObject> _managedObjects = new List<OptimizableObject>(512);
        private int _currentIndex = 0;

        private Camera _mainCamera;
        private Transform _playerTransform;
        private readonly Plane[] _frustumPlanes = new Plane[6];

        // Name of the scene this BetterGameplayManager lives in — objects here are NEVER managed.
        private string _ownSceneName;

        // Objects closer than this are never frustum-culled (prevents pop-in on turn)
        private const float NeverFrustumCullRadius = 25f;

        // ── Unity lifecycle ────────────────────────────────────────────────────

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;
            _ownSceneName = gameObject.scene.name;
        }

        private void OnEnable()
        {
            SceneManager.sceneLoaded   += OnSceneLoaded;
            SceneManager.sceneUnloaded += OnSceneUnloaded;
        }

        private void OnDisable()
        {
            SceneManager.sceneLoaded   -= OnSceneLoaded;
            SceneManager.sceneUnloaded -= OnSceneUnloaded;
        }

        private void Start()
        {
            FindPlayerAndCamera();
            // Scan all scenes that were already loaded before this manager started
            // (e.g. editor play mode where all scenes are open)
            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                Scene s = SceneManager.GetSceneAt(i);
                if (s.isLoaded && s.name != _ownSceneName)
                {
                    StartCoroutine(ScanSceneDelayed(s, 1));
                }
            }
        }

        private void Update()
        {
            if (_managedObjects.Count == 0) return;

            // Re-find player/camera lazily (handles RV ↔ Player switching)
            if (_mainCamera == null || _playerTransform == null || !_playerTransform.gameObject.activeInHierarchy)
            {
                FindPlayerAndCamera();
            }

            if (_mainCamera != null)
                GeometryUtility.CalculateFrustumPlanes(_mainCamera, _frustumPlanes);

            Vector3 playerPos = _playerTransform != null ? _playerTransform.position : Vector3.zero;

            int limit = Mathf.Min(objectsCheckedPerFrame, _managedObjects.Count);
            float hideDistSq = (defaultVisibilityRadius + hysteresis) * (defaultVisibilityRadius + hysteresis);
            float showDistSq = defaultVisibilityRadius * defaultVisibilityRadius;
            float neverCullDistSq = NeverFrustumCullRadius * NeverFrustumCullRadius;

            for (int i = 0; i < limit; i++)
            {
                if (_currentIndex >= _managedObjects.Count)
                    _currentIndex = 0;

                OptimizableObject obj = _managedObjects[_currentIndex];
                if (obj == null || obj.isDestroyed)
                {
                    // Clean up destroyed references
                    _managedObjects.RemoveAt(_currentIndex);
                    if (_currentIndex >= _managedObjects.Count)
                        _currentIndex = 0;
                    continue;
                }

                ProcessObject(obj, playerPos, showDistSq, hideDistSq, neverCullDistSq);
                _currentIndex++;
            }
        }

        // ── Scene event handlers ───────────────────────────────────────────────

        private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            if (scene.name == _ownSceneName) return;
            StartCoroutine(ScanSceneDelayed(scene, scanDelayFrames));
        }

        private void OnSceneUnloaded(Scene scene)
        {
            // OptimizableObject.OnDestroy will unregister itself; nothing extra needed.
            // But do a fast purge of null entries to keep the list tidy.
            _managedObjects.RemoveAll(o => o == null || o.isDestroyed);
            if (_currentIndex >= _managedObjects.Count)
                _currentIndex = 0;
        }

        // ── Registration (called by OptimizableObject) ─────────────────────────

        public void Register(OptimizableObject obj)
        {
            if (!_managedObjects.Contains(obj))
                _managedObjects.Add(obj);
        }

        public void Unregister(OptimizableObject obj)
        {
            int index = _managedObjects.IndexOf(obj);
            if (index == -1) return;

            _managedObjects.RemoveAt(index);

            if (index < _currentIndex)
                _currentIndex--;

            if (_currentIndex >= _managedObjects.Count)
                _currentIndex = 0;
        }

        // ── Auto-scanning ──────────────────────────────────────────────────────

        private IEnumerator ScanSceneDelayed(Scene scene, int delayFrames)
        {
            for (int i = 0; i < delayFrames; i++)
                yield return null;

            // Scene may have been unloaded during the delay
            if (!scene.isLoaded) yield break;

            ScanScene(scene);
        }

        /// <summary>
        /// Walk every root GameObject in a scene.
        /// Skip terrain roots (DesertTerrainChunk / Terrain).
        /// For all others, ensure every child (and the root itself) that has a Renderer
        /// gets an OptimizableObject so it is distance-culled by this manager.
        /// </summary>
        private void ScanScene(Scene scene)
        {
            if (!scene.isLoaded) return;

            GameObject[] roots = scene.GetRootGameObjects();
            int added = 0;

            foreach (GameObject root in roots)
            {
                // Skip terrain roots and explicitly excluded objects
                if (ShouldSkipRoot(root)) continue;

                // Scan all renderable children (including root itself)
                added += EnsureOptimizableRecursive(root);
            }

            if (verboseLogging)
                Debug.Log($"[BetterGameplayManager] Scanned scene '{scene.name}': {added} objects registered. Total: {_managedObjects.Count}");
        }

        /// <summary>
        /// Returns true if this root GameObject should be completely excluded from management.
        /// This covers terrain meshes and any object explicitly marked ExcludeFromOptimization.
        /// </summary>
        private static bool ShouldSkipRoot(GameObject root)
        {
            // Unity built-in Terrain component
            if (root.GetComponent<Terrain>() != null) return true;
            // Our custom procedural terrain chunk
            if (root.GetComponent<DesertTerrainChunk>() != null) return true;
            // Explicitly excluded objects (player, RV, held items, etc.)
            if (root.GetComponent<ExcludeFromOptimization>() != null) return true;
            return false;
        }

        /// <summary>
        /// Recursively walk the transform hierarchy.
        /// Strategy: add ONE OptimizableObject per distinct renderable sub-hierarchy root
        /// to keep the managed list lean. A "renderable sub-hierarchy root" is any Transform
        /// that has a Renderer itself OR has Renderer descendants.
        ///
        /// For simplicity and correctness we add OptimizableObject directly on the root
        /// of each scene root (one entry per root object). This is fine because:
        ///  - The root bounds encapsulate all children for frustum tests.
        ///  - disableEntireGameObject = true hides the whole hierarchy at once.
        /// </summary>
        private int EnsureOptimizableRecursive(GameObject root)
        {
            // Only manage objects that have at least one Renderer somewhere in the hierarchy
            if (root.GetComponentInChildren<Renderer>(true) == null) return 0;

            // Skip if explicitly excluded
            if (root.GetComponent<ExcludeFromOptimization>() != null) return 0;

            // Add OptimizableObject to the root if not already present
            OptimizableObject opt = root.GetComponent<OptimizableObject>();
            if (opt == null)
            {
                opt = root.AddComponent<OptimizableObject>();
            }

            // Default: disable whole GO (cheapest) + frustum culling for static scene props.
            // Individual objects can override these flags in their own Awake().
            opt.disableEntireGameObject = true;
            opt.useFrustumCulling = true;

            // Force-register now (OnEnable may have already fired before this manager existed)
            Register(opt);
            return 1;
        }

        // ── Per-object culling logic ───────────────────────────────────────────

        private void ProcessObject(OptimizableObject obj, Vector3 playerPos,
                                   float showDistSq, float hideDistSq, float neverCullDistSq)
        {
            float sqrDist = (obj.CachedPosition - playerPos).sqrMagnitude;
            bool withinDistance;

            if (obj.isHiddenByManager)
                withinDistance = sqrDist <= showDistSq;   // re-show when close enough
            else
                withinDistance = sqrDist <= hideDistSq;   // stay visible until far enough

            // Frustum culling — skipped for very close objects and for objects that opt out
            bool inFrustum = true;
            if (withinDistance && obj.useFrustumCulling && _mainCamera != null && sqrDist > neverCullDistSq)
            {
                inFrustum = obj.HasBounds
                    ? GeometryUtility.TestPlanesAABB(_frustumPlanes, obj.GetBounds())
                    : IsPointInFrustum(obj.CachedPosition);
            }

            bool shouldBeVisible = withinDistance && inFrustum;

            if (shouldBeVisible && obj.isHiddenByManager)
                obj.SetVisibility(true);
            else if (!shouldBeVisible && !obj.isHiddenByManager)
                obj.SetVisibility(false);
        }

        private bool IsPointInFrustum(Vector3 point)
        {
            for (int i = 0; i < _frustumPlanes.Length; i++)
            {
                if (_frustumPlanes[i].GetDistanceToPoint(point) < 0)
                    return false;
            }
            return true;
        }

        // ── Player / Camera discovery ──────────────────────────────────────────

        private void FindPlayerAndCamera()
        {
            _mainCamera = Camera.main;

            GameObject player = GameObject.FindGameObjectWithTag("Player");
            if (player != null && player.activeInHierarchy)
            {
                _playerTransform = player.transform;
                return;
            }

            var rv = FindObjectOfType<RVSystem.RVController>();
            if (rv != null && rv.gameObject.activeInHierarchy)
            {
                _playerTransform = rv.transform;
                return;
            }

            if (_mainCamera != null)
                _playerTransform = _mainCamera.transform;
        }

        // ── Editor helpers ────────────────────────────────────────────────────

#if UNITY_EDITOR
        private void OnDrawGizmosSelected()
        {
            Vector3 center = _playerTransform != null ? _playerTransform.position : transform.position;
            Gizmos.color = new Color(0.2f, 1f, 0.4f, 0.25f);
            Gizmos.DrawWireSphere(center, defaultVisibilityRadius);
            Gizmos.color = new Color(1f, 0.9f, 0.2f, 0.15f);
            Gizmos.DrawWireSphere(center, defaultVisibilityRadius + hysteresis);
        }
#endif
    }
}
