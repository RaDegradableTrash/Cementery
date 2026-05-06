using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

public class PCSSScreenSpaceShadowFeature : ScriptableRendererFeature
{
    [System.Serializable]
    public class PCSSSettings
    {
        [Tooltip("Size of the light source. Larger values create softer shadows.")]
        [Range(0.01f, 30f)] public float lightSize = 5f;
        
        [Tooltip("Maximum allowed blur radius for the shadows.")]
        [Range(0.001f, 0.3f)] public float maxPenumbraSize = 0.1f;
        
        [Tooltip("Overall opacity of the shadows.")]
        [Range(0f, 1f)] public float shadowIntensity = 1.0f;
        
        public Shader pcssShader;
    }

    public PCSSSettings settings = new PCSSSettings();
    private PCSSRenderPass _pcssPass;

    public override void Create()
    {
        if (settings.pcssShader == null)
        {
            settings.pcssShader = Shader.Find("Hidden/Universal Render Pipeline/PCSS_ScreenSpaceShadows");
        }

        if (_pcssPass == null)
        {
            _pcssPass = new PCSSRenderPass(settings);
            // After pre-passes, before Opaques, so the screen space shadow map is ready for lighting
            _pcssPass.renderPassEvent = RenderPassEvent.AfterRenderingPrePasses; 
        }
    }

    public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
    {
        if (settings.pcssShader == null)
        {
            Debug.LogError("PCSS Screen Space Shadow Feature: Shader is missing.");
            return;
        }

        // Only render if shadows are enabled
        if (!renderingData.shadowData.supportsMainLightShadows || renderingData.lightData.mainLightIndex == -1)
            return;

        _pcssPass.Setup(settings);
        renderer.EnqueuePass(_pcssPass);
    }

    protected override void Dispose(bool disposing)
    {
        _pcssPass?.Dispose();
    }
}

public class PCSSRenderPass : ScriptableRenderPass
{
    private PCSSScreenSpaceShadowFeature.PCSSSettings _settings;
    private Material _pcssMaterial;
    private RTHandle _screenSpaceShadowmap;
    
    private static readonly int LightSizeId = Shader.PropertyToID("_LightSize");
    private static readonly int MaxPenumbraSizeId = Shader.PropertyToID("_MaxPenumbraSize");
    private static readonly int ShadowIntensityId = Shader.PropertyToID("_ShadowIntensity");
    private static readonly int ScreenSpaceShadowmapTextureId = Shader.PropertyToID("_ScreenSpaceShadowmapTexture");

    public PCSSRenderPass(PCSSScreenSpaceShadowFeature.PCSSSettings settings)
    {
        _settings = settings;
    }

    public void Setup(PCSSScreenSpaceShadowFeature.PCSSSettings settings)
    {
        _settings = settings;
        if (_pcssMaterial == null && _settings.pcssShader != null)
        {
            _pcssMaterial = CoreUtils.CreateEngineMaterial(_settings.pcssShader);
        }
    }

    public override void OnCameraSetup(CommandBuffer cmd, ref RenderingData renderingData)
    {
        RenderTextureDescriptor desc = renderingData.cameraData.cameraTargetDescriptor;
        desc.depthBufferBits = 0;
        desc.colorFormat = RenderTextureFormat.R8; // Single channel is enough for shadow mask
        desc.sRGB = false;

        RenderingUtils.ReAllocateIfNeeded(ref _screenSpaceShadowmap, desc, name: "_ScreenSpaceShadowmapTexture");

        ConfigureTarget(_screenSpaceShadowmap);
        ConfigureClear(ClearFlag.Color, Color.white); // White means fully lit
    }

    public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
    {
        if (_pcssMaterial == null) return;

        CommandBuffer cmd = CommandBufferPool.Get("PCSS Shadows");

        _pcssMaterial.SetFloat(LightSizeId, _settings.lightSize);
        _pcssMaterial.SetFloat(MaxPenumbraSizeId, _settings.maxPenumbraSize);
        _pcssMaterial.SetFloat(ShadowIntensityId, _settings.shadowIntensity);

        // Bind the existing shadow map to a custom texture name so we can safely sample raw depth
        cmd.SetGlobalTexture("_MyMainLightShadowmapTexture", new RenderTargetIdentifier("_MainLightShadowmapTexture"));

        // Draw full screen quad using the PCSS Material
        Blitter.BlitCameraTexture(cmd, renderingData.cameraData.renderer.cameraColorTargetHandle, _screenSpaceShadowmap, _pcssMaterial, 0);

        // Assign global texture for URP Shaders
        cmd.SetGlobalTexture(ScreenSpaceShadowmapTextureId, _screenSpaceShadowmap);
        
        // Force URP to use Screen Space Shadows
        cmd.EnableShaderKeyword("_MAIN_LIGHT_SHADOWS_SCREEN");

        context.ExecuteCommandBuffer(cmd);
        CommandBufferPool.Release(cmd);
    }

    public override void OnCameraCleanup(CommandBuffer cmd)
    {
        // Don't disable keyword here, Opaques need to read it.
    }

    public void Dispose()
    {
        if (_screenSpaceShadowmap != null) _screenSpaceShadowmap.Release();
        CoreUtils.Destroy(_pcssMaterial);
    }
}
