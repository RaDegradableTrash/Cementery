Shader "Hidden/LocalSnowModification"
{
    Properties { _MainTex ("Texture", 2D) = "white" {} }
    SubShader
    {
        Cull Off ZWrite Off ZTest Always
        Pass {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"
            struct v2f { float2 uv : TEXCOORD0; float4 vertex : SV_POSITION; };
            v2f vert(appdata_base v) { v2f o; o.vertex = UnityObjectToClipPos(v.vertex); o.uv = v.texcoord; return o; }
            sampler2D _MainTex;
            
            float4 _BrushParams; // x: u, y: v, z: radiusU, w: radiusV
            float4 _BrushStrength; // x: amount
            
            fixed4 frag (v2f i) : SV_Target {
                float2 current = tex2D(_MainTex, i.uv).rg;
                
                // Calculate distance in UV space, but normalized to make it a perfect circle
                float2 toBrush = i.uv - _BrushParams.xy;
                toBrush.x /= _BrushParams.z;
                toBrush.y /= _BrushParams.w;
                float dist = length(toBrush);
                
                float t = saturate(1.0 - dist);
                float brush = smoothstep(0, 1, pow(t, 2.5)) * _BrushStrength.x;
                
                float newSnow = saturate(current.r + brush);
                float currentY = current.g;
                float newY = (brush > 0.01) ? max(currentY, _BrushStrength.y) : currentY;
                
                return float4(newSnow, newY, 0, 1);
            }
            ENDCG
        }
    }
}
