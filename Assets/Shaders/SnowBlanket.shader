Shader "Environment/SnowBlanket"
{
    Properties
    {
        _LightGreen ("Light Snow", Color) = (1.0, 1.0, 1.0, 1.0)
        _DarkGreen ("Deep Snow", Color) = (0.92, 0.96, 1.0, 1.0)
        _DisplacementScale ("Height Displacement", Float) = 1.5
        _NormalBlend ("Normal Smoothness", Range(0, 1)) = 0.85
        _Cutoff ("Snow Threshold", Range(0, 0.1)) = 0.05
    }
    SubShader
    {
        Tags { "RenderType"="Transparent" "RenderPipeline"="UniversalPipeline" "Queue"="Transparent" }
        LOD 300
        ZWrite Off
        Blend SrcAlpha OneMinusSrcAlpha

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode"="UniversalForward" }

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            // target 3.5 for vertex texture fetch
            #pragma target 3.5 

            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS_CASCADE
            #pragma multi_compile _ _SHADOWS_SOFT

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
                float snowHeight : TEXCOORD3;
            };

            float4 _LightGreen;
            float4 _DarkGreen;
            float _DisplacementScale;
            float _NormalBlend;
            float _Cutoff;
            float _SnowDebugGreen;
            
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
                
                // Use smoothstep to prevent harsh spikes (0.05 to 0.2)
                float displacement = smoothstep(_Cutoff, _Cutoff + 0.3, h) * _DisplacementScale;
                
                // Geometric Decoupling Fix: The high-poly snow mesh forms smooth saddle curves,
                // while the low-poly terrain forms rigid diagonal ridges that poke through.
                // We add a tiny 15cm bias that smoothly fades in to hoist the snow over the ridges.
                float offsetBias = smoothstep(0.01, 0.1, displacement) * 0.15;
                
                positionWS.y += min(displacement, 1.5) + offsetBias;
                
                output.positionCS = TransformWorldToHClip(positionWS);
                output.positionWS = positionWS;
                output.normalWS = normalWS;
                output.snowHeight = h;
                
                VertexPositionInputs vertexInput = GetVertexPositionInputs(input.positionOS.xyz);
                output.shadowCoord = GetShadowCoord(vertexInput);
                
                return output;
            }

            float4 frag(Varyings input) : SV_Target
            {
                float h = input.snowHeight;
                
                // 1. Remove clip completely for smooth alpha blending
                // clip(h - _Cutoff);

                // 2. Normal Blending for butter-smooth shading
                float3 worldNormal = normalize(input.normalWS);
                float3 upNormal = float3(0, 1, 0);
                
                // The thicker the snow, the more it points UP to hide the sharp terrain geometry, 
                // but we also keep some world normal to avoid looking flat.
                float blendFactor = saturate(h * 2.0) * _NormalBlend * 0.5;
                float3 finalNormalWS = normalize(lerp(worldNormal, upNormal, blendFactor * (1.0 - saturate(h * 0.5))));

                // 3. Snow PBR Approximation (High diffuse, specular highlights, variable ambient)
                Light mainLight = GetMainLight(input.shadowCoord);
                
                // Diffuse (Ensuring shadow areas have at least 70% brightness to keep it extremely white)
                float NdotL = saturate(dot(finalNormalWS, mainLight.direction) * 0.5 + 0.5);
                float shadowTerm = max(mainLight.shadowAttenuation, 0.7); // Greatly reduced shadow impact
                float3 diffuse = mainLight.color * NdotL * shadowTerm;
                
                // Specular (Soft reflection for snow surface, not sharp ice)
                float3 viewDirWS = normalize(_WorldSpaceCameraPos - input.positionWS);
                float3 halfVector = normalize(mainLight.direction + viewDirWS);
                float NdotH = saturate(dot(finalNormalWS, halfVector));
                float specularIntensity = pow(NdotH, 16.0) * 0.3 * shadowTerm; 
                
                // Ambient (Strong flat ambient to overpower environmental darkness)
                float3 ambient = SampleSH(finalNormalWS) * 0.5 + float3(0.6, 0.65, 0.7); 
                
                // Base pure white color (slightly tinted blue to contrast with pure white sky)
                float3 snowColor = float3(0.95, 0.98, 1.0);
                
                // Debug mode: FORCE absolute opaque green!
                if (_SnowDebugGreen > 0.5) 
                {
                    return float4(0.0, 1.0, 0.0, 1.0);
                }
                
                float3 finalColor = snowColor * (diffuse + ambient) + (specularIntensity * snowColor);
                
                // Alpha fade out based on height to smoothly blend with terrain
                float alpha = smoothstep(_Cutoff * 0.5, _Cutoff * 2.0, h);
                
                return float4(finalColor, alpha);
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
                
                float h = _GlobalSnowHeightMap.SampleLevel(sampler_GlobalSnowHeightMap, float2(u, v), 0).r;
                
                // Fade out snow near the edges of the 100x100 map to prevent sharp pixelated boundaries
                float distFromCenter = length(positionWS.xz - _GlobalSnowMapParams.xy) / halfSize;
                float edgeFade = 1.0 - smoothstep(0.8, 1.0, distFromCenter);
                
                return h * edgeFade;
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
