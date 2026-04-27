Shader "Hidden/URPSilhouetteMask"
{
    SubShader
    {
        Tags { "RenderType"="Opaque" "RenderPipeline"="UniversalPipeline" }
        Pass
        {
            ZWrite Off
            ZTest Always
            Cull Off

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            float4x4 _CustomVP;
            float4 _MaskColor;

            float4 vert(float4 vertex : POSITION) : SV_POSITION
            {
                float4 worldPos = mul(unity_ObjectToWorld, vertex);
                return mul(_CustomVP, worldPos);
            }

            float4 frag() : SV_Target
            {
                return _MaskColor;
            }
            ENDHLSL
        }
    }
}
