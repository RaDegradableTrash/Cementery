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

    // Cached directional light – looked up once per second to avoid FindObjectsOfType every frame.
    private Light _cachedSunLight;
    private float _lightCacheTime = -10f;
    private const float LightCacheInterval = 2f;


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

        // Resolve sun direction – prefer RenderSettings.sun, then cached light.
        Vector3 lightDir = Vector3.up;

        if (RenderSettings.sun != null)
        {
            lightDir = -RenderSettings.sun.transform.forward;
        }
        else
        {
            // Refresh cached light every LightCacheInterval seconds (NOT every frame)
            if (Time.time - _lightCacheTime > LightCacheInterval || _cachedSunLight == null)
            {
                _lightCacheTime = Time.time;
                _cachedSunLight = null;
                Light[] lights = GameObject.FindObjectsOfType<Light>();
                foreach (var l in lights)
                {
                    if (l.type == LightType.Directional && l.enabled)
                    {
                        _cachedSunLight = l;
                        break;
                    }
                }
            }
            if (_cachedSunLight != null)
                lightDir = -_cachedSunLight.transform.forward;
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

        // Pass settings to material
        _material.SetFloat(ThresholdId, _settings.threshold);
        _material.SetFloat(BlurWidthId, _settings.blurWidth);
        _material.SetFloat(IntensityId, _settings.intensity);
        _material.SetColor(RayColorId, _settings.rayColor);
        _material.SetVector(SunScreenPosId, new Vector4(sunViewportPos.x, sunViewportPos.y, 0, 0));
        _material.SetInt(SamplesId, _settings.samples);

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
