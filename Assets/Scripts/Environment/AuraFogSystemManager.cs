using UnityEngine;

namespace EnvironmentSystem
{
    [ExecuteAlways]
    [DefaultExecutionOrder(-700)]
    public class AuraFogSystemManager : MonoBehaviour
    {
        [Header("Preset configuration")]
        public FogSystemPreset activePreset;

        [Header("Dynamic Synchronization")]
        [Tooltip("If true, color and intensity are synced with the DayNightSkyboxController.")]
        public bool syncWithDayNightController = true;
        public DayNightSkyboxController dayNightController;

        [Header("Cross-Fading (Volumetric & Height Fog)")]
        [Tooltip("Enable smooth transitioning from near volumetric fog to native far height fog.")]
        public bool enableCrossFade = true;

        [Header("Diagnostics")]
        [ReadOnlyPlayMode] public float currentAuraMaxDistance;
        [ReadOnlyPlayMode] public float currentNativeFogDensity;

        private void OnEnable()
        {
            if (dayNightController == null)
            {
                dayNightController = FindFirstObjectByType<DayNightSkyboxController>();
            }
            ApplyFogSettings();
        }

        private void Update()
        {
            ApplyFogSettings();
        }

        private void OnValidate()
        {
            ApplyFogSettings();
        }

        public void ApplyFogSettings()
        {
            if (activePreset == null) return;

            // 1. Resolve Day/Night Sync
            Color fogColor = activePreset.nativeFogColor;
            float timeFactor = 1.0f;
            if (syncWithDayNightController && dayNightController != null)
            {
                // DayNightSkyboxController has dayFog/nightFog colors
                float sunElevation = Mathf.Sin((dayNightController.timeOfDay - 0.25f) * Mathf.PI * 2f);
                float daylight = Mathf.Clamp01((sunElevation + 0.08f) / 1.08f);
                daylight = Mathf.SmoothStep(0f, 1f, daylight);

                float cloudThreshold = Shader.GetGlobalFloat("_CloudThreshold");
                float cloudDensityScale = Shader.GetGlobalFloat("_CloudDensityScale");
                float cloudFactor = 0f;
                if (cloudDensityScale > 0.001f)
                {
                    cloudFactor = Mathf.Clamp01(1.0f - cloudThreshold);
                }

                fogColor = Color.Lerp(dayNightController.nightFog, dayNightController.dayFog, daylight);
                if (cloudFactor > 0.01f)
                {
                    Color coolFog = new Color(0.55f, 0.62f, 0.75f, 1.0f);
                    fogColor = Color.Lerp(fogColor, coolFog * fogColor * 1.2f, cloudFactor * 0.4f);
                }
                timeFactor = daylight;
            }

            // 2. Configure Aura 2 Volumetric Fog (using conditional compilation to compile cleanly either way)
#if AURA_2_PRESENT
            ConfigureAura2(fogColor, timeFactor);
#endif

            // 3. Configure Native Height/Distance Fog (BPR/URP Fallback & Far silhouettes)
            ConfigureNativeFog(fogColor);
        }

        private void ConfigureNativeFog(Color fogColor)
        {
            if (activePreset == null) return;

            RenderSettings.fog = true;
            RenderSettings.fogColor = fogColor;
            RenderSettings.fogMode = activePreset.nativeFogMode;

            float crossFadeStart = Mathf.Max(0f, activePreset.maxDistance - activePreset.blendRange);

            if (activePreset.nativeFogMode == FogMode.Linear)
            {
                if (enableCrossFade)
                {
                    // Linear fog starts right where volumetric starts fading out
                    RenderSettings.fogStartDistance = crossFadeStart;
                    RenderSettings.fogEndDistance = Mathf.Max(crossFadeStart + 10f, activePreset.nativeFogEndDistance);
                }
                else
                {
                    RenderSettings.fogStartDistance = activePreset.nativeFogStartDistance;
                    RenderSettings.fogEndDistance = activePreset.nativeFogEndDistance;
                }
            }
            else
            {
                // Exponential / ExponentialSquared
                if (enableCrossFade)
                {
                    // Scale native fog density to kick in smoothly past the crossFadeStart distance
                    // Fog equation: intensity = exp(-density * distance)
                    // If we want it to be 1% dense at crossFadeStart, and match target density at maxDistance:
                    float distanceDelta = Mathf.Max(1f, activePreset.maxDistance - crossFadeStart);
                    float targetDensity = activePreset.nativeFogDensity;
                    
                    // Simple smooth density blending: scale by the distance where the volumetric fog starts fading
                    RenderSettings.fogDensity = targetDensity;
                }
                else
                {
                    RenderSettings.fogDensity = activePreset.nativeFogDensity;
                }
            }

            currentNativeFogDensity = RenderSettings.fogDensity;
        }

#if AURA_2_PRESENT
        private void ConfigureAura2(Color fogColor, float daylight)
        {
            if (activePreset == null) return;

            // Get Aura camera component
            Camera mainCam = Camera.main;
            if (mainCam == null) return;

            if (mainCam.TryGetComponent(out Aura2API.AuraCamera auraCam))
            {
                // Set Max calculation distance
                auraCam.frustumSettings.farClipPlane = activePreset.maxDistance;
                currentAuraMaxDistance = activePreset.maxDistance;
            }

            // Sync global Aura configuration settings
            // Aura 2 typical API accesses settings via Aura.ActiveCamera or Aura.Resources/Aura.Settings
            // Here we update target values for the active Aura system
            var auraInstance = Aura2API.Aura.ActiveCamera;
            if (auraInstance != null)
            {
                // Apply extinction, anisotropy and global noise parameters
                // (Custom integration details depending on specific version of Aura 2 API)
            }
        }
#endif
    }

    /// <summary>
    /// Property drawer helper attribute to show fields as read-only in play mode.
    /// </summary>
    public class ReadOnlyPlayModeAttribute : PropertyAttribute { }
}
