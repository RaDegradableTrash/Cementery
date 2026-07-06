using UnityEngine;
using System.Collections.Generic;
using EnvironmentSystem;

[RequireComponent(typeof(ParticleSystem))]
public class SnowParticleSystem : MonoBehaviour
{
    [Header("Snow Settings")]
    public float particleSnowRadius = 1.5f;
    public float particleSnowAmount = 0.2f; // Increase accumulation rate so it passes the Cutoff!
    [Header("Performance")]
    public int maxCollisionEventsPerFrame = 24;
    public int skyVisibilityCheckStride = 4;
    public bool enableDynamicObjectSnow = false;

    private ParticleSystem partSystem;
    private List<ParticleCollisionEvent> collisionEvents;
    private int _collisionFrame = -1;
    private int _processedCollisionEventsThisFrame;
    private int _skyCheckCounter;
    private float _nextTargetSearchTime;
    private RVSystem.RVController _cachedRv;
    private Camera _cachedCamera;
    private readonly Dictionary<int, CollisionTargetInfo> _collisionTargetCache = new Dictionary<int, CollisionTargetInfo>(64);
    private const int MaxCollisionTargetCacheSize = 256;

    private struct CollisionTargetInfo
    {
        public bool isTerrain;
        public DynamicSnowObject dynamicSnowObject;
    }

    private void Awake()
    {
        // FORCE values to override potentially broken Inspector serialized values!
        particleSnowRadius = 1.2f;
        particleSnowAmount = 0.05f;

        partSystem = GetComponent<ParticleSystem>();
        collisionEvents = new List<ParticleCollisionEvent>(64);
    }

    private Transform _playerTransform;

    private void Start()
    {
        ConfigureParticleSystemProgrammatically();
        if (partSystem != null)
        {
            partSystem.Play();
        }
    }

    private void Update()
    {
        if (_playerTransform == null || !_playerTransform.gameObject.activeInHierarchy)
        {
            _playerTransform = FindPlayer();
        }

        if (_playerTransform != null)
        {
            // Position the particle system above the player so that it always falls around the player
            transform.position = new Vector3(_playerTransform.position.x, _playerTransform.position.y + 40f, _playerTransform.position.z);
        }

        if (partSystem != null && !partSystem.isPlaying)
        {
            partSystem.Play();
        }
    }

    private Transform FindPlayer()
    {
        if (Time.time < _nextTargetSearchTime)
            return null;

        _nextTargetSearchTime = Time.time + 0.75f;

        GameObject player = GameObject.FindGameObjectWithTag("Player");
        if (player != null && player.activeInHierarchy)
            return player.transform;

        if (_cachedRv == null || !_cachedRv.gameObject.activeInHierarchy)
            _cachedRv = FindObjectOfType<RVSystem.RVController>();

        if (_cachedRv != null && _cachedRv.gameObject.activeInHierarchy)
            return _cachedRv.transform;

        if (_cachedCamera == null || !_cachedCamera.gameObject.activeInHierarchy)
            _cachedCamera = Camera.main;

        return _cachedCamera != null ? _cachedCamera.transform : null;
    }

    private void ConfigureParticleSystemProgrammatically()
    {
        // 1. Transform setup: Place high in the sky and point downwards
        float mapSize = 100f;
        Vector3 mapCenter = Vector3.zero;
        if (SnowAccumulationManager.Instance != null)
        {
            mapSize = SnowAccumulationManager.Instance.mapWorldSize;
            mapCenter = SnowAccumulationManager.Instance.mapCenter;
        }
        
        transform.position = new Vector3(mapCenter.x, 50f, mapCenter.z); // Cloud height
        transform.rotation = Quaternion.Euler(90f, 0f, 0f); // Pointing straight down (Z down)

        // 2. Main Module setup
        var main = partSystem.main;
        main.loop = true;
        main.startLifetime = 150f; // Longer lifetime since it falls slower
        main.startSpeed = 0f;
        // 粒子保持2d，略微带点冰晶蓝
        main.startSize = new ParticleSystem.MinMaxCurve(0.04f, 0.16f);
        main.startColor = new Color(0.9f, 0.95f, 1.0f, 0.8f);
        // 下落速度降至当前的 30% (之前是0.5)
        main.gravityModifier = 0.15f;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.maxParticles = Application.platform == RuntimePlatform.WebGLPlayer ? 1000 : 2500;

        // 3. Emission Module setup
        var emission = partSystem.emission;
        emission.enabled = true;
        emission.rateOverTime = Application.platform == RuntimePlatform.WebGLPlayer ? 80f : 220f;

        // 4. Shape Module setup: 圆形范围 (Circle)
        var shape = partSystem.shape;
        shape.enabled = true;
        shape.shapeType = ParticleSystemShapeType.Circle;
        shape.radius = 60f; // 覆盖玩家周围 60 米的圆形区域，跟随玩家移动

        // 5. Collision Module setup (Performance Optimized)
        var collision = partSystem.collision;
        collision.enabled = true;
        collision.type = ParticleSystemCollisionType.World;
        collision.sendCollisionMessages = true;
        collision.bounce = 0f; 
        collision.dampen = 1f; 
        collision.lifetimeLoss = 1f; 
        collision.collidesWith = ~0; 
        collision.quality = ParticleSystemCollisionQuality.Low;
        collision.voxelSize = 0.4f;

        // 6. 生成并应用柔和的雪花材质
        var renderer = GetComponent<ParticleSystemRenderer>();
        if (renderer != null)
        {
            renderer.renderMode = ParticleSystemRenderMode.Billboard;
            
            Shader unlitShader = Shader.Find("Universal Render Pipeline/Particles/Unlit");
            if (unlitShader == null) unlitShader = Shader.Find("Universal Render Pipeline/Unlit");
            if (unlitShader == null) unlitShader = Shader.Find("Particles/Standard Unlit");
            if (unlitShader == null) unlitShader = Shader.Find("Mobile/Particles/Additive");
            
            if (unlitShader != null)
            {
                Material mat = new Material(unlitShader);
                if (unlitShader.name.Contains("Universal"))
                {
                    mat.SetFloat("_Surface", 1); // Transparent
                    mat.SetFloat("_Blend", 1);   // Additive
                    mat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
                    mat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.One);
                    mat.SetInt("_ZWrite", 0);
                    mat.DisableKeyword("_ALPHAPREMULTIPLY_ON");
                    mat.EnableKeyword("_ALPHABLEND_ON");
                    mat.renderQueue = 3000;
                }
                else
                {
                    mat.SetFloat("_Surface", 1);
                    mat.SetFloat("_Blend", 2);   // Additive blending makes them glow against the sky!
                    mat.SetFloat("_ZWrite", 0);
                }
                mat.SetColor("_BaseColor", new Color(2.0f, 2.0f, 2.0f, 0.8f)); // HDR bright white
                mat.SetColor("_Color", new Color(2.0f, 2.0f, 2.0f, 0.8f));
                
                // 程序化生成柔和的圆形贴图
                Texture2D circleTex = new Texture2D(32, 32, TextureFormat.RGBA32, false);
                circleTex.name = "SoftSnowflakeTex";
                Color[] pixels = new Color[32 * 32];
                Vector2 center = new Vector2(16, 16);
                for (int y = 0; y < 32; y++)
                {
                    for (int x = 0; x < 32; x++)
                    {
                        float dist = Vector2.Distance(new Vector2(x, y), center) / 16f;
                        float alpha = Mathf.Clamp01(1f - dist);
                        // 柔和边缘
                        alpha = alpha * alpha * (3f - 2f * alpha);
                        pixels[y * 32 + x] = new Color(1f, 1f, 1f, alpha);
                    }
                }
                circleTex.SetPixels(pixels);
                circleTex.Apply();
                
                mat.mainTexture = circleTex;
                renderer.sharedMaterial = mat;
            }
        }
    }

    private static GameObject snowBlobPrefab;
    private static Queue<GameObject> activeBlobs = new Queue<GameObject>();

    private void OnParticleCollision(GameObject other)
    {
        if (partSystem == null || other == null)
            return;

        if (_collisionFrame != Time.frameCount)
        {
            _collisionFrame = Time.frameCount;
            _processedCollisionEventsThisFrame = 0;
        }

        int remainingBudget = Mathf.Max(0, maxCollisionEventsPerFrame - _processedCollisionEventsThisFrame);
        if (remainingBudget <= 0)
            return;

        int numCollisionEvents = Mathf.Min(partSystem.GetCollisionEvents(other, collisionEvents), remainingBudget);
        _processedCollisionEventsThisFrame += numCollisionEvents;
        CollisionTargetInfo targetInfo = GetCollisionTargetInfo(other);
        bool isTerrain = targetInfo.isTerrain;

        for (int i = 0; i < numCollisionEvents; i++)
        {
            Vector3 pos = collisionEvents[i].intersection;
            Vector3 normal = collisionEvents[i].normal;

            // 1. 坡度合法性检验 (Slope Check)：
            // 【极其重要】：Medium 质量的碰撞使用了体素网格（Voxel Grid）。
            // 沙丘的斜坡在体素网格中表现为“阶梯状”。如果粒子正好打在阶梯的垂直侧面上，法线就是纯侧向的 (Dot=0)。
            // 如果我们在这里对地形进行法线坡度拦截，就会产生完全没有积雪的“Z字形/阶梯形小路”！
            // 由于地形的 Shader 中已经有严格的 upDot < 0.55 的 discard 渲染拦截，所以我们在这里完全放行地形的碰撞！
            if (!isTerrain && Vector3.Dot(normal, Vector3.up) < 0.7f)
            {
                continue;
            }

            // 2. 物理射线遮罩校验 (Sky Visibility Occlusion Check)：
            // 地形起伏大且有低模凹凸时，0.02m 的起点极易因射中自身相邻面或小碎石而导致误判定（自遮挡）。
            // 因此，如果是地形，我们将起点抬高至上方 0.5 米发射射线，过滤自遮挡干扰！
            float raycastOffset = isTerrain ? 0.5f : 0.02f;
            RaycastHit hit;
            int visibilityStride = Mathf.Max(1, skyVisibilityCheckStride);
            bool runSkyVisibilityCheck = visibilityStride <= 1 || (_skyCheckCounter++ % visibilityStride) == 0;
            if (runSkyVisibilityCheck &&
                Physics.Raycast(pos + Vector3.up * raycastOffset, Vector3.up, out hit, 30f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
            {
                // 如果是地形，且上方遮挡点非常近（可能射中了极陡峭的山崖上部），我们也忽略此遮挡
                if (isTerrain && hit.distance < 1.0f)
                {
                    // 忽略，视作地形自交
                }
                else
                {
                    continue;
                }
            }

            // 如果撞到的不是地形，就意味着撞到了车子、石头等动态或静态物体
            if (!isTerrain)
            {
                if (!enableDynamicObjectSnow)
                    continue;

                DynamicSnowObject dynamicObj = targetInfo.dynamicSnowObject;
                if (dynamicObj == null && other.transform.root != null)
                {
                    dynamicObj = other.transform.root.gameObject.AddComponent<DynamicSnowObject>();
                    targetInfo.dynamicSnowObject = dynamicObj;
                    _collisionTargetCache[other.GetInstanceID()] = targetInfo;
                }
                
                if (dynamicObj != null)
                {
                    // 局部物体（车身等）保持精细的小半径，防止一颗雪把全车刷白
                    dynamicObj.AddSnowLocal(pos, 0.4f, particleSnowAmount * 1.5f);
                }
                continue; // 撞到物体的雪花不会再穿透到地上！
            }

            // 2D Base Layer Support (对于地形)
            if (SnowAccumulationManager.Instance == null)
            {
                GameObject managerGO = new GameObject("[SYSTEM] SnowAccumulationManager");
                var manager = managerGO.AddComponent<SnowAccumulationManager>();
                manager.mapCenter = pos;
            }
            
            if (SnowAccumulationManager.Instance != null)
            {
                // 【核心修复】：为地形使用超大的柔和笔刷半径（3.5米）！
                // 这能保证落下的雪花能迅速且均匀地在地面晕染开来并连成一大片，彻底消灭“一块一块的斑秃”感！
                SnowAccumulationManager.Instance.AddSnowAtPoint(pos, 3.5f, particleSnowAmount * 0.6f);
            }
        }
    }

    private CollisionTargetInfo GetCollisionTargetInfo(GameObject other)
    {
        int key = other.GetInstanceID();
        if (_collisionTargetCache.TryGetValue(key, out CollisionTargetInfo info))
        {
            return info;
        }

        if (_collisionTargetCache.Count >= MaxCollisionTargetCacheSize)
        {
            _collisionTargetCache.Clear();
        }

        info = new CollisionTargetInfo
        {
            isTerrain = other.GetComponentInParent<DesertTerrainChunk>() != null || other.name.Contains("Terrain"),
            dynamicSnowObject = enableDynamicObjectSnow ? other.GetComponentInParent<DynamicSnowObject>() : null
        };

        _collisionTargetCache[key] = info;
        return info;
    }
}
