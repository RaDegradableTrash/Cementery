using System;
using System.Collections.Generic; 
using UnityEngine; 
using UnityEngine.Rendering;

/// PBR-friendly day/night lighting controller for URP.
/// </summary>
[DefaultExecutionOrder(-800)]
[ExecuteAlways]
public class DayNightSkyboxController : MonoBehaviour
{
    private const string DefaultSkyboxResourcePath = "Skybox/MinecraftDayNightSkybox";

    [Header("Cycle")]
    [Min(10f)] public float dayLengthSeconds = 200f;
    [Range(0f, 1f)] public float timeOfDay = 0.23f;
    public bool autoAdvance = true;
    public bool useUnscaledTime = false;
    [Range(0f, 360f)] public float sunAzimuth = 165f;

    public Material skyboxTemplate;

    [Header("Sun")]
    public Light sunLight;
    [Min(0f)] public float daySunIntensity = 0.005f;
    [Min(0f)] public float nightSunIntensity = 0.03f;
    public Color daySunColor = new Color(1f, 0.95f, 0.86f, 1f);
    public Color sunriseSunColor = new Color(0.98f, 0.72f, 0.52f, 1f);
    public Color nightSunColor = new Color(0.38f, 0.46f, 0.62f, 1f);

    [Header("Sunrise / Sunset Shaping")]
    [Tooltip("Reduce direct sun intensity near the horizon so sunrise/sunset appears as a clearer red disk.")]
    public bool shapeSunIntensityByElevation = true;
    [Range(0.05f, 1f)] public float horizonIntensityFloor = 0.0f;
    [Range(0.5f, 8f)] public float horizonIntensityPower = 2.0f;
    [Range(0f, 1f)] public float sunriseRedBoost = 0.35f;
    public Color sunriseRedColor = new Color(1f, 0.36f, 0.2f, 1f);

    [Header("Performance")]
    [Tooltip("Reduce runtime spikes by throttling expensive updates.")]
    public bool optimizeForStableFrameTime = true;

    [Min(0.5f)] public float probeRendererCacheRefreshInterval = 10f;
    [Min(8)] public int probeSyncBatchSize = 128;
    [Tooltip("Dynamically reduce shadow rendering cost when direct sunlight is weak.")]
    public bool useAdaptiveShadowBudget = true;
    [Range(128, 8192)] public int adaptiveDayShadowResolution = 2048;
    [Range(128, 8192)] public int adaptiveNightShadowResolution = 1024;
    [Range(10f, 300f)] public float adaptiveDayShadowDistance = 110f;
    [Range(10f, 300f)] public float adaptiveNightShadowDistance = 60f;
    [Tooltip("Changing shadow distance continuously can cause cascade rings. Keep disabled for stable transitions.")]
    public bool adaptShadowDistanceOverDay = false;
    [Range(1f, 30f)] public float adaptiveShadowDistanceStep = 8f;

    [Header("URP Sky Godray")]
    [Tooltip("Adds a lightweight godray contribution in the custom skybox shader when using URP.")]
    public bool enableUrpSkyboxGodray = true;
    [Range(0f, 1f)] public float urpGodrayMaxStrength = 0.38f;
    [Range(0.5f, 8f)] public float urpGodrayPower = 2.6f;
    [Range(0f, 1f)] public float urpGodrayTwilightBoost = 0.2f;
    public Color urpGodrayTint = new Color(1f, 0.82f, 0.62f, 1f);

    [Header("Shadows")]
    public bool forceRealtimeShadows = true;
    public LightShadows shadowMode = LightShadows.Soft;
    [Range(0f, 1f)] public float dayShadowStrength = 0.85f;
    [Range(0f, 1f)] public float nightShadowStrength = 0.65f;
    [Range(0f, 0.2f)] public float shadowBias = 0.05f;
    [Range(0f, 1f)] public float shadowNormalBias = 0.4f;
    [Range(0.01f, 1f)] public float shadowNearPlane = 0.2f;
    [Range(0, 8192)] public int shadowCustomResolution = 4096;

    public bool enforceQualityShadowProfile = true;
    [Range(10f, 300f)] public float qualityShadowDistance = 120f;
    [Range(0f, 3f)] public float qualityShadowNearPlaneOffset = 0.2f;
    [Range(0, 4)] public int qualityShadowCascades = 4;
    [Tooltip("Clamp runtime shadow settings to reduce cascade rings and surface shadow acne.")]
    public bool enforceShadowAntiBandingProfile = true;
    [Range(20f, 120f)] public float antiBandingShadowDistance = 60f;
    [Range(0, 4)] public int antiBandingMaxCascades = 2;
    [Range(0f, 0.2f)] public float antiBandingMinShadowBias = 0.02f;
    [Range(0f, 1f)] public float antiBandingMinShadowNormalBias = 0.28f;

    [Header("Skybox (Procedural)")]
    public Color daySkyTint = new Color(0.47f, 0.52f, 0.6f, 1f);
    public Color sunsetSkyTint = new Color(0.82f, 0.58f, 0.47f, 1f);
    public Color nightSkyTint = new Color(0.04f, 0.07f, 0.14f, 1f);
    public Color dayGroundColor = new Color(0.34f, 0.37f, 0.42f, 1f);
    public Color nightGroundColor = new Color(0.02f, 0.03f, 0.05f, 1f);

    [Range(0f, 8f)] public float dayExposure = 1.0f;
    [Range(0f, 8f)] public float nightExposure = 0.2f;
    [Range(0f, 5f)] public float dayAtmosphereThickness = 0.8f;
    [Range(0f, 5f)] public float nightAtmosphereThickness = 0.28f;
    [Range(0.001f, 0.2f)] public float sunDiskSize = 4f;
    [Range(0.0005f, 0.1f)] public float sunDiskSoftness = 0.005f;

    [Header("Ambient & Fog")]
    public bool controlAmbient = true;
    [Tooltip("For PBR consistency use Trilight. Flat ambient can remove directional cues.")]
    public bool useTrilightAmbient = false;

    public Color dayAmbientSky = new Color(0.55f, 0.60f, 0.65f, 1f);
    public Color dayAmbientEquator = new Color(0.40f, 0.45f, 0.50f, 1f);
    public Color dayAmbientGround = new Color(0.30f, 0.32f, 0.35f, 1f);

    public Color nightAmbientSky = new Color(0.20f, 0.25f, 0.35f, 1f);
    public Color nightAmbientEquator = new Color(0.15f, 0.20f, 0.28f, 1f);
    public Color nightAmbientGround = new Color(0.10f, 0.12f, 0.15f, 1f);

    [Range(0f, 2f)] public float ambientIntensity = 1.2f;
    public bool controlFog = true;
    public Color dayFog = new Color(0.54f, 0.6f, 0.68f, 1f);
    public Color nightFog = new Color(0.015f, 0.02f, 0.035f, 1f);

    [Header("Reflections")]
    public bool controlReflection = true;
    [Range(0f, 2f)] public float dayReflectionIntensity = 1.2f;
    [Range(0f, 2f)] public float nightReflectionIntensity = 0.6f;
    [Min(1)] public int reflectionBounces = 1;

    public bool updateEnvironmentReflections = false;
    [Min(1.0f)] public float reflectionUpdateInterval = 5f;

    [Header("PBR Probe Integration")]
    [Tooltip("Automatically set dynamic renderers to sample Light Probes and Reflection Probes.")]
    public bool enforceDynamicProbeSampling = false;
    [Tooltip("When true, probe usage sync is repeated so runtime-spawned objects are also covered.")]
    public bool keepSyncingDynamicProbeSampling = true;
    [Min(0.1f)] public float probeSyncInterval = 2f;
    public bool includeInactiveRenderersForProbeSync = false;
    public bool logProbeAutoFixes = false;

    private static readonly int SkyTintId = Shader.PropertyToID("_SkyTint");
    private static readonly int GroundColorId = Shader.PropertyToID("_GroundColor");
    private static readonly int SunColorId = Shader.PropertyToID("_SunColor");
    private static readonly int ExposureId = Shader.PropertyToID("_Exposure");
    private static readonly int AtmosphereThicknessId = Shader.PropertyToID("_AtmosphereThickness");
    private static readonly int SunSizeId = Shader.PropertyToID("_SunSize");
    private static readonly int SunSoftnessId = Shader.PropertyToID("_SunSoftness");
    private static readonly int GodrayStrengthId = Shader.PropertyToID("_GodrayStrength");
    private static readonly int GodrayPowerId = Shader.PropertyToID("_GodrayPower");
    private static readonly int GodrayTintId = Shader.PropertyToID("_GodrayTint");
    private static readonly int AlbedoColorId = Shader.PropertyToID("_Color");

    private Material _runtimeSkybox;
    private bool _ownsSkyboxMaterial;
    private float _reflectionTimer;
    private float _probeSyncTimer;
    private float _probeCacheRefreshTimer;
    private int _probeSyncCursor;
    private readonly List<Renderer> _cachedDynamicRenderers = new List<Renderer>(256);


    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void AutoBootstrap()
    {
        DayNightSkyboxController existing = FindFirstObjectByType<DayNightSkyboxController>(FindObjectsInactive.Include);
        if (existing != null)
            return;

        GameObject runtimeGo = new GameObject("DayNightSkyboxController");
        runtimeGo.AddComponent<DayNightSkyboxController>();
    }

    private void Awake()
    {
        EnsureSunLight();
        EnsureRuntimeSkybox();

        enforceDynamicProbeSampling = true; // Enabled to run the fix pass
        if (enforceDynamicProbeSampling)
            RebuildDynamicRendererCache();

        SyncDynamicProbeSampling(true);
        ApplyCycle(0f, true);
    }

    private void Update()
    {
        float delta = useUnscaledTime ? Time.unscaledDeltaTime : Time.deltaTime;

        if (Application.isPlaying)
        {
            if (autoAdvance)
                timeOfDay = Mathf.Repeat(timeOfDay + delta / Mathf.Max(10f, dayLengthSeconds), 1f);

            if (enforceDynamicProbeSampling && keepSyncingDynamicProbeSampling)
            {
                _probeSyncTimer += delta;
                if (optimizeForStableFrameTime)
                    _probeCacheRefreshTimer += delta;

                if (optimizeForStableFrameTime && _probeCacheRefreshTimer >= probeRendererCacheRefreshInterval)
                {
                    _probeCacheRefreshTimer = 0f;
                    RebuildDynamicRendererCache();
                }

                if (_probeSyncTimer >= probeSyncInterval)
                {
                    _probeSyncTimer = 0f;
                    SyncDynamicProbeSampling();
                }
            }
        }

        ApplyCycle(delta, false);
    }

    private void OnValidate()
    {
        dayLengthSeconds = Mathf.Max(10f, dayLengthSeconds);
        reflectionUpdateInterval = Mathf.Max(0.1f, reflectionUpdateInterval);
        probeSyncInterval = Mathf.Max(0.1f, probeSyncInterval);

        dayShadowStrength = Mathf.Clamp01(dayShadowStrength);
        nightShadowStrength = Mathf.Clamp01(nightShadowStrength);
        shadowBias = Mathf.Clamp(shadowBias, 0f, 0.2f);
        shadowNormalBias = Mathf.Clamp(shadowNormalBias, 0f, 1f);
        shadowNearPlane = Mathf.Clamp(shadowNearPlane, 0.01f, 1f);
        shadowCustomResolution = Mathf.Clamp(shadowCustomResolution, 0, 8192);

        qualityShadowDistance = Mathf.Clamp(qualityShadowDistance, 10f, 300f);
        qualityShadowNearPlaneOffset = Mathf.Clamp(qualityShadowNearPlaneOffset, 0f, 3f);
        qualityShadowCascades = Mathf.Clamp(qualityShadowCascades, 0, 4);
        antiBandingShadowDistance = Mathf.Clamp(antiBandingShadowDistance, 20f, 120f);
        antiBandingMaxCascades = Mathf.Clamp(antiBandingMaxCascades, 0, 4);
        antiBandingMinShadowBias = Mathf.Clamp(antiBandingMinShadowBias, 0f, 0.2f);
        antiBandingMinShadowNormalBias = Mathf.Clamp(antiBandingMinShadowNormalBias, 0f, 1f);

        dayExposure = Mathf.Clamp(dayExposure, 0f, 8f);
        nightExposure = Mathf.Clamp(nightExposure, 0f, 8f);
        dayAtmosphereThickness = Mathf.Clamp(dayAtmosphereThickness, 0f, 5f);
        nightAtmosphereThickness = Mathf.Clamp(nightAtmosphereThickness, 0f, 5f);
        sunDiskSize = Mathf.Clamp(sunDiskSize, 0.001f, 0.2f);
        sunDiskSoftness = Mathf.Clamp(sunDiskSoftness, 0.0005f, 0.1f);

        ambientIntensity = Mathf.Clamp(ambientIntensity, 0f, 2f);
        dayReflectionIntensity = Mathf.Clamp(dayReflectionIntensity, 0f, 2f);
        nightReflectionIntensity = Mathf.Clamp(nightReflectionIntensity, 0f, 2f);
        reflectionBounces = Mathf.Max(1, reflectionBounces);



        if (Application.isPlaying)
        {
            enforceDynamicProbeSampling = true;
            if (enforceDynamicProbeSampling)
                RebuildDynamicRendererCache();

            SyncDynamicProbeSampling(true);
        }
        
        ApplyCycle(0f, true);
    }

    private void OnDestroy()
    {
        if (_ownsSkyboxMaterial && _runtimeSkybox != null)
        {
            Destroy(_runtimeSkybox);
            _runtimeSkybox = null;
        }
    }

    private void EnsureSunLight()
    {
        if (sunLight == null)
        {
            if (RenderSettings.sun != null)
            {
                sunLight = RenderSettings.sun;
            }
            else
            {
                Light[] lights = FindObjectsByType<Light>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
                
                // 1. Prioritize directional lights with "sun" in the name
                for (int i = 0; i < lights.Length; i++)
                {
                    if (lights[i] != null && lights[i].type == LightType.Directional && lights[i].name.ToLower().Contains("sun"))
                    {
                        sunLight = lights[i];
                        break;
                    }
                }

                // 2. Fallback to any directional light
                if (sunLight == null)
                {
                    for (int i = 0; i < lights.Length; i++)
                    {
                        if (lights[i] != null && lights[i].type == LightType.Directional)
                        {
                            sunLight = lights[i];
                            break;
                        }
                    }
                }
            }

            if (sunLight != null && RenderSettings.sun == null)
                RenderSettings.sun = sunLight;
        }

        // 3. Auto-disable any other active directional lights at runtime to prevent double-sun conflicts
        if (sunLight != null && Application.isPlaying)
        {
            Light[] lights = FindObjectsByType<Light>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
            for (int i = 0; i < lights.Length; i++)
            {
                Light l = lights[i];
                if (l != null && l.type == LightType.Directional && l != sunLight && l.enabled)
                {
                    Debug.LogWarning($"[DayNightSkyboxController] Auto-disabled duplicate Directional Light '{l.name}' to prevent lighting conflict.");
                    l.enabled = false;
                }
            }
        }
    }

    private void EnsureRuntimeSkybox()
    {

        if (_runtimeSkybox != null)
            return;

        Material source = skyboxTemplate;
        if (source == null)
            source = Resources.Load<Material>(DefaultSkyboxResourcePath);
        if (source == null)
            source = RenderSettings.skybox;

        if (source == null || !source.HasProperty(SkyTintId))
        {
            Shader procedural = Shader.Find("Skybox/Procedural");
            if (procedural == null)
                return;

            source = new Material(procedural);
        }

        _runtimeSkybox = new Material(source)
        {
            name = "Runtime_DayNightSkybox"
        };

        RenderSettings.skybox = _runtimeSkybox;
        _ownsSkyboxMaterial = true;
    }

    private void ApplyCycle(float deltaTime, bool forceReflectionUpdate)
    {
        EnsureSunLight();
        EnsureRuntimeSkybox();

        float sunElevation = Mathf.Sin((timeOfDay - 0.25f) * Mathf.PI * 2f);
        float daylight = Mathf.Clamp01((sunElevation + 0.08f) / 1.08f);
        daylight = Mathf.SmoothStep(0f, 1f, daylight);
        float twilight = 1f - Mathf.Clamp01(Mathf.Abs(sunElevation) / 0.28f);
        float sunAngle = timeOfDay * 360f - 90f;

        if (sunLight != null)
            ApplySunLighting(daylight, twilight, sunAngle, sunElevation);

        float godrayBlend = EvaluateGodrayBlend(daylight, twilight);

        ApplySkybox(daylight, twilight, godrayBlend);

        if (controlAmbient || controlFog)
            ApplyAmbientAndFog(daylight);

        if (controlReflection)
            ApplyReflections(daylight, deltaTime, forceReflectionUpdate);
    }

    private void ApplySunLighting(float daylight, float twilight, float sunAngle, float sunElevation)
    {
        sunLight.transform.rotation = Quaternion.Euler(sunAngle, sunAzimuth, 0f);

        float minIntensity = nightSunIntensity;
        float maxIntensity = daySunIntensity;

        float horizonAttenuation = 1f;
        if (shapeSunIntensityByElevation)
        {
            float sunAboveHorizon01 = Mathf.Clamp01((sunElevation + 0.02f) / 0.75f);
            horizonAttenuation = Mathf.Lerp(horizonIntensityFloor, 1f, Mathf.Pow(sunAboveHorizon01, horizonIntensityPower));
        }

        // --- DYNAMIC CLOUD OCCLUSION DIMMING ---
        // Dynamically queries the globally set cloud variables to evaluate overall coverage.
        // Lower thresholds and higher densities represent overcast conditions, which dims and cools the main light source.
        float cloudLightOcclusion = 1.0f;
        float cloudCoverageFactor = 0.0f;
        
        float globalThreshold = Shader.GetGlobalFloat("_CloudThreshold");
        float globalDensityScale = Shader.GetGlobalFloat("_CloudDensityScale");
        if (globalDensityScale > 0.001f)
        {
            // High coverage corresponds to low threshold settings (0 = overcast, 1 = clear)
            cloudCoverageFactor = Mathf.Clamp01(1.0f - globalThreshold);
            // Let the sun intensity dim by up to 68% under heavy overcast skies
            cloudLightOcclusion = Mathf.Lerp(1.0f, 0.32f, cloudCoverageFactor * Mathf.Clamp01(globalDensityScale * 0.4f));
        }

        sunLight.intensity = Mathf.Lerp(minIntensity, maxIntensity, daylight) * horizonAttenuation * cloudLightOcclusion;

        Color baseSunColor = Color.Lerp(nightSunColor, daySunColor, daylight);
        Color twilightColor = Color.Lerp(baseSunColor, sunriseSunColor, twilight);
        float redDiskBlend = Mathf.Clamp01(twilight * (1f - Mathf.Clamp01((sunElevation + 0.05f) / 0.9f)) * 1.35f * sunriseRedBoost);
        Color finalSunColor = Color.Lerp(twilightColor, sunriseRedColor, redDiskBlend);

        // Shift color towards cool sky reflection shadows under heavy cloud cover
        if (cloudCoverageFactor > 0.01f)
        {
            Color cloudShadowTint = new Color(0.75f, 0.82f, 0.94f); // Cool ambient skylight refraction
            finalSunColor = Color.Lerp(finalSunColor, finalSunColor * cloudShadowTint, cloudCoverageFactor * 0.45f);
        }
        sunLight.color = finalSunColor;

        if (!forceRealtimeShadows)
            return;

        if (shadowMode == LightShadows.Soft)
        {
            if (QualitySettings.shadows != ShadowQuality.All)
                QualitySettings.shadows = ShadowQuality.All;
        }
        else if (QualitySettings.shadows == ShadowQuality.Disable)
        {
            QualitySettings.shadows = ShadowQuality.All;
        }

        if (QualitySettings.shadowProjection != ShadowProjection.StableFit)
            QualitySettings.shadowProjection = ShadowProjection.StableFit;

        int targetShadowResolution = shadowCustomResolution;
        float targetShadowDistance = qualityShadowDistance;
        int targetShadowCascades = qualityShadowCascades;

        if (enforceShadowAntiBandingProfile)
        {
            targetShadowDistance = Mathf.Min(targetShadowDistance, antiBandingShadowDistance);
            targetShadowCascades = Mathf.Min(targetShadowCascades, antiBandingMaxCascades);
        }

        float runtimeShadowBias = shadowBias;
        float runtimeShadowNormalBias = shadowNormalBias;
        if (enforceShadowAntiBandingProfile)
        {
            runtimeShadowBias = Mathf.Max(runtimeShadowBias, antiBandingMinShadowBias);
            runtimeShadowNormalBias = Mathf.Max(runtimeShadowNormalBias, antiBandingMinShadowNormalBias);
        }

        sunLight.shadows = shadowMode;
        sunLight.shadowStrength = Mathf.Lerp(nightShadowStrength, dayShadowStrength, daylight);
        sunLight.shadowBias = runtimeShadowBias;
        sunLight.shadowNormalBias = runtimeShadowNormalBias;
        sunLight.shadowNearPlane = shadowNearPlane;
        if (sunLight.shadowCustomResolution != targetShadowResolution)
            sunLight.shadowCustomResolution = targetShadowResolution;

        if (!enforceQualityShadowProfile && !enforceShadowAntiBandingProfile)
            return;

        if (Mathf.Abs(QualitySettings.shadowDistance - targetShadowDistance) > 0.5f)
            QualitySettings.shadowDistance = targetShadowDistance;
        if (Mathf.Abs(QualitySettings.shadowNearPlaneOffset - qualityShadowNearPlaneOffset) > 0.0005f)
            QualitySettings.shadowNearPlaneOffset = qualityShadowNearPlaneOffset;
        if (QualitySettings.shadowCascades != targetShadowCascades)
            QualitySettings.shadowCascades = targetShadowCascades;
    }

    private float EvaluateGodrayBlend(float daylight, float twilight)
    {
        float referenceIntensity = Mathf.Max(0.0001f, daySunIntensity);

        float normalizedIntensity = daylight;
        if (sunLight != null)
            normalizedIntensity = Mathf.Clamp01(sunLight.intensity / referenceIntensity);

        float godrayBlend = Mathf.SmoothStep(0f, 1f, normalizedIntensity);
        godrayBlend = Mathf.Clamp01(godrayBlend + twilight * urpGodrayTwilightBoost);
        return godrayBlend;
    }

    private void ApplySkybox(float daylight, float twilight, float godrayBlend)
    {

        if (_runtimeSkybox == null)
            return;

        float twilightBlend = Mathf.SmoothStep(0f, 1f, twilight);
        Color baseSkyColor = Color.Lerp(nightSkyTint, daySkyTint, daylight);
        Color skyColor = Color.Lerp(baseSkyColor, sunsetSkyTint, twilightBlend);
        Color groundColor = Color.Lerp(nightGroundColor, dayGroundColor, daylight);

        float exposure = Mathf.Lerp(nightExposure, dayExposure, daylight);
        float atmosphere = Mathf.Lerp(nightAtmosphereThickness, dayAtmosphereThickness, daylight);

        if (_runtimeSkybox.HasProperty(SkyTintId))
            _runtimeSkybox.SetColor(SkyTintId, skyColor);
        if (_runtimeSkybox.HasProperty(GroundColorId))
            _runtimeSkybox.SetColor(GroundColorId, groundColor);
        if (_runtimeSkybox.HasProperty(SunColorId))
            _runtimeSkybox.SetColor(SunColorId, sunLight != null ? sunLight.color : Color.white);
        if (_runtimeSkybox.HasProperty(ExposureId))
            _runtimeSkybox.SetFloat(ExposureId, exposure);
        if (_runtimeSkybox.HasProperty(AtmosphereThicknessId))
            _runtimeSkybox.SetFloat(AtmosphereThicknessId, atmosphere);
        if (_runtimeSkybox.HasProperty(SunSizeId))
            _runtimeSkybox.SetFloat(SunSizeId, sunDiskSize);
        if (_runtimeSkybox.HasProperty(SunSoftnessId))
            _runtimeSkybox.SetFloat(SunSoftnessId, sunDiskSoftness);
        if (_runtimeSkybox.HasProperty(GodrayStrengthId))
        {
            float strength = enableUrpSkyboxGodray ? godrayBlend * urpGodrayMaxStrength : 0f;
            _runtimeSkybox.SetFloat(GodrayStrengthId, strength);
        }
        if (_runtimeSkybox.HasProperty(GodrayPowerId))
            _runtimeSkybox.SetFloat(GodrayPowerId, urpGodrayPower);
        if (_runtimeSkybox.HasProperty(GodrayTintId))
            _runtimeSkybox.SetColor(GodrayTintId, urpGodrayTint);
    }

    private void ApplyAmbientAndFog(float daylight)
    {

        if (controlAmbient)
        {
            if (useTrilightAmbient)
            {
                RenderSettings.ambientMode = AmbientMode.Trilight;
                RenderSettings.ambientSkyColor = Color.Lerp(nightAmbientSky, dayAmbientSky, daylight);
                RenderSettings.ambientEquatorColor = Color.Lerp(nightAmbientEquator, dayAmbientEquator, daylight);
                RenderSettings.ambientGroundColor = Color.Lerp(nightAmbientGround, dayAmbientGround, daylight);
            }
            else
            {
                RenderSettings.ambientMode = AmbientMode.Flat;
                Color flatAmbient = Color.Lerp(nightAmbientEquator, dayAmbientEquator, daylight);
                flatAmbient.a = 1f;
                RenderSettings.ambientLight = flatAmbient;
            }

            RenderSettings.ambientIntensity = ambientIntensity;
        }

        if (controlFog)
            RenderSettings.fogColor = Color.Lerp(nightFog, dayFog, daylight);
    }

    private void ApplyReflections(float daylight, float deltaTime, bool forceReflectionUpdate)
    {

        RenderSettings.defaultReflectionMode = DefaultReflectionMode.Skybox;
        RenderSettings.reflectionBounces = reflectionBounces;
        RenderSettings.reflectionIntensity = Mathf.Lerp(nightReflectionIntensity, dayReflectionIntensity, daylight);

        if (!updateEnvironmentReflections)
            return;

        _reflectionTimer += deltaTime;
        if (forceReflectionUpdate || _reflectionTimer >= reflectionUpdateInterval)
        {
            _reflectionTimer = 0f;
            DynamicGI.UpdateEnvironment();
        }
    }

    private void SyncDynamicProbeSampling(bool forceFullPass = false)
    {
        if (!enforceDynamicProbeSampling)
            return;

        if (!optimizeForStableFrameTime)
        {
            SyncDynamicProbeSamplingFullScan();
            return;
        }

        if (_cachedDynamicRenderers.Count == 0)
            RebuildDynamicRendererCache();

        if (_cachedDynamicRenderers.Count == 0)
            return;

        int processCount = forceFullPass
            ? _cachedDynamicRenderers.Count
            : Mathf.Min(probeSyncBatchSize, _cachedDynamicRenderers.Count);

        int changedCount = 0;
        bool sawNullRenderer = false;
        for (int i = 0; i < processCount; i++)
        {
            int index = (_probeSyncCursor + i) % _cachedDynamicRenderers.Count;
            Renderer renderer = _cachedDynamicRenderers[index];
            if (renderer == null)
            {
                sawNullRenderer = true;
                continue;
            }

            if (ApplyProbeSamplingToRenderer(renderer))
                changedCount++;
        }

        _probeSyncCursor = (_probeSyncCursor + processCount) % _cachedDynamicRenderers.Count;

        if (sawNullRenderer)
            RebuildDynamicRendererCache();

        if (logProbeAutoFixes && changedCount > 0)
            Debug.Log($"[DayNightSkyboxController] Updated probe sampling on {changedCount} dynamic renderer(s).", this);
    }

    private void SyncDynamicProbeSamplingFullScan()
    {
        if (_cachedDynamicRenderers.Count == 0)
            RebuildDynamicRendererCache();

        int changedCount = 0;
        foreach (var renderer in _cachedDynamicRenderers)
        {
            if (renderer == null) continue;

            if (ApplyProbeSamplingToRenderer(renderer))
                changedCount++;
        }

        if (logProbeAutoFixes && changedCount > 0)
            Debug.Log($"[DayNightSkyboxController] Updated probe sampling on {changedCount} dynamic renderer(s).", this);
    }

    private void RebuildDynamicRendererCache()
    {
        _cachedDynamicRenderers.Clear();
        HashSet<Renderer> unique = new HashSet<Renderer>();

        bool includeInactive = includeInactiveRenderersForProbeSync;

        // 1. SkinnedMeshRenderers
        SkinnedMeshRenderer[] smrs = FindObjectsByType<SkinnedMeshRenderer>(
            includeInactive ? FindObjectsInactive.Include : FindObjectsInactive.Exclude,
            FindObjectsSortMode.None);
        foreach (var smr in smrs)
        {
            if (smr != null && !smr.gameObject.isStatic)
                unique.Add(smr);
        }

        // 2. Renderers under Rigidbodies
        Rigidbody[] rbs = FindObjectsByType<Rigidbody>(
            includeInactive ? FindObjectsInactive.Include : FindObjectsInactive.Exclude,
            FindObjectsSortMode.None);
        foreach (var rb in rbs)
        {
            if (rb == null) continue;
            Renderer[] rbRenderers = rb.GetComponentsInChildren<Renderer>(includeInactive);
            foreach (var r in rbRenderers)
            {
                if (r != null && !r.gameObject.isStatic)
                    unique.Add(r);
            }
        }

        // 3. Renderers under Animators
        Animator[] anims = FindObjectsByType<Animator>(
            includeInactive ? FindObjectsInactive.Include : FindObjectsInactive.Exclude,
            FindObjectsSortMode.None);
        foreach (var anim in anims)
        {
            if (anim == null) continue;
            Renderer[] animRenderers = anim.GetComponentsInChildren<Renderer>(includeInactive);
            foreach (var r in animRenderers)
            {
                if (r != null && !r.gameObject.isStatic)
                    unique.Add(r);
            }
        }

        _cachedDynamicRenderers.AddRange(unique);
        _probeSyncCursor = 0;
    }

    private static bool ApplyProbeSamplingToRenderer(Renderer renderer)
    {
        bool changed = false;

        if (renderer.lightProbeUsage != LightProbeUsage.BlendProbes)
        {
            renderer.lightProbeUsage = LightProbeUsage.BlendProbes;
            changed = true;
        }

        if (renderer.reflectionProbeUsage != ReflectionProbeUsage.BlendProbes)
        {
            renderer.reflectionProbeUsage = ReflectionProbeUsage.BlendProbes;
            changed = true;
        }

        return changed;
    }
}
