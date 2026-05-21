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

        // Circular buffer arrays passed to Shader
        private Vector4[] _deformerPositions = new Vector4[MaxDeformers];
        private Vector4[] _deformerParams = new Vector4[MaxDeformers]; // x: depth, y: rimWidth, z: rimHeight, w: fade
        private float[] _lifetimes = new float[MaxDeformers];
        private float[] _maxLifetimes = new float[MaxDeformers];
        
        private int _currentIndex = 0;

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
            _deformerPositions[index] = new Vector4(position.x, position.y, position.z, radius);
            _deformerParams[index] = new Vector4(depth, rimWidth, rimHeight, 1f);
            _lifetimes[index] = lifetime;
            _maxLifetimes[index] = lifetime;

            _currentIndex = (_currentIndex + 1) % MaxDeformers;
        }

        private float _nextSweepTime = 0f;

        private void Update()
        {
            // 🌟 Second Insurance: Periodically sweep the scene to auto-bind dynamically spawned, 
            // enabled, or respawned players and vehicles at runtime (every 1.2 seconds)
            if (Time.time >= _nextSweepTime)
            {
                _nextSweepTime = Time.time + 1.2f;
                AutoBindDynamicDeformers();
            }

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

        private void AutoBindDynamicDeformers()
        {
            // 1. Detect and bind to ALL WheelColliders universally in the entire scene!
            // Decouples the system from any specific class names (RVController, CarControl, etc.)
            var wheelColliders = Object.FindObjectsOfType<WheelCollider>(true);
            foreach (var wc in wheelColliders)
            {
                if (wc != null && wc.GetComponent<SandDeformer>() == null)
                {
                    var deformer = wc.gameObject.AddComponent<SandDeformer>();
                    
                    // Wheel tire imprint characteristics
                    deformer.radius = 0.58f;
                    deformer.depth = 0.22f;
                    deformer.rimWidth = 0.22f;
                    deformer.rimHeight = 0.065f;
                    deformer.stampSpacing = 0.75f; // Extremely optimized spacing for extended persistence!
                    deformer.lifetime = 32f;
                }
            }

            // 2. Detect and bind to the Player character dynamically by Tag
            var players = GameObject.FindGameObjectsWithTag("Player");
            foreach (var player in players)
            {
                if (player != null && player.GetComponent<SandDeformer>() == null)
                {
                    var deformer = player.AddComponent<SandDeformer>();
                    
                    // Character foot print characteristics
                    deformer.radius = 0.35f;
                    deformer.depth = 0.12f;
                    deformer.rimWidth = 0.12f;
                    deformer.rimHeight = 0.035f;
                    deformer.stampSpacing = 0.6f; // Balanced spacing for character footprints
                    deformer.lifetime = 24f;
                }
            }

            // 3. Backup: Detect Player character dynamically by PlayerController script name
            var allBehaviors = Object.FindObjectsOfType<MonoBehaviour>();
            foreach (var mb in allBehaviors)
            {
                if (mb != null && mb.GetType().Name == "PlayerController" && mb.GetComponent<SandDeformer>() == null)
                {
                    var deformer = mb.gameObject.AddComponent<SandDeformer>();
                    
                    deformer.radius = 0.35f;
                    deformer.depth = 0.12f;
                    deformer.rimWidth = 0.12f;
                    deformer.rimHeight = 0.035f;
                    deformer.stampSpacing = 0.6f;
                    deformer.lifetime = 24f;
                }
            }

            // 4. Detect and bind to generic heavy physical props (boxes, barrels, boulders, loose Rigidbodies)
            var allRigidbodies = Object.FindObjectsOfType<Rigidbody>(true);
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
                        
                        // Dynamically scale stamp parameters based on collider bounds and physical mass!
                        float boundsScale = col.bounds.extents.magnitude;
                        deformer.radius = Mathf.Clamp(boundsScale * 0.6f, 0.25f, 1.8f);
                        deformer.depth = Mathf.Clamp(rb.mass * 0.0012f, 0.06f, 0.28f);
                        deformer.rimWidth = deformer.radius * 0.35f;
                        deformer.rimHeight = deformer.depth * 0.25f;
                        deformer.stampSpacing = deformer.radius * 0.5f;
                        deformer.lifetime = 20f;

                        Debug.Log($"[SandDeformationManager] Dynamically registered interactive prop: '{rb.name}' with footprint radius {deformer.radius:F2}m (mass: {rb.mass}kg).");
                    }
                }
            }
        }
    }
}
