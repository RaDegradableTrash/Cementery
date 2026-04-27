Shader "Hidden/URPSilhouetteOutline"
{
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" }
        Pass
        {
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            ZTest Always
            Cull Off

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

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

            TEXTURE2D(_SilhouetteMask);
            SAMPLER(sampler_SilhouetteMask);
            float4 _SilhouetteMask_TexelSize;
            
            float4 _OutlineColor;
            float _OutlineWidth;

            Varyings vert(Attributes input)
            {
                Varyings output;
                // Draw a fullscreen quad (assuming input vertices are already in clip space [-1, 1])
                output.positionCS = float4(input.positionOS.xy, 0.0, 1.0);
                output.uv = input.uv;
                return output;
            }

            float4 frag(Varyings input) : SV_Target
            {
                float2 uv = input.uv;
                float center = SAMPLE_TEXTURE2D(_SilhouetteMask, sampler_SilhouetteMask, uv).r;
                
                // Distance in pixels
                float dx = _SilhouetteMask_TexelSize.x * _OutlineWidth;
                float dy = _SilhouetteMask_TexelSize.y * _OutlineWidth;
                
                // 8-tap sample to find maximum intensity in neighborhood
                float t  = SAMPLE_TEXTURE2D(_SilhouetteMask, sampler_SilhouetteMask, uv + float2(0, dy)).r;
                float b  = SAMPLE_TEXTURE2D(_SilhouetteMask, sampler_SilhouetteMask, uv + float2(0, -dy)).r;
                float l  = SAMPLE_TEXTURE2D(_SilhouetteMask, sampler_SilhouetteMask, uv + float2(-dx, 0)).r;
                float r  = SAMPLE_TEXTURE2D(_SilhouetteMask, sampler_SilhouetteMask, uv + float2(dx, 0)).r;
                float tl = SAMPLE_TEXTURE2D(_SilhouetteMask, sampler_SilhouetteMask, uv + float2(-dx, dy)).r;
                float tr = SAMPLE_TEXTURE2D(_SilhouetteMask, sampler_SilhouetteMask, uv + float2(dx, dy)).r;
                float bl = SAMPLE_TEXTURE2D(_SilhouetteMask, sampler_SilhouetteMask, uv + float2(-dx, -dy)).r;
                float br = SAMPLE_TEXTURE2D(_SilhouetteMask, sampler_SilhouetteMask, uv + float2(dx, -dy)).r;

                float maxCross = max(max(t, b), max(l, r));
                float maxDiag = max(max(tl, tr), max(bl, br));
                float maxMask = max(maxCross, maxDiag);
                
                // Outline is strictly outside the mask
                float border = saturate(maxMask - center);

                if (border > 0.01)
                {
                    return float4(_OutlineColor.rgb, border * _OutlineColor.a);
                }
                
                return float4(0, 0, 0, 0); // Transparent where no outline exists
            }
            ENDHLSL
        }
    }
}
