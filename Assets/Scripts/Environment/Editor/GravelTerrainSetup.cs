using UnityEngine;
using UnityEditor;
using System.IO;

namespace EnvironmentSystem
{
    public class GravelTerrainSetup : EditorWindow
    {
        [MenuItem("Tools/Gravel Terrain Setup")]
        public static void ShowWindow()
        {
            SetupGravelMaterial();
        }

        public static void SetupGravelMaterial()
        {
            // 1. Ensure Textures are imported properly as 2D textures and Normal Maps
            string albedoPath = "Assets/Textures/Gravel_Albedo.png";
            string normalPath = "Assets/Textures/Gravel_Normal.png";

            if (!File.Exists(albedoPath))
            {
                Debug.LogError($"Albedo texture not found at {albedoPath}");
                return;
            }

            // Force import textures
            AssetDatabase.ImportAsset(albedoPath);
            AssetDatabase.ImportAsset(normalPath);

            TextureImporter albedoImporter = AssetImporter.GetAtPath(albedoPath) as TextureImporter;
            if (albedoImporter != null)
            {
                albedoImporter.textureType = TextureImporterType.Default;
                albedoImporter.sRGBTexture = true;
                albedoImporter.SaveAndReimport();
            }

            TextureImporter normalImporter = AssetImporter.GetAtPath(normalPath) as TextureImporter;
            if (normalImporter != null)
            {
                normalImporter.textureType = TextureImporterType.NormalMap;
                normalImporter.SaveAndReimport();
            }

            // 2. Load or Create the Material
            string matPath = "Assets/Resources/GravelTerrain.mat";
            Material mat = AssetDatabase.LoadAssetAtPath<Material>(matPath);

            if (mat == null)
            {
                Shader triplanarShader = Shader.Find("Environment/URPTriplanarEnvironment");
                if (triplanarShader == null)
                {
                    Debug.LogError("Shader Environment/URPTriplanarEnvironment not found!");
                    return;
                }
                mat = new Material(triplanarShader);
                AssetDatabase.CreateAsset(mat, matPath);
            }

            // 3. Configure Material properties
            Texture albedoTex = AssetDatabase.LoadAssetAtPath<Texture>(albedoPath);
            Texture normalTex = AssetDatabase.LoadAssetAtPath<Texture>(normalPath);

            mat.SetTexture("_MainTex", albedoTex);
            mat.SetTexture("_NormalMap", normalTex);
            mat.SetColor("_Color", Color.white); // Neutral tint to avoid blacking out textures
            mat.SetFloat("_TriplanarScale", 1.2f); // Adjust scale for nice gravel density
            mat.SetFloat("_BlendSharpness", 6.0f);

            EditorUtility.SetDirty(mat);
            AssetDatabase.SaveAssets();

            string matGuid = AssetDatabase.AssetPathToGUID(matPath);
            Debug.Log($"[GravelTerrainSetup] Gravel Material created and configured! GUID: {matGuid}");
        }
    }
}
