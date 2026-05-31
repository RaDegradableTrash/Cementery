using UnityEngine;

public class DynamicSnowObject : MonoBehaviour
{
    [Header("Settings")]
    public float snowResolution = 0.05f; // 5cm per pixel
    public float cutoff = 0.1f;
    
    private RenderTexture localSnowMap;
    private Material localSnowMat;
    private Material localModMat;
    private Bounds localBounds;

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
        // Initialize to black
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
        if (localSnowMat != null)
        {
            localSnowMat.SetMatrix("_RootWorldToLocal", transform.worldToLocalMatrix);
        }
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
        
        RenderTexture temp = RenderTexture.GetTemporary(localSnowMap.width, localSnowMap.height, 0, localSnowMap.format);
        
        localModMat.SetVector("_BrushParams", new Vector4(u, v, radiusU, radiusV));
        localModMat.SetVector("_BrushStrength", new Vector4(amount, localPos.y, 0, 0));
        
        Graphics.Blit(localSnowMap, temp, localModMat, 0);
        Graphics.Blit(temp, localSnowMap);
        
        RenderTexture.ReleaseTemporary(temp);
    }

    private void OnDestroy()
    {
        if (localSnowMap != null)
        {
            localSnowMap.Release();
            Destroy(localSnowMap);
        }
        if (localSnowMat != null) Destroy(localSnowMat);
        if (localModMat != null) Destroy(localModMat);
    }
}
