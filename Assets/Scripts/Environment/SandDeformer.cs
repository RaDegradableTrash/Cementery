using UnityEngine;

namespace EnvironmentSystem
{
    /// <summary>
    /// Attach to any object (feet, wheels, heavy physics blocks) to dynamically deform the sand terrain.
    /// Supports automatic ground-alignment and specific optimization for WheelColliders to draw perfect tracks.
    /// </summary>
    public class SandDeformer : MonoBehaviour
    {
        [Header("Deformation Dimensions")]
        public float radius = 0.5f;        // Stamp footprint radius
        public float depth = 0.16f;        // Downward compression depth (meters)
        public float rimWidth = 0.15f;     // Width of the displaced sand pushed up at the borders
        public float rimHeight = 0.05f;    // Height of the displaced sand pushed up at the borders
        public float lifetime = 18f;       // Lifetime in seconds before sand fills back in

        [Header("Grounded Triggers")]
        public float stampSpacing = 0.25f; // Draw new footprint every X meters moved
        public LayerMask groundLayer;      // Layer to cast raycast against
        [Min(0.02f)]
        [Tooltip("Minimum time between ground checks. Higher values reduce raycast cost on many deformers.")]
        public float groundCheckInterval = 0.08f;

        private Vector3 _lastStampPosition;
        private bool _isFirstFrame = true;
        private WheelCollider _wheelCollider; // Optional: auto-detected if attached to a wheel
        private Collider _cachedCollider;
        private SandDeformationManager _manager;
        private float _nextGroundCheckTime;
        private bool _lastGrounded;
        private Vector3 _lastGroundPoint;
        private Vector3 _lastGroundCheckPosition;
        private bool _hasGroundCheckPosition;
        private float _cachedGroundCheckInterval;
        private float _cachedMovementThresholdSq;
        private float _cachedStampSpacingSq;
        private float _lastCachedGroundCheckInterval = float.NaN;
        private float _lastCachedStampSpacing = float.NaN;

        private void Start()
        {
            // Auto-detect WheelCollider to draw incredibly smooth tire tracks
            _wheelCollider = GetComponent<WheelCollider>();
            _cachedCollider = GetComponent<Collider>();
            _manager = SandDeformationManager.Instance;
            
            // Build ground layer default if not assigned
            if (groundLayer == 0)
            {
                groundLayer = LayerMask.GetMask("Default", "Terrain");
            }

            RefreshDerivedSettings();
        }

        private void Update()
        {
            // Defensive runtime check to ensure global manager is active
            if (_manager == null)
            {
                _manager = SandDeformationManager.Instance;
                if (_manager == null)
                {
                    GameObject managerObj = new GameObject("[SandDeformationManager]");
                    _manager = managerObj.AddComponent<SandDeformationManager>();
                }
            }

            Vector3 currentPos = transform.position;
            bool isGrounded = _lastGrounded;
            Vector3 groundPoint = _lastGroundPoint;

            if (Time.time >= _nextGroundCheckTime)
            {
                RefreshDerivedSettings();
                _nextGroundCheckTime = Time.time + _cachedGroundCheckInterval;
                bool shouldRunGroundCheck = true;

                if (_wheelCollider == null && !_isFirstFrame && _hasGroundCheckPosition)
                {
                    shouldRunGroundCheck = (currentPos - _lastGroundCheckPosition).sqrMagnitude >= _cachedMovementThresholdSq;
                }

                if (shouldRunGroundCheck)
                {
                    isGrounded = false;
                    groundPoint = currentPos;

                    if (_wheelCollider != null)
                    {
                        WheelHit hit;
                        isGrounded = _wheelCollider.GetGroundHit(out hit);
                        groundPoint = hit.point;
                    }
                    else
                    {
                        RaycastHit hit;
                        float startHeight = 0.5f;
                        float rayDistance = 1.8f;

                        if (_cachedCollider != null)
                        {
                            float extentsY = _cachedCollider.bounds.extents.y;
                            startHeight = extentsY + 0.2f;
                            rayDistance = extentsY * 2.0f + 0.6f;
                        }

                        if (Physics.Raycast(currentPos + Vector3.up * startHeight, Vector3.down, out hit, rayDistance, groundLayer))
                        {
                            isGrounded = true;
                            groundPoint = hit.point;
                        }
                    }

                    _lastGrounded = isGrounded;
                    _lastGroundPoint = groundPoint;
                    _lastGroundCheckPosition = currentPos;
                    _hasGroundCheckPosition = true;
                }
            }

            // 3. Register stamp on coordinate delta
            if (isGrounded)
            {
                if (_isFirstFrame)
                {
                    _lastStampPosition = groundPoint;
                    _isFirstFrame = false;
                    _manager.RegisterDeformation(groundPoint, radius, depth, rimWidth, rimHeight, lifetime);
                }
                else
                {
                    if ((groundPoint - _lastStampPosition).sqrMagnitude >= _cachedStampSpacingSq)
                    {
                        _lastStampPosition = groundPoint;
                        _manager.RegisterDeformation(groundPoint, radius, depth, rimWidth, rimHeight, lifetime);
                    }
                }
            }
            else
            {
                // Reset frame tracking if air-borne (e.g. jumping or vehicle flying)
                _isFirstFrame = true;
            }
        }

        private void RefreshDerivedSettings()
        {
            if (Mathf.Approximately(_lastCachedGroundCheckInterval, groundCheckInterval) &&
                Mathf.Approximately(_lastCachedStampSpacing, stampSpacing))
            {
                return;
            }

            _lastCachedGroundCheckInterval = groundCheckInterval;
            _lastCachedStampSpacing = stampSpacing;
            _cachedGroundCheckInterval = Mathf.Max(0.02f, groundCheckInterval);

            float movementThreshold = Mathf.Max(0.02f, Mathf.Min(stampSpacing * 0.25f, 0.1f));
            _cachedMovementThresholdSq = movementThreshold * movementThreshold;

            float minDistance = Mathf.Max(0.01f, stampSpacing);
            _cachedStampSpacingSq = minDistance * minDistance;
        }
    }
}
