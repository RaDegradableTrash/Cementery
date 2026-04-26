using UnityEngine;
using UnityEngine.Rendering;
#if UNITY_PIPELINE_URP
using UnityEngine.Rendering.Universal;
#endif

/// <summary>
/// 挂载到场景中任意物体（如 Directional Light 或 Camera），
/// 在代码层面强制限制阴影偏移以解决渗光问题，并提升电影感画质。
/// </summary>
[ExecuteAlways]
public class CinematicLightingEnforcer : MonoBehaviour
{
    [Header("Shadow Leak Fixes (解决直角渗光)")]
    [Tooltip("强制设定定向光的阴影偏移量。越小越不会漏光，但太小会有自阴影条纹(Acne)")]
    [Range(0f, 0.1f)] public float enforcedShadowBias = 0.01f;
    [Range(0f, 1f)] public float enforcedNormalBias = 0.1f;
    
    [Header("Cinematic Quality (电影感提升)")]
    [Tooltip("是否在运行时强制开启最高阴影分辨率与 4 级级联阴影")]
    public bool enforceCinematicShadows = true;

    void Start()
    {
        ApplyCinematicLighting();
    }

    void OnValidate()
    {
        ApplyCinematicLighting();
    }

    public void ApplyCinematicLighting()
    {
        // 1. 修复渗光：强制所有光源尤其是平行光降低 Bias
        Light[] allLights = FindObjectsOfType<Light>();
        foreach (var light in allLights)
        {
            if (light.type == LightType.Directional)
            {
                // 设置极低的 Bias 是防止直角处光线穿透的最有效代码手段
                light.shadowBias = enforcedShadowBias;
                light.shadowNormalBias = enforcedNormalBias;
                light.shadows = LightShadows.Soft; // 必须使用软阴影增加电影感
            }
            else if (light.type == LightType.Spot || light.type == LightType.Point)
            {
                // 点光源和聚光灯同样需要限制，并且【必须】开启阴影，否则会直接穿透墙壁！
                light.shadowBias = Mathf.Clamp(enforcedShadowBias, 0f, 0.05f);
                light.shadowNormalBias = Mathf.Clamp(enforcedNormalBias, 0f, 0.2f);
                light.shadows = LightShadows.Soft; 
            }
        }

        // 修复暗部“一圈圈的光圈”（色彩断层 Color Banding）问题
        // 在黑暗环境中，8bit 显示器极易出现光晕断层，需要强制开启相机的 Dithering（抖动）
        Camera mainCam = Camera.main;
        if (mainCam != null)
        {
#if UNITY_PIPELINE_URP
            var camData = mainCam.GetComponent<UniversalAdditionalCameraData>();
            if (camData != null)
            {
                camData.dithering = true; // 开启抖动消除光圈断层
                camData.renderPostProcessing = true; // 确保后期处理开启
            }
#endif
        }

        // 2. 电影感管线设置 (URP / 内置管线兼容)
        if (enforceCinematicShadows)
        {
            // 确保渲染质量为最高（级联阴影是防止大范围漏光的重要设置）
            QualitySettings.shadows = ShadowQuality.All;
            QualitySettings.shadowResolution = ShadowResolution.VeryHigh;
            QualitySettings.shadowDistance = 150f; 
            QualitySettings.shadowCascades = 4; // 4级级联能让近处（柜子）获得极致阴影精度
        }

#if UNITY_PIPELINE_URP
        // 3. 针对 URP：尝试限制 URP Asset 内部的深度偏移（如果引用可用）
        if (GraphicsSettings.currentRenderPipeline is UniversalRenderPipelineAsset urpAsset)
        {
            // 在运行时提升 URP 管线阴影资产质量
            urpAsset.shadowCascadeCount = 4;
            // 注意：SSAO (屏幕空间环境光遮蔽) 是彻底遮盖直角漏光的终极方案，
            // 建议确保你的 URP Renderer Feature 中添加了 SSAO。
        }
#endif
    }
}
