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
            sampler2D _OcclusionMap;
            float4 _BrushParams, _BrushStrength, _SnowMapParams;

            float pseudoNoise(float2 uv) {
                return frac(sin(dot(uv, float2(12.9898, 78.233))) * 43758.5453);
            }

            fixed4 frag (v2f i) : SV_Target {
                float current = tex2D(_MainTex, i.uv).r;
                float occlusion = tex2D(_OcclusionMap, i.uv).r; // 1 = can accumulate, 0 = blocked
                float2 worldPos = (i.uv - 0.5) * _SnowMapParams.z + _SnowMapParams.xy;
                float dist = distance(worldPos, _BrushParams.xz);
                
                // Use a perfect smooth circle without noise to prevent high-frequency spikes
                float warpedRadius = _BrushParams.w; 
                
                float t = saturate(1.0 - (dist / warpedRadius));
                // Use a smoothstep blended with noise for muddy, irregular accumulation
                float brush = smoothstep(0, 1, pow(t, 2.5)) * _BrushStrength.x * occlusion;
                
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
        Pass { // Pass 2: 车底遮挡投影 (生成 Occlusion Map)
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"
            struct v2f { float2 uv : TEXCOORD0; float4 vertex : SV_POSITION; };
            v2f vert(appdata_base v) { v2f o; o.vertex = UnityObjectToClipPos(v.vertex); o.uv = v.texcoord; return o; }
            float4 _CarParams, _CarParamsForward, _SnowMapParams; // pos, width | forward, length
            
            float4 frag(v2f i) : SV_Target
            {
                float2 worldPos = (i.uv - 0.5) * _SnowMapParams.z + _SnowMapParams.xy;
                
                // Calculate local position relative to car
                float2 toPixel = worldPos - _CarParams.xz;
                float2 forward2D = normalize(_CarParamsForward.xz);
                float2 right2D = float2(forward2D.y, -forward2D.x);
                
                // Project onto car's local axes
                float localX = abs(dot(toPixel, right2D));
                float localZ = abs(dot(toPixel, forward2D));
                
                // Box SDF
                float boxX = max(0.0, localX - _CarParams.w);
                float boxZ = max(0.0, localZ - _CarParamsForward.w);
                float dist = length(float2(boxX, boxZ));
                
                // Extremely soft transition to avoid any jagged edges!
                float mask = smoothstep(0.0, 1.5, dist);
                
                return float4(mask, mask, mask, 1.0);
            }
            ENDCG
        }
        Pass { // Pass 3: Global Accumulation
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"
            struct v2f { float2 uv : TEXCOORD0; float4 vertex : SV_POSITION; };
            v2f vert(appdata_base v) { v2f o; o.vertex = UnityObjectToClipPos(v.vertex); o.uv = v.texcoord; return o; }
            sampler2D _MainTex;
            sampler2D _OcclusionMap;
            float4 _BrushStrength;
            fixed4 frag (v2f i) : SV_Target {
                float current = tex2D(_MainTex, i.uv).r;
                float occlusion = tex2D(_OcclusionMap, i.uv).r;
                return saturate(current + _BrushStrength.x * occlusion * unity_DeltaTime.x);
            }
            ENDCG
        }
    }
}
