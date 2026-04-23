using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Manual one-click diagnostics for PBR scene hygiene.
/// Attach to any scene object and use context menu "Run PBR Diagnostics".
/// </summary>
[DisallowMultipleComponent]
public class PbrSceneDiagnostics : MonoBehaviour
{
    [Range(0, 255)] public int minAlbedoSrgb = 30;
    [Range(0, 255)] public int maxAlbedoSrgb = 240;
    public bool includeInactiveRenderers = false;

    [ContextMenu("Run PBR Diagnostics")]
    public void RunDiagnostics()
    {
        int warningCount = 0;
        StringBuilder report = new StringBuilder();
        report.AppendLine("[PBR Diagnostics] Starting scan...");

        if (QualitySettings.activeColorSpace != ColorSpace.Linear)
        {
            warningCount++;
            report.AppendLine("- Color Space is not Linear.");
        }
        else
        {
            report.AppendLine("- Color Space: Linear (OK)");
        }

        ReflectionProbe[] reflectionProbes = FindObjectsByType<ReflectionProbe>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
        if (reflectionProbes.Length == 0)
        {
            warningCount++;
            report.AppendLine("- No ReflectionProbe found.");
        }
        else
        {
            report.AppendLine($"- ReflectionProbes: {reflectionProbes.Length} (OK)");
        }

        LightProbeGroup[] lightProbeGroups = FindObjectsByType<LightProbeGroup>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
        if (lightProbeGroups.Length == 0)
        {
            warningCount++;
            report.AppendLine("- No LightProbeGroup found.");
        }
        else
        {
            report.AppendLine($"- LightProbeGroups: {lightProbeGroups.Length} (OK)");
        }

        Renderer[] renderers = FindObjectsByType<Renderer>(
            includeInactiveRenderers ? FindObjectsInactive.Include : FindObjectsInactive.Exclude,
            FindObjectsSortMode.None);

        int dynamicWithoutBlendProbe = 0;
        int dynamicWithoutBlendReflection = 0;
        HashSet<Material> uniqueMaterials = new HashSet<Material>();

        for (int i = 0; i < renderers.Length; i++)
        {
            Renderer renderer = renderers[i];
            if (renderer == null)
                continue;

            bool dynamic = IsDynamicRenderer(renderer);
            if (dynamic)
            {
                if (renderer.lightProbeUsage != LightProbeUsage.BlendProbes)
                    dynamicWithoutBlendProbe++;
                if (renderer.reflectionProbeUsage != ReflectionProbeUsage.BlendProbes)
                    dynamicWithoutBlendReflection++;
            }

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

        if (dynamicWithoutBlendProbe > 0)
        {
            warningCount++;
            report.AppendLine($"- Dynamic renderers without Blend Probes: {dynamicWithoutBlendProbe}");
        }
        else
        {
            report.AppendLine("- Dynamic renderer Light Probes: OK");
        }

        if (dynamicWithoutBlendReflection > 0)
        {
            warningCount++;
            report.AppendLine($"- Dynamic renderers without Reflection Blend Probes: {dynamicWithoutBlendReflection}");
        }
        else
        {
            report.AppendLine("- Dynamic renderer Reflection Probes: OK");
        }

        float minLuma = minAlbedoSrgb / 255f;
        float maxLuma = maxAlbedoSrgb / 255f;
        int suspiciousAlbedoMaterials = 0;

        foreach (Material mat in uniqueMaterials)
        {
            if (mat == null || !mat.HasProperty("_Color"))
                continue;

            if (!LooksLikeLitShader(mat.shader))
                continue;

            Color gammaColor = mat.GetColor("_Color").gamma;
            float luma = 0.2126f * gammaColor.r + 0.7152f * gammaColor.g + 0.0722f * gammaColor.b;
            if (luma < minLuma || luma > maxLuma)
                suspiciousAlbedoMaterials++;
        }

        if (suspiciousAlbedoMaterials > 0)
        {
            warningCount++;
            report.AppendLine($"- Materials with suspicious albedo luminance: {suspiciousAlbedoMaterials}");
        }
        else
        {
            report.AppendLine("- Material albedo range: OK");
        }

        report.AppendLine($"[PBR Diagnostics] Completed with {warningCount} warning(s).");
        if (warningCount > 0)
            Debug.LogWarning(report.ToString(), this);
        else
            Debug.Log(report.ToString(), this);
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

    private static bool LooksLikeLitShader(Shader shader)
    {
        if (shader == null)
            return false;

        string name = shader.name;
        return name.Contains("Standard") || name.Contains("Lit") || name.Contains("PBR");
    }
}
