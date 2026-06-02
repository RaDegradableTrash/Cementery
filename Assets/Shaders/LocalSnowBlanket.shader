Shader "Environment/LocalSnowBlanket"
{
    Properties
    {
        _Cutoff ("Snow Cutoff", Range(0, 1)) = 0.1
        _DisplacementScale ("Displacement Scale", Float) = 0.2
        _NormalBlend ("Normal Blend", Range(0, 1)) = 0.8
        _SnowColor ("Snow Color", Color) = (0.95, 0.98, 1.0, 1.0)
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
        Offset -1, -1

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }
            Offset -1, -1

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
                float3 positionOS : TEXCOORD3;
            };

            float _Cutoff;
            float _NormalBlend;
            float4 _SnowColor;
            float _DisplacementScale;
            float _ShadowSoftness;
            float _LambertSoftness;
            float _AmbientBoost;
            float4 _ShadowTint;
            
            float4 _LocalSnowBounds; // x: minX, y: minZ, z: lengthX, w: lengthZ
            float4x4 _RootWorldToLocal;
            Texture2D _LocalSnowHeightMap;
            SamplerState sampler_LocalSnowHeightMap;

            void GetLocalSnowData(float3 positionWS, out float rawH, out float pillowH, out float3 pixelNormal)
            {
                float4 rootLocalPos = mul(_RootWorldToLocal, float4(positionWS, 1.0));
                
                float u = (rootLocalPos.x - _LocalSnowBounds.x) / _LocalSnowBounds.z;
                float v = (rootLocalPos.z - _LocalSnowBounds.y) / _LocalSnowBounds.w;
                
                if (u < 0 || u > 1 || v < 0 || v > 1) 
                {
                    rawH = 0; pillowH = 0; pixelNormal = float3(0,0,0);
                    return;
                }
                
                float2 snowData = _LocalSnowHeightMap.SampleLevel(sampler_LocalSnowHeightMap, float2(u, v), 0).rg;
                rawH = snowData.r;
                float hitY = snowData.g;
                
                // 如果当前像素没有直接的粒子撞击记录，但周围邻域有积雪（发生柔和溢出过渡），
                // 我们必须借用邻域里最接近的有效 hitY 写入作为遮挡依据，否则未初始化高度 (-1000) 会放行所有底部和内部天花板顶点！
                if (hitY < -500.0)
                {
                    float deltaH = 0.015;
                    float gR = _LocalSnowHeightMap.SampleLevel(sampler_LocalSnowHeightMap, float2(u + deltaH, v), 0).g;
                    float gL = _LocalSnowHeightMap.SampleLevel(sampler_LocalSnowHeightMap, float2(u - deltaH, v), 0).g;
                    float gU = _LocalSnowHeightMap.SampleLevel(sampler_LocalSnowHeightMap, float2(u, v + deltaH), 0).g;
                    float gD = _LocalSnowHeightMap.SampleLevel(sampler_LocalSnowHeightMap, float2(u, v - deltaH), 0).g;
                    
                    if (gR > -500.0) hitY = gR;
                    else if (gL > -500.0) hitY = gL;
                    else if (gU > -500.0) hitY = gU;
                    else if (gD > -500.0) hitY = gD;
                }
                
                // 极度严格的垂直差值遮挡（限 2 厘米内），彻底杜绝室内天花板和侧壁在未初始化边界的漏色！
                if (rootLocalPos.y < hitY - 0.02) 
                {
                    rawH = 0; pillowH = 0; pixelNormal = float3(0,0,0);
                    return;
                }
                
                // 核心：使用 smoothstep 替代含有无限大导数的 pow(..., 0.25)，彻底抹平边缘法线极化！
                // 核心：使用 smoothstep 且较平缓的过渡曲线
                float visibleH = saturate(rawH);
                pillowH = smoothstep(0.0, 1.0, visibleH); 
                
                // 增加采样步长以进一步软化和抹平法线阶梯
                float spread = 0.01;
                float rawR = _LocalSnowHeightMap.SampleLevel(sampler_LocalSnowHeightMap, float2(u + spread, v), 0).r;
                float rawL = _LocalSnowHeightMap.SampleLevel(sampler_LocalSnowHeightMap, float2(u - spread, v), 0).r;
                float rawU = _LocalSnowHeightMap.SampleLevel(sampler_LocalSnowHeightMap, float2(u, v + spread), 0).r;
                float rawD = _LocalSnowHeightMap.SampleLevel(sampler_LocalSnowHeightMap, float2(u, v - spread), 0).r;
                
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
                
                // 极度严格的平坦朝上坡度遮罩（0.75 - 0.95）：排除车顶边缘微小弧度的圆角/斜坡，完全锁死任何垂直面拉伸位移！
                float upDot = dot(normalize(normalWS), float3(0, 1, 0));
                float slopeMask = smoothstep(0.75, 0.95, upDot);
                
                float rawH, pillowH;
                float3 dummyNormal;
                GetLocalSnowData(positionWS, rawH, pillowH, dummyNormal);
                
                rawH *= slopeMask;
                
                // 核心：无硬悬崖，融合 FBM 噪声使之呈现极度饱满且有机的起伏结构！
                float fade = saturate(rawH / 0.15);
                float noiseMound = fbm2D(positionWS.xz * 1.2) * 0.06; // 6cm 级有机噪声起伏
                positionWS += normalWS * ((0.01 + pillowH * _DisplacementScale + noiseMound) * fade);
                
                output.positionCS = TransformWorldToHClip(positionWS);
                output.positionWS = positionWS;
                output.normalWS = normalWS;
                
                VertexPositionInputs vertexInput = GetVertexPositionInputs(input.positionOS.xyz);
                output.shadowCoord = GetShadowCoord(vertexInput);
                
                return output;
            }

            float4 frag(Varyings input) : SV_Target
            {
                // 【绝对防御】：只有当法线朝上超过 0.85 时，才计算雪，彻底杀死垂直面（如车门、墙壁）上的所有积雪计算
                float3 normalWS = normalize(input.normalWS);
                if (dot(normalWS, float3(0, 1, 0)) < 0.85) 
                {
                    discard;
                    return float4(0,0,0,0);
                }
                
                float upDot = dot(normalize(input.normalWS), float3(0, 1, 0));
                // 极度严格的朝上坡度遮罩：只允许近乎完美水平的顶面（upDot > 0.75）进行渲染，彻底切断边缘向下溢出！
                float slopeMask = smoothstep(0.75, 0.95, upDot);
                
                float rawH, pillowH;
                float3 pixelNormal;
                GetLocalSnowData(input.positionWS, rawH, pillowH, pixelNormal);
                rawH *= slopeMask;
                
                // 核心：无硬裁切线！为了让所有雪地边缘（包含高度图硬陡降的区域）都拥有柔和、平滑模糊的过渡边缘，
                // 我们在计算 Alpha 时对高度图进行 4 邻域平滑模糊采样！
                float4 rootLocalPos = mul(_RootWorldToLocal, float4(input.positionWS, 1.0));
                float u = (rootLocalPos.x - _LocalSnowBounds.x) / _LocalSnowBounds.z;
                float v = (rootLocalPos.z - _LocalSnowBounds.y) / _LocalSnowBounds.w;
                
                float spreadH = 0.012; // 局部高度图平滑范围
                float hC = rawH;
                float hR = _LocalSnowHeightMap.SampleLevel(sampler_LocalSnowHeightMap, float2(u + spreadH, v), 0).r;
                float hL = _LocalSnowHeightMap.SampleLevel(sampler_LocalSnowHeightMap, float2(u - spreadH, v), 0).r;
                float hU = _LocalSnowHeightMap.SampleLevel(sampler_LocalSnowHeightMap, float2(u, v + spreadH), 0).r;
                float hD = _LocalSnowHeightMap.SampleLevel(sampler_LocalSnowHeightMap, float2(u, v - spreadH), 0).r;
                
                if (u < 0.0 || u > 1.0 || v < 0.0 || v > 1.0)
                {
                    hR = 0; hL = 0; hU = 0; hD = 0;
                }
                
                float blurredH = (hC + hR + hL + hU + hD) * 0.2;
                float alpha = saturate(blurredH / 0.18);
                
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

                float4 shadowCoord = TransformWorldToShadowCoord(input.positionWS);
                Light mainLight = GetMainLight(shadowCoord);
                
                // 2. 真实光照：采用半兰伯特漫反射模型（支持软化阴影），大幅降低自阴影灰度，模拟雪地次表面散射
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
