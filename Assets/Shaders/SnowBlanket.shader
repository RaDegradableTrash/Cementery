Shader "Environment/SnowBlanket"
{
    Properties
    {
        _Cutoff ("Snow Cutoff", Range(0, 1)) = 0.1
        _DisplacementScale ("Displacement Scale", Float) = 0.2
        _NormalBlend ("Normal Blend", Range(0, 1)) = 0.8
        _SnowColor ("Snow Color", Color) = (0.95, 0.98, 1.0, 1.0)
        _SnowDebugGreen ("Debug Green", Float) = 0
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" "Queue"="AlphaTest" "RenderPipeline"="UniversalPipeline" }
        LOD 200
        
        // Solid opaque snow: ZWrite On to prevent chunk double-blending seams!
        ZWrite On
        Blend One Zero
        
        // Depth offset to eliminate low-poly Z-fighting natively without vertex displacement!
        Offset -1, -1

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.5

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 normalWS : TEXCOORD1;
                float4 shadowCoord : TEXCOORD2;
            };

            float _DisplacementScale;
            float _Cutoff;
            float _NormalBlend;
            float _SnowDebugGreen;
            
            float4 _GlobalSnowMapParams; // x: minX, y: minZ, z: size, w: 1/size
            Texture2D _GlobalSnowHeightMap;
            SamplerState sampler_GlobalSnowHeightMap;

            float GetSnowHeight(float3 positionWS)
            {
                float halfSize = _GlobalSnowMapParams.z * 0.5;
                float u = (positionWS.x - _GlobalSnowMapParams.x + halfSize) * _GlobalSnowMapParams.w;
                float v = (positionWS.z - _GlobalSnowMapParams.y + halfSize) * _GlobalSnowMapParams.w;
                
                // Sample texture with bilinear filtering
                float h = _GlobalSnowHeightMap.SampleLevel(sampler_GlobalSnowHeightMap, float2(u, v), 0).r;
                
                // Fade out snow near the edges
                float distFromCenter = length(positionWS.xz - _GlobalSnowMapParams.xy) / halfSize;
                float edgeFade = 1.0 - smoothstep(0.8, 1.0, distFromCenter);
                
                return h * edgeFade;
            }

            Varyings vert(Attributes input)
            {
                Varyings output;
                float3 positionWS = TransformObjectToWorld(input.positionOS.xyz);
                output.positionCS = TransformWorldToHClip(positionWS);
                
                output.positionWS = positionWS;
                output.normalWS = TransformObjectToWorldNormal(input.normalOS);
                
                VertexPositionInputs vertexInput = GetVertexPositionInputs(input.positionOS.xyz);
                output.shadowCoord = GetShadowCoord(vertexInput);
                
                return output;
            }

            float4 frag(Varyings input) : SV_Target
            {
                float h = GetSnowHeight(input.positionWS);
                
                // Debug mode: show green WITHOUT clipping
                if (_SnowDebugGreen > 0.5) 
                {
                    return float4(0.0, 1.0, 0.0, 1.0);
                }

                // Revert to reliable hard clip! AlphaToMask may fail if MSAA is disabled in URP
                clip(h - _Cutoff);
                
                float3 worldNormal = normalize(input.normalWS);
                float3 upNormal = float3(0, 1, 0);
                float blendFactor = saturate(h * 2.0) * _NormalBlend * 0.5;
                float3 finalNormalWS = normalize(lerp(worldNormal, upNormal, blendFactor * (1.0 - saturate(h * 0.5))));

                Light mainLight = GetMainLight(input.shadowCoord);
                float NdotL = saturate(dot(finalNormalWS, mainLight.direction));
                // Use wrap lighting to simulate snow subsurface scattering and increase brightness
                float wrap = 0.4;
                float NdotLWrap = saturate((dot(finalNormalWS, mainLight.direction) + wrap) / (1.0 + wrap));
                
                float shadowTerm = mainLight.shadowAttenuation; 
                float litFactor = NdotLWrap * shadowTerm;
                
                // Boost direct light significantly so snow looks bright white in the sun!
                float3 directLight = mainLight.color * litFactor * 1.8;
                
                // Kill specular completely in shadows!
                float shadowMask = smoothstep(0.1, 0.5, shadowTerm);
                
                float3 viewDirWS = normalize(_WorldSpaceCameraPos - input.positionWS);
                float3 halfVector = normalize(mainLight.direction + viewDirWS);
                float NdotH = saturate(dot(finalNormalWS, halfVector));
                
                // Stronger, sharper specular glints for snow crystals, ONLY in direct sunlight
                float specularIntensity = pow(NdotH, 32.0) * 0.4 * litFactor * shadowMask; 
                
                // Ambient lighting. Make it darker in shadows to feel "deeper"
                float3 ambient = SampleSH(finalNormalWS) * lerp(0.6, 1.1, shadowMask); 
                
                float3 snowColor = float3(0.95, 0.98, 1.0);
                float3 finalColor = snowColor * (directLight + ambient) + (specularIntensity * snowColor);
                
                return float4(finalColor, 1.0);
            }
            ENDHLSL
        }
        
        Pass
        {
            Name "ShadowCaster"
            Tags{"LightMode" = "ShadowCaster"}

            ZWrite On
            ZTest LEqual
            ColorMask 0
            Cull Back

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.5

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes { float4 positionOS : POSITION; };
            struct Varyings { float4 positionCS : SV_POSITION; float snowHeight : TEXCOORD0; };

            float _Cutoff;
            float4 _GlobalSnowMapParams;
            Texture2D _GlobalSnowHeightMap;
            SamplerState sampler_GlobalSnowHeightMap;

            float GetSnowHeight(float3 positionWS)
            {
                float halfSize = _GlobalSnowMapParams.z * 0.5;
                float u = (positionWS.x - _GlobalSnowMapParams.x + halfSize) * _GlobalSnowMapParams.w;
                float v = (positionWS.z - _GlobalSnowMapParams.y + halfSize) * _GlobalSnowMapParams.w;
                float h = _GlobalSnowHeightMap.SampleLevel(sampler_GlobalSnowHeightMap, float2(u, v), 0).r;
                float distFromCenter = length(positionWS.xz - _GlobalSnowMapParams.xy) / halfSize;
                return h * (1.0 - smoothstep(0.8, 1.0, distFromCenter));
            }

            Varyings vert(Attributes input)
            {
                Varyings output;
                float3 positionWS = TransformObjectToWorld(input.positionOS.xyz);
                output.positionCS = TransformWorldToHClip(positionWS);
                output.snowHeight = GetSnowHeight(positionWS);
                return output;
            }

            float4 frag(Varyings input) : SV_Target
            {
                clip(input.snowHeight - _Cutoff);
                return 0;
            }
            ENDHLSL
        }
    }
}
