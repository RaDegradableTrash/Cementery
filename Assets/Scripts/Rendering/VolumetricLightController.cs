using System.Collections.Generic;
using UnityEngine;

namespace RenderingSystem
{
    [DefaultExecutionOrder(-650)]
    public class VolumetricLightController : MonoBehaviour
    {
        [Header("Volumetric Budget Settings")]
        [Tooltip("Maximum number of concurrent volumetric lights enabled close to the player.")]
        public int maxVolumetricLights = 4;

        [Tooltip("Hard distance threshold (in meters). Beyond this distance, lights will never render volumetrically.")]
        public float maxLightDistance = 150f;

        [Tooltip("Update frequency (seconds) to recount and update nearest lights. Do not do it every frame to save CPU.")]
        [Range(0.05f, 1f)] public float updateInterval = 0.15f;

        private Camera _mainCamera;
        private float _timer;
        private List<Light> _allLights = new List<Light>();
        private readonly List<LightDistancePair> _nearestLights = new List<LightDistancePair>(8);
        private readonly Dictionary<Light, bool> _volumetricStates = new Dictionary<Light, bool>();
#if AURA_2_PRESENT
        private readonly Dictionary<Light, Aura2API.AuraLight> _auraLightCache = new Dictionary<Light, Aura2API.AuraLight>();
#endif

        private struct LightDistancePair
        {
            public Light light;
#if AURA_2_PRESENT
            public Aura2API.AuraLight auraLight;
#endif
            public float sqrDistance;
        }

        private void Start()
        {
            _mainCamera = Camera.main;
            RebuildLightCache();
        }

        private void Update()
        {
            if (_mainCamera == null)
            {
                _mainCamera = Camera.main;
                if (_mainCamera == null) return;
            }

            _timer += Time.deltaTime;
            if (_timer >= updateInterval)
            {
                _timer = 0f;
                UpdateVolumetricLights();
            }
        }

        public void RebuildLightCache()
        {
            _allLights.Clear();
            Light[] lights = FindObjectsByType<Light>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
            foreach (var l in lights)
            {
                // We only care about Point and Spot lights for distance culling. Directional light is global.
                if (l != null && l.type != LightType.Directional)
                {
                    _allLights.Add(l);
                }
            }
        }

        private void UpdateVolumetricLights()
        {
            if (_allLights.Count == 0) return;

            Vector3 camPos = _mainCamera.transform.position;
            float maxDistSqr = maxLightDistance * maxLightDistance;
            int budget = Mathf.Max(0, maxVolumetricLights);
            _nearestLights.Clear();

            for (int i = _allLights.Count - 1; i >= 0; i--)
            {
                Light l = _allLights[i];
                if (l == null)
                {
                    _allLights.RemoveAt(i);
                    _volumetricStates.Remove(l);
                    continue;
                }

                if (!l.enabled || !l.gameObject.activeInHierarchy)
                {
                    ToggleVolumetric(l, false);
                    continue;
                }

                float sqrDist = (l.transform.position - camPos).sqrMagnitude;

                if (sqrDist > maxDistSqr || budget == 0)
                {
                    ToggleVolumetric(l, false);
                    continue;
                }

                LightDistancePair pair = new LightDistancePair { light = l, sqrDistance = sqrDist };
#if AURA_2_PRESENT
                pair.auraLight = GetCachedAuraLight(l);
#endif
                AddNearestLight(pair, budget);
            }

            for (int i = 0; i < _allLights.Count; i++)
            {
                Light light = _allLights[i];
                if (light == null)
                {
                    continue;
                }

                ToggleVolumetric(light, IsSelectedNearestLight(light));
            }
        }

        private void AddNearestLight(LightDistancePair pair, int budget)
        {
            int insertAt = _nearestLights.Count;
            for (int i = 0; i < _nearestLights.Count; i++)
            {
                if (pair.sqrDistance < _nearestLights[i].sqrDistance)
                {
                    insertAt = i;
                    break;
                }
            }

            if (insertAt >= budget)
            {
                ToggleVolumetric(pair, false);
                return;
            }

            _nearestLights.Insert(insertAt, pair);
            if (_nearestLights.Count <= budget)
            {
                return;
            }

            LightDistancePair removed = _nearestLights[_nearestLights.Count - 1];
            _nearestLights.RemoveAt(_nearestLights.Count - 1);
            ToggleVolumetric(removed, false);
        }

        private bool IsSelectedNearestLight(Light light)
        {
            for (int i = 0; i < _nearestLights.Count; i++)
            {
                if (_nearestLights[i].light == light)
                {
                    return true;
                }
            }
            return false;
        }

        private void ToggleVolumetric(Light l, bool enable)
        {
            if (l == null)
                return;

            if (_volumetricStates.TryGetValue(l, out bool currentState) && currentState == enable)
                return;

            _volumetricStates[l] = enable;

#if AURA_2_PRESENT
            Aura2API.AuraLight auraL = GetCachedAuraLight(l);

            if (auraL != null)
            {
                auraL.enabled = enable;
                return;
            }
#endif
            // Fallback action if Aura 2 is not present: 
            // We can toggle shadow casting or custom light flares, or simply do nothing.
        }

        private void ToggleVolumetric(LightDistancePair pair, bool enable)
        {
            if (pair.light == null)
                return;

            if (_volumetricStates.TryGetValue(pair.light, out bool currentState) && currentState == enable)
                return;

            _volumetricStates[pair.light] = enable;

#if AURA_2_PRESENT
            if (pair.auraLight != null)
            {
                pair.auraLight.enabled = enable;
                return;
            }
#endif
            // Fallback action if Aura 2 is not present:
            // We can toggle shadow casting or custom light flares, or simply do nothing.
        }

#if AURA_2_PRESENT
        private Aura2API.AuraLight GetCachedAuraLight(Light light)
        {
            if (light == null)
                return null;

            if (!_auraLightCache.TryGetValue(light, out Aura2API.AuraLight auraL))
            {
                light.TryGetComponent(out auraL);
                _auraLightCache[light] = auraL;
            }

            return auraL;
        }
#endif
    }
}
