Shader "Hidden/Universal Render Pipeline/VolumetricCloud"
{
    Properties
    {
        _BaseNoiseTex ("Base Noise Tex (3D)", 3D) = "" {}
        _DetailNoiseTex ("Detail Noise Tex (3D)", 3D) = "" {}
        
        _CloudMinHeight ("Cloud Min Height", Float) = 1000
        _CloudMaxHeight ("Cloud Max Height", Float) = 2000
        
        _CloudDensityScale ("Cloud Density Scale", Float) = 1.0
        _CloudThreshold ("Cloud Threshold", Range(0, 1)) = 0.2
        
        _BaseScale ("Base Noise Scale", Float) = 0.0005
        _DetailScale ("Detail Noise Scale", Float) = 0.003
        _DetailInfluence ("Detail Influence", Range(0, 1)) = 0.3
        
        _Absorption ("Light Absorption", Float) = 2.0
        _ShadowColor ("Shadow Color", Color) = (0.2, 0.25, 0.35, 1)
        _MaxLightColor ("Max Light Color", Color) = (1.0, 0.95, 0.85, 1)
        
        _BaseWindSpeed ("Base Wind Speed", Vector) = (2.0, 0, 1.0, 0)
        _DetailWindSpeed ("Detail Wind Speed", Vector) = (1.0, 1.0, 1.0, 0)
        
        _StepCount ("Max Ray Steps", Float) = 16
        _LightStepDistance ("Shadow Sample Distance", Float) = 40.0
    }

    SubShader
    {
        Tags { "RenderType" = "Transparent" "RenderPipeline" = "UniversalPipeline" }
        ZTest Always ZWrite Off Cull Off
        Blend SrcAlpha OneMinusSrcAlpha

        Pass
        {
            Name "Volumetric Clouds"

            HLSLPROGRAM
            #pragma vertex FullscreenVert
            #pragma fragment Fragment

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            // 3D Textures
            TEXTURE3D(_BaseNoiseTex);
            TEXTURE3D(_DetailNoiseTex);

            CBUFFER_START(UnityPerMaterial)
                float _CloudMinHeight;
                float _CloudMaxHeight;
                float _CloudDensityScale;
                float _CloudThreshold;
                float _BaseScale;
                float _DetailScale;
                float _DetailInfluence;
                float _Absorption;
                float4 _ShadowColor;
                float4 _MaxLightColor;
                float4 _BaseWindSpeed;
                float4 _DetailWindSpeed;
                float _StepCount;
                float _LightStepDistance;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
            };

            Varyings FullscreenVert(Attributes input)
            {
                Varyings output;
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.uv = input.uv;
                return output;
            }

            // Pseudo-random jitter offset based on interleaved gradient noise
            float InterleavedGradientNoise(float2 uv)
            {
                float3 magic = float3(0.06711056, 0.00583715, 52.9829189);
                return frac(magic.z * frac(dot(uv, magic.xy)));
            }

            // Slab intersection for flat horizontal cloud layer
            bool IntersectCloudBox(float3 rayOrigin, float3 rayDir, float sceneDist, out float tNear, out float tFar)
            {
                tNear = 0.0;
                tFar = 0.0;
                
                float minH = _CloudMinHeight;
                float maxH = _CloudMaxHeight;
                
                if (abs(rayDir.y) < 0.0001)
                {
                    // Ray is strictly horizontal
                    if (rayOrigin.y >= minH && rayOrigin.y <= maxH)
                    {
                        tNear = 0.0;
                        tFar = sceneDist;
                        return true;
                    }
                    return false;
                }
                
                float t1 = (minH - rayOrigin.y) / rayDir.y;
                float t2 = (maxH - rayOrigin.y) / rayDir.y;
                
                float t_entry = min(t1, t2);
                float t_exit = max(t1, t2);
                
                if (t_exit < 0.0) return false; // Cloud layer is behind the camera
                
                tNear = max(0.0, t_entry);
                tFar = min(sceneDist, t_exit);
                
                return tNear < tFar;
            }

            // Sample cloud density at a given world position
            float SampleCloudDensity(float3 pos)
            {
                // Height restriction mask to keep the clouds in a neat boundary
                float heightFactor = (pos.y - _CloudMinHeight) / (_CloudMaxHeight - _CloudMinHeight);
                if (heightFactor < 0.0 || heightFactor > 1.0) return 0.0;

                 // --- ADVANCED PROCEDURAL SPACE WARPING FIELD (动力学空间扭曲场) ---
                 // Computes tiling-aligned convection vectors to sculpt flat blobs into towering cumulative pyramids!
                 float3 uvwBaseRaw = pos * _BaseScale + _BaseWindSpeed.xyz * _Time.y;
                 
                 // Sample the raw base Perlin noise first (unwarped) to isolate the extremely active wind cores.
                 float baseNoiseRaw = SAMPLE_TEXTURE3D(_BaseNoiseTex, sampler_LinearRepeat, uvwBaseRaw).r;
                 
                 // Non-linear Convective Core Isolation: pow(x, 3.2) limits vertical ballooning and roll-convection
                 // to only the most active 5% of cloud centers! Rest of the clouds remain naturally flat & low.
                 float activeTower = pow(baseNoiseRaw, 3.2);
                 
                 // 1. Procedural Radial Factor & Outward Direction based on periodic cell tiling
                 // Aligns perfectly with the [0, 1] base noise repeating frequency
                 float radialFactor = saturate(sin(uvwBaseRaw.x * 3.1415926) * sin(uvwBaseRaw.z * 3.1415926));
                 float2 outwardDir = float2(cos(uvwBaseRaw.x * 3.1415926), cos(uvwBaseRaw.z * 3.1415926));
                 
                 // 2. Calculate spatial distortion offset in world-space meters (在世界空间下进行物理协同形变)
                 float3 warpOffset = float3(0, 0, 0);
                 
                 // - Vertical Sink (方案 1): pull coordinates up at the bottom (which physically sinks/flattens the cloud base)
                 float verticalSink = 130.0 * (1.0 - heightFactor * heightFactor);
                 warpOffset.y += verticalSink;
                 
                 // - Convection Rolling (方案 3): center goes up (shift pos.y down), edges go down (shift pos.y up). Scaled by activeTower!
                 float rollWarpY = 110.0 * (radialFactor * 2.0 - 1.0) * sin(heightFactor * 3.1415926) * activeTower;
                 warpOffset.y -= rollWarpY;
                 
                 // - Outward Expansion (金字塔尖外扩): push coordinates outward at the tops to form beautiful billowy crowns. Scaled by activeTower!
                 warpOffset.xz += outwardDir * 90.0 * (1.0 - radialFactor) * heightFactor * activeTower;
                 
                 // Apply warping field to get distorted sampling world positions
                 float3 warpedPos = pos + warpOffset;
                 
                 // Seamless tiling wind coordinates using warped positions (完美协同形变)
                 float3 uvwBase = warpedPos * _BaseScale + _BaseWindSpeed.xyz * _Time.y;
                 float3 uvwDetail = warpedPos * _DetailScale + _DetailWindSpeed.xyz * _Time.y;
                 // -----------------------------------------------------------------

                 // Sample the procedurally generated 3D Noise Textures
                 float baseNoise = SAMPLE_TEXTURE3D(_BaseNoiseTex, sampler_LinearRepeat, uvwBase).r;
                 float detailNoise = SAMPLE_TEXTURE3D(_DetailNoiseTex, sampler_LinearRepeat, uvwDetail).r;

                // Cubic Hermite Smoothing: round off the sharp cell creases, completely erasing dark outlines.
                float smoothDetail = smoothstep(0.0, 1.0, detailNoise);

                // 1. Isolate Cloud Towers (孤立云塔): raise the Perlin noise threshold to filter out chaotic wispy sheets
                float baseClumps = smoothstep(0.25, 0.62, baseNoise);

                 // 2D Island Coverage Mask (2D 岛屿疏密宏观滤镜): group clouds into horizontal islands (晴空与云岛)
                 float3 uvwCoverage = pos * (_BaseScale * 0.22) + _BaseWindSpeed.xyz * _Time.y * 0.15;
                 uvwCoverage.y = 0.0; // Strictly 2D XZ plane projection to eliminate vertical stacking
                 float coverageRaw = SAMPLE_TEXTURE3D(_BaseNoiseTex, sampler_LinearRepeat, uvwCoverage).r;
                 float coverageMask = smoothstep(0.32, 0.58, coverageRaw);
                 baseClumps *= coverageMask;

                // 2. High-quality Towering Cumulus vertical envelope: perfectly flat bottom, vertical billowy columns
                // The cloud ceiling (towerFactor) is kept flat and low (e.g. 0.35) for most clouds, 
                // and only shoots up very high (e.g. 0.85) in active storm towers! (非线性高度激活)
                float towerFactor = 0.32 + activeTower * 0.65;
                float topMask = saturate((towerFactor - heightFactor) / (towerFactor * 0.35 + 0.01));
                float bottomMask = saturate(heightFactor * 6.0);
                float cumulusHeightMask = bottomMask * topMask;
                
                float baseShape = baseClumps * cumulusHeightMask;

                // 3. Popcorn Stacking (椰菜花气泡堆叠): use a low baseline and high multiplier to exaggerate
                // round, bulging bubble domes (隆起感) and cut deep crevices for dramatic volumetric self-shadowing!
                // 3. Subtractive Edge Carving (经典边缘侵蚀): erodes cloud borders into wispy cells, keeping core solid.
                // This completely erases the hard "plastic/clay shell" look, creating 100% soft, breathing volumes!
                float edgeCarve = (1.0 - smoothDetail) * (1.0 - baseShape) * _DetailInfluence * 0.48;
                float carvedShape = saturate(baseShape - edgeCarve);

                // 4. Convective bubble puff addition: soft, round bubble domes towards the top of the active towers (云顶椰菜花泡泡)
                float bubblePuffs = smoothDetail * 0.32 * heightFactor * activeTower;
                float cloudDensity = carvedShape + bubblePuffs * baseShape;
                
                // 5. Smooth density scaling with Threshold subtraction restored for inspector control
                cloudDensity = saturate((cloudDensity - _CloudThreshold) * _CloudDensityScale);

                return cloudDensity;
            }

            // Beer-Powder volumetric lighting model
            float LightEnergy(float densityToSun, float absorption)
            {
                // Beer's Law Soft Normalization (防黑超载柔和归一化)
                float beer = exp(-densityToSun * absorption * 0.15);
                
                // Powder Effect (for multi-scattering bright border glows)
                float powder = 1.0 - exp(-densityToSun * 2.0);
                
                return beer * powder;
            }

            half4 Fragment(Varyings input) : SV_Target
            {
                float2 uv = input.uv;
                
                // Sample Scene Depth
                float depth = SampleSceneDepth(uv);
                
                // Generous skybox threshold to immunize against MSAA and depth precision filters
                bool isSkybox = false;
                #if UNITY_REVERSED_Z
                    if (depth < 0.0005) isSkybox = true;
                #else
                    if (depth > 0.9995) isSkybox = true;
                #endif

                // Reconstruct World Space camera ray
                float3 sceneWorldPos = ComputeWorldSpacePosition(uv, depth, UNITY_MATRIX_I_VP);
                float3 rayOrigin = _WorldSpaceCameraPos;
                float3 rayDir = normalize(sceneWorldPos - rayOrigin);
                
                // Cutoff raymarching at scene geometry
                float sceneDist = isSkybox ? 1.0e6 : length(sceneWorldPos - rayOrigin);

                // Intersect ray with the cloud boundaries
                float tNear, tFar;
                if (!IntersectCloudBox(rayOrigin, rayDir, sceneDist, tNear, tFar))
                {
                    return half4(0, 0, 0, 0); // No clouds intersected or occluded by scene
                }

                // Setup raymarching parameters
                int maxSteps = min((int)_StepCount, 64);
                float stepSize = (tFar - tNear) / (float)maxSteps;
                
                // Use screen-space pixel position (SV_POSITION) to eliminate floating-point moire diagonal banding lines!
                float jitter = InterleavedGradientNoise(input.positionCS.xy);
                
                float4 finalColor = float4(0, 0, 0, 0);
                Light mainLight = GetMainLight();
                
                // Apply a tiny randomized step size jitter (+/- 6%) per pixel to completely smash the grid aliasing shells,
                // making 3D texture undersampling Moire patterns 100% mathematically impossible!
                float pixelStepSize = stepSize * (1.0 + (jitter - 0.5) * 0.12);
                
                [loop]
                for (int i = 0; i < maxSteps; i++)
                {
                    // Calculate ray position using randomized pixel step size and starting jitter offset
                    float t = tNear + (float(i) + jitter * 0.95) * pixelStepSize;
                    float3 currentPos = rayOrigin + rayDir * t;
                    
                    float density = SampleCloudDensity(currentPos);
                    
                    // Smooth distance-based fade (渐隐衰减) to dissolve clouds before they tile weirdly at the horizon
                    // Clouds smoothly fade to 0 between 3200m and 5000m from the camera
                    float distanceFade = saturate((5000.0 - t) / 1800.0);
                    density *= distanceFade;
                    
                    if (density > 0.001)
                    {
                        // Cheap One-Step Solar shadow sampling along sun direction
                        float3 shadowSamplePos = currentPos + mainLight.direction * _LightStepDistance;
                        float densityToSun = SampleCloudDensity(shadowSamplePos) * distanceFade;
                        
                        // Calculate light absorption multiplier (太阳光自阴影衰减)
                        float lightMultiplier = LightEnergy(densityToSun, _Absorption);
                        
                        // Forward-scattering phase glow (Silver Lining) for dynamic rim lighting
                        float cosAngle = dot(rayDir, mainLight.direction);
                        float phaseGlow = lerp(0.8, 1.8, saturate(cosAngle * 0.5 + 0.5));
                        
                        // Direct Light term: directly coupled to mainLight color, scaled by self-shadowing and phase glow
                        float3 directLight = mainLight.color * lightMultiplier * phaseGlow * 1.3;
                        
                        // Height factor for cloud vertical profile
                        float heightFactor = (currentPos.y - _CloudMinHeight) / (_CloudMaxHeight - _CloudMinHeight);
                        
                        // Dynamic Ambient Sky color sampled directly from Unity's dynamic ambient probe (SH)
                        // Safeguarded with a daylight baseline so unbaked environments never turn clouds black!
                        float3 dynamicAmbient = max(SampleSH(float3(0.1, 1.0, 0.1)), mainLight.color * 0.22);
                        
                        // Multiple-scattering indirect light transmission (透光漫射): allows sunlight to bleed softly into backlit zones
                        float3 backlitGlow = mainLight.color * exp(-density * 0.2) * 0.28;
                        
                        // Ambient sky dome light (boosted baseline to 58% to prevent harsh dark shadows, softened with backlit glow)
                        float3 ambientLight = _ShadowColor.rgb * (dynamicAmbient + backlitGlow) * lerp(0.58, 1.0, heightFactor);
                        
                        // Total incoming light at this voxel (环境光与直接光求和)
                        float3 voxelLighting = ambientLight + directLight;
                        
                        // Real-world Forward Scattering Edge Glow (边缘前向散射亮化):
                        // Thin, wispy boundary layers with low density scatter light intensely, making them look white/bright.
                        // We blend the albedo towards _MaxLightColor at low densities to erase dirty black edges completely!
                        float edgeGlowFactor = saturate(1.0 - density * 3.2); // 1.0 at thin edges, 0.0 inside thick core
                        float3 voxelAlbedo = lerp(_ShadowColor.rgb, _MaxLightColor.rgb, max(heightFactor, edgeGlowFactor));
                        
                        // Final cloud color is the product of albedo and lighting (乘性着色，完美保留体积自阴影与明暗梯度)
                        float3 cloudColor = voxelAlbedo * voxelLighting;
                        
                        // Standard front-to-back alpha blending
                        float alpha = density * stepSize * 0.05;
                        finalColor.rgb += (1.0 - finalColor.a) * cloudColor * alpha;
                        finalColor.a += (1.0 - finalColor.a) * alpha;
                        
                        // Early exit if cloud has reached high opacity
                        if (finalColor.a >= 0.95)
                        {
                            finalColor.a = 1.0;
                            break;
                        }
                    }
                }
                
                return finalColor;
            }
            ENDHLSL
        }
    }
}
