Shader "Environment/RoadOverlay"
{
    Properties
    {
        _Color ("Road Tint", Color) = (0.3, 0.3, 0.3, 1.0)
        _MainTex ("Road Texture (Albedo)", 2D) = "white" {}
        _Tiling ("Texture Tiling", Float) = 10.0
        _BumpMap ("Normal Map", 2D) = "bump" {}
        _BumpScale ("Normal Strength", Range(0, 2)) = 1.0
        _Glossiness ("Smoothness", Range(0, 1)) = 0.1
        _SpecularColor ("Specular Color", Color) = (0.2, 0.2, 0.2, 1.0)
    }
    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Geometry+10" "RenderPipeline"="UniversalPipeline" }
        LOD 200

        ZWrite Off
        Blend SrcAlpha OneMinusSrcAlpha
        Offset -2, -2 // Push towards camera to prevent Z-fighting

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.0

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float2 uv : TEXCOORD0;
                float4 color : COLOR;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 normalWS : TEXCOORD1;
                float2 uv : TEXCOORD3;
                float4 color : COLOR;
            };

            float4 _Color;
            TEXTURE2D(_MainTex);
            SAMPLER(sampler_MainTex);
            float _Tiling;
            TEXTURE2D(_BumpMap);
            SAMPLER(sampler_BumpMap);
            float _BumpScale;
            float _Glossiness;
            float4 _SpecularColor;

            Varyings vert(Attributes input)
            {
                Varyings output;
                float3 positionWS = TransformObjectToWorld(input.positionOS.xyz);
                output.positionCS = TransformWorldToHClip(positionWS);
                output.positionWS = positionWS;
                output.normalWS = TransformObjectToWorldNormal(input.normalOS);
                output.uv = input.uv;
                output.color = input.color;
                return output;
            }

            float4 frag(Varyings input) : SV_Target
            {
                // Read vertex color alpha as the opacity mask
                float alpha = input.color.a;
                
                // Early out if fully transparent
                if (alpha <= 0.001)
                {
                    discard;
                    return float4(0, 0, 0, 0);
                }

                // Calculate tiled UV based on world XZ position for seamless tiling across chunks
                float2 worldUV = input.positionWS.xz / _Tiling;
                float4 texColor = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, worldUV);
                float4 finalColor = texColor * _Color;

                // Lighting
                float3 normal = normalize(input.normalWS);
                
                // Support normal mapping if provided
                float4 normalMap = SAMPLE_TEXTURE2D(_BumpMap, sampler_BumpMap, worldUV);
                if (normalMap.a > 0.01) // simple check for active texture
                {
                    float3 localNormal = UnpackNormal(normalMap);
                    localNormal.xy *= _BumpScale;
                    localNormal = normalize(localNormal);
                    
                    // Simple reconstruction of tangent space for XZ mapping
                    float3 tangent = float3(1, 0, 0);
                    float3 bitangent = float3(0, 0, 1);
                    normal = normalize(tangent * localNormal.x + bitangent * localNormal.y + normal * localNormal.z);
                }

                Light mainLight = GetMainLight();
                float NdotL = saturate(dot(normal, mainLight.direction));
                float3 diffuse = finalColor.rgb * mainLight.color * NdotL;
                
                // Add ambient light from SH
                float3 ambient = SampleSH(normal) * finalColor.rgb;

                // Simple specular reflection
                float3 viewDir = normalize(GetCameraPositionWS() - input.positionWS);
                float3 halfDir = normalize(mainLight.direction + viewDir);
                float NdotH = saturate(dot(normal, halfDir));
                float specular = pow(NdotH, _Glossiness * 128.0) * _Glossiness;
                float3 specColor = _SpecularColor.rgb * mainLight.color * specular;

                float3 finalRGB = diffuse + ambient + specColor;

                return float4(finalRGB, alpha);
            }
            ENDHLSL
        }
    }
}
