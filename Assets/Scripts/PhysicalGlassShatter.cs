using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class PhysicalGlassShatter : MonoBehaviour
{
    [Header("Shatter Generation")]
    public int numRays = 15;
    public float innerRadiusMin = 0.05f;
    public float innerRadiusMax = 0.15f;
    public float midRadiusMin = 0.3f;
    public float midRadiusMax = 0.5f;

    [Header("Death Crack Settings")]
    [Tooltip("死亡时初步裂开的缝隙大小")]
    public float crackSeparation = 0.04f; 

    [Header("Physics Settings")]
    public float explosionForce = 15f;
    public float explosionRadius = 5f;

    [Header("Camera & Rendering")]
    public Camera mainCamera;
    
    private GameObject shatterRoot;
    private Material glassMaterial;
    private RenderTexture screenRT;
    private Camera shatterCamera;

    private List<Rigidbody> shardRigidbodies = new List<Rigidbody>();
    private List<Collider> shardColliders = new List<Collider>();
    private bool isCracked = false;

    public void TriggerShatter()
    {
        isCracked = false;
        if (mainCamera == null) mainCamera = Camera.main;
        StartCoroutine(ShatterRoutine());
    }

    public void Crack()
    {
        isCracked = true;
        if (shatterRoot != null)
        {
            StartCoroutine(ShakeScreen(shatterRoot.transform, 1.0f));
        }
    }

    private IEnumerator ShatterRoutine()
    {
        // 1. 创建 RenderTexture 并接管主摄像机。使用 DefaultHDR 保证能够完美捕获天空盒和高光
        RenderTextureFormat format = mainCamera.allowHDR ? RenderTextureFormat.DefaultHDR : RenderTextureFormat.Default;
        screenRT = new RenderTexture(Screen.width, Screen.height, 24, format);
        screenRT.antiAliasing = Mathf.Max(1, QualitySettings.antiAliasing);
        screenRT.Create();
        mainCamera.targetTexture = screenRT;

        // Removed Canvas modification to allow Death UI (letterbox, text) to stay on top 
        // as ScreenSpaceOverlay, so it won't be shattered or covered by the flying glass fragments.
        // 主相机不再渲染 Water 层，避免穿帮
        mainCamera.cullingMask &= ~(1 << 4);

        // ！！关键修复！！
        // 此时 screenRT 还是空的（青色），必须等待当前帧渲染完毕，
        // 主摄像机把画面写入 screenRT 后，我们再生成并显示玻璃，这样就绝不会闪烁青色！
        yield return new WaitForEndOfFrame();

        // 3. 创建专属的 ShatterCamera 来渲染碎片到屏幕
        GameObject camObj = new GameObject("ShatterCamera");
        camObj.transform.position = mainCamera.transform.position;
        camObj.transform.rotation = mainCamera.transform.rotation;
        shatterCamera = camObj.AddComponent<Camera>();
        shatterCamera.CopyFrom(mainCamera);
        shatterCamera.targetTexture = null; // 输出到屏幕
        shatterCamera.cullingMask = 1 << 4; // 只看 Layer 4 (Water)
        shatterCamera.clearFlags = CameraClearFlags.SolidColor;
        shatterCamera.backgroundColor = Color.black;

        // 4. 生成动态玻璃材质 (使用自发光保证画面完全不丢失亮度，同时保留物理引擎的高光反射)
        glassMaterial = new Material(Shader.Find("Universal Render Pipeline/Lit"));
        
        // 核心修复：将画面放入自发光通道，这样不受场景环境光过暗影响，绝对还原原始画面！
        glassMaterial.SetTexture("_EmissionMap", screenRT);
        glassMaterial.SetColor("_EmissionColor", Color.white);
        glassMaterial.EnableKeyword("_EMISSION");
        
        // 基础底色压暗，只用来接受真实环境的光影折射和高光
        glassMaterial.SetColor("_BaseColor", new Color(0.1f, 0.1f, 0.1f, 1f));
        glassMaterial.SetFloat("_Smoothness", 0.98f); 
        glassMaterial.SetFloat("_Metallic", 0.8f);

        // 5. 生成 3D 碎片并悬停在空中
        GenerateShatterMeshes();
    }

    public void TriggerFall(System.Action onRespawnCallback)
    {
        StartCoroutine(FallRoutine(onRespawnCallback));
    }

    private IEnumerator FallRoutine(System.Action onRespawnCallback)
    {
        // 1. 主摄像机恢复，此时 RT 画面定格为最后一帧
        mainCamera.targetTexture = null;
        mainCamera.cullingMask |= (1 << 4); // 让主摄像机能看到玻璃
        
        // 销毁专属的 ShatterCamera
        if (shatterCamera != null)
        {
            Destroy(shatterCamera.gameObject);
        }

        // 2. 调用重生逻辑（玩家和摄像机瞬间传送到天上）
        onRespawnCallback?.Invoke();
        
        // 等待一帧，确保摄像机位置在重生点更新完毕
        yield return null; 

        // 3. 把玻璃瞬间移动到新摄像机的面前！
        if (shatterRoot != null)
        {
            shatterRoot.transform.SetParent(mainCamera.transform, false);
            shatterRoot.transform.localPosition = new Vector3(0, 0, 1.0f);
            shatterRoot.transform.localRotation = Quaternion.identity;
            shatterRoot.transform.SetParent(null, true);

            // 4. 重新启用碰撞器，解冻物理，引爆！
            foreach (Collider col in shardColliders)
            {
                if (col != null) col.enabled = true;
            }

            Vector3 impactCenter = Vector3.zero; // Local zero
            Vector3 explosionPos = shatterRoot.transform.TransformPoint(new Vector3(impactCenter.x, impactCenter.y, 0.3f)); 
            
            foreach (Rigidbody rb in shardRigidbodies)
            {
                if (rb == null) continue;
                rb.isKinematic = false;
                rb.AddExplosionForce(explosionForce, explosionPos, explosionRadius, 0.5f, ForceMode.Impulse);
                rb.AddTorque(Random.insideUnitSphere * 20f, ForceMode.Impulse);
            }

            // 5. 延迟清理
            Destroy(shatterRoot, 5f);
        }

        if (screenRT != null)
        {
            screenRT.Release();
            Destroy(screenRT, 5f);
        }
        if (glassMaterial != null)
        {
            Destroy(glassMaterial, 5f);
        }
    }

    private void GenerateShatterMeshes()
    {
        float zDist = 1.0f; 
        float h = 2.0f * zDist * Mathf.Tan(mainCamera.fieldOfView * 0.5f * Mathf.Deg2Rad);
        float w = h * mainCamera.aspect;
        float halfW = w / 2f;
        float halfH = h / 2f;

        // Bias impact center towards the edges/corners instead of the center
        float angleImpact = Random.Range(0f, Mathf.PI * 2f);
        float rX = Random.Range(0.6f, 0.95f) * halfW;
        float rY = Random.Range(0.6f, 0.95f) * halfH;
        Vector2 impactCenter = new Vector2(Mathf.Cos(angleImpact) * rX, Mathf.Sin(angleImpact) * rY);

        List<Vector2> boundaryPoints = new List<Vector2>();
        
        boundaryPoints.Add(new Vector2(-halfW, -halfH));
        boundaryPoints.Add(new Vector2(halfW, -halfH));
        boundaryPoints.Add(new Vector2(halfW, halfH));
        boundaryPoints.Add(new Vector2(-halfW, halfH));

        for(int i=0; i<numRays; i++)
        {
            float angle = Random.Range(0f, Mathf.PI * 2f);
            Vector2 dir = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
            boundaryPoints.Add(GetRectIntersection(impactCenter, dir, halfW, halfH));
        }

        boundaryPoints.Sort((a, b) => 
        {
            float angleA = Mathf.Atan2(a.y - impactCenter.y, a.x - impactCenter.x);
            float angleB = Mathf.Atan2(b.y - impactCenter.y, b.x - impactCenter.x);
            return angleA.CompareTo(angleB);
        });

        shatterRoot = new GameObject("PhysicalShatterRoot");
        shatterRoot.transform.SetParent(shatterCamera.transform, false);
        shatterRoot.transform.localPosition = new Vector3(0, 0, zDist);
        shatterRoot.transform.localRotation = Quaternion.identity;

        shardRigidbodies.Clear();
        shardColliders.Clear();

        for (int i = 0; i < boundaryPoints.Count; i++)
        {
            Vector2 p1 = boundaryPoints[i];
            Vector2 p2 = boundaryPoints[(i + 1) % boundaryPoints.Count];

            Vector2 dir1 = (p1 - impactCenter).normalized;
            Vector2 dir2 = (p2 - impactCenter).normalized;

            float r1_Inner = Random.Range(innerRadiusMin, innerRadiusMax) * halfH;
            float r2_Inner = Random.Range(innerRadiusMin, innerRadiusMax) * halfH;
            float r1_Mid = Random.Range(midRadiusMin, midRadiusMax) * halfH;
            float r2_Mid = Random.Range(midRadiusMin, midRadiusMax) * halfH;

            Vector2 inner1 = impactCenter + dir1 * Mathf.Min(r1_Inner, Vector2.Distance(impactCenter, p1));
            Vector2 inner2 = impactCenter + dir2 * Mathf.Min(r2_Inner, Vector2.Distance(impactCenter, p2));
            Vector2 mid1 = impactCenter + dir1 * Mathf.Min(r1_Mid, Vector2.Distance(impactCenter, p1));
            Vector2 mid2 = impactCenter + dir2 * Mathf.Min(r2_Mid, Vector2.Distance(impactCenter, p2));

            CreateFragment(new Vector2[] { impactCenter, inner1, inner2 }, w, h, impactCenter);
            CreateFragment(new Vector2[] { inner1, mid1, mid2, inner2 }, w, h, impactCenter);
            CreateFragment(new Vector2[] { mid1, p1, p2, mid2 }, w, h, impactCenter);
        }
    }

    private IEnumerator ShakeScreen(Transform target, float zDist)
    {
        if (target == null) yield break;

        Vector3 originalPos = new Vector3(0, 0, zDist);
        float duration = 0.25f; // Shake for a quarter second
        float elapsed = 0f;
        float magnitude = 0.08f; // Shake intensity

        while (elapsed < duration)
        {
            if (target == null) yield break;
            
            float x = Random.Range(-1f, 1f) * magnitude;
            float y = Random.Range(-1f, 1f) * magnitude;

            target.localPosition = originalPos + new Vector3(x, y, 0);

            elapsed += Time.deltaTime;
            
            // Decay the shake magnitude quickly
            magnitude = Mathf.Lerp(magnitude, 0f, elapsed / duration);
            
            yield return null;
        }

        if (target != null)
        {
            target.localPosition = originalPos;
        }
    }

    private void CreateFragment(Vector2[] points, float w, float h, Vector2 impactCenter)
    {
        if (points.Length < 3) return;

        GameObject frag = new GameObject("GlassFragment");
        frag.layer = 4; // Water 层，专供 ShatterCamera 看
        frag.transform.SetParent(shatterRoot.transform, false);

        Mesh mesh = new Mesh();
        int len = points.Length;
        Vector3[] vertices = new Vector3[len * 2]; 
        Vector2[] uvs = new Vector2[len * 2];
        float thickness = 0.02f; 

        Vector2 center2D = Vector2.zero;

        for (int i = 0; i < len; i++)
        {
            vertices[i] = new Vector3(points[i].x, points[i].y, 0);
            vertices[i + len] = new Vector3(points[i].x, points[i].y, thickness);
            Vector2 uv = new Vector2((points[i].x / w) + 0.5f, (points[i].y / h) + 0.5f);
            uvs[i] = uv;
            uvs[i + len] = uv; 
            center2D += points[i];
        }
        center2D /= len;

        int[] triangles;
        if (len == 3)
            triangles = new int[] { 0, 1, 2, 3 + 0, 3 + 2, 3 + 1 };
        else 
            triangles = new int[] { 0, 1, 2, 0, 2, 3, 4 + 0, 4 + 2, 4 + 1, 4 + 0, 4 + 3, 4 + 2 };

        mesh.vertices = vertices;
        mesh.uv = uvs;
        mesh.triangles = triangles;
        mesh.RecalculateNormals(); 
        mesh.RecalculateBounds();

        MeshFilter mf = frag.AddComponent<MeshFilter>();
        mf.mesh = mesh;

        MeshRenderer mr = frag.AddComponent<MeshRenderer>();
        mr.material = glassMaterial;

        // Use BoxCollider instead of MeshCollider to avoid PhysX convex generation errors
        // and significantly improve simulation performance for the falling fragments.
        BoxCollider bc = frag.AddComponent<BoxCollider>();
        bc.center = mesh.bounds.center;
        bc.size = mesh.bounds.size;
        bc.enabled = false; // Disable initially to prevent blocking UI raycasts/clicks!
        shardColliders.Add(bc);

        Rigidbody rb = frag.AddComponent<Rigidbody>();
        rb.mass = mesh.bounds.size.magnitude; 
        rb.isKinematic = true; // 冻结在屏幕上！
        rb.interpolation = RigidbodyInterpolation.Interpolate;

        shardRigidbodies.Add(rb);

        // Animate the crack spreading outward
        Vector2 pushDir = (center2D - impactCenter).normalized;
        float distToImpact = Vector2.Distance(center2D, impactCenter);
        float maxDist = new Vector2(w, h).magnitude;
        StartCoroutine(AnimateCrackPropagation(frag.transform, rb, pushDir, crackSeparation, distToImpact, maxDist));
    }

    private IEnumerator AnimateCrackPropagation(Transform fragTransform, Rigidbody rb, Vector2 pushDir, float finalSeparation, float distToImpact, float maxDist)
    {
        // Wait for the Crack() method to be called
        while (!isCracked)
        {
            yield return null;
        }

        // Delay based on distance from impact center to simulate outward force wave
        float normalizedDist = Mathf.Clamp01(distToImpact / (maxDist * 0.6f));
        float delay = normalizedDist * 0.175f; // 200% faster (halved from 0.35f)
        
        if (delay > 0f)
            yield return new WaitForSeconds(delay);

        float duration = 0.075f; // 200% faster (halved from 0.15f)
        float t = 0f;
        Vector3 targetOffset = new Vector3(pushDir.x, pushDir.y, 0) * finalSeparation;

        while (t < duration)
        {
            if (fragTransform == null || rb == null || !rb.isKinematic) yield break; // Stop if physics takes over
            
            t += Time.deltaTime;
            float progress = Mathf.Clamp01(t / duration);
            float eased = 1f - Mathf.Pow(1f - progress, 3f); // Ease out cubic
            fragTransform.localPosition = targetOffset * eased;
            
            yield return null;
        }

        if (fragTransform != null && rb != null && rb.isKinematic)
        {
            fragTransform.localPosition = targetOffset;
        }
    }

    private Vector2 GetRectIntersection(Vector2 origin, Vector2 dir, float halfW, float halfH)
    {
        float tX = dir.x != 0 ? (dir.x > 0 ? (halfW - origin.x) / dir.x : (-halfW - origin.x) / dir.x) : float.MaxValue;
        float tY = dir.y != 0 ? (dir.y > 0 ? (halfH - origin.y) / dir.y : (-halfH - origin.y) / dir.y) : float.MaxValue;
        
        float t = Mathf.Min(Mathf.Abs(tX), Mathf.Abs(tY));
        return origin + dir * t;
    }
}
