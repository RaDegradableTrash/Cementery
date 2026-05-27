Shader "Hidden/Universal Render Pipeline/ScreenSpaceGodrays"
{
    Properties
    {
        _MainTex ("Source Texture", 2D) = "white" {}
        _Threshold ("Threshold", Range(0, 1)) = 0.75
        _BlurWidth ("Blur Width", Range(0, 2)) = 0.85
        _Intensity ("Intensity", Float) = 1.5
        _RayColor ("Ray Color", Color) = (1, 0.9, 0.7, 1)
        _SunScreenPos ("Sun Screen Position", Vector) = (0.5, 0.5, 0, 0)
    }

    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" }
        ZTest Always ZWrite Off Cull Off

        // Pass 0: Sun & Cloud Mask
        Pass
        {
            Name "Godrays Mask"

            HLSLPROGRAM
            #pragma vertex FullscreenVert
            #pragma fragment FragMask

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"

            TEXTURE2D(_MainTex);
            SAMPLER(sampler_MainTex);

            float _Threshold;

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

            half4 FragMask(Varyings input) : SV_Target
            {
                float depth = SampleSceneDepth(input.uv);
                
                // Reversed-Z check for background/skybox occlusion
                bool isSky = false;
                #if UNITY_REVERSED_Z
                    if (depth < 0.0005) isSky = true;
                #else
                    if (depth > 0.9995) isSky = true;
                #endif

                half4 color = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, input.uv);
                
                // Solid geometry (terrain, buildings) completely blocks godrays
                if (!isSky)
                {
                    return half4(0, 0, 0, 0);
                }

                // Brightness extraction filter for glowing clouds and sun disk
                float brightness = dot(color.rgb, float3(0.299, 0.587, 0.114));
                float mask = saturate(brightness - _Threshold) / (1.0 - _Threshold + 0.0001);
                
                return color * mask;
            }
            ENDHLSL
        }

        // Pass 1: Radial Blur
        Pass
        {
            Name "Godrays Radial Blur"

            HLSLPROGRAM
            #pragma vertex FullscreenVert
            #pragma fragment FragBlur

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            TEXTURE2D(_MainTex);
            SAMPLER(sampler_MainTex);

            float4 _SunScreenPos;
            float _BlurWidth;
            int _Samples;

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

            half4 FragBlur(Varyings input) : SV_Target
            {
                float2 delta = _SunScreenPos.xy - input.uv;
                // Scale blur vector based on samples count
                float2 stepVec = delta * (_BlurWidth / (float)_Samples);
                
                half4 color = 0;
                float2 uv = input.uv;
                float illuminationDecay = 1.0;

                [unroll(16)]
                for (int i = 0; i < 16; i++)
                {
                    uv += stepVec;
                    half4 sampleCol = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, uv);
                    
                    color += sampleCol * illuminationDecay;
                    illuminationDecay *= 0.92; // Smooth attenuation along ray
                }

                return color / (float)_Samples;
            }
            ENDHLSL
        }

        // Pass 2: Additive Composite
        Pass
        {
            Name "Godrays Composite"
            Blend One One // Pure additive blending

            HLSLPROGRAM
            #pragma vertex FullscreenVert
            #pragma fragment FragComposite

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            TEXTURE2D(_MainTex);
            SAMPLER(sampler_MainTex);

            float _Intensity;
            float4 _RayColor;

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

            half4 FragComposite(Varyings input) : SV_Target
            {
                half4 blurCol = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, input.uv);
                return blurCol * _Intensity * _RayColor;
            }
            ENDHLSL
        }
    }
}
