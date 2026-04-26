Shader "Custom/URPPlacementGhost"
{
    Properties
    {
        _BaseColor("Base Color", Color) = (0, 1, 0, 0.2)
        _ContactColor("Contact Color", Color) = (0, 1, 0, 0.8)
        _PlaneNormal("Plane Normal", Vector) = (0, 1, 0, 0)
        _PlanePoint("Plane Point", Vector) = (0, 0, 0, 0)
        _FadeDistance("Fade Distance", Float) = 0.25
    }
    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent" "RenderPipeline" = "UniversalPipeline" }
        LOD 100

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }
            
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull Back

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes
            {
                float4 positionOS   : POSITION;
            };

            struct Varyings
            {
                float4 positionHCS  : SV_POSITION;
                float3 positionWS   : TEXCOORD0;
            };

            CBUFFER_START(UnityPerMaterial)
                half4 _BaseColor;
                half4 _ContactColor;
                float4 _PlaneNormal;
                float4 _PlanePoint;
                float _FadeDistance;
            CBUFFER_END

            Varyings vert(Attributes v)
            {
                Varyings o;
                o.positionWS = TransformObjectToWorld(v.positionOS.xyz);
                o.positionHCS = TransformWorldToHClip(o.positionWS);
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                float dist = abs(dot(_PlaneNormal.xyz, i.positionWS - _PlanePoint.xyz));
                float t = saturate(dist / _FadeDistance);
                half4 col = lerp(_ContactColor, _BaseColor, t);
                return col;
            }
            ENDHLSL
        }
    }
}
