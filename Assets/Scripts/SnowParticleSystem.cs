using UnityEngine;
using System.Collections.Generic;
using EnvironmentSystem;

[RequireComponent(typeof(ParticleSystem))]
public class SnowParticleSystem : MonoBehaviour
{
    [Header("Snow Settings")]
    public float particleSnowRadius = 2.5f; // 精细涂抹
    public float particleSnowAmount = 0.005f; // 极缓堆积，依靠高帧率实现平滑

    private ParticleSystem partSystem;
    private List<ParticleCollisionEvent> collisionEvents;

    private void Awake()
    {
        // FORCE values to override potentially broken Inspector serialized values!
        particleSnowRadius = 2.5f;
        particleSnowAmount = 0.005f;

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
        // 雪花更密集，提升到现在的250% (原100 -> 250)
        emission.rateOverTime = 250f;  

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
        collision.quality = ParticleSystemCollisionQuality.Medium; 

        // 6. 恢复2D贴图模式并接受光照
        var renderer = GetComponent<ParticleSystemRenderer>();
        if (renderer != null)
        {
            // 恢复为 2D 粒子模式 (Billboard)
            renderer.renderMode = ParticleSystemRenderMode.Billboard;

            // 使用受光影影响的 Lit 着色器，而不是 Unlit 发光
            Shader urpLitParticleShader = Shader.Find("Universal Render Pipeline/Particles/Lit");
            if (urpLitParticleShader == null) urpLitParticleShader = Shader.Find("Particles/Standard Surface");
            
            if (urpLitParticleShader != null)
            {
                Material litMat = new Material(urpLitParticleShader);
                // 开启 GPU 实例化优化性能
                litMat.enableInstancing = true; 
                
                // 设置为 Additive 透光模式并开启发光，消除灰尘感
                litMat.SetFloat("_Surface", 1); // Transparent
                litMat.SetFloat("_Blend", 2); // Additive
                litMat.SetFloat("_ZWrite", 0);
                litMat.SetColor("_BaseColor", new Color(0.9f, 0.95f, 1.0f, 0.5f));
                litMat.SetColor("_EmissionColor", new Color(0.2f, 0.4f, 0.8f) * 0.5f);
                litMat.EnableKeyword("_EMISSION");
                litMat.SetFloat("_Smoothness", 0.9f);
                
                renderer.sharedMaterial = litMat;
            }
        }
    }

    private void OnParticleCollision(GameObject other)
    {
        // 1. 如果雪花撞到的不是沙地（例如撞到了车顶），就不在高度图上累加积雪！
        // 这样车子底下就能完美呈现出“没下雪”的干净区域！
        if (other.GetComponent<DesertTerrainChunk>() == null && other.layer != LayerMask.NameToLayer("Default"))
        {
            return; 
        }

        int numCollisionEvents = partSystem.GetCollisionEvents(other, collisionEvents);

        for (int i = 0; i < numCollisionEvents; i++)
        {
            Vector3 pos = collisionEvents[i].intersection;

            // 2D Base Layer Support
            if (SnowAccumulationManager.Instance == null)
            {
                GameObject managerGO = new GameObject("[SYSTEM] SnowAccumulationManager");
                var manager = managerGO.AddComponent<SnowAccumulationManager>();
                manager.mapCenter = pos; // Align the 100x100 area exactly to where the snow is falling!
            }
            
            if (SnowAccumulationManager.Instance != null)
            {
                SnowAccumulationManager.Instance.AddSnowAtPoint(pos, particleSnowRadius, particleSnowAmount);
            }
        }
    }
}
