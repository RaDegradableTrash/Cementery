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
        _VerticalStretch ("Vertical Stretch", Float) = 3.5
        _ConvectiveWarp ("Convective Warp", Range(0, 2)) = 0.8
        _VerticalRandomness ("Vertical Randomness", Range(0, 1)) = 0.5
        _Puffiness ("Puffiness", Range(0, 1)) = 0.6
        _CloudBaseFlatness ("Cloud Base Flatness", Range(0, 1)) = 0.8
        _EdgeSoftness ("Edge Softness", Range(0.001, 0.5)) = 0.02
        _BacklitGlow ("Backlit Glow", Range(0, 2)) = 0.5
        
        _Absorption ("Light Absorption", Float) = 2.0
        _ShadowColor ("Shadow Color", Color) = (0.2, 0.25, 0.35, 1)
        _MaxLightColor ("Max Light Color", Color) = (1.0, 0.95, 0.85, 1)
        
        _BaseWindSpeed ("Base Wind Speed", Vector) = (2.0, 0, 1.0, 0)
        _DetailWindSpeed ("Detail Wind Speed", Vector) = (1.0, 1.0, 1.0, 0)
        
        _StepCount ("Max Ray Steps", Float) = 16
        _JitterStrength ("Dither Jitter Strength", Range(0, 1)) = 0.2
        _LightStepDistance ("Shadow Sample Distance", Float) = 40.0
        
        _MaxRenderDist ("Max Render Distance", Float) = 4000.0
        _FarDist ("Far Distance Optimization", Float) = 4000.0
        _FarSteps ("Far Step Count", Float) = 4.0
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

            // Added shadow keywords to receive geometry shadows
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE
            #pragma multi_compile _ _SHADOWS_SOFT

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
                float _VerticalStretch;
                float _ConvectiveWarp;
                float _VerticalRandomness;
                float _Puffiness;
                float _CloudBaseFlatness;
                float _EdgeSoftness;
                float _BacklitGlow;
                float _Absorption;
                float4 _ShadowColor;
                float4 _MaxLightColor;
                float4 _BaseWindSpeed;
                float4 _DetailWindSpeed;
                float _StepCount;
                float _JitterStrength;
                float _LightStepDistance;
                float _MaxRenderDist;
                float _FarDist;
                float _FarSteps;
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
                // Apply vertical noise stretch to base and detail scales
                float3 baseScaleVec = float3(_BaseScale, _BaseScale / _VerticalStretch, _BaseScale);
                float3 detailScaleVec = float3(_DetailScale, _DetailScale / _VerticalStretch, _DetailScale);

                // --- 1. PROCEDURAL VERTICAL RANDOMNESS (纵向随机弯曲与起伏) ---
                // We sample a low-frequency noise based on horizontal coordinates to shift the vertical space.
                // This is 100% continuous and creates organic, wavy shapes without any block seams!
                float2 uvwVertShift = pos.xz * (_BaseScale * 0.4) + _BaseWindSpeed.xz * _Time.y * 0.15;
                float vertShiftNoise = SAMPLE_TEXTURE3D(_BaseNoiseTex, sampler_LinearRepeat, float3(uvwVertShift.x, 0.5, uvwVertShift.y)).r;
                
                // Add vertical shift to pos.y (distorts height up and down by up to 500 meters)
                float3 warpedPos = pos;
                warpedPos.y += (vertShiftNoise - 0.5) * 500.0 * _VerticalRandomness;

                // --- 2. MULTI-SCALE DYNAMIC HEIGHT ENVELOPE (宏观高度遮罩) ---
                // Sample unwarped base noise to determine the height range of the local cloud group
                float3 uvwBaseRaw = pos * baseScaleVec + _BaseWindSpeed.xyz * _Time.y;
                float baseNoiseRaw = SAMPLE_TEXTURE3D(_BaseNoiseTex, sampler_LinearRepeat, uvwBaseRaw).r;

                // Convective core isolation: raise to power of 2.0 to locate strong updraft cores
                float activeUpdraft = pow(baseNoiseRaw, 2.0);
                
                // Dynamic cloud base and top offsets to give each cloud tower a unique height/volume
                float localMinH = _CloudMinHeight;
                float localMaxH = _CloudMaxHeight - (1.0 - activeUpdraft) * (_CloudMaxHeight - _CloudMinHeight) * 0.6 * _VerticalRandomness;

                float heightFactor = (warpedPos.y - localMinH) / (localMaxH - localMinH + 0.01);
                if (heightFactor < 0.0 || heightFactor > 1.0) return 0.0;

                // Envelope shapes: sharp flat bottom deck, beautifully curved top dome
                float bottomMask = saturate(heightFactor * lerp(2.5, 12.0, _CloudBaseFlatness));
                float topMask = saturate((1.0 - heightFactor) * 2.2);
                float verticalEnvelope = bottomMask * topMask;

                // --- 3. 100% CONTINUOUS DOMAIN WARPING (中频空间扰动) ---
                // Sample 3D noise continuously to construct a smooth warp vector.
                // This is fully continuous and completely immune to grid coordinate wrapping seams!
                float3 uvwWarp = warpedPos * (baseScaleVec * 1.5) + _BaseWindSpeed.xyz * _Time.y * 0.4;
                float warpX = SAMPLE_TEXTURE3D(_BaseNoiseTex, sampler_LinearRepeat, uvwWarp + float3(0.1, 0.2, 0.3)).r;
                float warpY = SAMPLE_TEXTURE3D(_BaseNoiseTex, sampler_LinearRepeat, uvwWarp + float3(0.4, 0.5, 0.6)).r;
                float warpZ = SAMPLE_TEXTURE3D(_BaseNoiseTex, sampler_LinearRepeat, uvwWarp + float3(0.7, 0.8, 0.9)).r;
                
                // Secondary smooth warping (distorts coordinates smoothly by up to 350 meters)
                float3 secondaryWarp = float3(warpX - 0.5, warpY - 0.5, warpZ - 0.5) * 350.0;
                warpedPos += secondaryWarp;

                // --- 4. CELLULAR BASE SHAPE (基础 Worley 形状) ---
                float3 uvwBase = warpedPos * baseScaleVec + _BaseWindSpeed.xyz * _Time.y;
                float baseNoise = SAMPLE_TEXTURE3D(_BaseNoiseTex, sampler_LinearRepeat, uvwBase).r;
                
                // --- 5. CONVECTIVE MUSHROOM SPREADING via DYNAMIC THRESHOLD (横向无缝蔓生) ---
                // Instead of discontinuous coordinate offsets, we use a continuous vertical gradient 
                // to shrink the cloud base and expand the cloud top horizontally!
                // This spreads the cloud top outwards by up to 45% of the cell radius completely seamlessly!
                float baseSpread = (1.0 - heightFactor) * _ConvectiveWarp * 0.36;

                // --- 6. LOW-FREQUENCY ISLAND COVERAGE (宏观云岛分布) ---
                float3 uvwCoverage = warpedPos * (baseScaleVec * 0.2) + _BaseWindSpeed.xyz * _Time.y * 0.1;
                uvwCoverage.y = 0.0;
                float coverage = SAMPLE_TEXTURE3D(_BaseNoiseTex, sampler_LinearRepeat, uvwCoverage).r;
                
                // --- 7. DYNAMIC RADIUS THRESHOLDING (云块体积与大小控制)
                // Make the coverage noise higher contrast to create larger empty gaps
                float coverageMask = smoothstep(0.32, 0.58, coverage);
                
                float distToCam = length(pos - _WorldSpaceCameraPos);
                float distRatio = saturate(distToCam / _MaxRenderDist);
                // 距离大片云（云岛）越远，(1.0 - coverageMask) 的惩罚越重（从 0.35 提至 1.5），
                // 彻底抹除岛屿外围的细碎小云。同时随距离 distRatio 增加基础抹除率。
                float localThreshold = _CloudThreshold * 0.5 + baseSpread + (1.0 - coverageMask) * 1.5 + distRatio * 0.25;
                
                // Apply vertical profile envelope directly to shape
                float baseShape = baseNoise * verticalEnvelope;
                float cloudVal = baseShape - localThreshold;

                if (cloudVal <= 0.0) return 0.0;

                // --- 8. BULGING CAULIFLOWER PUFFS & DETAIL CARVING (椰菜花无缝堆叠) ---
                float3 uvwDetail = warpedPos * detailScaleVec + _DetailWindSpeed.xyz * _Time.y;
                float detailNoise = SAMPLE_TEXTURE3D(_DetailNoiseTex, sampler_LinearRepeat, uvwDetail).r;
                
                // Warp the boundary of the cloud slightly outward in detailed spherical patterns (convective bubbling)
                // Kept very gentle to make the clouds look light, clean, and通透 rather than sticky/hairy!
                // 近实远虚：远处细节噪声衰减，减少噪点和破碎感，保留大块平滑的体积
                float detailFade = saturate(1.0 - distRatio * 1.5);
                // --- 8. INTENSE CAULIFLOWER CARVING & POPCORN STRUCTURE ---
                // Increase erosion multiplier to carve deep, structured canyons into the smooth ellipse
                float erosionModifier = lerp(0.2, 1.2, heightFactor); // Carve more at the fluffy tops, less at the flat bottoms
                float edgeCarving = (1.0 - detailNoise) * (_DetailInfluence * detailFade) * erosionModifier * 0.7;
                
                // Subtract carving to create distinct structured clumps
                float carvedShape = cloudVal - edgeCarving;
                
                // Add popcorn bulging to make the clumps spherical and volumetric
                float boundaryWarp = detailNoise * (_Puffiness * detailFade) * 0.4 * saturate(carvedShape * 2.0);
                
                float finalShape = carvedShape + boundaryWarp;

                // --- 9. SOLID CORE NORMALIZATION (动漫风格坚实边缘切片) ---
                // Dividing by max(_EdgeSoftness, 0.001) scales the shape gradient, creating beautifully crisp,
                // sharp anime-style boundaries that completely erase fuzzy/powdery pixels!
                float normalizedDensity = saturate(finalShape / max(_EdgeSoftness, 0.001));
                float finalDensity = saturate(normalizedDensity * _CloudDensityScale);

                return finalDensity;
            }

            // Translucent backlit-friendly volumetric lighting model (无灰黑边缘)
            float LightEnergy(float densityToSun, float absorption)
            {
                // Beer's law attenuation along the sun direction
                float beer = exp(-densityToSun * absorption);
                
                // Translucent backlit glow transmission through boundaries
                float backlit = exp(-densityToSun * 0.15) * _BacklitGlow;
                
                return max(beer, backlit);
            }

            
            

            
            // --- ROBUST 3D-PARALLAX CLOUD SHADOW & TYNDALL MASK ---
            float SampleShadowMask(float3 worldPos, float3 sunDir)
            {
                if (sunDir.y <= 0.05) return 0.0;
                
                // To guarantee 1-to-1 correspondence with the clouds, we take 3 samples 
                // along the sun's ray slicing through the volume. This flawlessly captures 
                // the 3D volume shape, convective warp, and prevents low-angle sun parallax mismatches!
                
                float diff = _CloudMaxHeight - _CloudMinHeight;
                float h1 = _CloudMinHeight + diff * 0.2;
                float h2 = _CloudMinHeight + diff * 0.5;
                float h3 = _CloudMinHeight + diff * 0.8;
                
                float3 cp1 = worldPos + sunDir * ((h1 - worldPos.y) / sunDir.y);
                float3 cp2 = worldPos + sunDir * ((h2 - worldPos.y) / sunDir.y);
                float3 cp3 = worldPos + sunDir * ((h3 - worldPos.y) / sunDir.y);
                
                float3 baseScaleVec = float3(_BaseScale, _BaseScale / _VerticalStretch, _BaseScale);
                
                // Sample lower, mid, and upper slices with accurate ConvectiveWarp offsets
                float3 uvw1 = cp1 * baseScaleVec + _BaseWindSpeed.xyz * _Time.y - float3(0, _ConvectiveWarp * 0.2, 0);
                float n1 = SAMPLE_TEXTURE3D_LOD(_BaseNoiseTex, sampler_LinearRepeat, uvw1, 0).r;
                
                float3 uvw2 = cp2 * baseScaleVec + _BaseWindSpeed.xyz * _Time.y - float3(0, _ConvectiveWarp * 0.5, 0);
                float n2 = SAMPLE_TEXTURE3D_LOD(_BaseNoiseTex, sampler_LinearRepeat, uvw2, 0).r;
                
                float3 uvw3 = cp3 * baseScaleVec + _BaseWindSpeed.xyz * _Time.y - float3(0, _ConvectiveWarp * 0.8, 0);
                float n3 = SAMPLE_TEXTURE3D_LOD(_BaseNoiseTex, sampler_LinearRepeat, uvw3, 0).r;
                
                // Max blend ensures we catch the thickest part of the cloud along the ray
                float baseNoise = max(n1, max(n2, n3));
                
                float3 uvwCoverage = cp2 * (baseScaleVec * 0.2) + _BaseWindSpeed.xyz * _Time.y * 0.1;
                uvwCoverage.y = 0.0;
                float coverage = SAMPLE_TEXTURE3D_LOD(_BaseNoiseTex, sampler_LinearRepeat, uvwCoverage, 0).r;
                float coverageMask = smoothstep(0.32, 0.58, coverage);
                
                float threshold = _CloudThreshold * 0.5 + (1.0 - coverageMask) * 1.5;
                float density = saturate(baseNoise - threshold);
                
                return saturate(pow(density * 2.5, 1.5));
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
                bool hitClouds = IntersectCloudBox(rayOrigin, rayDir, sceneDist, tNear, tFar);
                
                float4 finalColor = float4(0, 0, 0, 0);
                Light mainLight = GetMainLight();
                float jitter = InterleavedGradientNoise(input.positionCS.xy);
                
                // === REALISTIC GROUND SHADOWS AND TYNDALL RAYS ===
                
                // 1. Realistic Ground Cloud Shadows
                if (!isSkybox && sceneDist < 20000.0)
                {
                    float shadow = SampleShadowMask(sceneWorldPos, mainLight.direction);
                    
                    if (shadow > 0.01) 
                    {
                        // 使用更大的系数提高阴影的最暗处的深度（增强反差），同时因为 shadow 变量现在是渐变的，
                        // 所以阴影边缘会自然过渡，同一片云也会呈现出深浅不一的质感。
                        finalColor.rgb = float3(0.0, 0.0, 0.0);
                        finalColor.a = shadow * 0.51; // 降低到原来的 60% (0.85 * 0.6 = 0.51)，不再黑得过头
                    }
                }
                
                // 2. Volumetric Tyndall God Rays
                float maxTyndallDist = isSkybox ? (hitClouds ? min(tNear, 3000.0) : 3000.0) : min(sceneDist, 3000.0);
                
                if (maxTyndallDist > 20.0)
                {
                    int tyndallSteps = 16; // 高精度步数
                    float stepSize = maxTyndallDist / (float)tyndallSteps;
                    float accumulatedLight = 0.0;
                    
                    float t = jitter * stepSize;
                    
                    [loop]
                    for (int j = 0; j < tyndallSteps; j++)
                    {
                        float3 pos = rayOrigin + rayDir * t;
                        float shadowAtPos = SampleShadowMask(pos, mainLight.direction);
                        
                        // 1.0 means full sunlight, 0.0 means shadowed by clouds
                        float illumination = pow(1.0 - shadowAtPos, 2.0);
                        
                        // Add light based on step distance (Riemann sum)
                        accumulatedLight += illumination * stepSize * 0.0005;
                        
                        t += stepSize;
                    }
                    
                    if (accumulatedLight > 0.01)
                    {
                        // Phase function gives sun glare
                        float phase = lerp(0.5, 3.0, pow(saturate(dot(rayDir, mainLight.direction) * 0.5 + 0.5), 3.0));
                        float3 godRayColor = _MaxLightColor.rgb * accumulatedLight * phase;
                        
                        // Additive blending for realistic light shafts
                        finalColor.rgb += godRayColor * (1.0 - finalColor.a);
                        finalColor.a = saturate(finalColor.a + accumulatedLight * 0.3);
                    }
                }
                
                if (!hitClouds)
                {
                    return finalColor; 
                }

                // Setup raymarching parameters
                int maxSteps = min((int)_StepCount, 64);
                
                // Clamp far clipping plane to maximum visible distance to concentrate steps in the active cloud area!
                // This gives extremely high visual density and eliminates banding under macOS Metal
                tFar = min(tFar, 160000.0);
                
                float stepSize = (tFar - tNear) / (float)maxSteps;
                
                // Use screen-space pixel position (SV_POSITION) to eliminate floating-point moire diagonal banding lines!
                // Apply a tiny randomized step size jitter (+/- 6%) per pixel to completely smash the grid aliasing shells,
                // making 3D texture undersampling Moire patterns 100% mathematically impossible!
                float pixelStepSize = stepSize * (1.0 + (jitter - 0.5) * 0.12);
                
                [loop]
                for (int i = 0; i < maxSteps; i++)
                {
                    // Full step jitter eliminates banding (slicing) at low step counts
                    // Scale by _JitterStrength (typically 1.0 to fully hide slices)
                    float t = tNear + (float(i) + jitter * max(0.5, _JitterStrength * 2.0)) * stepSize;
                    float3 currentPos = rayOrigin + rayDir * t;
                    
                    float density = SampleCloudDensity(currentPos);
                    
                    // Smooth distance-based fade (渐隐衰减) to dissolve clouds before they tile weirdly at the horizon
                    // Clouds smoothly fade to 0 between 96000m and 160000m from the camera to prevent sharp circular borders (800% range)
                    float distanceFade = saturate((_MaxRenderDist - t) / (_MaxRenderDist * 0.4));
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
                        // Thin, wispy boundary layers scatter light intensely, making them look bright and translucent.
                        // We blend the shadow color towards the sky light at low densities to keep edges light and通透!
                        float edgeGlowFactor = saturate(1.0 - density * 1.5);
                        float3 voxelAlbedo = lerp(_ShadowColor.rgb, _MaxLightColor.rgb, saturate(heightFactor + edgeGlowFactor * 1.6));
                        
                        // Final cloud color is the product of albedo and lighting (乘性着色，完美保留体积自阴影与明暗梯度)
                        float3 cloudColor = voxelAlbedo * voxelLighting;
                        
                        // Beer-Lambert law transmittance: mathematically invariant to stepSize and stepCount,
                        // completely eliminating the contour slicing/banding artifacts!
                        float alpha = 1.0 - exp(-density * pixelStepSize * 0.0055);
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
