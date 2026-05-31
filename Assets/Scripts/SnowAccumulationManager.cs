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
    
    [Header("Occlusion Setup")]
    public Transform playerCar;
    public float carOcclusionRadius = 3.5f;
    public float globalSnowRate = 0.05f;
    
    [Header("Runtime Debug (Do not set)")]
    public Material modificationMaterial;
    private RenderTexture snowHeightMap;
    private RenderTexture occlusionMap;

    private void Awake()
    {
        if (Instance == null) Instance = this;
        else Destroy(gameObject);

        if (playerCar == null)
        {
            var carControl = FindObjectOfType<CarControl>();
            if (carControl != null) playerCar = carControl.transform;
            else 
            {
                GameObject p = GameObject.FindGameObjectWithTag("Player");
                if (p != null) playerCar = p.transform;
            }
        }

        if (modificationShader == null)
        {
            modificationShader = Shader.Find("Hidden/SnowModification");
        }

        InitializeMap();
    }

    private void InitializeMap()
    {
        // 1. Create a 1024x1024 RenderTexture for high precision (10cm per pixel for 100x100m)
        snowHeightMap = new RenderTexture(1024, 1024, 0, RenderTextureFormat.RHalf);
        snowHeightMap.name = "SnowHeightMap";
        snowHeightMap.filterMode = FilterMode.Bilinear;
        
        // CLEAR garbage data from RenderTexture initialization!
        RenderTexture.active = snowHeightMap;
        GL.Clear(false, true, Color.clear);
        RenderTexture.active = null;

        // 2. Create the occlusion map
        occlusionMap = new RenderTexture(512, 512, 0, RenderTextureFormat.RHalf);
        occlusionMap.name = "OcclusionMap";
        occlusionMap.filterMode = FilterMode.Bilinear;
        occlusionMap.wrapMode = TextureWrapMode.Clamp;
        occlusionMap.Create();

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

    public void UpdateGlobalShaderParams()
    {
        if (snowHeightMap != null)
        {
            Shader.SetGlobalTexture("_GlobalSnowHeightMap", snowHeightMap);
        }
        if (modificationMaterial != null && occlusionMap != null)
        {
            modificationMaterial.SetTexture("_OcclusionMap", occlusionMap);
        }
        Vector4 snowParams = new Vector4(mapCenter.x, mapCenter.z, mapWorldSize, 1f / mapWorldSize);
        Shader.SetGlobalVector("_GlobalSnowMapParams", snowParams);
    }

    private void UpdateOcclusionMap()
    {
        if (occlusionMap == null || modificationMaterial == null) return;

        if (playerCar != null)
        {
            modificationMaterial.SetVector("_CarParams", new Vector4(playerCar.position.x, playerCar.position.y, playerCar.position.z, carOcclusionRadius));
            modificationMaterial.SetVector("_CarParamsForward", new Vector4(playerCar.forward.x, playerCar.forward.y, playerCar.forward.z, 4.5f));
            Vector4 snowParams = new Vector4(mapCenter.x, mapCenter.z, mapWorldSize, 1f / mapWorldSize);
            modificationMaterial.SetVector("_SnowMapParams", snowParams);
            
            // Pass 2: Draw Occlusion mask
            Graphics.Blit(null, occlusionMap, modificationMaterial, 2);
        }
        else
        {
            // If no car, clear to white (no occlusion)
            RenderTexture.active = occlusionMap;
            GL.Clear(false, true, Color.white);
            RenderTexture.active = null;
        }
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

        RenderTexture.ReleaseTemporary(tempRT);
    }

    private void OnDestroy()
    {
        if (snowHeightMap != null)
        {
            snowHeightMap.Release();
            Destroy(snowHeightMap);
        }
        if (occlusionMap != null)
        {
            occlusionMap.Release();
            Destroy(occlusionMap);
        }
        if (modificationMaterial != null)
        {
            Destroy(modificationMaterial);
        }
    }

    // --- 调试代码：用于输出 RenderTexture 中心的最大高度 ---
    private void Update()
    {
        UpdateOcclusionMap();



        UpdateGlobalShaderParams(); // Ensure params are always synced, even if mapCenter is modified externally
        
        if (Input.GetKeyDown(KeyCode.P))
        {
            DebugSnowHeight();
        }

        // 调试按键：按住 ] 键将雪变为绿色
        if (Input.GetKey(KeyCode.RightBracket))
        {
            Shader.SetGlobalFloat("_SnowDebugGreen", 1f);
        }
        else
        {
            Shader.SetGlobalFloat("_SnowDebugGreen", 0f);
        }

        UpdateOcclusionMap();
    }

    private void DebugSnowHeight()
    {
        if (snowHeightMap == null) return;
        RenderTexture.active = snowHeightMap;
        Texture2D tex = new Texture2D(snowHeightMap.width, snowHeightMap.height, TextureFormat.RGBAFloat, false);
        tex.ReadPixels(new Rect(0, 0, snowHeightMap.width, snowHeightMap.height), 0, 0);
        tex.Apply();
        RenderTexture.active = null;

        float maxHeight = 0f;
        Color[] pixels = tex.GetPixels();
        foreach (var p in pixels)
        {
            if (p.r > maxHeight) maxHeight = p.r;
        }
        Debug.Log($"[SnowDebug] 当前高度图中探测到的最高雪层厚度为: {maxHeight}。Shader Cutoff 为 0.05");
        Destroy(tex);
    }
}
