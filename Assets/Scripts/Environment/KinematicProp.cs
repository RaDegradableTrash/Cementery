using System.Collections.Generic;
using UnityEngine;

namespace EnvironmentSystem
{
    /// <summary>
    /// Keeps a prop with a Rigidbody permanently kinematic (static on the ground),
    /// and wakes it into full physics only when a non-terrain collider hits it hard enough.
    ///
    /// Ground/terrain detection: uses geometry (TerrainCollider, MeshCollider on a
    /// Desert_Chunk root) — no Unity tags required.
    /// </summary>
    [DefaultExecutionOrder(-32000)]   // Run before all other scripts
    [RequireComponent(typeof(Rigidbody))]
    public class KinematicProp : MonoBehaviour
    {
        [Tooltip("Minimum collision impulse (N·s) required to wake this prop into full physics.")]
        public float wakeImpulseThreshold = 5f;

        private Rigidbody _rb;
        private bool _awoken = false;
        private const int MaxTerrainColliderCacheSize = 1024;
        private static readonly Dictionary<int, bool> TerrainColliderCache = new Dictionary<int, bool>(256);

        private void Awake()
        {
            _rb = GetComponent<Rigidbody>();
            Sleep();
        }

        // ── Public API ────────────────────────────────────────────────────────

        public void Sleep()
        {
            if (_rb == null) return;

            if (!_rb.isKinematic)
            {
                _rb.velocity = Vector3.zero;
                _rb.angularVelocity = Vector3.zero;
                _rb.isKinematic = true;
            }

            if (_rb.useGravity)
            {
                _rb.useGravity = false;
            }

            _awoken = false;
        }

        public void WakeUp()
        {
            if (_awoken || _rb == null) return;
            _awoken         = true;
            _rb.isKinematic = false;
            _rb.useGravity  = true;
        }

        // ── Collision handling ─────────────────────────────────────────────────

        private void OnCollisionEnter(Collision collision)
        {
            // Already dynamic.
            if (_awoken) return;

            // Ignore terrain / ground contacts entirely.
            if (IsTerrainCollider(collision.collider)) return;

            // Ignore collisions with other KinematicProps (e.g. cactuses spawning overlapping).
            if (collision.collider.GetComponentInParent<KinematicProp>() != null) return;

            // Wake only on hard enough hits.
            if (collision.impulse.magnitude < wakeImpulseThreshold) return;

            // Switch to dynamic. DO NOT set velocity manually — Unity's physics solver
            // already has the correct contact response queued; setting velocity here would
            // double-apply the impulse and cause objects to fly.
            _awoken         = true;
            _rb.isKinematic = false;
            _rb.useGravity  = true;
        }

        // ── Terrain detection (no tag dependency) ─────────────────────────────

        private static bool IsTerrainCollider(Collider col)
        {
            if (col == null) return true;   // treat null as ground (safe default)

            int cacheKey = col.GetInstanceID();
            if (TerrainColliderCache.TryGetValue(cacheKey, out bool cachedResult))
                return cachedResult;

            bool isTerrain = ComputeIsTerrainCollider(col);
            if (TerrainColliderCache.Count >= MaxTerrainColliderCacheSize)
                TerrainColliderCache.Clear();

            TerrainColliderCache[cacheKey] = isTerrain;
            return isTerrain;
        }

        private static bool ComputeIsTerrainCollider(Collider col)
        {
            // Unity built-in height-map terrain.
            if (col.GetComponent<TerrainCollider>() != null) return true;

            // Component-based check — any ancestor has DesertTerrainChunk.
            if (col.GetComponentInParent<DesertTerrainChunk>() != null) return true;

            // Name-based check: walk up to the root and look for "Desert_Chunk" prefix.
            Transform t = col.transform;
            while (t.parent != null) t = t.parent;
            if (t.name.StartsWith("Desert_Chunk", System.StringComparison.OrdinalIgnoreCase))
                return true;

            // Catch-all: any MeshCollider on an object whose root name contains "Desert".
            if (col.GetComponent<MeshCollider>() != null &&
                t.name.IndexOf("Desert", System.StringComparison.OrdinalIgnoreCase) >= 0)
                return true;

            return false;
        }
    }
}
