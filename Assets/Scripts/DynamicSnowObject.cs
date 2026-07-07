using UnityEngine;

public class DynamicSnowObject : MonoBehaviour
{
    [Header("Settings")]
    public float snowResolution = 0.05f; // 5cm per pixel
    public float cutoff = 0.1f;
    
    private RenderTexture localSnowMap;
    private RenderTexture localScratchMap;
    private Material localSnowMat;
    private Material localModMat;
    private Bounds localBounds;
    private bool hasLocalSnowMaterial;

    void Start()
    {
        // 1. Calculate Local Bounds of all renderers
        var renderers = GetComponentsInChildren<MeshRenderer>();
        if (renderers.Length == 0) return;

        localBounds = new Bounds(Vector3.zero, new Vector3(12f, 12f, 12f));

        // 2. Create Local Snow Map
        int width = Mathf.CeilToInt(localBounds.size.x / snowResolution);
        int depth = Mathf.CeilToInt(localBounds.size.z / snowResolution);
        width = Mathf.Clamp(width, 16, 512);
        depth = Mathf.Clamp(depth, 16, 512);

        localSnowMap = new RenderTexture(width, depth, 0, RenderTextureFormat.ARGBHalf);
        localSnowMap.filterMode = FilterMode.Bilinear;
        localSnowMap.wrapMode = TextureWrapMode.Clamp;

        localScratchMap = new RenderTexture(width, depth, 0, RenderTextureFormat.ARGBHalf);
        localScratchMap.name = name + "_LocalSnowScratchMap";
        localScratchMap.filterMode = FilterMode.Bilinear;
        localScratchMap.wrapMode = TextureWrapMode.Clamp;
        localScratchMap.Create();
        // Initialize to black
        localSnowMap.DiscardContents();
        RenderTexture.active = localSnowMap;
        GL.Clear(false, true, new Color(0, -1000f, 0, 0)); // R: snow height, G: highest hit Y
        RenderTexture.active = null;

        // 3. Create Material
        Shader shader = Shader.Find("Environment/LocalSnowBlanket");
        if (shader != null)
        {
            localSnowMat = new Material(shader);
            localSnowMat.SetTexture("_LocalSnowHeightMap", localSnowMap);
            localSnowMat.SetVector("_LocalSnowBounds", new Vector4(localBounds.min.x, localBounds.min.z, localBounds.size.x, localBounds.size.z));
            localSnowMat.SetFloat("_Cutoff", cutoff);
            UpdateSnowMaterialTransform();
            hasLocalSnowMaterial = true;
        }
        
        Shader modShader = Shader.Find("Hidden/LocalSnowModification");
        if (modShader != null)
        {
            localModMat = new Material(modShader);
        }

        // 4. Create Snow Meshes
        foreach (var r in renderers)
        {
            if (r.name.Contains("SnowLayer")) continue;
            
            // 物理名称过滤：彻底排除车轮、轮胎、车内地板、方向盘、座椅、玻璃等不可能落雪的内部或底盘部件！
            string lowerName = r.name.ToLower();
            if (lowerName.Contains("wheel") || lowerName.Contains("tire") || 
                lowerName.Contains("interior") || lowerName.Contains("floor") || 
                lowerName.Contains("seat") || lowerName.Contains("steering") || 
                lowerName.Contains("glass") || lowerName.Contains("window") ||
                lowerName.Contains("cabin") || lowerName.Contains("door") || 
                lowerName.Contains("underside") || lowerName.Contains("chassis"))
            {
                continue;
            }

            MeshFilter mf = r.GetComponent<MeshFilter>();
            if (mf == null || mf.sharedMesh == null) continue;

            GameObject snowGO = new GameObject(r.name + "_SnowLayer");
            snowGO.transform.SetParent(r.transform, false);
            snowGO.transform.localPosition = Vector3.zero;
            snowGO.transform.localRotation = Quaternion.identity;
            snowGO.transform.localScale = Vector3.one;

            var snowMF = snowGO.AddComponent<MeshFilter>();
            snowMF.sharedMesh = mf.sharedMesh;
            
            var snowMR = snowGO.AddComponent<MeshRenderer>();
            snowMR.sharedMaterial = localSnowMat;
            snowMR.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        }
    }

    void Update()
    {
        if (hasLocalSnowMaterial && transform.hasChanged)
        {
            UpdateSnowMaterialTransform();
        }
    }

    private void UpdateSnowMaterialTransform()
    {
        if (localSnowMat == null)
        {
            return;
        }

        localSnowMat.SetMatrix("_RootWorldToLocal", transform.worldToLocalMatrix);
        transform.hasChanged = false;
    }

    public void AddSnowLocal(Vector3 worldPos, float radius, float amount)
    {
        if (localSnowMap == null || SnowAccumulationManager.Instance == null) return;
        
        Vector3 localPos = transform.InverseTransformPoint(worldPos);
        
        // Convert to UV
        float u = (localPos.x - localBounds.min.x) / localBounds.size.x;
        float v = (localPos.z - localBounds.min.z) / localBounds.size.z;
        
        if (u < 0 || u > 1 || v < 0 || v > 1) return;

        // Convert world radius to UV radius
        float radiusU = radius / localBounds.size.x;
        float radiusV = radius / localBounds.size.z;

        if (localModMat == null) return;
        
        if (!EnsureLocalScratchMap())
        {
            return;
        }
        
        localModMat.SetVector("_BrushParams", new Vector4(u, v, radiusU, radiusV));
        localModMat.SetVector("_BrushStrength", new Vector4(amount, localPos.y, 0, 0));
        
        Graphics.Blit(localSnowMap, localScratchMap, localModMat, 0);
        Graphics.Blit(localScratchMap, localSnowMap);
    }

    private bool EnsureLocalScratchMap()
    {
        if (localSnowMap == null)
            return false;

        if (localScratchMap != null &&
            localScratchMap.width == localSnowMap.width &&
            localScratchMap.height == localSnowMap.height &&
            localScratchMap.format == localSnowMap.format)
        {
            return true;
        }

        if (localScratchMap != null)
        {
            localScratchMap.Release();
            Destroy(localScratchMap);
        }

        localScratchMap = new RenderTexture(localSnowMap.width, localSnowMap.height, 0, localSnowMap.format);
        localScratchMap.name = name + "_LocalSnowScratchMap";
        localScratchMap.filterMode = localSnowMap.filterMode;
        localScratchMap.wrapMode = localSnowMap.wrapMode;
        localScratchMap.Create();
        return true;
    }

    private void OnDestroy()
    {
        if (localSnowMap != null)
        {
            localSnowMap.Release();
            Destroy(localSnowMap);
        }
        if (localScratchMap != null)
        {
            localScratchMap.Release();
            Destroy(localScratchMap);
        }
        if (localSnowMat != null) Destroy(localSnowMat);
        if (localModMat != null) Destroy(localModMat);
    }
}
