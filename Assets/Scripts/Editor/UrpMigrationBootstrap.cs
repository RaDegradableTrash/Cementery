using System;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

[InitializeOnLoad]
public static class UrpMigrationBootstrap
{
    private const string UrpFolderPath = "Assets/Settings/URP";
    private const string PipelineAssetPath = UrpFolderPath + "/URP_Performance.asset";
    private const string RendererAssetPath = UrpFolderPath + "/URP_Performance_Renderer.asset";

    private static bool _runQueued;
    private static int _retryCount;

    static UrpMigrationBootstrap()
    {
        QueueRun();
    }

    private static void QueueRun()
    {
        if (_runQueued)
            return;

        _runQueued = true;
        EditorApplication.delayCall += Run;
    }

    private static void Run()
    {
        _runQueued = false;

        if (EditorApplication.isCompiling || EditorApplication.isUpdating || BuildPipeline.isBuildingPlayer)
        {
            QueueRun();
            return;
        }

        if (!TryConfigureUrp(out bool shouldRetry))
        {
            if (shouldRetry && _retryCount < 12)
            {
                _retryCount++;
                QueueRun();
            }

            return;
        }

        _retryCount = 0;
    }

    private static bool TryConfigureUrp(out bool shouldRetry)
    {
        shouldRetry = false;

        Type urpAssetType = Type.GetType("UnityEngine.Rendering.Universal.UniversalRenderPipelineAsset, Unity.RenderPipelines.Universal.Runtime");
        Type urpRendererDataType = Type.GetType("UnityEngine.Rendering.Universal.UniversalRendererData, Unity.RenderPipelines.Universal.Runtime");

        if (urpAssetType == null || urpRendererDataType == null)
        {
            shouldRetry = true;
            return false;
        }

        EnsureFolder(UrpFolderPath);

        ScriptableObject rendererData = AssetDatabase.LoadAssetAtPath<ScriptableObject>(RendererAssetPath);
        if (rendererData == null)
        {
            rendererData = ScriptableObject.CreateInstance(urpRendererDataType);
            rendererData.name = "URP_Performance_Renderer";
            AssetDatabase.CreateAsset(rendererData, RendererAssetPath);
        }

        bool pipelineCreated = false;
        ScriptableObject pipelineAsset = AssetDatabase.LoadAssetAtPath<ScriptableObject>(PipelineAssetPath);
        if (pipelineAsset == null)
        {
            pipelineAsset = ScriptableObject.CreateInstance(urpAssetType);
            pipelineAsset.name = "URP_Performance";
            AssetDatabase.CreateAsset(pipelineAsset, PipelineAssetPath);
            pipelineCreated = true;
        }

        bool changed = false;
        changed |= BindRendererData(pipelineAsset, rendererData);

        // Apply defaults only when the pipeline asset is first created.
        // Avoid overwriting user quality decisions on every domain reload.
        if (pipelineCreated)
            changed |= ApplyPerformanceDefaults(pipelineAsset);

        RenderPipelineAsset rpAsset = pipelineAsset as RenderPipelineAsset;
        if (rpAsset != null)
        {
            if (GraphicsSettings.defaultRenderPipeline != rpAsset)
            {
                GraphicsSettings.defaultRenderPipeline = rpAsset;
                changed = true;
            }

            int qualityCount = QualitySettings.names.Length;
            int originalQualityLevel = QualitySettings.GetQualityLevel();
            try
            {
                for (int i = 0; i < qualityCount; i++)
                {
                    if (QualitySettings.GetRenderPipelineAssetAt(i) == rpAsset)
                        continue;

                    if (QualitySettings.GetQualityLevel() != i)
                        QualitySettings.SetQualityLevel(i, false);

                    if (QualitySettings.renderPipeline != rpAsset)
                    {
                        QualitySettings.renderPipeline = rpAsset;
                        changed = true;
                    }
                }
            }
            finally
            {
                if (QualitySettings.GetQualityLevel() != originalQualityLevel)
                    QualitySettings.SetQualityLevel(originalQualityLevel, false);
            }
        }

        changed |= ConvertHdrpMaterialsToUrpLit();
        RegisterChunkScenesInBuildSettings();

        if (changed)
        {
            EditorUtility.SetDirty(pipelineAsset);
            EditorUtility.SetDirty(rendererData);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log("[URP Migration] URP pipeline configured. Converted HDRP materials where possible.");
        }

        return true;
    }

    private static void RegisterChunkScenesInBuildSettings()
    {
        string[] sceneGuids = AssetDatabase.FindAssets("t:Scene", new[] { "Assets/Scenes/Chunks", "Assets/Scenes/DesertChunks" });
        if (sceneGuids.Length == 0) return;

        var currentScenes = EditorBuildSettings.scenes;
        var newSceneList = new System.Collections.Generic.List<EditorBuildSettingsScene>(currentScenes);

        bool changed = false;
        foreach (string guid in sceneGuids)
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            
            bool exists = false;
            foreach (var ebsScene in currentScenes)
            {
                if (ebsScene.path.Equals(path, StringComparison.OrdinalIgnoreCase))
                {
                    exists = true;
                    break;
                }
            }

            if (!exists)
            {
                newSceneList.Add(new EditorBuildSettingsScene(path, true));
                changed = true;
            }
        }

        if (changed)
        {
            EditorBuildSettings.scenes = newSceneList.ToArray();
            Debug.Log($"[URP Migration] Successfully registered {newSceneList.Count - currentScenes.Length} streamable chunk scenes in Editor Build Settings!");
        }
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

    private static bool BindRendererData(ScriptableObject pipelineAsset, ScriptableObject rendererData)
    {
        bool changed = false;
        SerializedObject serializedPipeline = new SerializedObject(pipelineAsset);

        SerializedProperty rendererList = serializedPipeline.FindProperty("m_RendererDataList");
        if (rendererList != null)
        {
            if (rendererList.arraySize != 1)
            {
                rendererList.arraySize = 1;
                changed = true;
            }

            SerializedProperty firstRenderer = rendererList.GetArrayElementAtIndex(0);
            if (firstRenderer.objectReferenceValue != rendererData)
            {
                firstRenderer.objectReferenceValue = rendererData;
                changed = true;
            }
        }

        SerializedProperty defaultRenderer = serializedPipeline.FindProperty("m_DefaultRendererIndex");
        if (defaultRenderer != null && defaultRenderer.intValue != 0)
        {
            defaultRenderer.intValue = 0;
            changed = true;
        }

        if (changed)
            serializedPipeline.ApplyModifiedPropertiesWithoutUndo();

        return changed;
    }

    private static bool ApplyPerformanceDefaults(ScriptableObject pipelineAsset)
    {
        bool changed = false;
        SerializedObject serializedPipeline = new SerializedObject(pipelineAsset);

        changed |= SetSerializedBool(serializedPipeline, "m_RequireDepthTexture", true);
        changed |= SetSerializedBool(serializedPipeline, "m_RequireOpaqueTexture", true);
        changed |= SetSerializedInt(serializedPipeline, "m_MSAA", 1);
        changed |= SetSerializedInt(serializedPipeline, "m_ColorGradingMode", 1);
        changed |= SetSerializedFloat(serializedPipeline, "m_RenderScale", 1f);
        changed |= SetSerializedFloat(serializedPipeline, "m_ShadowDistance", 60f);
        changed |= SetSerializedInt(serializedPipeline, "m_MainLightShadowmapResolution", 4096);
        changed |= SetSerializedInt(serializedPipeline, "m_ShadowCascadeCount", 2);
        changed |= SetSerializedFloat(serializedPipeline, "m_ShadowDepthBias", 0.8f);
        changed |= SetSerializedFloat(serializedPipeline, "m_ShadowNormalBias", 0.45f);
        changed |= SetSerializedInt(serializedPipeline, "m_AdditionalLightsPerObjectLimit", 4);
        changed |= SetSerializedBool(serializedPipeline, "m_SupportsHDR", true);
        changed |= SetSerializedBool(serializedPipeline, "m_MainLightShadowsSupported", true);
        changed |= SetSerializedBool(serializedPipeline, "m_AdditionalLightShadowsSupported", false);
        changed |= SetSerializedBool(serializedPipeline, "m_SoftShadowsSupported", true);

        if (changed)
            serializedPipeline.ApplyModifiedPropertiesWithoutUndo();

        return changed;
    }

    private static bool SetSerializedBool(SerializedObject so, string propertyName, bool value)
    {
        SerializedProperty property = so.FindProperty(propertyName);
        if (property == null || property.propertyType != SerializedPropertyType.Boolean || property.boolValue == value)
            return false;

        property.boolValue = value;
        return true;
    }

    private static bool SetSerializedInt(SerializedObject so, string propertyName, int value)
    {
        SerializedProperty property = so.FindProperty(propertyName);
        if (property == null || property.propertyType != SerializedPropertyType.Integer || property.intValue == value)
            return false;

        property.intValue = value;
        return true;
    }

    private static bool SetSerializedFloat(SerializedObject so, string propertyName, float value)
    {
        SerializedProperty property = so.FindProperty(propertyName);
        if (property == null || property.propertyType != SerializedPropertyType.Float || Mathf.Approximately(property.floatValue, value))
            return false;

        property.floatValue = value;
        return true;
    }

    private static bool ConvertHdrpMaterialsToUrpLit()
    {
        Shader urpLit = Shader.Find("Universal Render Pipeline/Lit");
        if (urpLit == null)
            return false;

        string[] materialGuids = AssetDatabase.FindAssets("t:Material");
        int convertedCount = 0;

        for (int i = 0; i < materialGuids.Length; i++)
        {
            string materialPath = AssetDatabase.GUIDToAssetPath(materialGuids[i]);
            Material material = AssetDatabase.LoadAssetAtPath<Material>(materialPath);
            if (material == null || material.shader == null)
                continue;

            string shaderName = material.shader.name;
            if (!LooksLikeHdrpShader(shaderName))
                continue;

            Color baseColor = ResolveBaseColor(material);
            Texture baseMap = ResolveBaseMap(material);
            Texture normalMap = ResolveNormalMap(material);
            Texture emissionMap = ResolveEmissionMap(material);
            Color emissionColor = ResolveEmissionColor(material);
            float metallic = material.HasProperty("_Metallic") ? material.GetFloat("_Metallic") : 0f;
            float smoothness = material.HasProperty("_Smoothness") ? material.GetFloat("_Smoothness") : 0.5f;
            bool transparent = material.HasProperty("_SurfaceType") && material.GetFloat("_SurfaceType") > 0.5f;
            bool alphaClip = material.HasProperty("_AlphaCutoffEnable") && material.GetFloat("_AlphaCutoffEnable") > 0.5f;
            float cutoff = material.HasProperty("_AlphaCutoff") ? material.GetFloat("_AlphaCutoff") : 0.5f;

            material.shader = urpLit;

            if (material.HasProperty("_BaseColor"))
                material.SetColor("_BaseColor", baseColor);

            if (material.HasProperty("_BaseMap") && baseMap != null)
                material.SetTexture("_BaseMap", baseMap);

            if (material.HasProperty("_BumpMap") && normalMap != null)
            {
                material.SetTexture("_BumpMap", normalMap);
                material.EnableKeyword("_NORMALMAP");
            }

            if (material.HasProperty("_Metallic"))
                material.SetFloat("_Metallic", metallic);

            if (material.HasProperty("_Smoothness"))
                material.SetFloat("_Smoothness", smoothness);

            if (material.HasProperty("_EmissionMap") && emissionMap != null)
                material.SetTexture("_EmissionMap", emissionMap);

            if (material.HasProperty("_EmissionColor"))
                material.SetColor("_EmissionColor", emissionColor);

            if ((emissionMap != null || emissionColor.maxColorComponent > 0.0001f) && material.HasProperty("_EmissionColor"))
                material.EnableKeyword("_EMISSION");

            if (material.HasProperty("_AlphaClip"))
                material.SetFloat("_AlphaClip", alphaClip ? 1f : 0f);

            if (material.HasProperty("_Cutoff"))
                material.SetFloat("_Cutoff", cutoff);

            if (material.HasProperty("_Surface"))
                material.SetFloat("_Surface", transparent ? 1f : 0f);

            if (transparent)
                material.renderQueue = (int)RenderQueue.Transparent;
            else if (alphaClip)
                material.renderQueue = (int)RenderQueue.AlphaTest;
            else
                material.renderQueue = -1;

            EditorUtility.SetDirty(material);
            convertedCount++;
        }

        if (convertedCount > 0)
            Debug.Log($"[URP Migration] Converted {convertedCount} HDRP materials to URP/Lit.");

        return convertedCount > 0;
    }

    private static bool LooksLikeHdrpShader(string shaderName)
    {
        if (string.IsNullOrWhiteSpace(shaderName))
            return false;

        return shaderName.IndexOf("HDRP", StringComparison.OrdinalIgnoreCase) >= 0 ||
               shaderName.IndexOf("High Definition Render Pipeline", StringComparison.OrdinalIgnoreCase) >= 0;
    }

    private static Color ResolveBaseColor(Material material)
    {
        if (material.HasProperty("_BaseColor"))
            return material.GetColor("_BaseColor");

        if (material.HasProperty("_Color"))
            return material.GetColor("_Color");

        return Color.white;
    }

    private static Texture ResolveBaseMap(Material material)
    {
        if (material.HasProperty("_BaseColorMap"))
            return material.GetTexture("_BaseColorMap");

        if (material.HasProperty("_BaseMap"))
            return material.GetTexture("_BaseMap");

        if (material.HasProperty("_MainTex"))
            return material.GetTexture("_MainTex");

        return null;
    }

    private static Texture ResolveNormalMap(Material material)
    {
        if (material.HasProperty("_NormalMap"))
            return material.GetTexture("_NormalMap");

        if (material.HasProperty("_BumpMap"))
            return material.GetTexture("_BumpMap");

        return null;
    }

    private static Texture ResolveEmissionMap(Material material)
    {
        if (material.HasProperty("_EmissiveColorMap"))
            return material.GetTexture("_EmissiveColorMap");

        if (material.HasProperty("_EmissionMap"))
            return material.GetTexture("_EmissionMap");

        return null;
    }

    private static Color ResolveEmissionColor(Material material)
    {
        if (material.HasProperty("_EmissiveColor"))
            return material.GetColor("_EmissiveColor");

        if (material.HasProperty("_EmissionColor"))
            return material.GetColor("_EmissionColor");

        return Color.black;
    }
}
