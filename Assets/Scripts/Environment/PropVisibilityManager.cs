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

        // Cached list of all direct child renderers grouped by child index for batch toggling
        private Transform[] _props;
        private bool[] _visible;

        private Transform _playerTransform;
        private float _checkTimer;

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

            for (int i = 0; i < count; i++)
            {
                _props[i] = transform.GetChild(i);
                _visible[i] = true; // Assume all start visible
            }
        }

        private Transform FindPlayer()
        {
            // Priority: tagged Player → RV → Main Camera
            GameObject player = GameObject.FindGameObjectWithTag("Player");
            if (player != null && player.activeInHierarchy)
                return player.transform;

            var rv = FindObjectOfType<RVSystem.RVController>();
            if (rv != null && rv.gameObject.activeInHierarchy)
                return rv.transform;

            return Camera.main != null ? Camera.main.transform : null;
        }

        private IEnumerator VisibilityLoop()
        {
            // Stagger startup across all managers to spread the load across frames
            yield return new WaitForSeconds(Random.Range(0f, checkInterval));

            while (true)
            {
                yield return new WaitForSeconds(checkInterval);

                // Re-find player lazily (handles RV/player switching)
                if (_playerTransform == null || !_playerTransform.gameObject.activeInHierarchy)
                    _playerTransform = FindPlayer();

                if (_playerTransform == null || _props == null) continue;

                Vector3 playerXZ = new Vector3(_playerTransform.position.x, 0f, _playerTransform.position.z);

                float showDist = visibilityRadius;
                float hideDist = visibilityRadius + hysteresis;

                for (int i = 0; i < _props.Length; i++)
                {
                    if (_props[i] == null) continue;

                    Vector3 propXZ = new Vector3(_props[i].position.x, 0f, _props[i].position.z);
                    float dist = Vector3.Distance(playerXZ, propXZ);

                    if (_visible[i])
                    {
                        // Currently visible → hide if beyond hide threshold
                        if (dist > hideDist)
                        {
                            _props[i].gameObject.SetActive(false);
                            _visible[i] = false;
                        }
                    }
                    else
                    {
                        // Currently hidden → show if within show threshold
                        if (dist <= showDist)
                        {
                            _props[i].gameObject.SetActive(true);
                            _visible[i] = true;
                        }
                    }
                }
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
