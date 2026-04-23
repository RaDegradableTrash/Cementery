using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

[InitializeOnLoad]
public static class UrpMaterialRepairTool
{
    private const string SessionRunKey = "Cementery.URP.MaterialRepair.RunOnce";
    private const string UrpFolderPath = "Assets/Settings/URP";
    private const string FallbackMaterialPath = UrpFolderPath + "/URP_Fallback_Material.mat";
    private const string RepairReportPath = UrpFolderPath + "/URP_MaterialRepairReport.txt";

    static UrpMaterialRepairTool()
    {
        EditorApplication.delayCall += AutoRunOnce;
    }

    [MenuItem("Tools/Rendering/Run Full URP Material Repair")]
    public static void RunFromMenu()
    {
        RunRepair("manual");
    }

    private static void AutoRunOnce()
    {
        if (SessionState.GetBool(SessionRunKey, false))
            return;

        SessionState.SetBool(SessionRunKey, true);
        RunRepair("auto");
    }

    private static void RunRepair(string runMode)
    {
        if (EditorApplication.isCompiling || EditorApplication.isUpdating || BuildPipeline.isBuildingPlayer)
        {
            EditorApplication.delayCall += () => RunRepair(runMode);
            return;
        }

        Shader urpLit = Shader.Find("Universal Render Pipeline/Lit");
        Shader urpUnlit = Shader.Find("Universal Render Pipeline/Unlit");
        Shader skyboxProcedural = Shader.Find("Skybox/Procedural");

        if (urpLit == null || urpUnlit == null)
        {
            Debug.LogError("[URP Material Repair] URP shaders are not available yet. Wait for package import and run Tools/Rendering/Run Full URP Material Repair.");
            return;
        }

        EnsureFolder(UrpFolderPath);

        Material fallback = EnsureFallbackMaterial(urpLit);
        List<string> reportLines = new List<string>(256)
        {
            "URP MATERIAL REPAIR REPORT",
            "Generated: " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
            "Mode: " + runMode,
            string.Empty
        };

        int repairedMaterials = RepairMaterials(urpLit, urpUnlit, skyboxProcedural, reportLines);
        int repairedPrefabSlots = RepairPrefabsMissingMaterialSlots(fallback, reportLines);
        int repairedSceneSlots = RepairSceneMissingMaterialSlots(fallback, reportLines);

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        reportLines.Add(string.Empty);
        reportLines.Add("SUMMARY");
        reportLines.Add("Repaired materials: " + repairedMaterials);
        reportLines.Add("Repaired prefab material slots: " + repairedPrefabSlots);
        reportLines.Add("Repaired scene material slots: " + repairedSceneSlots);

        File.WriteAllLines(RepairReportPath, reportLines);
        AssetDatabase.ImportAsset(RepairReportPath);

        Debug.Log("[URP Material Repair] Completed. Materials: " + repairedMaterials + ", Prefab slots: " + repairedPrefabSlots + ", Scene slots: " + repairedSceneSlots + ". Report: " + RepairReportPath);
    }

    private static int RepairMaterials(Shader urpLit, Shader urpUnlit, Shader skyboxProcedural, List<string> reportLines)
    {
        string[] materialGuids = AssetDatabase.FindAssets("t:Material", new[] { "Assets" });
        int repaired = 0;

        for (int i = 0; i < materialGuids.Length; i++)
        {
            string materialPath = AssetDatabase.GUIDToAssetPath(materialGuids[i]);
            Material material = AssetDatabase.LoadAssetAtPath<Material>(materialPath);
            if (material == null)
                continue;

            if (ShouldSkipMaterial(material, materialPath))
                continue;

            bool isSkyboxLike = IsSkyboxLikeMaterial(material, materialPath);
            bool missingShader = material.shader == null || material.shader.name == "Hidden/InternalErrorShader";
            bool hdrpLike = missingShader || IsHdrpLikeShader(material.shader) || HasHdrpLikeProperties(material);
            if (!isSkyboxLike && !hdrpLike)
                continue;

            string oldShaderName = material.shader != null ? material.shader.name : "<null>";
            bool changed;

            if (isSkyboxLike)
            {
                changed = ForceShader(material, skyboxProcedural);
            }
            else
            {
                LegacyMaterialData data = CaptureLegacyMaterialData(material, materialPath);
                Shader targetShader = data.preferUnlit ? urpUnlit : urpLit;
                changed = ForceShader(material, targetShader);
                changed |= ApplyLegacyMaterialData(material, data, targetShader == urpUnlit);
            }

            if (!changed)
                continue;

            EditorUtility.SetDirty(material);
            repaired++;
            reportLines.Add("MATERIAL_REPAIRED " + materialPath + " :: " + oldShaderName + " -> " + (material.shader != null ? material.shader.name : "<null>"));
        }

        return repaired;
    }

    private static int RepairPrefabsMissingMaterialSlots(Material fallback, List<string> reportLines)
    {
        string[] prefabGuids = AssetDatabase.FindAssets("t:Prefab", new[] { "Assets" });
        int repairedSlots = 0;

        for (int i = 0; i < prefabGuids.Length; i++)
        {
            string prefabPath = AssetDatabase.GUIDToAssetPath(prefabGuids[i]);
            GameObject root = PrefabUtility.LoadPrefabContents(prefabPath);
            bool changed = false;

            try
            {
                Renderer[] renderers = root.GetComponentsInChildren<Renderer>(true);
                int fixedInPrefab = RepairNullMaterialSlots(renderers, fallback);
                if (fixedInPrefab > 0)
                {
                    repairedSlots += fixedInPrefab;
                    changed = true;
                    reportLines.Add("PREFAB_SLOTS_REPAIRED " + prefabPath + " :: " + fixedInPrefab);
                }

                if (changed)
                    PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        return repairedSlots;
    }

    private static int RepairSceneMissingMaterialSlots(Material fallback, List<string> reportLines)
    {
        string[] sceneGuids = AssetDatabase.FindAssets("t:Scene", new[] { "Assets" });
        SceneSetup[] initialSetup = EditorSceneManager.GetSceneManagerSetup();
        int repairedSlots = 0;

        try
        {
            for (int i = 0; i < sceneGuids.Length; i++)
            {
                string scenePath = AssetDatabase.GUIDToAssetPath(sceneGuids[i]);
                if (string.IsNullOrEmpty(scenePath) ||
                    !scenePath.StartsWith("Assets/", StringComparison.Ordinal) ||
                    AssetDatabase.LoadAssetAtPath<SceneAsset>(scenePath) == null)
                {
                    reportLines.Add("SCENE_SKIPPED " + scenePath + " :: not a project scene asset");
                    continue;
                }

                Scene scene = EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single);
                Renderer[] renderers = UnityEngine.Object.FindObjectsByType<Renderer>(FindObjectsInactive.Include, FindObjectsSortMode.None);
                int fixedInScene = RepairNullMaterialSlots(renderers, fallback);
                if (fixedInScene <= 0)
                    continue;

                repairedSlots += fixedInScene;
                EditorSceneManager.SaveScene(scene);
                reportLines.Add("SCENE_SLOTS_REPAIRED " + scenePath + " :: " + fixedInScene);
            }
        }
        finally
        {
            if (initialSetup != null && initialSetup.Length > 0)
                EditorSceneManager.RestoreSceneManagerSetup(initialSetup);
        }

        return repairedSlots;
    }

    private static int RepairNullMaterialSlots(Renderer[] renderers, Material fallback)
    {
        int fixedSlots = 0;
        if (renderers == null)
            return fixedSlots;

        for (int i = 0; i < renderers.Length; i++)
        {
            Renderer renderer = renderers[i];
            if (renderer == null)
                continue;

            Material[] mats = renderer.sharedMaterials;
            if (mats == null || mats.Length == 0)
                continue;

            bool changed = false;
            for (int m = 0; m < mats.Length; m++)
            {
                if (mats[m] != null)
                    continue;

                mats[m] = fallback;
                fixedSlots++;
                changed = true;
            }

            if (changed)
                renderer.sharedMaterials = mats;
        }

        return fixedSlots;
    }

    private static bool ShouldSkipMaterial(Material material, string materialPath)
    {
        if (material == null)
            return true;

        if (material.shader != null && material.shader.name.IndexOf("TextMeshPro", StringComparison.OrdinalIgnoreCase) >= 0)
            return true;

        if (materialPath.IndexOf("TextMesh Pro/Resources/Fonts & Materials", StringComparison.OrdinalIgnoreCase) >= 0)
            return true;

        return false;
    }

    private static bool IsSkyboxLikeMaterial(Material material, string materialPath)
    {
        if (materialPath.IndexOf("Skybox", StringComparison.OrdinalIgnoreCase) >= 0)
            return true;

        return material.HasProperty("_SkyTint") &&
               material.HasProperty("_GroundColor") &&
               material.HasProperty("_AtmosphereThickness") &&
               material.HasProperty("_SunSize");
    }

    private static bool IsHdrpLikeShader(Shader shader)
    {
        if (shader == null)
            return true;

        string shaderName = shader.name;
        return shaderName.IndexOf("HDRP", StringComparison.OrdinalIgnoreCase) >= 0 ||
               shaderName.IndexOf("High Definition Render Pipeline", StringComparison.OrdinalIgnoreCase) >= 0;
    }

    private static bool HasHdrpLikeProperties(Material material)
    {
        return material.HasProperty("_BaseColorMap") ||
               material.HasProperty("_SurfaceType") ||
               material.HasProperty("_AlphaCutoffEnable") ||
               material.HasProperty("_DoubleSidedEnable") ||
               material.HasProperty("_DistortionVectorMap");
    }

    private static bool ForceShader(Material material, Shader shader)
    {
        if (material == null || shader == null || material.shader == shader)
            return false;

        material.shader = shader;
        return true;
    }

    private struct LegacyMaterialData
    {
        public Color baseColor;
        public Texture baseMap;
        public Texture normalMap;
        public Texture emissionMap;
        public Color emissionColor;
        public float metallic;
        public float smoothness;
        public bool transparent;
        public bool alphaClip;
        public float cutoff;
        public bool preferUnlit;
    }

    private static LegacyMaterialData CaptureLegacyMaterialData(Material material, string materialPath)
    {
        LegacyMaterialData data = new LegacyMaterialData
        {
            baseColor = Color.white,
            baseMap = null,
            normalMap = null,
            emissionMap = null,
            emissionColor = Color.black,
            metallic = 0f,
            smoothness = 0.5f,
            transparent = false,
            alphaClip = false,
            cutoff = 0.5f,
            preferUnlit = false
        };

        if (material.HasProperty("_BaseColor"))
            data.baseColor = material.GetColor("_BaseColor");
        else if (material.HasProperty("_Color"))
            data.baseColor = material.GetColor("_Color");
        else if (material.HasProperty("_UnlitColor"))
            data.baseColor = material.GetColor("_UnlitColor");

        if (material.HasProperty("_BaseColorMap"))
            data.baseMap = material.GetTexture("_BaseColorMap");
        else if (material.HasProperty("_BaseMap"))
            data.baseMap = material.GetTexture("_BaseMap");
        else if (material.HasProperty("_MainTex"))
            data.baseMap = material.GetTexture("_MainTex");
        else if (material.HasProperty("_UnlitColorMap"))
            data.baseMap = material.GetTexture("_UnlitColorMap");

        if (material.HasProperty("_NormalMap"))
            data.normalMap = material.GetTexture("_NormalMap");
        else if (material.HasProperty("_BumpMap"))
            data.normalMap = material.GetTexture("_BumpMap");

        if (material.HasProperty("_EmissiveColorMap"))
            data.emissionMap = material.GetTexture("_EmissiveColorMap");
        else if (material.HasProperty("_EmissionMap"))
            data.emissionMap = material.GetTexture("_EmissionMap");

        if (material.HasProperty("_EmissiveColor"))
            data.emissionColor = material.GetColor("_EmissiveColor");
        else if (material.HasProperty("_EmissionColor"))
            data.emissionColor = material.GetColor("_EmissionColor");

        if (material.HasProperty("_Metallic"))
            data.metallic = material.GetFloat("_Metallic");

        if (material.HasProperty("_Smoothness"))
            data.smoothness = material.GetFloat("_Smoothness");
        else if (material.HasProperty("_Glossiness"))
            data.smoothness = material.GetFloat("_Glossiness");

        if (material.HasProperty("_SurfaceType"))
            data.transparent = material.GetFloat("_SurfaceType") > 0.5f;
        else if (material.HasProperty("_Surface"))
            data.transparent = material.GetFloat("_Surface") > 0.5f;
        else if (material.renderQueue >= (int)RenderQueue.Transparent)
            data.transparent = true;

        if (data.baseColor.a < 0.99f)
            data.transparent = true;

        if (material.HasProperty("_AlphaCutoffEnable"))
            data.alphaClip = material.GetFloat("_AlphaCutoffEnable") > 0.5f;
        else if (material.HasProperty("_AlphaClip"))
            data.alphaClip = material.GetFloat("_AlphaClip") > 0.5f;

        if (material.HasProperty("_AlphaCutoff"))
            data.cutoff = material.GetFloat("_AlphaCutoff");
        else if (material.HasProperty("_Cutoff"))
            data.cutoff = material.GetFloat("_Cutoff");

        data.preferUnlit = materialPath.IndexOf("Trans", StringComparison.OrdinalIgnoreCase) >= 0 ||
                           material.HasProperty("_UnlitColor") ||
                           (material.HasProperty("_LightingEnabled") && material.GetFloat("_LightingEnabled") < 0.5f);

        return data;
    }

    private static bool ApplyLegacyMaterialData(Material material, LegacyMaterialData data, bool isUnlit)
    {
        bool changed = false;

        changed |= SetColorIfDifferent(material, "_BaseColor", data.baseColor);
        changed |= SetTextureIfDifferent(material, "_BaseMap", data.baseMap);

        if (!isUnlit)
        {
            changed |= SetFloatIfDifferent(material, "_Metallic", data.metallic);
            changed |= SetFloatIfDifferent(material, "_Smoothness", data.smoothness);
            changed |= SetTextureIfDifferent(material, "_BumpMap", data.normalMap);
            if (data.normalMap != null)
                material.EnableKeyword("_NORMALMAP");
        }

        changed |= SetTextureIfDifferent(material, "_EmissionMap", data.emissionMap);
        changed |= SetColorIfDifferent(material, "_EmissionColor", data.emissionColor);

        bool hasEmission = data.emissionMap != null || data.emissionColor.maxColorComponent > 0.0001f;
        if (hasEmission)
            material.EnableKeyword("_EMISSION");
        else
            material.DisableKeyword("_EMISSION");

        changed |= SetFloatIfDifferent(material, "_AlphaClip", data.alphaClip ? 1f : 0f);
        changed |= SetFloatIfDifferent(material, "_Cutoff", data.cutoff);

        if (data.transparent)
        {
            changed |= SetFloatIfDifferent(material, "_Surface", 1f);
            changed |= SetFloatIfDifferent(material, "_Blend", 0f);
            changed |= SetFloatIfDifferent(material, "_SrcBlend", (float)BlendMode.SrcAlpha);
            changed |= SetFloatIfDifferent(material, "_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
            changed |= SetFloatIfDifferent(material, "_ZWrite", 0f);
            if (material.renderQueue != (int)RenderQueue.Transparent)
            {
                material.renderQueue = (int)RenderQueue.Transparent;
                changed = true;
            }
        }
        else
        {
            changed |= SetFloatIfDifferent(material, "_Surface", 0f);
            changed |= SetFloatIfDifferent(material, "_SrcBlend", (float)BlendMode.One);
            changed |= SetFloatIfDifferent(material, "_DstBlend", (float)BlendMode.Zero);
            changed |= SetFloatIfDifferent(material, "_ZWrite", 1f);
            if (material.renderQueue != -1)
            {
                material.renderQueue = -1;
                changed = true;
            }
        }

        return changed;
    }

    private static bool SetFloatIfDifferent(Material material, string propertyName, float value)
    {
        if (!material.HasProperty(propertyName))
            return false;

        float current = material.GetFloat(propertyName);
        if (Mathf.Approximately(current, value))
            return false;

        material.SetFloat(propertyName, value);
        return true;
    }

    private static bool SetColorIfDifferent(Material material, string propertyName, Color value)
    {
        if (!material.HasProperty(propertyName))
            return false;

        Color current = material.GetColor(propertyName);
        if (current == value)
            return false;

        material.SetColor(propertyName, value);
        return true;
    }

    private static bool SetTextureIfDifferent(Material material, string propertyName, Texture value)
    {
        if (!material.HasProperty(propertyName))
            return false;

        Texture current = material.GetTexture(propertyName);
        if (current == value)
            return false;

        material.SetTexture(propertyName, value);
        return true;
    }

    private static Material EnsureFallbackMaterial(Shader urpLit)
    {
        Material fallback = AssetDatabase.LoadAssetAtPath<Material>(FallbackMaterialPath);
        if (fallback != null)
            return fallback;

        fallback = new Material(urpLit)
        {
            name = "URP_Fallback_Material"
        };

        if (fallback.HasProperty("_BaseColor"))
            fallback.SetColor("_BaseColor", new Color(0.6f, 0.6f, 0.6f, 1f));

        AssetDatabase.CreateAsset(fallback, FallbackMaterialPath);
        return fallback;
    }

    private static void EnsureFolder(string folderPath)
    {
        string[] parts = folderPath.Split('/');
        if (parts.Length == 0)
            return;

        string current = parts[0];
        for (int i = 1; i < parts.Length; i++)
        {
            string next = current + "/" + parts[i];
            if (!AssetDatabase.IsValidFolder(next))
                AssetDatabase.CreateFolder(current, parts[i]);

            current = next;
        }
    }
}
