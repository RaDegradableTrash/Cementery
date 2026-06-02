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
        private List<LightDistancePair> _measuredLights = new List<LightDistancePair>();

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
            _measuredLights.Clear();

            // 1. Calculate sqrMagnitude distance for cached lights
            for (int i = _allLights.Count - 1; i >= 0; i--)
            {
                Light l = _allLights[i];
                if (l == null)
                {
                    _allLights.RemoveAt(i);
                    continue;
                }

                if (!l.enabled || !l.gameObject.activeInHierarchy)
                {
                    continue;
                }

                float sqrDist = (l.transform.position - camPos).sqrMagnitude;
                
                // Hard distance culling
                if (sqrDist <= maxDistSqr)
                {
                    LightDistancePair pair = new LightDistancePair { light = l, sqrDistance = sqrDist };
#if AURA_2_PRESENT
                    if (l.TryGetComponent(out Aura2API.AuraLight auraL))
                    {
                        pair.auraLight = auraL;
                    }
#endif
                    _measuredLights.Add(pair);
                }
                else
                {
                    // Disable volumetric components if outside hard range
                    ToggleVolumetric(l, false);
                }
            }

            // 2. Sort by distance (closest first)
            _measuredLights.Sort((a, b) => a.sqrDistance.CompareTo(b.sqrDistance));

            // 3. Enable closest N lights, disable the rest
            for (int i = 0; i < _measuredLights.Count; i++)
            {
                bool enableVolumetric = (i < maxVolumetricLights);
                ToggleVolumetric(_measuredLights[i], enableVolumetric);
            }
        }

        private void ToggleVolumetric(Light l, bool enable)
        {
#if AURA_2_PRESENT
            if (l.TryGetComponent(out Aura2API.AuraLight auraL))
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
#if AURA_2_PRESENT
            if (pair.auraLight != null)
            {
                pair.auraLight.enabled = enable;
                return;
            }
#endif
            ToggleVolumetric(pair.light, enable);
        }
    }
}
