using UnityEngine;

public class SnowAccumulationManager : MonoBehaviour
{
    public static SnowAccumulationManager Instance { get; private set; }

    [Header("Snow Map Settings")]
    public int mapResolution = 1024;
    public float mapWorldSize = 100f;
    public Vector3 mapCenter = Vector3.zero;

    [Header("Resources")]
    public Shader modificationShader;
    
    [Header("Runtime Debug (Do not set)")]
    public RenderTexture snowHeightMap;
    public Material modificationMaterial;

    private void Awake()
    {
        if (Instance == null) Instance = this;
        else Destroy(gameObject);

        InitializeMap();
    }

    private void InitializeMap()
    {
        snowHeightMap = new RenderTexture(mapResolution, mapResolution, 0, RenderTextureFormat.RFloat);
        snowHeightMap.name = "SnowHeightMap";
        snowHeightMap.Create();

        ClearSnow();

        if (modificationShader != null)
        {
            modificationMaterial = new Material(modificationShader);
        }
        else
        {
            Debug.LogError("Modification Shader is not assigned on SnowAccumulationManager.");
        }

        UpdateGlobalShaderParams();
    }

    public void ClearSnow()
    {
        if (snowHeightMap != null)
        {
            RenderTexture.active = snowHeightMap;
            GL.Clear(false, true, Color.black);
            RenderTexture.active = null;
        }
    }

    private void UpdateGlobalShaderParams()
    {
        Shader.SetGlobalTexture("_GlobalSnowHeightMap", snowHeightMap);
        Vector4 snowParams = new Vector4(mapCenter.x, mapCenter.z, mapWorldSize, 1f / mapWorldSize);
        Shader.SetGlobalVector("_GlobalSnowMapParams", snowParams);
    }

    public void VacuumSnow(Vector3 pos, float radius, float speed)
    {
        // speed represents amount removed per second, we'll convert to per frame
        ModifySnow(pos, radius, -speed * Time.deltaTime);
    }

    public void AddSnowAtPoint(Vector3 pos, float radius, float amount)
    {
        ModifySnow(pos, radius, amount);
    }

    private void ModifySnow(Vector3 pos, float radius, float amount)
    {
        if (modificationMaterial == null || snowHeightMap == null) return;

        modificationMaterial.SetVector("_BrushParams", new Vector4(pos.x, pos.y, pos.z, radius));
        modificationMaterial.SetVector("_BrushStrength", new Vector4(amount, 0, 0, 0));
        
        Vector4 snowParams = new Vector4(mapCenter.x, mapCenter.z, mapWorldSize, 1f / mapWorldSize);
        modificationMaterial.SetVector("_SnowMapParams", snowParams);

        RenderTexture tempRT = RenderTexture.GetTemporary(snowHeightMap.descriptor);
        
        // Pass 0: Add Snow
        Graphics.Blit(snowHeightMap, tempRT, modificationMaterial, 0);
        Graphics.Blit(tempRT, snowHeightMap);

        // Pass 1: Multiple Blur Iterations for smooth transition (5 times)
        for (int i = 0; i < 5; i++)
        {
            Graphics.Blit(snowHeightMap, tempRT, modificationMaterial, 1);
            Graphics.Blit(tempRT, snowHeightMap);
        }

        RenderTexture.ReleaseTemporary(tempRT);
    }

    private void OnDestroy()
    {
        if (snowHeightMap != null)
        {
            snowHeightMap.Release();
            Destroy(snowHeightMap);
        }
        if (modificationMaterial != null)
        {
            Destroy(modificationMaterial);
        }
    }
}
