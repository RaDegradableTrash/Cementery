using UnityEngine;
using RVSystem;

namespace EnvironmentSystem
{
    /// <summary>
    /// Manages real-time interactive sand deformation globally.
    /// Passes deformer coordinates and deformation parameters to the URP triplanar terrain shader.
    /// Features footprint overlapping point merging and smooth windward sand-filling recovery.
    /// </summary>
    public class SandDeformationManager : MonoBehaviour
    {
        public static SandDeformationManager Instance { get; private set; }

        private const int MaxDeformers = 128;
        private const float DefaultSweepInterval = 8f;
        private const float WebGlSweepInterval = 12f;
        private const float StableSweepInterval = 30f;

        // Circular buffer arrays passed to Shader
        private Vector4[] _deformerPositions = new Vector4[MaxDeformers];
        private Vector4[] _deformerParams = new Vector4[MaxDeformers]; // x: depth, y: rimWidth, z: rimHeight, w: fade
        private float[] _lifetimes = new float[MaxDeformers];
        private float[] _maxLifetimes = new float[MaxDeformers];
        
        private int _currentIndex = 0;
        private int _activeDeformerCount;
        private bool _dynamicBindingsStable;
        [Header("Runtime Binding")]
        [Tooltip("Periodically attach deformers to player and vehicle wheels that appear after scene load.")]
        public bool autoBindPlayerAndVehicleDeformers = true;
        [Tooltip("Attach sand deformers to loose rigidbody props. Leave off for normal gameplay; it can add many raycasting components.")]
        public bool autoBindLoosePropDeformers = false;
        [Tooltip("Enable logs when runtime deformers are attached.")]
        public bool verboseBindingLogs = false;

        // Shader Property IDs for lightning-fast GPU uploads
        private static readonly int DeformerPositionsId = Shader.PropertyToID("_DeformerPositions");
        private static readonly int DeformerParamsId = Shader.PropertyToID("_DeformerParams");

        private void Awake()
        {
            if (Instance == null)
            {
                Instance = this;
                // Keep persistent across stream-loaded scenes
                DontDestroyOnLoad(gameObject);
            }
            else
            {
                Destroy(gameObject);
                return;
            }

            InitializeArrays();
        }

        private void InitializeArrays()
        {
            for (int i = 0; i < MaxDeformers; i++)
            {
                _deformerPositions[i] = Vector4.zero;
                _deformerParams[i] = Vector4.zero;
                _lifetimes[i] = 0f;
                _maxLifetimes[i] = 1f;
            }
            Shader.SetGlobalVectorArray(DeformerPositionsId, _deformerPositions);
            Shader.SetGlobalVectorArray(DeformerParamsId, _deformerParams);
        }

        /// <summary>
        /// Registers a new deformation footprint, or merges/updates it if extremely close to save buffer slots.
        /// </summary>
        public void RegisterDeformation(Vector3 position, float radius, float depth, float rimWidth, float rimHeight, float lifetime)
        {
            // 1. Footprint Merging: If an existing footprint is within 30cm, update it in-place instead of allocating a new slot.
            // This ensures standing characters or idling vehicles don't choke the circular buffer!
            for (int i = 0; i < MaxDeformers; i++)
            {
                if (_deformerParams[i].w > 0.05f) // is active
                {
                    float distSq = (new Vector3(_deformerPositions[i].x, _deformerPositions[i].y, _deformerPositions[i].z) - position).sqrMagnitude;
                    if (distSq < 0.09f) // 30cm radius square
                    {
                        _deformerPositions[i] = new Vector4(position.x, position.y, position.z, radius);
                        _deformerParams[i] = new Vector4(depth, rimWidth, rimHeight, 1f);
                        _lifetimes[i] = lifetime;
                        _maxLifetimes[i] = lifetime;
                        return;
                    }
                }
            }

            // 2. Circular allocation for brand new footsteps/tire tracks
            int index = _currentIndex;
            bool replacingActiveSlot = _lifetimes[index] > 0f || _deformerParams[index].w > 0.05f;
            _deformerPositions[index] = new Vector4(position.x, position.y, position.z, radius);
            _deformerParams[index] = new Vector4(depth, rimWidth, rimHeight, 1f);
            _lifetimes[index] = lifetime;
            _maxLifetimes[index] = lifetime;
            if (!replacingActiveSlot)
            {
                _activeDeformerCount++;
            }

            _currentIndex = (_currentIndex + 1) % MaxDeformers;
        }

        private float _nextSweepTime = 0f;

        private void Update()
        {
            if (autoBindPlayerAndVehicleDeformers && Time.time >= _nextSweepTime)
            {
                float interval = GetDynamicBindingSweepInterval();
                _nextSweepTime = Time.time + interval;
                _dynamicBindingsStable = AutoBindDynamicDeformers();
            }

            if (_activeDeformerCount <= 0)
                return;

            bool hasChanged = false;

            // Fade lifetimes smoothly over time (simulating granular sand filling back in organically)
            for (int i = 0; i < MaxDeformers; i++)
            {
                if (_lifetimes[i] > 0f)
                {
                    _lifetimes[i] -= Time.deltaTime;
                    float fade = Mathf.Clamp01(_lifetimes[i] / _maxLifetimes[i]);
                    
                    // Smooth quadratic decay for realistic soil/sand shifting recovery
                    _deformerParams[i].w = fade * fade;
                    hasChanged = true;

                    if (_lifetimes[i] <= 0f)
                    {
                        _deformerPositions[i] = Vector4.zero;
                        _deformerParams[i] = Vector4.zero;
                        _activeDeformerCount = Mathf.Max(0, _activeDeformerCount - 1);
                    }
                }
            }

            // Upload the compiled arrays to global shader memory
            if (hasChanged || Time.frameCount % 8 == 0)
            {
                Shader.SetGlobalVectorArray(DeformerPositionsId, _deformerPositions);
                Shader.SetGlobalVectorArray(DeformerParamsId, _deformerParams);
            }
        }

        private float GetDynamicBindingSweepInterval()
        {
            if (_dynamicBindingsStable)
                return StableSweepInterval;

            return Application.platform == RuntimePlatform.WebGLPlayer ? WebGlSweepInterval : DefaultSweepInterval;
        }

        private bool AutoBindDynamicDeformers()
        {
            bool sawPlayerOrWheel = false;
            bool addedDeformer = false;

            var wheelColliders = Object.FindObjectsByType<WheelCollider>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            foreach (var wc in wheelColliders)
            {
                if (wc == null)
                    continue;

                sawPlayerOrWheel = true;
                if (wc.GetComponent<SandDeformer>() == null)
                {
                    var deformer = wc.gameObject.AddComponent<SandDeformer>();
                    addedDeformer = true;
                    
                    // Wheel tire imprint characteristics
                    deformer.radius = 0.58f;
                    deformer.depth = 0.22f;
                    deformer.rimWidth = 0.22f;
                    deformer.rimHeight = 0.065f;
                    deformer.stampSpacing = 0.75f;
                    deformer.lifetime = 32f;
                }
            }

            var players = GameObject.FindGameObjectsWithTag("Player");
            foreach (var player in players)
            {
                if (player == null)
                    continue;

                sawPlayerOrWheel = true;
                if (player.GetComponent<SandDeformer>() == null)
                {
                    var deformer = player.AddComponent<SandDeformer>();
                    addedDeformer = true;
                    
                    // Character foot print characteristics
                    deformer.radius = 0.35f;
                    deformer.depth = 0.12f;
                    deformer.rimWidth = 0.12f;
                    deformer.rimHeight = 0.035f;
                    deformer.stampSpacing = 0.6f;
                    deformer.lifetime = 24f;
                }
            }

            if (!autoBindLoosePropDeformers)
                return sawPlayerOrWheel && !addedDeformer;

            var allRigidbodies = Object.FindObjectsByType<Rigidbody>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            foreach (var rb in allRigidbodies)
            {
                if (rb != null && 
                    !rb.CompareTag("Player") && 
                    rb.GetComponent<PlayerController>() == null && 
                    rb.GetComponent<CarControl>() == null &&
                    rb.GetComponent<SandDeformer>() == null)
                {
                    var col = rb.GetComponent<Collider>();
                    if (col != null && !col.isTrigger)
                    {
                        var deformer = rb.gameObject.AddComponent<SandDeformer>();
                        addedDeformer = true;
                        
                        // Dynamically scale stamp parameters based on collider bounds and physical mass!
                        float boundsScale = col.bounds.extents.magnitude;
                        deformer.radius = Mathf.Clamp(boundsScale * 0.6f, 0.25f, 1.8f);
                        deformer.depth = Mathf.Clamp(rb.mass * 0.0012f, 0.06f, 0.28f);
                        deformer.rimWidth = deformer.radius * 0.35f;
                        deformer.rimHeight = deformer.depth * 0.25f;
                        deformer.stampSpacing = deformer.radius * 0.5f;
                        deformer.lifetime = 20f;

                        if (verboseBindingLogs)
                        {
                            Debug.Log($"[SandDeformationManager] Dynamically registered interactive prop: '{rb.name}' with footprint radius {deformer.radius:F2}m (mass: {rb.mass}kg).");
                        }
                    }
                }
            }

            return sawPlayerOrWheel && !addedDeformer;
        }
    }
}
