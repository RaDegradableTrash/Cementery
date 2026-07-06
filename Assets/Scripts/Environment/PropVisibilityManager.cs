using UnityEngine;
using System.Collections;

namespace EnvironmentSystem
{
    /// <summary>
    /// Simple distance-based visibility manager for spawned props (e.g. cacti).
    /// Attached automatically to every "Spawned_Props" holder by DesertPropPlacementTool.
    /// Uses a coroutine with spatial bucketing to cheaply toggle prop visibility
    /// based on distance to the player, without any physics overhead.
    /// </summary>
    public class PropVisibilityManager : MonoBehaviour
    {
        [Tooltip("Props closer than this distance to the player will be visible.")]
        public float visibilityRadius = 120f;

        [Tooltip("Hysteresis band to prevent rapid toggling at the boundary. " +
                 "A prop must move (visibilityRadius + hysteresis) away before being hidden.")]
        public float hysteresis = 15f;

        [Tooltip("How often (seconds) the visibility check runs. Higher = cheaper but less responsive.")]
        public float checkInterval = 0.4f;

        [Tooltip("Maximum props checked per interval. Large holders are spread across multiple ticks.")]
        [Min(8)] public int maxPropsCheckedPerTick = 256;

        // Cached list of all direct child renderers grouped by child index for batch toggling
        private Transform[] _props;
        private bool[] _visible;
        private int _visibilityCursor;
        private WaitForSeconds _visibilityWait;
        private float _cachedCheckInterval = -1f;

        private Transform _playerTransform;
        private RVSystem.RVController _cachedRv;
        private Camera _cachedCamera;
        private float _nextTargetSearchTime;
        private const float TargetSearchInterval = 0.75f;

        // ── Lifecycle ─────────────────────────────────────────────────────────

        private void OnEnable()
        {
            CacheProps();
            StartCoroutine(VisibilityLoop());
        }

        private void OnDisable()
        {
            StopAllCoroutines();
        }

        // ── Internal helpers ──────────────────────────────────────────────────

        private void CacheProps()
        {
            int count = transform.childCount;
            _props = new Transform[count];
            _visible = new bool[count];
            _visibilityCursor = 0;

            for (int i = 0; i < count; i++)
            {
                _props[i] = transform.GetChild(i);
                _visible[i] = true; // Assume all start visible
            }
        }

        private Transform FindPlayer()
        {
            if (Time.time < _nextTargetSearchTime)
                return null;

            _nextTargetSearchTime = Time.time + TargetSearchInterval;

            // Priority: tagged Player → RV → Main Camera
            GameObject player = GameObject.FindGameObjectWithTag("Player");
            if (player != null && player.activeInHierarchy)
                return player.transform;

            if (_cachedRv == null || !_cachedRv.gameObject.activeInHierarchy)
                _cachedRv = FindObjectOfType<RVSystem.RVController>();

            if (_cachedRv != null && _cachedRv.gameObject.activeInHierarchy)
                return _cachedRv.transform;

            if (_cachedCamera == null || !_cachedCamera.gameObject.activeInHierarchy)
                _cachedCamera = Camera.main;

            return _cachedCamera != null ? _cachedCamera.transform : null;
        }

        private IEnumerator VisibilityLoop()
        {
            // Stagger startup across all managers to spread the load across frames
            yield return new WaitForSeconds(Random.Range(0f, checkInterval));
            RefreshVisibilityWait();

            while (true)
            {
                RefreshVisibilityWait();
                yield return _visibilityWait;

                // Re-find player lazily (handles RV/player switching)
                if (_playerTransform == null || !_playerTransform.gameObject.activeInHierarchy)
                    _playerTransform = FindPlayer();

                if (_playerTransform == null || _props == null) continue;

                Vector3 playerPos = _playerTransform.position;

                float showDistSq = visibilityRadius * visibilityRadius;
                float hideDist = visibilityRadius + hysteresis;
                float hideDistSq = hideDist * hideDist;

                int propCount = _props.Length;
                int checksThisTick = Mathf.Min(Mathf.Max(8, maxPropsCheckedPerTick), propCount);
                for (int i = 0; i < checksThisTick; i++)
                {
                    if (_visibilityCursor >= propCount)
                        _visibilityCursor = 0;

                    int propIndex = _visibilityCursor++;
                    Transform prop = _props[propIndex];
                    if (prop == null) continue;

                    Vector3 propPos = prop.position;
                    float dx = playerPos.x - propPos.x;
                    float dz = playerPos.z - propPos.z;
                    float distSq = dx * dx + dz * dz;

                    if (_visible[propIndex])
                    {
                        // Currently visible → hide if beyond hide threshold
                        if (distSq > hideDistSq)
                        {
                            prop.gameObject.SetActive(false);
                            _visible[propIndex] = false;
                        }
                    }
                    else
                    {
                        // Currently hidden → show if within show threshold
                        if (distSq <= showDistSq)
                        {
                            prop.gameObject.SetActive(true);
                            _visible[propIndex] = true;
                        }
                    }
                }
            }
        }

        private void RefreshVisibilityWait()
        {
            float interval = Mathf.Max(0.05f, checkInterval);
            if (_visibilityWait == null || !Mathf.Approximately(_cachedCheckInterval, interval))
            {
                _cachedCheckInterval = interval;
                _visibilityWait = new WaitForSeconds(interval);
            }
        }

        // ── Editor helper: visualise radius in Scene View ─────────────────────
#if UNITY_EDITOR
        private void OnDrawGizmosSelected()
        {
            // Draw show radius (green) and hide radius (yellow) around this holder
            Gizmos.color = new Color(0.2f, 1f, 0.4f, 0.35f);
            Gizmos.DrawWireSphere(transform.position, visibilityRadius);

            Gizmos.color = new Color(1f, 0.9f, 0.2f, 0.2f);
            Gizmos.DrawWireSphere(transform.position, visibilityRadius + hysteresis);
        }
#endif
    }
}
