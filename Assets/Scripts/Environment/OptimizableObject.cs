using UnityEngine;

namespace EnvironmentSystem
{
    /// <summary>
    /// Managed visibility component added automatically by BetterGameplayManager.
    /// You do NOT need to add this manually — the manager discovers objects at runtime.
    ///
    /// If you add this to a prefab manually, it will still work correctly.
    ///
    /// Two culling modes:
    ///   disableEntireGameObject = true  → SetActive(false) when culled (default, cheapest)
    ///   disableEntireGameObject = false → Only disable Renderers (keeps physics/colliders alive)
    ///
    ///   useFrustumCulling = true  → Also hide when outside camera view (good for static props)
    ///   useFrustumCulling = false → Distance-only culling (good for interactive/physics objects)
    /// </summary>
    [DisallowMultipleComponent]
    public class OptimizableObject : MonoBehaviour
    {
        [Tooltip("Disable the entire GameObject when culled (cheapest). " +
                 "Set to false to only hide Renderers and keep colliders active.")]
        public bool disableEntireGameObject = true;

        [Tooltip("Also cull when outside the camera frustum. " +
                 "Best for static scene props. Disable for interactive or moving objects.")]
        public bool useFrustumCulling = false;

        // ── State (read by BetterGameplayManager) ──────────────────────────────
        [HideInInspector] public bool isHiddenByManager = false;
        [HideInInspector] public bool isDestroyed = false;

        // ── Cached data ────────────────────────────────────────────────────────
        private Vector3 _cachedPosition;
        private Bounds  _cachedBounds;
        private bool    _hasBounds;
        private Renderer[] _renderers;

        public Vector3 CachedPosition => _cachedPosition;
        public bool    HasBounds      => _hasBounds;

        // ── Lifecycle ──────────────────────────────────────────────────────────

        private void Awake()
        {
            RebuildCache();
        }

        private void OnEnable()
        {
            // Refresh position cache every time the object is re-enabled
            // (e.g. after being hidden by the manager and then made visible again,
            //  or when a dropped item lands in a new position)
            _cachedPosition = transform.position;

            // Self-register only if the manager already exists.
            // If it doesn't exist yet, ScanScene will register us later.
            if (!isHiddenByManager && BetterGameplayManager.Instance != null)
                BetterGameplayManager.Instance.Register(this);
        }

        private void OnDisable()
        {
            // Unregister only when disabled by something OTHER than the manager
            // (e.g. a chunk being unloaded, or the object being hidden by game logic).
            if (!isHiddenByManager && BetterGameplayManager.Instance != null)
                BetterGameplayManager.Instance.Unregister(this);
        }

        private void OnDestroy()
        {
            isDestroyed = true;
            if (BetterGameplayManager.Instance != null)
                BetterGameplayManager.Instance.Unregister(this);
        }

        // ── Public API ─────────────────────────────────────────────────────────

        /// <summary>
        /// Called by BetterGameplayManager to show or hide this object.
        /// </summary>
        public void SetVisibility(bool visible)
        {
            isHiddenByManager = !visible;

            if (disableEntireGameObject)
            {
                gameObject.SetActive(visible);
            }
            else
            {
                if (_renderers != null)
                {
                    for (int i = 0; i < _renderers.Length; i++)
                    {
                        if (_renderers[i] != null)
                            _renderers[i].enabled = visible;
                    }
                }
            }
        }

        /// <summary>
        /// Call this when the object has moved (e.g. dropped item after physics settles).
        /// BetterGameplayManager uses the cached position for distance checks.
        /// </summary>
        public void UpdateCachedPosition()
        {
            _cachedPosition = transform.position;
        }

        public Bounds GetBounds() => _cachedBounds;

        // ── Internal helpers ───────────────────────────────────────────────────

        private void RebuildCache()
        {
            _cachedPosition = transform.position;
            _renderers = GetComponentsInChildren<Renderer>(true);

            _hasBounds = _renderers != null && _renderers.Length > 0;
            if (_hasBounds)
            {
                _cachedBounds = _renderers[0].bounds;
                for (int i = 1; i < _renderers.Length; i++)
                    _cachedBounds.Encapsulate(_renderers[i].bounds);
            }
        }
    }
}
