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

        HLSLINCLUDE
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
                float4x4 _InvViewProj;
                float _ShaderReversedZ;
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
                float2 uvwVertShift = pos.xz * (_BaseScale * 0.4) + _BaseWindSpeed.xz * _Time.y * 0.15;
                float vertShiftNoise = SAMPLE_TEXTURE3D(_BaseNoiseTex, sampler_LinearRepeat, float3(uvwVertShift.x, 0.5, uvwVertShift.y)).r;
                
                // Add vertical shift to pos.y (distorts height up and down by up to 500 meters)
                float3 warpedPos = pos;
                warpedPos.y += (vertShiftNoise - 0.5) * 500.0 * _VerticalRandomness;

                // --- 2. MULTI-SCALE DYNAMIC HEIGHT ENVELOPE (宏观高度遮罩) ---
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
                float baseSpread = (1.0 - heightFactor) * _ConvectiveWarp * 0.36;

                // --- 6. LOW-FREQUENCY ISLAND COVERAGE (宏观云岛分布) ---
                float3 uvwCoverage = warpedPos * (baseScaleVec * 0.2) + _BaseWindSpeed.xyz * _Time.y * 0.1;
                uvwCoverage.y = 0.0;
                float coverage = SAMPLE_TEXTURE3D(_BaseNoiseTex, sampler_LinearRepeat, uvwCoverage).r;
                
                // --- 7. DYNAMIC RADIUS THRESHOLDING (云块体积与大小控制)
                float coverageMask = smoothstep(0.32, 0.58, coverage);
                
                float distToCam = length(pos - _WorldSpaceCameraPos);
                float distRatio = saturate(distToCam / _MaxRenderDist);
                float localThreshold = _CloudThreshold * 0.5 + baseSpread + (1.0 - coverageMask) * 1.5 + distRatio * 0.25;
                
                // Apply vertical profile envelope directly to shape
                float baseShape = baseNoise * verticalEnvelope;
                float cloudVal = baseShape - localThreshold;

                if (cloudVal <= 0.0) return 0.0;

                // --- 8. BULGING CAULIFLOWER PUFFS & DETAIL CARVING (椰菜花无缝堆叠) ---
                float3 uvwDetail = warpedPos * detailScaleVec + _DetailWindSpeed.xyz * _Time.y;
                float detailNoise = SAMPLE_TEXTURE3D(_DetailNoiseTex, sampler_LinearRepeat, uvwDetail).r;
                
                float detailFade = saturate(1.0 - distRatio * 1.5);
                float erosionModifier = lerp(0.2, 1.2, heightFactor); // Carve more at the fluffy tops, less at the flat bottoms
                float edgeCarving = (1.0 - detailNoise) * (_DetailInfluence * detailFade) * erosionModifier * 0.7;
                
                // Subtract carving to create distinct structured clumps
                float carvedShape = cloudVal - edgeCarving;
                
                // Add popcorn bulging to make the clumps spherical and volumetric
                float boundaryWarp = detailNoise * (_Puffiness * detailFade) * 0.4 * saturate(carvedShape * 2.0);
                
                float finalShape = carvedShape + boundaryWarp;

                // --- 9. SOLID CORE NORMALIZATION (动漫风格坚实边缘切片) ---
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
                if (_ShaderReversedZ > 0.5)
                {
                    if (depth < 0.0005) isSkybox = true;
                }
                else
                {
                    if (depth > 0.9995) isSkybox = true;
                }

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
                        finalColor.rgb = float3(0.0, 0.0, 0.0);
                        finalColor.a = shadow * 0.51;
                    }
                }
                
                // 2. Volumetric Tyndall God Rays
                float maxTyndallDist = isSkybox ? (hitClouds ? min(tNear, 3000.0) : 3000.0) : min(sceneDist, 3000.0);
                
                if (maxTyndallDist > 20.0)
                {
                    int tyndallSteps = 16;
                    float stepSize = maxTyndallDist / (float)tyndallSteps;
                    float accumulatedLight = 0.0;
                    
                    float t = jitter * stepSize;
                    
                    [loop]
                    for (int j = 0; j < tyndallSteps; j++)
                    {
                        float3 pos = rayOrigin + rayDir * t;
                        float shadowAtPos = SampleShadowMask(pos, mainLight.direction);
                        
                        float illumination = pow(1.0 - shadowAtPos, 2.0);
                        accumulatedLight += illumination * stepSize * 0.0005;
                        
                        t += stepSize;
                    }
                    
                    if (accumulatedLight > 0.01)
                    {
                        float phase = lerp(0.5, 3.0, pow(saturate(dot(rayDir, mainLight.direction) * 0.5 + 0.5), 3.0));
                        float3 godRayColor = _MaxLightColor.rgb * accumulatedLight * phase;
                        
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
                
                tFar = min(tFar, 160000.0);
                
                float stepSize = (tFar - tNear) / (float)maxSteps;
                float pixelStepSize = stepSize * (1.0 + (jitter - 0.5) * 0.12);
                
                [loop]
                for (int i = 0; i < maxSteps; i++)
                {
                    float t = tNear + (float(i) + jitter * max(0.5, _JitterStrength * 2.0)) * stepSize;
                    float3 currentPos = rayOrigin + rayDir * t;
                    
                    float density = SampleCloudDensity(currentPos);
                    
                    float distanceFade = saturate((_MaxRenderDist - t) / (_MaxRenderDist * 0.4));
                    density *= distanceFade;
                    
                    if (density > 0.001)
                    {
                        float3 shadowSamplePos = currentPos + mainLight.direction * _LightStepDistance;
                        float densityToSun = SampleCloudDensity(shadowSamplePos) * distanceFade;
                        
                        float lightMultiplier = LightEnergy(densityToSun, _Absorption);
                        
                        float cosAngle = dot(rayDir, mainLight.direction);
                        float phaseGlow = lerp(0.8, 1.8, saturate(cosAngle * 0.5 + 0.5));
                        
                        float3 directLight = mainLight.color * lightMultiplier * phaseGlow * 1.3;
                        
                        float heightFactor = (currentPos.y - _CloudMinHeight) / (_CloudMaxHeight - _CloudMinHeight);
                        
                        float3 dynamicAmbient = max(SampleSH(float3(0.1, 1.0, 0.1)), mainLight.color * 0.22);
                        
                        float3 backlitGlow = mainLight.color * exp(-density * 0.2) * 0.28;
                        
                        float3 ambientLight = _ShadowColor.rgb * (dynamicAmbient + backlitGlow) * lerp(0.58, 1.0, heightFactor);
                        
                        float3 voxelLighting = ambientLight + directLight;
                        
                        float edgeGlowFactor = saturate(1.0 - density * 1.5);
                        float3 voxelAlbedo = lerp(_ShadowColor.rgb, _MaxLightColor.rgb, saturate(heightFactor + edgeGlowFactor * 1.6));
                        
                        float3 cloudColor = voxelAlbedo * voxelLighting;
                        
                        float alpha = 1.0 - exp(-density * pixelStepSize * 0.0055);
                        finalColor.rgb += (1.0 - finalColor.a) * cloudColor * alpha;
                        finalColor.a += (1.0 - finalColor.a) * alpha;
                        
                        if (finalColor.a >= 0.95)
                        {
                            finalColor.a = 1.0;
                            break;
                        }
                    }
                }
                
                return finalColor;
            }

            half4 FragmentFull(Varyings input) : SV_Target
            {
                return Fragment(input);
            }
        ENDHLSL

        Pass
        {
            Name "Volumetric Clouds Full"
            Blend SrcAlpha OneMinusSrcAlpha
            HLSLPROGRAM
            #pragma vertex FullscreenVert
            #pragma fragment FragmentFull
            ENDHLSL
        }

        Pass
        {
            Name "Volumetric Clouds LowRes"
            Blend Off
            HLSLPROGRAM
            #pragma vertex FullscreenVert
            #pragma fragment Fragment
            ENDHLSL
        }

        Pass
        {
            Name "Volumetric Clouds Upscale"
            Blend SrcAlpha OneMinusSrcAlpha
            HLSLPROGRAM
            #pragma vertex FullscreenVert
            #pragma fragment FragmentUpscale
            
            TEXTURE2D(_MainTex);
            SAMPLER(sampler_MainTex);
            
            half4 FragmentUpscale(Varyings input) : SV_Target
            {
                return SAMPLE_TEXTURE2D(_MainTex, sampler_LinearClamp, input.uv);
            }
            ENDHLSL
        }
    }
}
