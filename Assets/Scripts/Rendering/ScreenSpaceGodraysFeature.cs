using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

public class ScreenSpaceGodraysFeature : ScriptableRendererFeature
{
    [System.Serializable]
    public class GodraySettings
    {
        [Header("Intensity & Colors")]
        [Range(0.0f, 5.0f)] public float intensity = 1.5f;
        [Range(0.0f, 1.0f)] public float threshold = 0.72f;
        public Color rayColor = new Color(1.0f, 0.9f, 0.72f, 1.0f);

        [Header("Radial Blur Settings")]
        [Range(0.0f, 2.0f)] public float blurWidth = 0.85f;
        [Range(4, 32)] public int samples = 16;

        [Header("Performance Settings")]
        [Tooltip("Downsampling factor for godray buffers. 2 = Half Resolution, 4 = Quarter Resolution (highly recommended for performance).")]
        [Range(1, 4)] public int downsample = 2;

        [Header("Shader Reference")]
        public Shader godraysShader;
    }

    public GodraySettings settings = new GodraySettings();
    private ScreenSpaceGodraysPass _godraysPass;

    public override void Create()
    {
        // Auto-find the shader
        settings.godraysShader = Shader.Find("Hidden/Universal Render Pipeline/ScreenSpaceGodrays");

        if (_godraysPass != null)
        {
            _godraysPass.Dispose();
            _godraysPass = null;
        }

        _godraysPass = new ScreenSpaceGodraysPass(settings);
        // Execute after transparents and volumetric clouds to capture cloud borders properly!
        _godraysPass.renderPassEvent = RenderPassEvent.AfterRenderingTransparents;
    }

    public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
    {
        if (settings.godraysShader == null)
        {
            settings.godraysShader = Shader.Find("Hidden/Universal Render Pipeline/ScreenSpaceGodrays");
        }

        if (settings.godraysShader == null)
        {
            return;
        }

        // WebGL Performance overrides
        if (Application.platform == RuntimePlatform.WebGLPlayer)
        {
            settings.samples = Mathf.Min(settings.samples, 8);
            settings.downsample = Mathf.Max(settings.downsample, 4);
        }

        _godraysPass.Setup(settings);
        renderer.EnqueuePass(_godraysPass);
    }

    protected override void Dispose(bool disposing)
    {
        _godraysPass?.Dispose();
    }
}

public class ScreenSpaceGodraysPass : ScriptableRenderPass
{
    private ScreenSpaceGodraysFeature.GodraySettings _settings;
    private Material _material;
    private RTHandle _maskTex;
    private RTHandle _blurTex;

    private static readonly int ThresholdId = Shader.PropertyToID("_Threshold");
    private static readonly int BlurWidthId = Shader.PropertyToID("_BlurWidth");
    private static readonly int IntensityId = Shader.PropertyToID("_Intensity");
    private static readonly int RayColorId = Shader.PropertyToID("_RayColor");
    private static readonly int SunScreenPosId = Shader.PropertyToID("_SunScreenPos");
    private static readonly int SamplesId = Shader.PropertyToID("_Samples");

    // Cached directional light; only search the scene again after the cache is invalid.
    private Light _cachedSunLight;
    private float _nextLightSearchTime;
    private const float LightSearchInterval = 8f;

    private bool _hasCachedMaterialSettings;
    private float _lastThreshold;
    private float _lastBlurWidth;
    private float _lastIntensity;
    private Color _lastRayColor;
    private int _lastSamples;


    public ScreenSpaceGodraysPass(ScreenSpaceGodraysFeature.GodraySettings settings)
    {
        _settings = settings;
    }

    public void Setup(ScreenSpaceGodraysFeature.GodraySettings settings)
    {
        _settings = settings;

        if (_material == null || _material.shader != _settings.godraysShader)
        {
            if (_material != null)
            {
                CoreUtils.Destroy(_material);
            }

            if (_settings.godraysShader != null)
            {
                _material = CoreUtils.CreateEngineMaterial(_settings.godraysShader);
            }

            _hasCachedMaterialSettings = false;
        }
    }
    public override void OnCameraSetup(CommandBuffer cmd, ref RenderingData renderingData)
    {
        ConfigureTarget(renderingData.cameraData.renderer.cameraColorTargetHandle);

        RenderTextureDescriptor desc = renderingData.cameraData.cameraTargetDescriptor;
        desc.depthBufferBits = 0;
        desc.colorFormat = RenderTextureFormat.ARGB32;
        desc.sRGB = renderingData.cameraData.cameraTargetDescriptor.sRGB;

        int scale = Mathf.Max(1, _settings.downsample);
        desc.width = Mathf.Max(1, desc.width / scale);
        desc.height = Mathf.Max(1, desc.height / scale);

        RenderingUtils.ReAllocateIfNeeded(ref _maskTex, desc, FilterMode.Bilinear, TextureWrapMode.Clamp, name: "_GodrayMaskTex");
        RenderingUtils.ReAllocateIfNeeded(ref _blurTex, desc, FilterMode.Bilinear, TextureWrapMode.Clamp, name: "_GodrayBlurTex");
    }

    public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
    {
        if (_material == null || _maskTex == null || _blurTex == null)
            return;

        Camera camera = renderingData.cameraData.camera;

        if (!TryResolveLightDirection(out Vector3 lightDir))
        {
            return;
        }

        // Project the virtual sun position 1000m away onto the viewport
        Vector3 sunWorldPos = camera.transform.position + lightDir * 1000.0f;
        Vector3 sunViewportPos = camera.WorldToViewportPoint(sunWorldPos);

        // If the sun is behind the camera, skip rendering godrays
        if (sunViewportPos.z < 0) return;

        // Skip if the sun is too far off-screen
        if (sunViewportPos.x < -0.3f || sunViewportPos.x > 1.3f || sunViewportPos.y < -0.3f || sunViewportPos.y > 1.3f)
            return;

        CommandBuffer cmd = CommandBufferPool.Get("Screen Space Godrays");

        ApplyMaterialSettings();
        _material.SetVector(SunScreenPosId, new Vector4(sunViewportPos.x, sunViewportPos.y, 0, 0));

        RTHandle cameraColorTarget = renderingData.cameraData.renderer.cameraColorTargetHandle;

        // 1. Extract high-brightness sun/cloud mask occluded by geometry
        cmd.Blit(cameraColorTarget, _maskTex, _material, 0);

        // 2. Perform radial blur centered at sun's screen position
        cmd.Blit(_maskTex, _blurTex, _material, 1);

        // 3. Additively blend radial rays onto camera color target
        cmd.Blit(_blurTex, cameraColorTarget, _material, 2);

        context.ExecuteCommandBuffer(cmd);
        CommandBufferPool.Release(cmd);
    }

    private bool TryResolveLightDirection(out Vector3 lightDir)
    {
        lightDir = Vector3.up;

        Light renderSun = RenderSettings.sun;
        if (renderSun != null && renderSun.enabled)
        {
            lightDir = -renderSun.transform.forward;
            _cachedSunLight = renderSun;
            return true;
        }

        if (_cachedSunLight != null && _cachedSunLight.enabled && _cachedSunLight.type == LightType.Directional)
        {
            lightDir = -_cachedSunLight.transform.forward;
            return true;
        }

        if (Time.time < _nextLightSearchTime)
        {
            return false;
        }

        _nextLightSearchTime = Time.time + LightSearchInterval;
        _cachedSunLight = null;
        Light[] lights = Object.FindObjectsByType<Light>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
        foreach (Light light in lights)
        {
            if (light.type == LightType.Directional && light.enabled)
            {
                _cachedSunLight = light;
                lightDir = -light.transform.forward;
                return true;
            }
        }

        return false;
    }

    private void ApplyMaterialSettings()
    {
        if (_hasCachedMaterialSettings
            && Mathf.Approximately(_lastThreshold, _settings.threshold)
            && Mathf.Approximately(_lastBlurWidth, _settings.blurWidth)
            && Mathf.Approximately(_lastIntensity, _settings.intensity)
            && _lastRayColor == _settings.rayColor
            && _lastSamples == _settings.samples)
        {
            return;
        }

        _material.SetFloat(ThresholdId, _settings.threshold);
        _material.SetFloat(BlurWidthId, _settings.blurWidth);
        _material.SetFloat(IntensityId, _settings.intensity);
        _material.SetColor(RayColorId, _settings.rayColor);
        _material.SetInt(SamplesId, _settings.samples);

        _lastThreshold = _settings.threshold;
        _lastBlurWidth = _settings.blurWidth;
        _lastIntensity = _settings.intensity;
        _lastRayColor = _settings.rayColor;
        _lastSamples = _settings.samples;
        _hasCachedMaterialSettings = true;
    }

    public override void OnCameraCleanup(CommandBuffer cmd)
    {
        // No cleanup needed
    }

    public void Dispose()
    {
        if (_maskTex != null) _maskTex.Release();
        if (_blurTex != null) _blurTex.Release();
        CoreUtils.Destroy(_material);
    }
}
