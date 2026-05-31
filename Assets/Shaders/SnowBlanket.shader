Shader "Environment/SnowBlanket"
{
    Properties
    {
        _SnowColor ("Snow Color", Color) = (1.0, 0.5, 0.8, 1)
        _MaxAlpha ("Max Alpha", Range(0, 1)) = 0.9
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
            };

            float4 _SnowColor;
            float _MaxAlpha;
            
            float2 _MapCenter;
            float _MapWorldSize;
            Texture2D _GlobalSnowHeightMap;
            SamplerState sampler_GlobalSnowHeightMap;

            float GetSnowHeight(float3 positionWS)
            {
                float halfSize = _MapWorldSize * 0.5;
                float u = (positionWS.x - _MapCenter.x + halfSize) / _MapWorldSize;
                float v = (positionWS.z - _MapCenter.y + halfSize) / _MapWorldSize;
                
                if (u < 0.0 || u > 1.0 || v < 0.0 || v > 1.0)
                    return 0.0;
                    
                float h = _GlobalSnowHeightMap.SampleLevel(sampler_GlobalSnowHeightMap, float2(u, v), 0).r;
                return h;
            }

            Varyings vert(Attributes input)
            {
                Varyings output;
                float3 positionWS = TransformObjectToWorld(input.positionOS.xyz);
                output.positionCS = TransformWorldToHClip(positionWS);
                output.positionWS = positionWS;
                output.normalWS = TransformObjectToWorldNormal(input.normalOS);
                return output;
            }

            float4 frag(Varyings input) : SV_Target
            {
                float h = GetSnowHeight(input.positionWS);
                
                // No clip! Use height as smooth alpha
                float alpha = saturate(h * 5.0) * _MaxAlpha; // Boost the alpha so it appears quickly
                if (alpha <= 0.01) discard;

                Light mainLight = GetMainLight();
                
                float3 upNormal = float3(0, 1, 0);
                float3 finalNormalWS = normalize(lerp(input.normalWS, upNormal, 0.7));

                float NdotL = saturate(dot(finalNormalWS, mainLight.direction) * 0.5 + 0.5);
                float3 diffuse = mainLight.color * NdotL * mainLight.shadowAttenuation;
                float3 ambient = SampleSH(finalNormalWS) * 1.2;
                
                float3 finalColor = _SnowColor.rgb * (diffuse + ambient);
                
                return float4(finalColor, alpha);
            }
            ENDHLSL
        }
    }
}
