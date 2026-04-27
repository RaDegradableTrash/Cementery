Shader "Custom/URPOutline"
{
    Properties
    {
        _OutlineColor ("Outline Color", Color) = (1, 1, 0.5, 1) // Default soft yellow
        _OutlineWidth ("Outline Width", Range(0.001, 0.1)) = 0.005
    }

    SubShader
    {
        Tags { "RenderType"="Opaque" "RenderPipeline"="UniversalPipeline" }
        LOD 100

        Pass
        {
            Name "Outline"
            Tags { "LightMode"="UniversalForward" }
            
            Cull Front
            ZWrite On
            ZTest LEqual

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes
            {
                float4 positionOS   : POSITION;
                float3 normalOS     : NORMAL;
            };

            struct Varyings
            {
                float4 positionCS   : SV_POSITION;
            };

            CBUFFER_START(UnityPerMaterial)
                half4 _OutlineColor;
                float _OutlineWidth;
            CBUFFER_END

            Varyings vert(Attributes input)
            {
                Varyings output;
                
                // Expand vertex position along its normal
                float3 expandedPos = input.positionOS.xyz + input.normalOS * _OutlineWidth;
                output.positionCS = TransformObjectToHClip(expandedPos);
                
                return output;
            }

            half4 frag(Varyings input) : SV_Target
            {
                return _OutlineColor;
            }
            ENDHLSL
        }
    }
}
