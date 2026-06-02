Shader "Environment/SnowBlanket"
{
    Properties
    {
        _Cutoff ("Snow Cutoff", Range(0, 1)) = 0.1
        _DisplacementScale ("Displacement Scale", Float) = 0.2
        _NormalBlend ("Normal Blend", Range(0, 1)) = 0.8
        _SnowColor ("Snow Color", Color) = (0.95, 0.98, 1.0, 1.0)
        _SnowDebugGreen ("Debug Green", Float) = 0
        _ShadowSoftness ("Shadow Softness", Range(0, 1)) = 0.6
        _LambertSoftness ("Lambert Softness", Range(0, 1)) = 0.5
        _AmbientBoost ("Ambient Boost", Range(0, 3)) = 1.6
        _ShadowTint ("Shadow Tint (Sky Blue)", Color) = (0.82, 0.92, 1.0, 1.0)
    }
    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Geometry+50" "RenderPipeline"="UniversalPipeline" }
        LOD 200
        
        ZWrite Off
        Blend SrcAlpha OneMinusSrcAlpha
        
        // Depth offset to eliminate low-poly Z-fighting natively without vertex displacement!
        Offset -1, -1

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.5
            
            // Required for URP shadow receiving
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS_CASCADE
            #pragma multi_compile _ _SHADOWS_SOFT

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 normalWS : TEXCOORD1;
                float4 shadowCoord : TEXCOORD2;
            };

            float _DisplacementScale;
            float _Cutoff;
            float _NormalBlend;
            float _SnowDebugGreen;
            float4 _SnowColor;
            float _ShadowSoftness;
            float _LambertSoftness;
            float _AmbientBoost;
            float4 _ShadowTint;
            
            float4 _GlobalSnowMapParams; // x: minX, y: minZ, z: size, w: 1/size
            TEXTURE2D(_GlobalSnowHeightMap);

            float GetRawH(float2 uv) 
            {
                // 绝对防御边界：如果超出 0-1 范围，直接返回 0 (无雪)
                if (uv.x < 0.0 || uv.x > 1.0 || uv.y < 0.0 || uv.y > 1.0) return 0.0;
                float rawH = SAMPLE_TEXTURE2D_LOD(_GlobalSnowHeightMap, sampler_LinearClamp, uv, 0).r;
                
                float halfSize = _GlobalSnowMapParams.z * 0.5;
                float2 worldPosXZ = float2(
                    (uv.x / _GlobalSnowMapParams.w) + _GlobalSnowMapParams.x - halfSize,
                    (uv.y / _GlobalSnowMapParams.w) + _GlobalSnowMapParams.y - halfSize
                );
                float dist = length(worldPosXZ - _GlobalSnowMapParams.xy) / halfSize;
                // 将淡出范围从 0.8-1.0 扩大到 0.2-1.0，使 100米大地图边界淡出极其平缓（跨度达40米），彻底消除视觉硬边缘折线！
                rawH *= 1.0 - smoothstep(0.2, 1.0, dist);
                return rawH;
            }

            void GetSnowData(float3 positionWS, out float rawH, out float pillowH, out float3 pixelNormal)
            {
                float halfSize = _GlobalSnowMapParams.z * 0.5;
                float u = (positionWS.x - _GlobalSnowMapParams.x + halfSize) * _GlobalSnowMapParams.w;
                float v = (positionWS.z - _GlobalSnowMapParams.y + halfSize) * _GlobalSnowMapParams.w;
                
                rawH = GetRawH(float2(u, v));
                
                // 核心：去除硬 Cutoff，使用渐进式厚度曲线
                float visibleH = saturate(rawH);
                pillowH = smoothstep(0.0, 1.0, visibleH); 
                
                // 增加采样步长以软化法线
                float spread = _GlobalSnowMapParams.w * 4.0;
                float rawR = GetRawH(float2(u + spread, v));
                float rawL = GetRawH(float2(u - spread, v));
                float rawU = GetRawH(float2(u, v + spread));
                float rawD = GetRawH(float2(u, v - spread));
                
                float pR = smoothstep(0.0, 1.0, saturate(rawR));
                float pL = smoothstep(0.0, 1.0, saturate(rawL));
                float pU = smoothstep(0.0, 1.0, saturate(rawU));
                float pD = smoothstep(0.0, 1.0, saturate(rawD));
                
                // Sobel 算子平滑，彻底消除阶梯圆圈
                float dHdX = (pR - pL) * 2.0;
                float dHdZ = (pU - pD) * 2.0;
                
                pixelNormal = float3(-dHdX * _DisplacementScale, 0.0, -dHdZ * _DisplacementScale);
            }

            float hash2D(float2 p) 
            { 
                return frac(sin(dot(p, float2(127.1, 311.7))) * 43758.5453123); 
            }
            
            float noise2D(float2 p)
            {
                float2 i = floor(p);
                float2 f = frac(p);
                float2 u = f * f * (3.0 - 2.0 * f);
                return lerp(lerp(hash2D(i + float2(0.0,0.0)), hash2D(i + float2(1.0,0.0)), u.x),
                            lerp(hash2D(i + float2(0.0,1.0)), hash2D(i + float2(1.0,1.0)), u.x), u.y);
            }
            
            float fbm2D(float2 p)
            {
                float v = 0.0;
                float a = 0.5;
                float2 shift = float2(100.0, 100.0);
                float2x2 rot = float2x2(0.8, 0.6, -0.6, 0.8);
                for (int i = 0; i < 3; ++i) {
                    v += a * noise2D(p);
                    p = mul(rot, p) * 2.0 + shift;
                    a *= 0.5;
                }
                return v;
            }

            Varyings vert(Attributes input)
            {
                Varyings output;
                float3 positionWS = TransformObjectToWorld(input.positionOS.xyz);
                float3 normalWS = TransformObjectToWorldNormal(input.normalOS);
                
                // 核心：在顶点阶段计算朝上坡度遮罩，使用紧凑过渡 (0.51-0.56) 确保沙丘斜坡顶点能满额拉伸，同时消除垂直侧面拉伸！
                float upDot = dot(normalize(normalWS), float3(0, 1, 0));
                float slopeMask = smoothstep(0.51, 0.56, upDot);
                
                float rawH, pillowH;
                float3 dummyNormal;
                GetSnowData(positionWS, rawH, pillowH, dummyNormal);
                
                rawH *= slopeMask;
                
                // 核心：无硬 Cutoff 悬崖！且融入云层噪声 (FBM)，使得雪地呈现极具体积感的饱满雪堆起伏！
                float fade = saturate(rawH / 0.15);
                float noiseMound = fbm2D(positionWS.xz * 1.2) * 0.06; // 6cm 级有机噪声起伏
                positionWS += normalWS * ((0.08 + pillowH * _DisplacementScale + noiseMound) * fade);
                
                output.positionCS = TransformWorldToHClip(positionWS);
                output.positionWS = positionWS;
                output.normalWS = normalWS;
                
                VertexPositionInputs vertexInput = GetVertexPositionInputs(input.positionOS.xyz);
                output.shadowCoord = GetShadowCoord(vertexInput);
                
                return output;
            }

            float4 frag(Varyings input) : SV_Target
            {
                // 【绝对防御】：只有当法线朝上超过 0.55 (即斜度小于约 56 度) 时，才计算雪，
                // 这样能完美容纳沙丘的波浪形陡坡积雪（防止低模产生波浪形无雪带），同时依然彻底杀死垂直峭壁面上的积雪计算
                float3 baseNormalInput = normalize(input.normalWS);
                if (dot(baseNormalInput, float3(0, 1, 0)) < 0.55) 
                {
                    discard;
                    return float4(0,0,0,0);
                }
                
                float rawH, pillowH;
                float3 pixelNormal;
                GetSnowData(input.positionWS, rawH, pillowH, pixelNormal);
                
                // 核心：无硬裁切线！为了让所有雪地边缘（包含高度图硬陡降的区域）都拥有柔和、平滑模糊的过渡边缘，
                // 我们在计算 Alpha 时对高度图进行 4 邻域平滑模糊采样！
                float halfSize = _GlobalSnowMapParams.z * 0.5;
                float u = (input.positionWS.x - _GlobalSnowMapParams.x + halfSize) * _GlobalSnowMapParams.w;
                float v = (input.positionWS.z - _GlobalSnowMapParams.y + halfSize) * _GlobalSnowMapParams.w;
                
                float spreadH = _GlobalSnowMapParams.w * 3.0; // 3像素宽度平滑范围 (约合大地图30-50厘米)
                float hC = rawH;
                float hR = GetRawH(float2(u + spreadH, v));
                float hL = GetRawH(float2(u - spreadH, v));
                float hU = GetRawH(float2(u, v + spreadH));
                float hD = GetRawH(float2(u, v - spreadH));
                
                float blurredH = (hC + hR + hL + hU + hD) * 0.2;
                float alpha = saturate(blurredH / 0.18); // 柔和地渐变淡出
                
                // 1. 混合高度图法线与高精微观 FBM 噪声法线，模拟蓬松雪堆表面的细密微凹凸，捕捉完美光影细节！
                float3 baseNormal = normalize(input.normalWS);
                
                float2 noiseUV = input.positionWS.xz * 4.0;
                float deltaE = 0.03;
                float nL = fbm2D(noiseUV + float2(-deltaE, 0));
                float nR = fbm2D(noiseUV + float2(deltaE, 0));
                float nD = fbm2D(noiseUV + float2(0, -deltaE));
                float nU = fbm2D(noiseUV + float2(0, deltaE));
                float3 microNormal = normalize(float3(nL - nR, 0.15, nD - nU));
                
                float3 normal = normalize(baseNormal + pixelNormal + microNormal * 0.35);
 
                // 核心修复：引入垂直度遮罩 (Verticality Mask)
                // 使用紧凑的 smoothstep (0.51-0.56) 确保沙丘斜面处的积雪不发生半透明淡出，保持 100% 满额积雪厚度与不透明度！
                float upDot = dot(normal, float3(0, 1, 0));
                float verticality = smoothstep(0.51, 0.56, upDot);
                alpha *= verticality;

                float4 shadowCoord = TransformWorldToShadowCoord(input.positionWS);
                Light mainLight = GetMainLight(shadowCoord);
                
                // 2. 真实光照：采用半兰伯特漫反射模型（支持软化阴影），大幅降低自阴影灰度
                float NdotL = dot(normal, mainLight.direction);
                float diffuseLight = saturate(NdotL * (1.0 - _LambertSoftness) + _LambertSoftness);
                
                // 降低投射阴影（动态阴影）乘区的权重，避免车身等动态阴影在白雪上过黑过灰
                float castShadow = lerp(_ShadowSoftness, 1.0, mainLight.shadowAttenuation);
                
                // Directional diffuse (对漫反射进行提亮，让雪地告别灰暗)
                float3 lightColor = mainLight.color;
                float3 diffuse = (_SnowColor.rgb * 1.25) * lightColor * diffuseLight * castShadow;
                
                // 3. 真实环境光：采用标准 SampleSH 采样，自然承接大地图的昼夜光线强弱
                float3 ambient = SampleSH(normal) * _SnowColor.rgb;
                
                // 4. 阴影染色与提亮（自然乘区）：
                // 我们在环境光 (ambient) 上叠加天蓝色阴影 tint 和 boost，这样阴影的亮度会完美与环境光强弱同步，夜晚绝不发光！
                float shadowMask = saturate(1.0 - (saturate(NdotL) * mainLight.shadowAttenuation));
                float3 tintedAmbient = ambient * lerp(float3(1.0, 1.0, 1.0), _ShadowTint.rgb * _AmbientBoost * 1.3, shadowMask * 0.85);
                
                // 5. 蓬松雪晶微弱反光/闪烁效果 (Sparkle)
                float sparkle = saturate(pow(hash2D(input.positionWS.xz * 150.0), 12.0) * 4.0) * castShadow;
                
                float3 finalColor = diffuse + tintedAmbient + sparkle * mainLight.color * 0.25;
                
                return float4(finalColor, alpha);
            }
            ENDHLSL
        }
    }
}
