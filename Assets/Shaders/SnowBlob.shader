Shader "Environment/SnowBlob"
{
    Properties
    {
        _SnowColor ("Snow Color", Color) = (1.0, 0.5, 0.8, 1)
        _BlendStrength ("Normal UP Blend Strength", Range(0, 1)) = 0.85
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" "RenderPipeline"="UniversalPipeline" "Queue"="Geometry+1" }
        LOD 300

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode"="UniversalForward" }

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 normalWS : TEXCOORD1;
                float4 positionOS : TEXCOORD3;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            UNITY_INSTANCING_BUFFER_START(Props)
                UNITY_DEFINE_INSTANCED_PROP(float4, _SnowColor)
                UNITY_DEFINE_INSTANCED_PROP(float, _BlendStrength)
            UNITY_INSTANCING_BUFFER_END(Props)

            // Simple noise function for edge corruption
            float pseudoNoise(float2 uv)
            {
                return frac(sin(dot(uv, float2(12.9898, 78.233))) * 43758.5453);
            }

            Varyings vert(Attributes input)
            {
                Varyings output;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, output);

                float3 positionWS = TransformObjectToWorld(input.positionOS.xyz);
                output.positionCS = TransformWorldToHClip(positionWS);
                output.positionWS = positionWS;
                output.normalWS = TransformObjectToWorldNormal(input.normalOS);
                output.positionOS = input.positionOS;
                return output;
            }

            float4 frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);
                
                // --- 1. Noise Mask & Edge Corrosive Clipping ---
                // distance from center of object space in XZ plane (sphere has radius 0.5)
                float d = length(input.positionOS.xz) * 2.0; 
                float noise = pseudoNoise(input.positionWS.xz * 2.0);
                
                // clip(1.0 - distance - noise) as requested
                clip(1.0 - d - noise * 0.2);

                // --- 2. Normal Blending for "Butter/Cream" transition ---
                float3 worldNormal = normalize(input.normalWS);
                float3 upNormal = float3(0, 1, 0);

                // Blend normal to UP near the center, allowing soft shading
                float blendStrength = UNITY_ACCESS_INSTANCED_PROP(Props, _BlendStrength);
                float blend = saturate((1.0 - d) * blendStrength);
                float3 finalNormal = normalize(lerp(worldNormal, upNormal, blend));

                // --- 3. Lighting ---
                Light mainLight = GetMainLight();
                float NdotL = saturate(dot(finalNormal, mainLight.direction) * 0.5 + 0.5);
                float3 diffuse = mainLight.color * NdotL * mainLight.shadowAttenuation;
                float3 ambient = SampleSH(finalNormal) * 1.2;
                
                float4 snowColor = UNITY_ACCESS_INSTANCED_PROP(Props, _SnowColor);
                float3 finalColor = snowColor.rgb * (diffuse + ambient);
                
                return float4(finalColor, 1.0);
            }
            ENDHLSL
        }

        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode"="ShadowCaster" }

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float4 positionOS : TEXCOORD0;
                float3 positionWS : TEXCOORD1;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            float pseudoNoise(float2 uv)
            {
                return frac(sin(dot(uv, float2(12.9898, 78.233))) * 43758.5453);
            }

            Varyings vert(Attributes input)
            {
                Varyings output;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, output);
                
                float3 positionWS = TransformObjectToWorld(input.positionOS.xyz);
                output.positionCS = TransformWorldToHClip(positionWS);
                output.positionOS = input.positionOS;
                output.positionWS = positionWS;
                return output;
            }

            float4 frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);
                
                // Match the clipping from ForwardLit
                float d = length(input.positionOS.xz) * 2.0; 
                float noise = pseudoNoise(input.positionWS.xz * 2.0);
                clip(1.0 - d - noise * 0.2);

                return 0;
            }
            ENDHLSL
        }
    }
}
