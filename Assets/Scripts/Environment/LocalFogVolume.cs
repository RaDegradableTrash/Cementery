using UnityEngine;

namespace EnvironmentSystem
{
    [RequireComponent(typeof(BoxCollider))]
    public class LocalFogVolume : MonoBehaviour
    {
        [Header("Fog Settings")]
        [Range(0f, 1f)] public float targetExtinction = 0.5f;
        public Color volumeColor = new Color(0.4f, 0.45f, 0.5f, 1f);

        [Header("Fade settings")]
        [Tooltip("The margin distance inside the collider bounds where the fog fades to full strength.")]
        public float fadeMargin = 5f;

        private BoxCollider _boxCollider;
        private Camera _mainCamera;

#if AURA_2_PRESENT
        private Aura2API.AuraVolume _auraVolume;
#endif

        private void Start()
        {
            _boxCollider = GetComponent<BoxCollider>();
            _boxCollider.isTrigger = true;
            _mainCamera = Camera.main;

#if AURA_2_PRESENT
            if (!TryGetComponent(out _auraVolume))
            {
                _auraVolume = gameObject.AddComponent<Aura2API.AuraVolume>();
            }
            // Setup Aura volume properties
            _auraVolume.densityValue = targetExtinction;
            _auraVolume.colorValue = volumeColor;
#endif
        }

        private void Update()
        {
            if (_mainCamera == null)
            {
                _mainCamera = Camera.main;
                if (_mainCamera == null) return;
            }

            Vector3 camPos = _mainCamera.transform.position;
            bool isInside = _boxCollider.bounds.Contains(camPos);

            if (isInside)
            {
                // Calculate fade coefficient based on distance to the closest boundary of the box
                float fadeFactor = CalculateFadeFactor(camPos);
                ApplyLocalFogDensity(fadeFactor);
            }
            else
            {
                // If camera is outside, let the system handle normal preset values
#if !AURA_2_PRESENT
                // Fallback: reset fog settings towards active preset if we modified global fog
#endif
            }
        }

        private float CalculateFadeFactor(Vector3 point)
        {
            if (_boxCollider == null) return 0f;

            // Compute distance to closest face of local space AABB
            Vector3 localPoint = transform.InverseTransformPoint(point);
            Vector3 extents = _boxCollider.size * 0.5f;
            Vector3 center = _boxCollider.center;

            float distToX = Mathf.Min(Mathf.Abs(localPoint.x - (center.x - extents.x)), Mathf.Abs(localPoint.x - (center.x + extents.x)));
            float distToY = Mathf.Min(Mathf.Abs(localPoint.y - (center.y - extents.y)), Mathf.Abs(localPoint.y - (center.y + extents.y)));
            float distToZ = Mathf.Min(Mathf.Abs(localPoint.z - (center.z - extents.z)), Mathf.Abs(localPoint.z - (center.z + extents.z)));

            float minDist = Mathf.Min(distToX, Mathf.Min(distToY, distToZ));

            // Convert world space margin
            float localMargin = transform.InverseTransformVector(new Vector3(fadeMargin, 0f, 0f)).magnitude;
            if (localMargin <= 0.001f) return 1f;

            return Mathf.Clamp01(minDist / localMargin);
        }

        private void ApplyLocalFogDensity(float factor)
        {
#if AURA_2_PRESENT
            if (_auraVolume != null)
            {
                _auraVolume.densityValue = targetExtinction * factor;
            }
#else
            // Fallback: Boost native fog density dynamically when player walks into low-lying dense fog areas
            AuraFogSystemManager manager = FindFirstObjectByType<AuraFogSystemManager>();
            if (manager != null && manager.activePreset != null)
            {
                float baseDensity = manager.activePreset.nativeFogDensity;
                float boostedDensity = Mathf.Max(baseDensity, targetExtinction * 0.05f); // Scale target extinction to realistic native fog range
                RenderSettings.fogDensity = Mathf.Lerp(baseDensity, boostedDensity, factor);
                RenderSettings.fogColor = Color.Lerp(RenderSettings.fogColor, volumeColor, factor);
            }
#endif
        }

        private void OnDrawGizmos()
        {
            if (_boxCollider == null) _boxCollider = GetComponent<BoxCollider>();
            if (_boxCollider == null) return;

            Gizmos.matrix = transform.localToWorldMatrix;
            Gizmos.color = new Color(volumeColor.r, volumeColor.g, volumeColor.b, 0.15f);
            Gizmos.DrawCube(_boxCollider.center, _boxCollider.size);

            Gizmos.color = new Color(volumeColor.r, volumeColor.g, volumeColor.b, 0.6f);
            Gizmos.DrawWireCube(_boxCollider.center, _boxCollider.size);
        }
    }
}
