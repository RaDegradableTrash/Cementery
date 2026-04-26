using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// PBR-friendly day/night lighting controller with built-in fallback and HDRP support.
/// In HDRP projects it can auto-configure a Physically Based Sky volume override.
/// </summary>
[DefaultExecutionOrder(-800)]
public class DayNightSkyboxController : MonoBehaviour
{
    private const string DefaultSkyboxResourcePath = "Skybox/MinecraftDayNightSkybox";

    [Header("Cycle")]
    [Min(10f)] public float dayLengthSeconds = 200f;
    [Range(0f, 1f)] public float timeOfDay = 0.23f;
    public bool autoAdvance = true;
    public bool useUnscaledTime = false;
    [Range(0f, 360f)] public float sunAzimuth = 165f;

    [Header("Skybox Source")]
    [Tooltip("Optional template material. If empty, uses Resources/Skybox/MinecraftDayNightSkybox or current RenderSettings skybox.")]
    public Material skyboxTemplate;

    [Header("HDRP Physically Based Sky")]
    [Tooltip("When HDRP is active, prefer Physically Based Sky over built-in procedural skybox.")]
    public bool preferHdrpPhysicallyBasedSky = false;
    [Tooltip("If no global volume exists, create a runtime global volume/profile for HDRP sky.")]
    public bool createRuntimeHdrpSkyVolume = true;
    [Tooltip("Optional global volume used to host VisualEnvironment and PhysicallyBasedSky overrides.")]
    public Volume hdrpSkyVolume;
    [Tooltip("Optional profile override. If not assigned, the volume profile is used.")]
    public VolumeProfile hdrpSkyProfile;

    [Header("Sun")]
    public Light sunLight;
    [Min(0f)] public float daySunIntensity = 0.07f;
    [Min(0f)] public float nightSunIntensity = 0.03f;
    public Color daySunColor = new Color(1f, 0.95f, 0.86f, 1f);
    public Color sunriseSunColor = new Color(0.98f, 0.72f, 0.52f, 1f);
    public Color nightSunColor = new Color(0.38f, 0.46f, 0.62f, 1f);
    [Tooltip("Use physical Lux values for directional light intensity when HDRP sky mode is active.")]
    public bool useHdrpPhysicalSunIntensity = true;
    [Min(1000f)] public float hdrpDaySunIntensityLux = 110000f;
    [Min(0f)] public float hdrpNightSunIntensityLux = 0.35f;

    [Header("Sunrise / Sunset Shaping")]
    [Tooltip("Reduce direct sun intensity near the horizon so sunrise/sunset appears as a clearer red disk.")]
    public bool shapeSunIntensityByElevation = true;
    [Range(0.05f, 1f)] public float horizonIntensityFloor = 0.0f;
    [Range(0.5f, 8f)] public float horizonIntensityPower = 2.0f;
    [Range(0f, 1f)] public float sunriseRedBoost = 0.35f;
    public Color sunriseRedColor = new Color(1f, 0.36f, 0.2f, 1f);

    [Header("HDRP Godray")]
    [Tooltip("Drive HDRP volumetric godray strength from current sun intensity.")]
    public bool enableAutoHdrpGodray = true;
    [Range(0f, 1f)] public float godrayIntensityThresholdNormalized = 0.62f;
    public bool forceEnableVolumetricFogForGodray = true;
    [Tooltip("Disable volumetric fog when godray is effectively inactive to reduce GPU cost.")]
    public bool disableHdrpVolumetricsWhenGodrayInactive = true;
    [Range(0f, 1f)] public float godrayVolumetricEnableThreshold = 0.04f;
    [Range(0f, 16f)] public float godrayVolumetricLightDimmerOn = 1.35f;
    [Range(0f, 16f)] public float godrayVolumetricLightDimmerOff = 0.2f;
    [Min(1f)] public float godrayMeanFreePathOn = 90f;
    [Min(1f)] public float godrayMeanFreePathOff = 420f;
    [Range(-1f, 1f)] public float godrayAnisotropyOn = 0.78f;
    [Range(-1f, 1f)] public float godrayAnisotropyOff = 0.05f;
    [Range(0f, 1f)] public float godrayGlobalProbeDimmerOn = 0.45f;
    [Range(0f, 1f)] public float godrayGlobalProbeDimmerOff = 1f;
    public Color godrayAlbedoOn = new Color(1f, 0.95f, 0.9f, 1f);
    public Color godrayAlbedoOff = Color.white;

    [Header("Performance")]
    [Tooltip("Reduce runtime spikes by throttling expensive updates.")]
    public bool optimizeForStableFrameTime = true;
    [Min(0f)] public float hdrpGodrayUpdateInterval = 0.08f;
    [Range(0f, 0.25f)] public float hdrpGodrayUpdateEpsilon = 0.01f;
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
    [Tooltip("Clamp HDRP volumetric fog quality to a budget-friendly setting.")]
    public bool clampHdrpVolumetricQuality = true;
    [Range(6.25f, 50f)] public float hdrpFogScreenResolutionPercentage = 12.5f;
    [Range(16, 128)] public int hdrpFogVolumeSliceCount = 48;
    [Min(16f)] public float hdrpFogDepthExtent = 56f;
    public bool hdrpDirectionalLightsOnly = true;
    [Range(0, 2)] public int hdrpFogDenoisingMode = 0;

    [Header("URP Plugin Bridge")]
    [Tooltip("Send normalized daylight/twilight/godray values into a third-party weather or post-process component via reflection.")]
    public bool driveUrpPluginBridge = true;
    [Tooltip("If no bridge target is assigned, auto-discover a component that exposes at least one configured float member.")]
    public bool autoFindPluginBridgeTarget = true;
    public MonoBehaviour pluginBridgeTarget;
    [Tooltip("Target float property/field name for daylight value (0..1).")]
    public string pluginDaylightMember = "daylight";
    [Tooltip("Target float property/field name for twilight value (0..1).")]
    public string pluginTwilightMember = "twilight";
    [Tooltip("Target float property/field name for godray blend value (0..1).")]
    public string pluginGodrayMember = "godrayBlend";
    public bool logPluginBridgeWarnings = false;

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
    [Range(0f, 1f)] public float dayShadowStrength = 1f;
    [Range(0f, 1f)] public float nightShadowStrength = 0.85f;
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
    [Range(0.001f, 0.05f)] public float sunDiskSize = 0.008f;
    [Range(0.0005f, 0.03f)] public float sunDiskSoftness = 0.0018f;

    [Header("Ambient & Fog")]
    public bool controlAmbient = true;
    [Tooltip("For PBR consistency use Trilight. Flat ambient can remove directional cues.")]
    public bool useTrilightAmbient = true;

    public Color dayAmbientSky = new Color(0.24f, 0.29f, 0.34f, 1f);
    public Color dayAmbientEquator = new Color(0.19f, 0.21f, 0.24f, 1f);
    public Color dayAmbientGround = new Color(0.1f, 0.11f, 0.12f, 1f);

    public Color nightAmbientSky = new Color(0.06f, 0.07f, 0.09f, 1f);
    public Color nightAmbientEquator = new Color(0.04f, 0.05f, 0.06f, 1f);
    public Color nightAmbientGround = new Color(0.02f, 0.02f, 0.025f, 1f);

    [Range(0f, 2f)] public float ambientIntensity = 1f;
    public bool controlFog = true;
    public Color dayFog = new Color(0.54f, 0.6f, 0.68f, 1f);
    public Color nightFog = new Color(0.015f, 0.02f, 0.035f, 1f);

    [Header("Reflections")]
    public bool controlReflection = true;
    [Range(0f, 2f)] public float dayReflectionIntensity = 1f;
    [Range(0f, 2f)] public float nightReflectionIntensity = 0.35f;
    [Min(1)] public int reflectionBounces = 1;

    public bool updateEnvironmentReflections = true;
    [Min(0.1f)] public float reflectionUpdateInterval = 1f;

    [Header("PBR Probe Integration")]
    [Tooltip("Automatically set dynamic renderers to sample Light Probes and Reflection Probes.")]
    public bool enforceDynamicProbeSampling = true;
    [Tooltip("When true, probe usage sync is repeated so runtime-spawned objects are also covered.")]
    public bool keepSyncingDynamicProbeSampling = true;
    [Min(0.1f)] public float probeSyncInterval = 2f;
    public bool includeInactiveRenderersForProbeSync = false;
    public bool logProbeAutoFixes = false;

    [Header("PBR Validation")]
    public bool validatePbrSetupOnStart = true;
    public bool warnIfColorSpaceNotLinear = true;
    public bool warnIfNoReflectionProbe = true;
    public bool warnIfNoLightProbeGroup = true;
    public bool warnForSuspiciousAlbedoRange = true;
    [Range(0, 255)] public int minAlbedoSrgb = 30;
    [Range(0, 255)] public int maxAlbedoSrgb = 240;
    public bool logValidationSummary = true;

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
    private float _godrayUpdateTimer;
    private float _lastGodrayBlend = -1f;
    private int _probeSyncCursor;
    private readonly List<Renderer> _cachedDynamicRenderers = new List<Renderer>(256);
    private int _lastBridgeTargetSearchFrame = -1;
    private readonly HashSet<string> _bridgeMissingMemberWarnings = new HashSet<string>();
    private int _bridgeWarningTargetId;
    private float _appliedAdaptiveShadowDistance = -1f;

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

        if (validatePbrSetupOnStart)
            ValidatePbrSetupInternal();

        if (enforceDynamicProbeSampling)
            RebuildDynamicRendererCache();

        SyncDynamicProbeSampling(true);
        ApplyCycle(0f, true);
    }

    private void Update()
    {
        float delta = useUnscaledTime ? Time.unscaledDeltaTime : Time.deltaTime;

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

        ApplyCycle(delta, false);
    }

    private void OnValidate()
    {
        dayLengthSeconds = Mathf.Max(10f, dayLengthSeconds);
        reflectionUpdateInterval = Mathf.Max(0.1f, reflectionUpdateInterval);
        probeSyncInterval = Mathf.Max(0.1f, probeSyncInterval);

        daySunIntensity = Mathf.Max(0f, daySunIntensity);
        nightSunIntensity = Mathf.Max(0f, nightSunIntensity);
        hdrpDaySunIntensityLux = Mathf.Max(1000f, hdrpDaySunIntensityLux);
        hdrpNightSunIntensityLux = Mathf.Max(0f, hdrpNightSunIntensityLux);
        horizonIntensityFloor = Mathf.Clamp(horizonIntensityFloor, 0.05f, 1f);
        horizonIntensityPower = Mathf.Clamp(horizonIntensityPower, 0.5f, 8f);
        sunriseRedBoost = Mathf.Clamp01(sunriseRedBoost);
        godrayIntensityThresholdNormalized = Mathf.Clamp01(godrayIntensityThresholdNormalized);
        godrayVolumetricEnableThreshold = Mathf.Clamp01(godrayVolumetricEnableThreshold);
        godrayVolumetricLightDimmerOn = Mathf.Clamp(godrayVolumetricLightDimmerOn, 0f, 16f);
        godrayVolumetricLightDimmerOff = Mathf.Clamp(godrayVolumetricLightDimmerOff, 0f, 16f);
        godrayMeanFreePathOn = Mathf.Max(1f, godrayMeanFreePathOn);
        godrayMeanFreePathOff = Mathf.Max(1f, godrayMeanFreePathOff);
        godrayAnisotropyOn = Mathf.Clamp(godrayAnisotropyOn, -1f, 1f);
        godrayAnisotropyOff = Mathf.Clamp(godrayAnisotropyOff, -1f, 1f);
        godrayGlobalProbeDimmerOn = Mathf.Clamp01(godrayGlobalProbeDimmerOn);
        godrayGlobalProbeDimmerOff = Mathf.Clamp01(godrayGlobalProbeDimmerOff);
        urpGodrayMaxStrength = Mathf.Clamp01(urpGodrayMaxStrength);
        urpGodrayPower = Mathf.Clamp(urpGodrayPower, 0.5f, 8f);
        urpGodrayTwilightBoost = Mathf.Clamp01(urpGodrayTwilightBoost);
        hdrpGodrayUpdateInterval = Mathf.Max(0f, hdrpGodrayUpdateInterval);
        hdrpGodrayUpdateEpsilon = Mathf.Clamp(hdrpGodrayUpdateEpsilon, 0f, 0.25f);
        probeRendererCacheRefreshInterval = Mathf.Max(0.5f, probeRendererCacheRefreshInterval);
        probeSyncBatchSize = Mathf.Max(8, probeSyncBatchSize);
        adaptiveDayShadowResolution = Mathf.Clamp(adaptiveDayShadowResolution, 128, 8192);
        adaptiveNightShadowResolution = Mathf.Clamp(adaptiveNightShadowResolution, 128, adaptiveDayShadowResolution);
        adaptiveDayShadowDistance = Mathf.Clamp(adaptiveDayShadowDistance, 10f, 300f);
        adaptiveNightShadowDistance = Mathf.Clamp(adaptiveNightShadowDistance, 10f, adaptiveDayShadowDistance);
        adaptiveShadowDistanceStep = Mathf.Clamp(adaptiveShadowDistanceStep, 1f, 30f);
        hdrpFogScreenResolutionPercentage = Mathf.Clamp(hdrpFogScreenResolutionPercentage, 6.25f, 50f);
        hdrpFogVolumeSliceCount = Mathf.Clamp(hdrpFogVolumeSliceCount, 16, 128);
        hdrpFogDepthExtent = Mathf.Max(16f, hdrpFogDepthExtent);

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
        sunDiskSize = Mathf.Clamp(sunDiskSize, 0.001f, 0.05f);
        sunDiskSoftness = Mathf.Clamp(sunDiskSoftness, 0.0005f, 0.03f);

        ambientIntensity = Mathf.Clamp(ambientIntensity, 0f, 2f);
        dayReflectionIntensity = Mathf.Clamp(dayReflectionIntensity, 0f, 2f);
        nightReflectionIntensity = Mathf.Clamp(nightReflectionIntensity, 0f, 2f);
        reflectionBounces = Mathf.Max(1, reflectionBounces);

        minAlbedoSrgb = Mathf.Clamp(minAlbedoSrgb, 0, 255);
        maxAlbedoSrgb = Mathf.Clamp(maxAlbedoSrgb, minAlbedoSrgb, 255);

        _lastGodrayBlend = -1f;
        _appliedAdaptiveShadowDistance = -1f;

        if (Application.isPlaying)
        {
            if (enforceDynamicProbeSampling)
                RebuildDynamicRendererCache();

            SyncDynamicProbeSampling(true);
            ApplyCycle(0f, true);
        }
    }

    private void OnDestroy()
    {
        if (_ownsSkyboxMaterial && _runtimeSkybox != null)
        {
            Destroy(_runtimeSkybox);
            _runtimeSkybox = null;
        }
    }

    [ContextMenu("Validate PBR Setup Now")]
    private void ValidatePbrSetupContextMenu()
    {
        ValidatePbrSetupInternal();
    }

    private void EnsureSunLight()
    {
        if (sunLight != null)
            return;

        if (RenderSettings.sun != null)
        {
            sunLight = RenderSettings.sun;
            return;
        }

        Light[] lights = FindObjectsByType<Light>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
        for (int i = 0; i < lights.Length; i++)
        {
            if (lights[i] != null && lights[i].type == LightType.Directional)
            {
                sunLight = lights[i];
                break;
            }
        }

        if (sunLight != null && RenderSettings.sun == null)
            RenderSettings.sun = sunLight;
    }

    private void EnsureRuntimeSkybox()
    {
        if (IsUsingHdrpSky())
        {
            EnsureHdrpPhysicallyBasedSkySetup();
            return;
        }

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

        if (IsUsingHdrpSky() && enableAutoHdrpGodray)
            ApplyHdrpGodrayFromBlend(deltaTime, godrayBlend);

        ApplyUrpPluginBridge(daylight, twilight, godrayBlend);

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
        if (IsUsingHdrpSky() && useHdrpPhysicalSunIntensity)
        {
            minIntensity = hdrpNightSunIntensityLux;
            maxIntensity = hdrpDaySunIntensityLux;
        }

        float horizonAttenuation = 1f;
        if (shapeSunIntensityByElevation)
        {
            float sunAboveHorizon01 = Mathf.Clamp01((sunElevation + 0.02f) / 0.75f);
            horizonAttenuation = Mathf.Lerp(horizonIntensityFloor, 1f, Mathf.Pow(sunAboveHorizon01, horizonIntensityPower));
        }

        sunLight.intensity = Mathf.Lerp(minIntensity, maxIntensity, daylight) * horizonAttenuation;

        Color baseSunColor = Color.Lerp(nightSunColor, daySunColor, daylight);
        Color twilightColor = Color.Lerp(baseSunColor, sunriseSunColor, twilight);
        float redDiskBlend = Mathf.Clamp01(twilight * (1f - Mathf.Clamp01((sunElevation + 0.05f) / 0.9f)) * 1.35f * sunriseRedBoost);
        sunLight.color = Color.Lerp(twilightColor, sunriseRedColor, redDiskBlend);

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
        if (optimizeForStableFrameTime && useAdaptiveShadowBudget)
        {
            targetShadowResolution = GetAdaptiveShadowResolution(daylight);
            if (adaptShadowDistanceOverDay)
                targetShadowDistance = GetAdaptiveShadowDistance(daylight);
        }

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

    private int GetAdaptiveShadowResolution(float daylight)
    {
        int target = Mathf.RoundToInt(Mathf.Lerp(adaptiveNightShadowResolution, adaptiveDayShadowResolution, daylight));
        target = Mathf.Clamp(target, 128, 8192);
        target = Mathf.RoundToInt(target / 64f) * 64;
        return Mathf.Clamp(target, 128, 8192);
    }

    private float GetAdaptiveShadowDistance(float daylight)
    {
        float target = Mathf.Lerp(adaptiveNightShadowDistance, adaptiveDayShadowDistance, daylight);
        float step = Mathf.Max(1f, adaptiveShadowDistanceStep);
        float quantized = Mathf.Round(target / step) * step;

        if (_appliedAdaptiveShadowDistance < 0f)
            _appliedAdaptiveShadowDistance = quantized;
        else if (Mathf.Abs(quantized - _appliedAdaptiveShadowDistance) >= step * 0.5f)
            _appliedAdaptiveShadowDistance = quantized;

        return Mathf.Clamp(_appliedAdaptiveShadowDistance, 10f, 300f);
    }

    private float EvaluateGodrayBlend(float daylight, float twilight)
    {
        float referenceIntensity = IsUsingHdrpSky() && useHdrpPhysicalSunIntensity
            ? Mathf.Max(1f, hdrpDaySunIntensityLux)
            : Mathf.Max(0.0001f, daySunIntensity);

        float normalizedIntensity = daylight;
        if (sunLight != null)
            normalizedIntensity = Mathf.Clamp01(sunLight.intensity / referenceIntensity);

        float godrayBlend = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(godrayIntensityThresholdNormalized, 1f, normalizedIntensity));
        godrayBlend = Mathf.Clamp01(godrayBlend + twilight * urpGodrayTwilightBoost);
        return godrayBlend;
    }

    private void ApplyHdrpGodrayFromBlend(float deltaTime, float godrayBlend)
    {
        if (sunLight == null)
            return;

        if (optimizeForStableFrameTime && hdrpGodrayUpdateInterval > 0f)
        {
            _godrayUpdateTimer += deltaTime;
            if (_godrayUpdateTimer < hdrpGodrayUpdateInterval)
                return;

            _godrayUpdateTimer = 0f;
        }

        if (optimizeForStableFrameTime && _lastGodrayBlend >= 0f && Mathf.Abs(godrayBlend - _lastGodrayBlend) < hdrpGodrayUpdateEpsilon)
            return;

        _lastGodrayBlend = godrayBlend;
    }

    private void ApplyUrpPluginBridge(float daylight, float twilight, float godrayBlend)
    {
        if (!driveUrpPluginBridge)
            return;

        EnsureBridgeTargetResolved();
        if (pluginBridgeTarget == null)
            return;

        TrySetBridgeFloat(pluginDaylightMember, daylight);
        TrySetBridgeFloat(pluginTwilightMember, twilight);
        TrySetBridgeFloat(pluginGodrayMember, godrayBlend);
    }

    private void EnsureBridgeTargetResolved()
    {
        if (pluginBridgeTarget != null || !autoFindPluginBridgeTarget)
            return;

        if (_lastBridgeTargetSearchFrame == Time.frameCount)
            return;

        _lastBridgeTargetSearchFrame = Time.frameCount;

        MonoBehaviour[] candidates = FindObjectsByType<MonoBehaviour>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        for (int i = 0; i < candidates.Length; i++)
        {
            MonoBehaviour candidate = candidates[i];
            if (candidate == null || candidate == this)
                continue;

            Type candidateType = candidate.GetType();
            if (HasWritableFloatMember(candidateType, pluginDaylightMember) ||
                HasWritableFloatMember(candidateType, pluginTwilightMember) ||
                HasWritableFloatMember(candidateType, pluginGodrayMember))
            {
                pluginBridgeTarget = candidate;
                _bridgeWarningTargetId = 0;
                _bridgeMissingMemberWarnings.Clear();
                return;
            }
        }
    }

    private bool TrySetBridgeFloat(string memberName, float value)
    {
        if (pluginBridgeTarget == null || string.IsNullOrWhiteSpace(memberName))
            return false;

        Type targetType = pluginBridgeTarget.GetType();
        int targetId = pluginBridgeTarget.GetInstanceID();
        if (targetId != _bridgeWarningTargetId)
        {
            _bridgeWarningTargetId = targetId;
            _bridgeMissingMemberWarnings.Clear();
        }

        PropertyInfo property = targetType.GetProperty(memberName, BindingFlags.Instance | BindingFlags.Public);
        if (property != null && property.CanWrite && property.PropertyType == typeof(float))
        {
            property.SetValue(pluginBridgeTarget, value);
            return true;
        }

        FieldInfo field = targetType.GetField(memberName, BindingFlags.Instance | BindingFlags.Public);
        if (field != null && field.FieldType == typeof(float))
        {
            field.SetValue(pluginBridgeTarget, value);
            return true;
        }

        string warningKey = targetType.FullName + "." + memberName;
        if (logPluginBridgeWarnings && _bridgeMissingMemberWarnings.Add(warningKey))
            Debug.LogWarning($"[DayNightSkyboxController] Plugin bridge member '{memberName}' was not found as a writable float on '{targetType.Name}'.", pluginBridgeTarget);

        return false;
    }

    private static bool HasWritableFloatMember(Type type, string memberName)
    {
        if (type == null || string.IsNullOrWhiteSpace(memberName))
            return false;

        PropertyInfo property = type.GetProperty(memberName, BindingFlags.Instance | BindingFlags.Public);
        if (property != null && property.CanWrite && property.PropertyType == typeof(float))
            return true;

        FieldInfo field = type.GetField(memberName, BindingFlags.Instance | BindingFlags.Public);
        return field != null && field.FieldType == typeof(float);
    }

    private void ApplySkybox(float daylight, float twilight, float godrayBlend)
    {
        if (IsUsingHdrpSky())
            return;

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
        if (IsUsingHdrpSky())
            return;

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
        if (IsUsingHdrpSky())
            return;

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
        Renderer[] renderers = FindObjectsByType<Renderer>(
            includeInactiveRenderersForProbeSync ? FindObjectsInactive.Include : FindObjectsInactive.Exclude,
            FindObjectsSortMode.None);

        int changedCount = 0;
        for (int i = 0; i < renderers.Length; i++)
        {
            Renderer renderer = renderers[i];
            if (renderer == null)
                continue;

            if (!IsDynamicRenderer(renderer))
                continue;

            if (ApplyProbeSamplingToRenderer(renderer))
                changedCount++;
        }

        if (logProbeAutoFixes && changedCount > 0)
            Debug.Log($"[DayNightSkyboxController] Updated probe sampling on {changedCount} dynamic renderer(s).", this);
    }

    private void RebuildDynamicRendererCache()
    {
        _cachedDynamicRenderers.Clear();

        Renderer[] renderers = FindObjectsByType<Renderer>(
            includeInactiveRenderersForProbeSync ? FindObjectsInactive.Include : FindObjectsInactive.Exclude,
            FindObjectsSortMode.None);

        for (int i = 0; i < renderers.Length; i++)
        {
            Renderer renderer = renderers[i];
            if (renderer == null)
                continue;

            if (!IsDynamicRenderer(renderer))
                continue;

            _cachedDynamicRenderers.Add(renderer);
        }

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

    private static bool IsDynamicRenderer(Renderer renderer)
    {
        if (renderer == null)
            return false;

        if (renderer.gameObject.isStatic)
            return false;

        if (renderer is SkinnedMeshRenderer)
            return true;

        if (renderer.GetComponentInParent<Rigidbody>() != null)
            return true;

        if (renderer.GetComponentInParent<CharacterController>() != null)
            return true;

        if (renderer.GetComponentInParent<Animator>() != null)
            return true;

        return false;
    }

    private void ValidatePbrSetupInternal()
    {
        int warningCount = 0;

        if (preferHdrpPhysicallyBasedSky)
        {
            if (!IsHdrpActive())
            {
                warningCount++;
                Debug.LogWarning("[DayNightSkyboxController] preferHdrpPhysicallyBasedSky is enabled, but HDRP pipeline is not active.", this);
            }
            else
            {
                warningCount += ValidateHdrpSkySetupWarnings();
            }
        }

        if (warnIfColorSpaceNotLinear && QualitySettings.activeColorSpace != ColorSpace.Linear)
        {
            warningCount++;
            Debug.LogWarning("[DayNightSkyboxController] PBR recommendation: set Player Settings > Color Space to Linear.", this);
        }

        if (warnIfNoReflectionProbe)
        {
            ReflectionProbe[] probes = FindObjectsByType<ReflectionProbe>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
            if (probes.Length == 0)
            {
                warningCount++;
                Debug.LogWarning("[DayNightSkyboxController] No ReflectionProbe found in scene. Metals and smooth surfaces may look incorrect in shadow.", this);
            }
        }

        if (warnIfNoLightProbeGroup)
        {
            LightProbeGroup[] probeGroups = FindObjectsByType<LightProbeGroup>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
            if (probeGroups.Length == 0)
            {
                warningCount++;
                Debug.LogWarning("[DayNightSkyboxController] No LightProbeGroup found in scene. Dynamic objects crossing shadow boundaries may shade incorrectly.", this);
            }
        }

        if (warnForSuspiciousAlbedoRange)
            warningCount += ValidateAlbedoRangeWarnings();

        if (logValidationSummary)
            Debug.Log($"[DayNightSkyboxController] PBR validation completed with {warningCount} warning(s).", this);
    }

    private int ValidateAlbedoRangeWarnings()
    {
        int warnings = 0;
        float minLuma = minAlbedoSrgb / 255f;
        float maxLuma = maxAlbedoSrgb / 255f;

        Renderer[] renderers = FindObjectsByType<Renderer>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
        HashSet<Material> uniqueMaterials = new HashSet<Material>();

        for (int i = 0; i < renderers.Length; i++)
        {
            Renderer renderer = renderers[i];
            if (renderer == null)
                continue;

            Material[] mats = renderer.sharedMaterials;
            if (mats == null)
                continue;

            for (int m = 0; m < mats.Length; m++)
            {
                Material mat = mats[m];
                if (mat != null)
                    uniqueMaterials.Add(mat);
            }
        }

        foreach (Material mat in uniqueMaterials)
        {
            if (mat == null || !mat.HasProperty(AlbedoColorId))
                continue;

            if (!LooksLikeLitShader(mat.shader))
                continue;

            Color gammaColor = mat.GetColor(AlbedoColorId).gamma;
            float luma = 0.2126f * gammaColor.r + 0.7152f * gammaColor.g + 0.0722f * gammaColor.b;

            if (luma < minLuma || luma > maxLuma)
            {
                warnings++;
                Debug.LogWarning(
                    $"[DayNightSkyboxController] Material '{mat.name}' has albedo luminance {luma:F2} (sRGB-like), outside recommended [{minLuma:F2}, {maxLuma:F2}] range.",
                    mat);
            }
        }

        return warnings;
    }

    private static bool LooksLikeLitShader(Shader shader)
    {
        if (shader == null)
            return false;

        string name = shader.name;
        return name.Contains("Standard") || name.Contains("Lit") || name.Contains("PBR");
    }

    private bool IsUsingHdrpSky()
    {
        return preferHdrpPhysicallyBasedSky && IsHdrpActive();
    }

    private static bool IsHdrpActive()
    {
        RenderPipelineAsset pipeline = GraphicsSettings.currentRenderPipeline;
        if (pipeline == null)
            return false;

        return pipeline.GetType().Name.Contains("HDRenderPipelineAsset");
    }

    private void EnsureHdrpPhysicallyBasedSkySetup()
    {
        // URP migration path: keep method for serialized compatibility and no-op semantics.
        TryResolveHdrpSkyProfile(out _, createRuntimeHdrpSkyVolume);
    }

    private void ApplyHdrpVolumetricPerformanceSetup(VolumeProfile profile)
    {
        // URP migration path: no HDRP volume overrides are applied.
    }

    private bool TryResolveHdrpSkyProfile(out VolumeProfile profile, bool createIfMissing)
    {
        profile = null;

        if (hdrpSkyProfile != null)
        {
            profile = hdrpSkyProfile;
            return true;
        }

        if (hdrpSkyVolume == null)
        {
            Volume[] volumes = FindObjectsByType<Volume>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            float bestPriority = float.NegativeInfinity;
            for (int i = 0; i < volumes.Length; i++)
            {
                Volume candidate = volumes[i];
                if (candidate == null || !candidate.isGlobal)
                    continue;

                if (candidate.priority > bestPriority)
                {
                    bestPriority = candidate.priority;
                    hdrpSkyVolume = candidate;
                }
            }
        }

        if (hdrpSkyVolume == null && createIfMissing)
        {
            GameObject go = new GameObject("URP_GlobalVolume_Bridge");
            hdrpSkyVolume = go.AddComponent<Volume>();
            hdrpSkyVolume.isGlobal = true;
            hdrpSkyVolume.priority = 100f;
            hdrpSkyVolume.weight = 1f;
        }

        if (hdrpSkyVolume == null)
            return false;

        profile = hdrpSkyVolume.sharedProfile;
        if (profile == null && createIfMissing)
        {
            profile = ScriptableObject.CreateInstance<VolumeProfile>();
            profile.name = "Runtime_URP_GlobalVolume_Profile";
            hdrpSkyVolume.sharedProfile = profile;
        }

        if (profile == null)
            return false;

        hdrpSkyProfile = profile;
        return true;
    }

    private int ValidateHdrpSkySetupWarnings()
    {
        if (!IsHdrpActive())
            return 0;

        if (TryResolveHdrpSkyProfile(out _, false))
            return 0;

        Debug.LogWarning("[DayNightSkyboxController] HDRP pipeline detected but no global volume profile is assigned.", this);
        return 1;
    }
}
