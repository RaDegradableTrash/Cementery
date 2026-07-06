using UnityEngine;

public class SnowAccumulationManager : MonoBehaviour
{
    public static SnowAccumulationManager Instance { get; private set; }

    [Header("Snow Map Settings")]
    public int mapResolution = 512;
    public int occlusionMapResolution = 256;
    [Tooltip("World-space diameter of the snow coverage area. This follows the player so snow is always global.")]
    public float mapWorldSize = 512f;
    [Tooltip("Auto-updated at runtime to follow the player. Do not set manually.")]
    public Vector3 mapCenter = Vector3.zero;
    [Tooltip("How many world-units the player must move before the snow map re-centers. Prevents constant flickering.")]
    public float trackingSnapInterval = 64f;
    [Min(0.02f)] public float occlusionUpdateInterval = 0.12f;
    [Min(0.02f)] public float globalSnowUpdateInterval = 0.25f;
    [Min(0.05f)] public float shaderParamRefreshInterval = 0.5f;

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
    private float _occlusionTimer;
    private float _globalSnowTimer;
    private float _shaderParamTimer;

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
        int snowResolution = Mathf.Clamp(mapResolution, 256, 1024);
        int occlusionResolution = Mathf.Clamp(occlusionMapResolution, 128, 512);

        snowHeightMap = new RenderTexture(snowResolution, snowResolution, 0, RenderTextureFormat.RHalf);
        snowHeightMap.name = "SnowHeightMap";
        snowHeightMap.filterMode = FilterMode.Bilinear;
        snowHeightMap.wrapMode = TextureWrapMode.Clamp;
        
        // CLEAR garbage data from RenderTexture initialization!
        snowHeightMap.DiscardContents();
        RenderTexture.active = snowHeightMap;
        GL.Clear(true, true, Color.black);
        RenderTexture.active = null;

        occlusionMap = new RenderTexture(occlusionResolution, occlusionResolution, 0, RenderTextureFormat.RHalf);
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
            snowHeightMap.DiscardContents();
            RenderTexture.active = snowHeightMap;
            GL.Clear(true, true, Color.black);
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

    private void AccumulateGlobalSnow(float deltaSeconds)
    {
        if (modificationMaterial == null || snowHeightMap == null || globalSnowRate <= 0f) return;

        modificationMaterial.SetVector("_BrushStrength", new Vector4(globalSnowRate * deltaSeconds, 0, 0, 0));
        
        Vector4 snowParams = new Vector4(mapCenter.x, mapCenter.z, mapWorldSize, 1f / mapWorldSize);
        modificationMaterial.SetVector("_SnowMapParams", snowParams);

        RenderTexture tempRT = RenderTexture.GetTemporary(snowHeightMap.descriptor);
        
        // Pass 3: Global Accumulation
        Graphics.Blit(snowHeightMap, tempRT, modificationMaterial, 3);
        Graphics.Blit(tempRT, snowHeightMap);

        RenderTexture.ReleaseTemporary(tempRT);
    }

    private void Update()
    {
        bool shaderParamsDirty = false;

        // ── Track player so snow coverage is always global ──────────────────
        Transform tracker = ResolveTrackingTarget();
        if (tracker != null)
        {
            // Snap to a grid to avoid the snow map sliding pixel-by-pixel every frame
            float snap = trackingSnapInterval;
            float snappedX = Mathf.Round(tracker.position.x / snap) * snap;
            float snappedZ = Mathf.Round(tracker.position.z / snap) * snap;
            Vector3 newCenter = new Vector3(snappedX, 0f, snappedZ);

            if (newCenter != mapCenter)
            {
                mapCenter = newCenter;
                shaderParamsDirty = true;
            }
        }

        _occlusionTimer += Time.deltaTime;
        if (_occlusionTimer >= Mathf.Max(0.02f, occlusionUpdateInterval))
        {
            _occlusionTimer = 0f;
            UpdateOcclusionMap();
        }

        _globalSnowTimer += Time.deltaTime;
        if (_globalSnowTimer >= Mathf.Max(0.02f, globalSnowUpdateInterval))
        {
            float elapsed = _globalSnowTimer;
            _globalSnowTimer = 0f;
            AccumulateGlobalSnow(elapsed);
        }

        _shaderParamTimer += Time.deltaTime;
        if (shaderParamsDirty || _shaderParamTimer >= Mathf.Max(0.05f, shaderParamRefreshInterval))
        {
            _shaderParamTimer = 0f;
            UpdateGlobalShaderParams();
        }

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

    private Transform ResolveTrackingTarget()
    {
        // Re-use the already-assigned playerCar reference if it is still valid
        if (playerCar != null && playerCar.gameObject.activeInHierarchy)
            return playerCar;

        // Otherwise search in the same priority order as WorldStreamer
        GameObject player = GameObject.FindGameObjectWithTag("Player");
        if (player != null && player.activeInHierarchy)
        {
            playerCar = player.transform;
            return playerCar;
        }

        var rv = FindObjectOfType<RVSystem.RVController>();
        if (rv != null && rv.gameObject.activeInHierarchy)
        {
            playerCar = rv.transform;
            return playerCar;
        }

        if (Camera.main != null)
            return Camera.main.transform;

        return null;
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
