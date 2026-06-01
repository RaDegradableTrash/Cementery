using UnityEngine;
using System.Collections.Generic;
using EnvironmentSystem;

[RequireComponent(typeof(ParticleSystem))]
public class SnowParticleSystem : MonoBehaviour
{
    [Header("Snow Settings")]
    public float particleSnowRadius = 1.5f; 
    public float particleSnowAmount = 0.2f; // Increase accumulation rate so it passes the Cutoff!

    private ParticleSystem partSystem;
    private List<ParticleCollisionEvent> collisionEvents;

    private void Awake()
    {
        // FORCE values to override potentially broken Inspector serialized values!
        particleSnowRadius = 1.2f;
        particleSnowAmount = 0.05f;

        partSystem = GetComponent<ParticleSystem>();
        collisionEvents = new List<ParticleCollisionEvent>();
    }

    private void Start()
    {
        ConfigureParticleSystemProgrammatically();
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
        main.maxParticles = 5000; 

        // 3. Emission Module setup
        var emission = partSystem.emission;
        emission.enabled = true;
        emission.rateOverTime = 300f;  

        // 4. Shape Module setup: 圆形范围 (Circle)
        var shape = partSystem.shape;
        shape.enabled = true;
        shape.shapeType = ParticleSystemShapeType.Circle;
        shape.radius = mapSize / 1.5f; // 覆盖更广的圆形区域

        // 5. Collision Module setup (Performance Optimized)
        var collision = partSystem.collision;
        collision.enabled = true;
        collision.type = ParticleSystemCollisionType.World;
        collision.sendCollisionMessages = true;
        collision.bounce = 0f; 
        collision.dampen = 1f; 
        collision.lifetimeLoss = 1f; 
        collision.collidesWith = ~0; 
        collision.quality = ParticleSystemCollisionQuality.High; // High drops collisions on 130k poly terrains!
        collision.voxelSize = 0.2f; // Increase voxel resolution to 20cm to eliminate grid-snapping lines!

        // 6. 生成并应用柔和的雪花材质
        var renderer = GetComponent<ParticleSystemRenderer>();
        if (renderer != null)
        {
            renderer.renderMode = ParticleSystemRenderMode.Billboard;
            
            Shader unlitShader = Shader.Find("Mobile/Particles/Additive");
            if (unlitShader == null) unlitShader = Shader.Find("Mobile/Particles/Additive");
            
            if (unlitShader != null)
            {
                Material mat = new Material(unlitShader);
                mat.SetFloat("_Surface", 1); // Transparent
                mat.SetFloat("_Blend", 2);   // Additive blending makes them glow against the sky!
                mat.SetFloat("_ZWrite", 0);
                mat.SetColor("_BaseColor", new Color(2.0f, 2.0f, 2.0f, 0.8f)); // HDR bright white
                
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
        int numCollisionEvents = partSystem.GetCollisionEvents(other, collisionEvents);

        for (int i = 0; i < numCollisionEvents; i++)
        {
            Vector3 pos = collisionEvents[i].intersection;
            Vector3 normal = collisionEvents[i].normal;

            // 如果撞到的不是地形，就意味着撞到了车子、石头等动态或静态物体
            if (other.GetComponentInParent<DesertTerrainChunk>() == null && !other.name.Contains("Terrain"))
            {
                var dynamicObj = other.GetComponentInParent<DynamicSnowObject>();
                if (dynamicObj == null && other.transform.root != null)
                {
                    dynamicObj = other.transform.root.gameObject.AddComponent<DynamicSnowObject>();
                }
                
                if (dynamicObj != null)
                {
                    dynamicObj.AddSnowLocal(pos, particleSnowRadius, particleSnowAmount);
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
                SnowAccumulationManager.Instance.AddSnowAtPoint(pos, particleSnowRadius, particleSnowAmount);
            }
        }
    }
}
