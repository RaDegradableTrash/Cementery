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
    private RenderTexture snowHeightMap;
    private Material modificationMaterial;
    private bool _needsBlur = false;

    private void Awake()
    {
        if (Instance == null) Instance = this;
        else Destroy(gameObject);

        if (modificationShader == null)
        {
            modificationShader = Shader.Find("Hidden/SnowModification");
        }

        InitializeMap();
    }

    private void InitializeMap()
    {
        snowHeightMap = new RenderTexture(mapResolution, mapResolution, 0, RenderTextureFormat.ARGBFloat);
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

    public void UpdateGlobalShaderParams()
    {
        if (snowHeightMap != null)
        {
            Shader.SetGlobalTexture("_GlobalSnowHeightMap", snowHeightMap);
        }
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

        RenderTexture.ReleaseTemporary(tempRT);
        
        _needsBlur = true; // Mark for a SINGLE blur pass this frame in LateUpdate
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

    // --- 调试代码：用于输出 RenderTexture 中心的最大高度 ---
    private void Update()
    {
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
    }

    private void LateUpdate()
    {
        // 优化：将所有粒子的模糊操作集中到同一帧的最后执行一次！
        // 既不会产生延迟闪烁（因为在渲染前执行），又把每帧几百次的 Blit 降为了 2 次！
        if (_needsBlur && modificationMaterial != null && snowHeightMap != null)
        {
            RenderTexture tempRT = RenderTexture.GetTemporary(snowHeightMap.descriptor);
            for (int i = 0; i < 2; i++) 
            {
                Graphics.Blit(snowHeightMap, tempRT, modificationMaterial, 1);
                Graphics.Blit(tempRT, snowHeightMap);
            }
            RenderTexture.ReleaseTemporary(tempRT);
            _needsBlur = false;
        }
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
