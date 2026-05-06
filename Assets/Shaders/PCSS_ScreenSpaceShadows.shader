Shader "Hidden/Universal Render Pipeline/PCSS_ScreenSpaceShadows"
{
    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" }
        ZTest Always ZWrite Off Cull Off

        Pass
        {
            Name "PCSS Shadows"

            HLSLPROGRAM
            #pragma vertex FullscreenVert
            #pragma fragment Fragment

            // URP Keywords required for shadow sampling
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE
            #pragma multi_compile _ _SHADOWS_SOFT

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            // Texture declarations are handled by Lighting.hlsl/Shadows.hlsl
            // But we need a custom float texture to safely sample raw depth on Metal
            TEXTURE2D_FLOAT(_MyMainLightShadowmapTexture);
            SAMPLER(sampler_MyMainLightShadowmapTexture);

            CBUFFER_START(UnityPerMaterial)
                float _LightSize;
                float _MaxPenumbraSize;
                float _ShadowIntensity;
                float _LightDistance; // To simulate directional light distance for penumbra calculation
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

            // Poisson Disk for better random distribution
            static const float2 poissonDisk[16] = {
                float2( -0.94201624, -0.39906216 ),
                float2( 0.94558609, -0.76890725 ),
                float2( -0.094184101, -0.92938870 ),
                float2( 0.34495938, 0.29387760 ),
                float2( -0.91588581, 0.45771432 ),
                float2( -0.81544232, -0.87912464 ),
                float2( -0.38277543, 0.27676845 ),
                float2( 0.97484398, 0.75648379 ),
                float2( 0.44323325, -0.97511554 ),
                float2( 0.53742981, -0.47373420 ),
                float2( -0.26496911, -0.41893023 ),
                float2( 0.79197514, 0.19090188 ),
                float2( -0.24188840, 0.99706507 ),
                float2( -0.81409955, 0.91437590 ),
                float2( 0.19984126, 0.78641367 ),
                float2( 0.14383161, -0.14100790 )
            };

            float rand(float2 uv)
            {
                return frac(sin(dot(uv, float2(12.9898, 78.233))) * 43758.5453);
            }

            half Fragment(Varyings input) : SV_Target
            {
                float2 uv = input.uv;
                
                // 1. Get Scene Depth
                #if UNITY_REVERSED_Z
                    float depth = SampleSceneDepth(uv);
                #else
                    float depth = lerp(UNITY_NEAR_CLIP_VALUE, 1, SampleSceneDepth(uv));
                #endif

                // Ignore skybox
                #if UNITY_REVERSED_Z
                if (depth < 0.00001) return 1.0;
                #else
                if (depth >= 0.99999) return 1.0;
                #endif

                // 2. Reconstruct World Position
                float3 worldPos = ComputeWorldSpacePosition(uv, depth, UNITY_MATRIX_I_VP);
                
                #if defined(_MAIN_LIGHT_SHADOWS) || defined(_MAIN_LIGHT_SHADOWS_CASCADE)
                    
                    float4 shadowCoord = TransformWorldToShadowCoord(worldPos);
                    float zReceiver = shadowCoord.z;
                    
                    // Simple Random Rotation Matrix
                    float rot = rand(uv) * 6.2831853;
                    float s, c;
                    sincos(rot, s, c);
                    float2x2 rotMat = float2x2(c, -s, s, c);

                    // --- STEP 1: BLOCKER SEARCH ---
                    float searchRadius = _LightSize * 0.005; // Search radius scaling
                    int blockers = 0;
                    float blockerSum = 0;
                    
                    for (int i = 0; i < 8; i++)
                    {
                        float2 offset = mul(rotMat, poissonDisk[i]) * searchRadius;
                        float2 sampleUV = shadowCoord.xy + offset;
                        
                        // Sample raw shadow depth map
                        float shadowMapDepth = SAMPLE_TEXTURE2D_LOD(_MyMainLightShadowmapTexture, sampler_MyMainLightShadowmapTexture, sampleUV, 0).r;
                        
                        // Blocker logic (assume Reversed Z for shadow map)
                        #if UNITY_REVERSED_Z
                            bool isBlocker = shadowMapDepth > zReceiver;
                        #else
                            bool isBlocker = shadowMapDepth < zReceiver;
                        #endif

                        if (isBlocker)
                        {
                            blockerSum += shadowMapDepth;
                            blockers++;
                        }
                    }

                    if (blockers == 0) return 1.0; // Fully lit

                    // --- STEP 2: PENUMBRA ESTIMATION ---
                    float avgBlockerDepth = blockerSum / (float)blockers;
                    
                    // Distance calculation for Orthographic projection (Reversed Z: 1 is near light, 0 is far)
                    float distToLight = 1.0 - zReceiver + 0.0001;
                    float blockerDist = abs(avgBlockerDepth - zReceiver);
                    
                    // PCSS Ratio: Distance from blocker / Distance from light to blocker
                    // avgBlockerDepth is the depth of the blocker. Distance from light to blocker = 1.0 - avgBlockerDepth.
                    float distToBlocker = 1.0 - avgBlockerDepth + 0.0001;
                    float penumbraRatio = blockerDist / distToBlocker;
                    
                    // Multiply by _LightSize to scale the blur
                    float filterRadius = clamp(penumbraRatio * _LightSize * 0.01, 0.0, _MaxPenumbraSize);

                    // --- STEP 3: PCF FILTERING ---
                    float shadow = 0.0;
                    for (int j = 0; j < 16; j++)
                    {
                        float2 offset = mul(rotMat, poissonDisk[j]) * filterRadius;
                        
                        // To get smooth hardware filtering and avoid cascade edge issues, 
                        // we modify the shadow coord and use MainLightRealtimeShadow.
                        float4 offsetCoord = shadowCoord;
                        offsetCoord.xy += offset;
                        
                        shadow += MainLightRealtimeShadow(offsetCoord);
                    }
                    
                    shadow /= 16.0;
                    return lerp(1.0, shadow, _ShadowIntensity);

                #else
                    return 1.0;
                #endif
            }
            ENDHLSL
        }
    }
}
