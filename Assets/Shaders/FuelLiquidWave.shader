Shader "Custom/FuelLiquidWave"
{
    Properties
    {
        _Color ("Liquid Color", Color) = (0, 0.8, 1, 1)
        _FillAmount ("Fill Amount", Range(0, 1)) = 0.5
        _WaveSpeed ("Wave Speed", Float) = 4.0
        _WaveAmplitude ("Wave Amplitude", Float) = 0.03
        _WaveFrequency ("Wave Frequency", Float) = 15.0
        _Alpha ("Global Alpha", Range(0, 1)) = 1.0
    }
    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent" "RenderPipeline"="UniversalPipeline" }
        LOD 100

        Blend SrcAlpha OneMinusSrcAlpha
        ZWrite Off
        Cull Off

        Pass
        {
            Name "ForwardLit"
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes
            {
                float4 positionOS   : POSITION;
                float2 uv           : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS   : SV_POSITION;
                float2 uv           : TEXCOORD0;
            };

            float4 _Color;
            float _FillAmount;
            float _WaveSpeed;
            float _WaveAmplitude;
            float _WaveFrequency;
            float _Alpha;

            Varyings vert(Attributes input)
            {
                Varyings output;
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.uv = input.uv;
                return output;
            }

            half4 frag(Varyings input) : SV_Target
            {
                // Calculate dynamic sine wave based on horizontal UV and time
                float wave = sin(input.uv.x * _WaveFrequency + _Time.y * _WaveSpeed) * _WaveAmplitude;
                
                // Effective fill height is the fill amount shifted by the wave displacement
                float fillHeight = _FillAmount + wave;

                // Discard pixels above the wave height to create the liquid cut-off
                if (input.uv.y > fillHeight)
                {
                    discard;
                }

                // Smooth out the top edge slightly for anti-aliasing
                float edgeDist = fillHeight - input.uv.y;
                float edgeAlpha = smoothstep(0.0, 0.005, edgeDist);

                half4 finalColor = _Color;
                finalColor.a *= _Alpha * edgeAlpha;

                return finalColor;
            }
            ENDHLSL
        }
    }
    FallBack "Hidden/Universal Render Pipeline/FallbackError"
}
