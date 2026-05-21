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

        private Vector3 _lastStampPosition;
        private bool _isFirstFrame = true;
        private WheelCollider _wheelCollider; // Optional: auto-detected if attached to a wheel

        private void Start()
        {
            // Auto-detect WheelCollider to draw incredibly smooth tire tracks
            _wheelCollider = GetComponent<WheelCollider>();
            
            // Build ground layer default if not assigned
            if (groundLayer == 0)
            {
                groundLayer = LayerMask.GetMask("Default", "Terrain");
            }
        }

        private void Update()
        {
            // Defensive runtime check to ensure global manager is active
            if (SandDeformationManager.Instance == null)
            {
                GameObject managerObj = new GameObject("[SandDeformationManager]");
                managerObj.AddComponent<SandDeformationManager>();
            }

            Vector3 currentPos = transform.position;
            bool isGrounded = false;
            Vector3 groundPoint = currentPos;

            // 1. Wheel-specific Grounding check
            if (_wheelCollider != null)
            {
                WheelHit hit;
                isGrounded = _wheelCollider.GetGroundHit(out hit);
                groundPoint = hit.point;
            }
            // 2. Raycast-specific Grounding check for Player feet or generic rigidbodies
            else
            {
                RaycastHit hit;
                float startHeight = 0.5f;
                float rayDistance = 1.8f;

                // Adjust raycast starting height and distance dynamically based on physical bounds
                var col = GetComponent<Collider>();
                if (col != null)
                {
                    float extentsY = col.bounds.extents.y;
                    startHeight = extentsY + 0.2f;
                    rayDistance = extentsY * 2.0f + 0.6f;
                }

                // Raycast from slightly above top of bounds downward to catch local sand surface
                if (Physics.Raycast(currentPos + Vector3.up * startHeight, Vector3.down, out hit, rayDistance, groundLayer))
                {
                    isGrounded = true;
                    groundPoint = hit.point;
                }
            }

            // 3. Register stamp on coordinate delta
            if (isGrounded)
            {
                if (_isFirstFrame)
                {
                    _lastStampPosition = groundPoint;
                    _isFirstFrame = false;
                    SandDeformationManager.Instance.RegisterDeformation(groundPoint, radius, depth, rimWidth, rimHeight, lifetime);
                }
                else
                {
                    float distMoved = Vector3.Distance(groundPoint, _lastStampPosition);
                    if (distMoved >= stampSpacing)
                    {
                        _lastStampPosition = groundPoint;
                        SandDeformationManager.Instance.RegisterDeformation(groundPoint, radius, depth, rimWidth, rimHeight, lifetime);
                    }
                }
            }
            else
            {
                // Reset frame tracking if air-borne (e.g. jumping or vehicle flying)
                _isFirstFrame = true;
            }
        }
    }
}
