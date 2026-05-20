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

                // Seamless tiling wind coordinates using direct inspector scales (使用面板直通的标准物理尺度)
                float3 uvwBase = pos * _BaseScale + _BaseWindSpeed.xyz * _Time.y;
                float3 uvwDetail = pos * _DetailScale + _DetailWindSpeed.xyz * _Time.y;

                // Sample the procedurally generated 3D Noise Textures
                float baseNoise = SAMPLE_TEXTURE3D(_BaseNoiseTex, sampler_LinearRepeat, uvwBase).r;
                float detailNoise = SAMPLE_TEXTURE3D(_DetailNoiseTex, sampler_LinearRepeat, uvwDetail).r;

                // 1. Convert Perlin noise into highly isolated clumps by raising the low smoothstep threshold
                float baseClumps = smoothstep(0.28, 0.48, baseNoise);

                // 2. High-quality Towering Cumulus vertical envelope: perfectly flat bottom, vertical billowy columns
                // Uses baseNoise to dynamically push the cloud ceiling higher in active zones! (纵向云塔高度位移)
                float towerFactor = baseNoise * 0.5 + 0.5;
                float topMask = saturate((towerFactor - heightFactor) / (towerFactor * 0.35 + 0.01));
                float bottomMask = saturate(heightFactor * 6.0);
                float cumulusHeightMask = bottomMask * topMask;
                
                float baseShape = baseClumps * cumulusHeightMask;

                // 3. Popcorn Stacking: Add round Worley bubble cells (detailNoise) to the macro shape 
                // to make them group and stack like overlapping puffy cloudlets (一小坨一小坨堆积)
                float bubblePuffs = detailNoise * 0.45;
                float stackedShape = baseShape * (0.6 + bubblePuffs);

                // 4. Smooth billowy carving: carve slightly on the outermost edges for painterly air flow
                float billowCarve = (1.0 - stackedShape) * detailNoise * _DetailInfluence * 0.22;
                float cloudDensity = stackedShape - billowCarve;
                
                // 5. Smooth density scaling with Threshold subtraction restored for inspector control
                cloudDensity = saturate((cloudDensity - _CloudThreshold) * _CloudDensityScale);

                return cloudDensity;
            }

            // Beer-Powder volumetric lighting model
            float LightEnergy(float densityToSun, float absorption)
            {
                // Beer's Law (absorption and shadowing)
                float beer = exp(-densityToSun * absorption);
                
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
                
                // Reduced Interleaved gradient noise dither offset for super smooth, grain-free volumes
                float jitter = InterleavedGradientNoise(uv * _ScreenParams.xy);
                
                float4 finalColor = float4(0, 0, 0, 0);
                Light mainLight = GetMainLight();
                
                [loop]
                for (int i = 0; i < maxSteps; i++)
                {
                    // Scale down the jitter weight to 25% to completely remove sand/powdery grain, maintaining a painterly blend
                    float t = tNear + (float(i) + jitter * 0.25) * stepSize;
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
                        
                        // Ambient sky dome light (softer baseline ambient, darker at bottom)
                        float3 ambientLight = _ShadowColor.rgb * lerp(0.3, 1.0, heightFactor);
                        
                        // Total incoming light at this voxel (环境光与直接光求和)
                        float3 voxelLighting = ambientLight + directLight;
                        
                        // Voxel Albedo: blend shadow and max light based on height (从底至顶的材质色渐变)
                        float3 voxelAlbedo = lerp(_ShadowColor.rgb, _MaxLightColor.rgb, heightFactor);
                        
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
