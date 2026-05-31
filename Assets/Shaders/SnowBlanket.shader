Shader "Environment/SnowBlanket"
{
    Properties
    {
        _LightGreen ("Light Green", Color) = (0.6, 1.0, 0.6, 1.0)
        _DarkGreen ("Dark Green", Color) = (0.1, 0.4, 0.1, 1.0)
        _DisplacementScale ("Height Displacement", Float) = 1.5
        _NormalBlend ("Normal Smoothness", Range(0, 1)) = 0.85
        _Cutoff ("Snow Threshold", Range(0, 0.1)) = 0.05
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" "RenderPipeline"="UniversalPipeline" "Queue"="Geometry" }
        LOD 300
        ZWrite On

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode"="UniversalForward" }

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            // target 3.5 for vertex texture fetch
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
                float snowHeight : TEXCOORD3;
            };

            float4 _LightGreen;
            float4 _DarkGreen;
            float _DisplacementScale;
            float _NormalBlend;
            float _Cutoff;
            
            float4 _GlobalSnowMapParams; // x: centerX, y: centerZ, z: worldSize, w: 1/worldSize
            Texture2D _GlobalSnowHeightMap;
            SamplerState sampler_GlobalSnowHeightMap;

            float GetSnowHeight(float3 positionWS)
            {
                float halfSize = _GlobalSnowMapParams.z * 0.5;
                float u = (positionWS.x - _GlobalSnowMapParams.x + halfSize) * _GlobalSnowMapParams.w;
                float v = (positionWS.z - _GlobalSnowMapParams.y + halfSize) * _GlobalSnowMapParams.w;
                
                if (u < 0.0 || u > 1.0 || v < 0.0 || v > 1.0)
                    return 0.0;
                    
                // Vertex texture fetch requires SampleLevel
                return _GlobalSnowHeightMap.SampleLevel(sampler_GlobalSnowHeightMap, float2(u, v), 0).r;
            }

            Varyings vert(Attributes input)
            {
                Varyings output;
                
                float3 positionWS = TransformObjectToWorld(input.positionOS.xyz);
                float3 normalWS = TransformObjectToWorldNormal(input.normalOS);
                
                // --- Vertex Displacement ---
                float h = GetSnowHeight(positionWS);
                
                // Push vertices up based on heightmap (using world UP for gravity-based piling)
                // We only displace if h > cutoff to ensure flat base
                float displacement = max(0.0, h - _Cutoff) * _DisplacementScale;
                positionWS.y += displacement;
                
                output.positionCS = TransformWorldToHClip(positionWS);
                output.positionWS = positionWS;
                output.normalWS = normalWS;
                output.snowHeight = h;
                
                return output;
            }

            float4 frag(Varyings input) : SV_Target
            {
                float h = input.snowHeight;
                
                // 1. Clip out areas with no snow so the sand underneath shows through
                clip(h - _Cutoff);

                // 2. Normal Blending for butter-smooth shading
                float3 worldNormal = normalize(input.normalWS);
                float3 upNormal = float3(0, 1, 0);
                
                // The thicker the snow, the more it points UP to hide the sharp terrain geometry
                float blendFactor = saturate(h * 2.0) * _NormalBlend;
                float3 finalNormalWS = normalize(lerp(worldNormal, upNormal, blendFactor));

                // 3. Snow PBR Approximation (High diffuse, low specular, high ambient)
                Light mainLight = GetMainLight();
                float NdotL = saturate(dot(finalNormalWS, mainLight.direction) * 0.5 + 0.5);
                
                float3 diffuse = mainLight.color * NdotL * mainLight.shadowAttenuation;
                float3 ambient = SampleSH(finalNormalWS) * 1.5; // boosted ambient for SSS
                
                // Color interpolation: Light green for thin snow, dark green for thick snow
                float3 snowColor = lerp(_LightGreen.rgb, _DarkGreen.rgb, saturate((h - _Cutoff) * 2.0));
                
                float3 finalColor = snowColor * (diffuse + ambient);
                
                return float4(finalColor, 1.0);
            }
            ENDHLSL
        }
        
        // Shadow Caster Pass
        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode"="ShadowCaster" }

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.5

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float snowHeight : TEXCOORD0;
            };

            float _DisplacementScale;
            float _Cutoff;
            float4 _GlobalSnowMapParams;
            Texture2D _GlobalSnowHeightMap;
            SamplerState sampler_GlobalSnowHeightMap;

            float GetSnowHeight(float3 positionWS)
            {
                float halfSize = _GlobalSnowMapParams.z * 0.5;
                float u = (positionWS.x - _GlobalSnowMapParams.x + halfSize) * _GlobalSnowMapParams.w;
                float v = (positionWS.z - _GlobalSnowMapParams.y + halfSize) * _GlobalSnowMapParams.w;
                
                if (u < 0.0 || u > 1.0 || v < 0.0 || v > 1.0) return 0.0;
                return _GlobalSnowHeightMap.SampleLevel(sampler_GlobalSnowHeightMap, float2(u, v), 0).r;
            }

            Varyings vert(Attributes input)
            {
                Varyings output;
                float3 positionWS = TransformObjectToWorld(input.positionOS.xyz);
                float h = GetSnowHeight(positionWS);
                
                float displacement = max(0.0, h - _Cutoff) * _DisplacementScale;
                positionWS.y += displacement;
                
                output.positionCS = TransformWorldToHClip(positionWS);
                output.snowHeight = h;
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
