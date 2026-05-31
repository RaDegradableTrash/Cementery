Shader "Hidden/SnowModification"
{
    Properties { _MainTex ("Texture", 2D) = "white" {} }
    SubShader
    {
        Cull Off ZWrite Off ZTest Always
        Pass { // Pass 0: 写入 (更柔和的 falloff)
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"
            struct v2f { float2 uv : TEXCOORD0; float4 vertex : SV_POSITION; };
            v2f vert(appdata_base v) { v2f o; o.vertex = UnityObjectToClipPos(v.vertex); o.uv = v.texcoord; return o; }
            sampler2D _MainTex;
            float4 _BrushParams, _BrushStrength, _SnowMapParams;

            float pseudoNoise(float2 uv) {
                return frac(sin(dot(uv, float2(12.9898, 78.233))) * 43758.5453);
            }

            fixed4 frag (v2f i) : SV_Target {
                float current = tex2D(_MainTex, i.uv).r;
                float2 worldPos = (i.uv - 0.5) * _SnowMapParams.z + _SnowMapParams.xy;
                float dist = distance(worldPos, _BrushParams.xz);
                
                // Use a perfect smooth circle without noise to prevent high-frequency spikes
                float warpedRadius = _BrushParams.w; 
                
                float t = saturate(1.0 - (dist / warpedRadius));
                // Use a smoothstep blended with noise for muddy, irregular accumulation
                float brush = smoothstep(0, 1, pow(t, 2.5)) * _BrushStrength.x;
                
                return saturate(current + brush);
            }
            ENDCG
        }
        Pass { // Pass 1: 强力高斯模糊 (平滑化所有馒头)
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"
            struct v2f { float2 uv : TEXCOORD0; float4 vertex : SV_POSITION; };
            v2f vert(appdata_base v) { v2f o; o.vertex = UnityObjectToClipPos(v.vertex); o.uv = v.texcoord; return o; }
            sampler2D _MainTex; float4 _MainTex_TexelSize;
            fixed4 frag (v2f i) : SV_Target {
                float4 sum = 0;
                for(int x=-2; x<=2; x++) 
                    for(int y=-2; y<=2; y++)
                        sum += tex2D(_MainTex, i.uv + float2(x, y) * _MainTex_TexelSize.xy);
                return sum / 25.0;
            }
            ENDCG
        }
    }
}
