Shader "Environment/LocalSnowBlanket"
{
    Properties
    {
        _Cutoff ("Snow Cutoff", Range(0, 1)) = 0.1
        _DisplacementScale ("Displacement Scale", Float) = 0.2
        _NormalBlend ("Normal Blend", Range(0, 1)) = 0.8
        _SnowColor ("Snow Color", Color) = (0.95, 0.98, 1.0, 1.0)
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" "Queue"="AlphaTest" "RenderPipeline"="UniversalPipeline" }
        LOD 200
        
        AlphaToMask On
        ZWrite On
        Blend One Zero
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
                float3 positionOS : TEXCOORD3;
            };

            float _Cutoff;
            float _NormalBlend;
            float4 _SnowColor;
            
            float4 _LocalSnowBounds; // x: minX, y: minZ, z: lengthX, w: lengthZ
            float4x4 _RootWorldToLocal;
            Texture2D _LocalSnowHeightMap;
            SamplerState sampler_LocalSnowHeightMap;

            float GetLocalSnowHeight(float3 positionWS)
            {
                // Convert world position to the Root object's local space
                float4 rootLocalPos = mul(_RootWorldToLocal, float4(positionWS, 1.0));
                
                float u = (rootLocalPos.x - _LocalSnowBounds.x) / _LocalSnowBounds.z;
                float v = (rootLocalPos.z - _LocalSnowBounds.y) / _LocalSnowBounds.w;
                
                if (u < 0 || u > 1 || v < 0 || v > 1) return 0;
                
                float2 snowData = _LocalSnowHeightMap.SampleLevel(sampler_LocalSnowHeightMap, float2(u, v), 0).rg;
                float h = snowData.r;
                float hitY = snowData.g;
                
                // Z-Buffer check: if this vertex is significantly below the highest particle hit in this column, it's inside or underneath!
                if (rootLocalPos.y < hitY - 0.5) 
                {
                    return 0; // Ignore this vertex!
                }
                
                return h;
            }

            Varyings vert(Attributes input)
            {
                Varyings output;
                
                float3 positionWS = TransformObjectToWorld(input.positionOS.xyz);
                float3 normalWS = TransformObjectToWorldNormal(input.normalOS);
                
                // Inflate in world space by exactly 5mm to avoid FBX scale bugs!
                positionWS += normalWS * 0.005;
                
                output.positionCS = TransformWorldToHClip(positionWS);
                output.positionWS = positionWS;
                output.normalWS = normalWS;
                
                VertexPositionInputs vertexInput = GetVertexPositionInputs(input.positionOS.xyz);
                output.shadowCoord = GetShadowCoord(vertexInput);
                
                return output;
            }

            float4 frag(Varyings input) : SV_Target
            {
                // Only render snow on upward facing surfaces
                float upDot = dot(normalize(input.normalWS), float3(0, 1, 0));
                float slopeMask = smoothstep(0.3, 0.6, upDot);
                
                float h = GetLocalSnowHeight(input.positionWS) * slopeMask;
                float alpha = smoothstep(_Cutoff - 0.05, _Cutoff + 0.05, h);
                clip(alpha - 0.01);
                
                float3 worldNormal = normalize(input.normalWS);
                float3 upNormal = float3(0, 1, 0);
                float blendFactor = saturate(h * 2.0) * _NormalBlend * 0.5;
                float3 finalNormalWS = normalize(lerp(worldNormal, upNormal, blendFactor));

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
                
                float3 finalColor = _SnowColor.rgb * (directLight + ambient) + (specularIntensity * _SnowColor.rgb);
                
                return float4(finalColor, alpha);
            }
            ENDHLSL
        }
    }
}
