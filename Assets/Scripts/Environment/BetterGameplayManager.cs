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

        [Header("Frustum Culling")]
        [Tooltip("Extra margin (metres) expanded around each object's bounds before frustum testing. " +
                 "Larger = less popping at screen edges, slight performance cost.")]
        public float frustumExpansion = 8f;

        [Tooltip("Objects closer than this distance are NEVER frustum-culled, regardless of direction. " +
                 "Prevents any pop-in in the player's immediate surroundings.")]
        public float neverFrustumCullRadius = 40f;

        [Header("Performance")]
        [Tooltip("How many objects are checked per frame in the normal incremental pass.")]
        public int objectsCheckedPerFrame = 500;

        [Tooltip("Extra pass: hidden objects that are within range get re-checked this many " +
                 "times per frame to reduce re-appearance latency. Keep <= objectsCheckedPerFrame.")]
        public int hiddenObjectFastRecheck = 200;

        [Tooltip("Delay (frames) after a scene loads before scanning it, to let Awake/Start settle.")]
        public int scanDelayFrames = 3;

        [Header("Debug")]
        [Tooltip("Log scan results to the Console.")]
        public bool verboseLogging = false;

        // ── Internal state ─────────────────────────────────────────────────────

        private readonly List<OptimizableObject> _managedObjects   = new List<OptimizableObject>(512);
        // Separate bucket of objects that are currently hidden but within load range — checked first.
        private readonly List<OptimizableObject> _hiddenNearby     = new List<OptimizableObject>(128);
        private int _currentIndex = 0;

        private Camera    _mainCamera;
        private Transform _playerTransform;
        private readonly Plane[] _frustumPlanes = new Plane[6];

        private Quaternion _lastCamRot;

        // Name of the scene this BetterGameplayManager lives in — objects here are NEVER managed.
        private string _ownSceneName;

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
            var go = new GameObject("DebugPhysics");
            go.AddComponent<DebugPhysicsState>();
            FindPlayerAndCamera();

            // Freeze ALL Rigidbodies in every already-loaded scene immediately (frame 0),
            // before physics runs. This prevents props rolling on slopes during the
            // scanDelayFrames window before KinematicProp is added by EnsureOptimizableRecursive.
            FreezeAllRigidbodiesImmediate();

            // Delayed full scan so Awake/Start on scene objects have had time to run.
            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                Scene s = SceneManager.GetSceneAt(i);
                if (s.isLoaded && s.name != _ownSceneName)
                    StartCoroutine(ScanSceneDelayed(s, 1));
            }
        }

        /// <summary>
        /// Instantly makes every Rigidbody in all loaded non-own scenes kinematic.
        /// Called on Start() before physics runs. EnsureOptimizableRecursive will later
        /// attach KinematicProp, which takes over permanent management.
        /// </summary>
        private void FreezeAllRigidbodiesImmediate()
        {
            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                Scene s = SceneManager.GetSceneAt(i);
                if (s.isLoaded && s.name != _ownSceneName)
                    FreezeRigidbodiesInScene(s);
            }
        }

        /// <summary>
        /// Immediately sets every non-terrain Rigidbody in <paramref name="scene"/> to kinematic.
        /// Called the instant a scene loads (OnSceneLoaded) to prevent any physics tick
        /// from running on props before KinematicProp is attached by the delayed scan.
        /// </summary>
        private void FreezeRigidbodiesInScene(Scene scene)
        {
            if (!scene.isLoaded) return;
            
            // ONLY freeze rigidbodies in dynamically loaded chunk scenes!
            // Freezing rigidbodies in StartScreen or Persistent scenes will cause them to hover forever 
            // since they lack a DesertTerrainChunk to wake them up.
            if (!scene.name.Contains("Chunk")) return;

            foreach (GameObject root in scene.GetRootGameObjects())
            {
                if (ShouldSkipRoot(root)) continue;
                foreach (Rigidbody rb in root.GetComponentsInChildren<Rigidbody>(true))
                {
                    rb.isKinematic = true;
                    rb.useGravity  = false;
                    rb.velocity        = Vector3.zero;
                    rb.angularVelocity = Vector3.zero;
                }
            }
        }

        private void Update()
        {
            if (_mainCamera == null || !_mainCamera.gameObject.activeInHierarchy || !_mainCamera.enabled)
            {
                _mainCamera = Camera.main;
            }

            if (_mainCamera != null)
            {
                // Recalculate frustum planes every frame (critical — without this objects stay hidden forever).
                GeometryUtility.CalculateFrustumPlanes(_mainCamera, _frustumPlanes);

                // On a significant rotation, do a full sweep immediately to avoid edge-popping.
                float rotDelta = Quaternion.Angle(_mainCamera.transform.rotation, _lastCamRot);
                if (rotDelta > 8f)
                {
                    _lastCamRot = _mainCamera.transform.rotation;
                    DoFullVisibilitySweep();
                    return;
                }
                _lastCamRot = _mainCamera.transform.rotation;
            }

            // ── Priority fast-recheck: hidden objects that are close ──────────────
            // These are the ones most likely to suddenly need to appear, so we check
            // them every frame with a dedicated budget before the normal incremental pass.
            DoHiddenNearbyRecheck();

            // ── Normal incremental pass ──────────────────────────────────────────
            DoIncrementalPass(Mathf.Min(objectsCheckedPerFrame, _managedObjects.Count));
        }

        // ── Visibility passes ──────────────────────────────────────────────────

        /// <summary>
        /// Full sweep of every managed object. Called when the camera rotates sharply.
        /// </summary>
        private void DoFullVisibilitySweep()
        {
            if (_managedObjects.Count == 0) return;

            Vector3 playerPos     = PlayerPos();
            float   showDistSq    = defaultVisibilityRadius * defaultVisibilityRadius;
            float   hideDistSq    = (defaultVisibilityRadius + hysteresis) * (defaultVisibilityRadius + hysteresis);
            float   neverCullSq   = neverFrustumCullRadius * neverFrustumCullRadius;

            _hiddenNearby.Clear();

            for (int i = _managedObjects.Count - 1; i >= 0; i--)
            {
                OptimizableObject obj = _managedObjects[i];
                if (obj == null || obj.isDestroyed) { _managedObjects.RemoveAt(i); continue; }
                ProcessObject(obj, playerPos, showDistSq, hideDistSq, neverCullSq);
            }

            // Rebuild the hidden-nearby bucket after the full sweep.
            RebuildHiddenNearbyBucket(playerPos, showDistSq);

            _currentIndex = 0; // Reset incremental pointer after a full sweep.
        }

        /// <summary>
        /// Fast-recheck of only the objects that are hidden but within display range.
        /// These are the most likely candidates to need re-appearing.
        /// </summary>
        private void DoHiddenNearbyRecheck()
        {
            if (_hiddenNearby.Count == 0) return;

            Vector3 playerPos   = PlayerPos();
            float   showDistSq  = defaultVisibilityRadius * defaultVisibilityRadius;
            float   hideDistSq  = (defaultVisibilityRadius + hysteresis) * (defaultVisibilityRadius + hysteresis);
            float   neverCullSq = neverFrustumCullRadius * neverFrustumCullRadius;

            int limit = Mathf.Min(hiddenObjectFastRecheck, _hiddenNearby.Count);

            for (int i = _hiddenNearby.Count - 1; i >= 0 && limit > 0; i--, limit--)
            {
                OptimizableObject obj = _hiddenNearby[i];
                if (obj == null || obj.isDestroyed || !obj.isHiddenByManager)
                {
                    _hiddenNearby.RemoveAt(i);
                    continue;
                }

                float sqrDist = (obj.CachedPosition - playerPos).sqrMagnitude;
                if (sqrDist > hideDistSq)
                {
                    // Moved out of range — remove from bucket.
                    _hiddenNearby.RemoveAt(i);
                    continue;
                }

                // Still in range — do full visibility check.
                ProcessObject(obj, playerPos, showDistSq, hideDistSq, neverCullSq);

                // If ProcessObject made it visible, remove from bucket.
                if (!obj.isHiddenByManager)
                    _hiddenNearby.RemoveAt(i);
            }
        }

        /// <summary>
        /// Incremental pass: checks <paramref name="limit"/> objects per frame, cycling through all objects.
        /// </summary>
        private void DoIncrementalPass(int limit)
        {
            if (_managedObjects.Count == 0) return;

            Vector3 playerPos   = PlayerPos();
            float   showDistSq  = defaultVisibilityRadius * defaultVisibilityRadius;
            float   hideDistSq  = (defaultVisibilityRadius + hysteresis) * (defaultVisibilityRadius + hysteresis);
            float   neverCullSq = neverFrustumCullRadius * neverFrustumCullRadius;

            for (int i = 0; i < limit; i++)
            {
                if (_currentIndex >= _managedObjects.Count) _currentIndex = 0;

                OptimizableObject obj = _managedObjects[_currentIndex];
                if (obj == null || obj.isDestroyed)
                {
                    _managedObjects.RemoveAt(_currentIndex);
                    continue;
                }

                bool wasHidden = obj.isHiddenByManager;
                ProcessObject(obj, playerPos, showDistSq, hideDistSq, neverCullSq);

                // If this object just got hidden and is within range, add to fast-recheck bucket.
                if (!wasHidden && obj.isHiddenByManager)
                {
                    float sqrDist = (obj.CachedPosition - playerPos).sqrMagnitude;
                    if (sqrDist <= hideDistSq && !_hiddenNearby.Contains(obj))
                        _hiddenNearby.Add(obj);
                }

                _currentIndex++;
            }
        }

        // ── Core per-object decision ───────────────────────────────────────────

        private void ProcessObject(OptimizableObject obj, Vector3 playerPos,
                                   float showDistSq, float hideDistSq, float neverCullSq)
        {
            float sqrDist = (obj.CachedPosition - playerPos).sqrMagnitude;

            // Distance gate with hysteresis.
            bool shouldBeVisible = sqrDist <= (obj.isHiddenByManager ? showDistSq : hideDistSq);

            if (!shouldBeVisible)
            {
                if (!obj.isHiddenByManager) obj.SetVisibility(false);
                return;
            }

            // Frustum cull — skipped for close objects to prevent near-edge pop-in.
            if (obj.useFrustumCulling && _mainCamera != null && sqrDist > neverCullSq)
            {
                bool inFrustum;
                if (obj.HasBounds)
                {
                    // Expand the bounds by frustumExpansion so objects just off-screen
                    // aren't culled prematurely. This is the key fix for edge popping.
                    Bounds expanded = obj.GetBounds();
                    expanded.Expand(frustumExpansion * 2f);
                    inFrustum = GeometryUtility.TestPlanesAABB(_frustumPlanes, expanded);
                }
                else
                {
                    // Point-in-frustum with an extra margin in world space.
                    inFrustum = IsPointNearFrustum(obj.CachedPosition, frustumExpansion);
                }

                shouldBeVisible = inFrustum;
            }

            if (shouldBeVisible != !obj.isHiddenByManager)
                obj.SetVisibility(shouldBeVisible);
        }

        // ── Helpers ───────────────────────────────────────────────────────────

        private Vector3 PlayerPos()
        {
            if (_mainCamera != null && _mainCamera.gameObject.activeInHierarchy && _mainCamera.enabled)
            {
                return _mainCamera.transform.position;
            }
            return _playerTransform != null ? _playerTransform.position : Vector3.zero;
        }

        /// <summary>
        /// Returns true when <paramref name="point"/> is inside the frustum OR within
        /// <paramref name="margin"/> metres of any frustum plane (i.e. just outside).
        /// This prevents culling objects that sit right at the screen edge.
        /// </summary>
        private bool IsPointNearFrustum(Vector3 point, float margin)
        {
            for (int i = 0; i < _frustumPlanes.Length; i++)
            {
                // GetDistanceToPoint returns negative when outside the plane.
                // We allow objects to be up to `margin` metres outside.
                if (_frustumPlanes[i].GetDistanceToPoint(point) < -margin)
                    return false;
            }
            return true;
        }

        private void RebuildHiddenNearbyBucket(Vector3 playerPos, float showDistSq)
        {
            _hiddenNearby.Clear();
            float hideDistSq = (defaultVisibilityRadius + hysteresis) * (defaultVisibilityRadius + hysteresis);
            foreach (var obj in _managedObjects)
            {
                if (obj == null || !obj.isHiddenByManager) continue;
                float sqrDist = (obj.CachedPosition - playerPos).sqrMagnitude;
                if (sqrDist <= hideDistSq)
                    _hiddenNearby.Add(obj);
            }
        }

        // ── Scene event handlers ───────────────────────────────────────────────

        private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            if (scene.name == _ownSceneName) return;

            // Freeze all Rigidbodies in this scene IMMEDIATELY (same frame as load),
            // before Unity's physics engine runs on them. This prevents props from
            // rolling on slopes during the scanDelayFrames window.
            FreezeRigidbodiesInScene(scene);

            StartCoroutine(ScanSceneDelayed(scene, scanDelayFrames));
        }

        private void OnSceneUnloaded(Scene scene)
        {
            _managedObjects.RemoveAll(o => o == null || o.isDestroyed);
            _hiddenNearby.RemoveAll(o => o == null || o.isDestroyed);
            if (_currentIndex >= _managedObjects.Count) _currentIndex = 0;
        }

        // ── Registration ───────────────────────────────────────────────────────

        public void Register(OptimizableObject obj)
        {
            if (!_managedObjects.Contains(obj))
                _managedObjects.Add(obj);
        }

        public void Unregister(OptimizableObject obj)
        {
            _hiddenNearby.Remove(obj);

            int index = _managedObjects.IndexOf(obj);
            if (index == -1) return;

            _managedObjects.RemoveAt(index);

            if (index < _currentIndex) _currentIndex--;
            if (_currentIndex >= _managedObjects.Count) _currentIndex = 0;
        }

        // ── Auto-scanning ──────────────────────────────────────────────────────

        private IEnumerator ScanSceneDelayed(Scene scene, int delayFrames)
        {
            for (int i = 0; i < delayFrames; i++)
                yield return null;

            if (!scene.isLoaded) yield break;
            ScanScene(scene);
        }

        private void ScanScene(Scene scene)
        {
            if (!scene.isLoaded) return;

            GameObject[] roots = scene.GetRootGameObjects();
            int added = 0;

            foreach (GameObject root in roots)
            {
                if (ShouldSkipRoot(root)) continue;
                added += EnsureOptimizableRecursive(root);
            }

            if (verboseLogging)
                Debug.Log($"[BetterGameplayManager] Scanned '{scene.name}': {added} added. Total: {_managedObjects.Count}");
        }

        private static bool ShouldSkipRoot(GameObject root)
        {
            if (root.GetComponent<Terrain>() != null) return true;
            if (root.GetComponent<DesertTerrainChunk>() != null) return true;
            if (root.GetComponent<ExcludeFromOptimization>() != null) return true;
            return false;
        }

        private int EnsureOptimizableRecursive(GameObject root)
        {
            if (root.GetComponentInChildren<Renderer>(true) == null) return 0;
            if (root.GetComponent<ExcludeFromOptimization>() != null) return 0;

            OptimizableObject opt = root.GetComponent<OptimizableObject>();
            if (opt == null)
                opt = root.AddComponent<OptimizableObject>();

            // Renderer-only culling: keeps Transform/Collider/Rigidbody active at all times.
            // This prevents the physics-reset jump that occurs with SetActive(false/true).
            opt.disableEntireGameObject = false;
            opt.useFrustumCulling = true;

            // Auto-attach KinematicProp only to simple rigidbodies that lack a WorldObject.
            // WorldObjects handle their own kinematic state via ApplyPushabilityState().
            Rigidbody rb = root.GetComponent<Rigidbody>();
            if (rb != null && root.GetComponent<KinematicProp>() == null)
            {
                WorldObject wo = root.GetComponent<WorldObject>();
                if (wo == null)
                {
                    KinematicProp kp = root.AddComponent<KinematicProp>();
                    kp.Sleep(); // Enforce kinematic immediately.
                }
            }

            Register(opt);
            return 1;
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

        // ── Editor gizmos ─────────────────────────────────────────────────────

#if UNITY_EDITOR
        private void OnDrawGizmosSelected()
        {
            Vector3 center = _playerTransform != null ? _playerTransform.position : transform.position;
            Gizmos.color = new Color(0.2f, 1f, 0.4f, 0.25f);
            Gizmos.DrawWireSphere(center, defaultVisibilityRadius);
            Gizmos.color = new Color(1f, 0.9f, 0.2f, 0.15f);
            Gizmos.DrawWireSphere(center, defaultVisibilityRadius + hysteresis);
            Gizmos.color = new Color(0.4f, 0.7f, 1f, 0.15f);
            Gizmos.DrawWireSphere(center, neverFrustumCullRadius);
        }
#endif
    }
}
