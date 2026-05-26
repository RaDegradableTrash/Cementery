Shader "Environment/URPTriplanarEnvironment"
{
    Properties
    {
        _MainTex ("Albedo Texture", 2D) = "white" {}
        _NormalMap ("Normal Map", 2D) = "bump" {}
        _TriplanarScale ("Texture Scale", Float) = 1.0
        _BlendSharpness ("Blend Sharpness", Range(1, 20)) = 5.0
        _Color ("Tint Color", Color) = (1,1,1,1)
    }

    SubShader
    {
        Tags { "RenderType"="Opaque" "RenderPipeline"="UniversalPipeline" "Queue"="Geometry" }
        LOD 300

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode"="UniversalForward" }
            ZWrite On

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.0
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
                float displacement : TEXCOORD2;
            };

            TEXTURE2D(_MainTex);
            SAMPLER(sampler_MainTex);
            
            TEXTURE2D(_NormalMap);
            SAMPLER(sampler_NormalMap);

            CBUFFER_START(UnityPerMaterial)
                float _TriplanarScale;
                float _BlendSharpness;
                float4 _Color;
            CBUFFER_END

            // --- GLOBAL CLOUD SHADOW PROPERTIES & SAMPLERS ---
            TEXTURE3D(_BaseNoiseTex);
            SAMPLER(sampler_BaseNoiseTex);

            float _CloudMinHeight;
            float _CloudMaxHeight;
            float _CloudThreshold;
            float _BaseScale;
            float _VerticalStretch;
            float _ConvectiveWarp;
            float4 _BaseWindSpeed;

            // Calculates the cloud shadow factor at a given world position
            float GetCloudShadow(float3 posWS, float3 lightDir)
            {
                if (lightDir.y <= 0.05) return 1.0; // The sun is below the horizon, direct light is 0 anyway

                // Calculate intersection of ray with the cloud center plane
                float cloudCenterY = (_CloudMinHeight + _CloudMaxHeight) * 0.5;
                float t = (cloudCenterY - posWS.y) / lightDir.y;
                
                // If the intersection is in the past, no shadow
                if (t < 0.0) return 1.0;

                float3 cloudPos = posWS + lightDir * t;

                // Seamless tiling wind coordinates (matches VolumetricCloud.shader)
                float3 baseScaleVec = float3(_BaseScale, _BaseScale / _VerticalStretch, _BaseScale);
                float3 uvwBase = cloudPos * baseScaleVec + _BaseWindSpeed.xyz * _Time.y;

                // Sample the base Worley noise with lod 0 for safety in fragment shaders
                float baseNoise = SAMPLE_TEXTURE3D_LOD(_BaseNoiseTex, sampler_BaseNoiseTex, uvwBase, 0).r;

                // Sample low-frequency coverage map to match the clouds' macro grouping
                float3 uvwCoverage = cloudPos * (baseScaleVec * 0.2) + _BaseWindSpeed.xyz * _Time.y * 0.1;
                uvwCoverage.y = 0.0;
                float coverage = SAMPLE_TEXTURE3D_LOD(_BaseNoiseTex, sampler_BaseNoiseTex, uvwCoverage, 0).r;
                float coverageMask = smoothstep(0.22, 0.48, coverage);

                // Calculate local threshold (identical to VolumetricCloud.shader)
                float baseSpread = 0.5 * 0.36; // Mid-height convective spreading
                float localThreshold = _CloudThreshold + baseSpread + (1.0 - coverageMask) * 0.65;

                // If density is greater than threshold, it casts a shadow!
                float cloudDensity = saturate((baseNoise - localThreshold) * 4.5);

                // Return shadow attenuation factor (0.0 = full shadow, 1.0 = fully lit)
                // We leave a healthy ambient light bleed (e.g. 0.35 minimum) so shadows look soft and stylized
                return lerp(1.0, 0.35, cloudDensity);
            }

            // --- DYNAMIC INTERACTIVE DEFORMATION GLOBALS ---
            float4 _DeformerPositions[128]; // .xyz = world pos, .w = radius
            float4 _DeformerParams[128];    // .x = depth, .y = rimWidth, .z = rimHeight, .w = fade

            // Calculates the granular sand displacement at a given world position
            float GetSandDeformation(float3 posWS)
            {
                float totalDisp = 0.0;
                for (int i = 0; i < 128; i++)
                {
                    float radius = _DeformerPositions[i].w;
                    if (radius <= 0.01) continue; // Early-pruning: skip unallocated or inactive slots instantly!
                    
                    float3 defPos = _DeformerPositions[i].xyz;
                    float4 defParam = _DeformerParams[i]; // x: depth, y: rimWidth, z: rimHeight, w: fade
                    
                    float d = distance(posWS.xz, defPos.xz);
                    float fade = defParam.w;
                    if (fade > 0.0 && d < (radius + defParam.y))
                    {
                        float disp = 0.0;
                        if (d < radius)
                        {
                            // Main dent indentation (smooth rounded cosine cup)
                            float k = cos((d / radius) * 1.570796);
                            disp = -defParam.x * k * k;
                        }
                        else
                        {
                            // Pushed-out volumetric rim ridge (beautiful circular sand build-up around footprints)
                            float t = (d - radius) / defParam.y;
                            disp = defParam.z * sin(t * 3.1415926);
                        }
                        totalDisp += disp * fade;
                    }
                }
                return totalDisp;
            }

            Varyings vert(Attributes input)
            {
                Varyings output;
                float3 positionWS = TransformObjectToWorld(input.positionOS.xyz);
                
                // 1. Physically cave in vertices vertically to form realistic 3D footprints
                float disp = GetSandDeformation(positionWS);
                positionWS.y += disp;
                
                output.positionCS = TransformWorldToHClip(positionWS);
                output.positionWS = positionWS;
                
                // 2. Analytical Normal Reconstruction at the Vertex Shader level (800x faster than per-pixel!)
                float delta = 0.15;
                float hRight = GetSandDeformation(positionWS + float3(delta, 0, 0));
                float hUp = GetSandDeformation(positionWS + float3(0, 0, delta));
                
                float3 normalOffset = float3(
                    (disp - hRight) / delta,
                    0.0,
                    (disp - hUp) / delta
                );
                
                output.normalWS = normalize(TransformObjectToWorldNormal(input.normalOS) + normalOffset * 1.5);
                output.displacement = disp;
                return output;
            }

            // Simple triplanar sampling helper
            float4 SampleTriplanar(TEXTURE2D_PARAM(tex, samp), float3 posWS, float3 blendWeights, float scale)
            {
                float2 uvX = posWS.zy * scale;
                float2 uvY = posWS.xz * scale;
                float2 uvZ = posWS.xy * scale;

                float4 colX = SAMPLE_TEXTURE2D(tex, samp, uvX);
                float4 colY = SAMPLE_TEXTURE2D(tex, samp, uvY);
                float4 colZ = SAMPLE_TEXTURE2D(tex, samp, uvZ);

                return colX * blendWeights.x + colY * blendWeights.y + colZ * blendWeights.z;
            }

            float4 frag(Varyings input) : SV_Target
            {
                float3 normalWS = normalize(input.normalWS);
                float hCenter = input.displacement;

                // Calculate blend weights based on normal
                float3 blendWeights = pow(abs(normalWS), _BlendSharpness);
                blendWeights /= (blendWeights.x + blendWeights.y + blendWeights.z); // Normalize

                // Sample Albedo
                float4 albedo = SampleTriplanar(_MainTex, sampler_MainTex, input.positionWS, blendWeights, _TriplanarScale) * _Color;

                // 🌟 Compacted Sand Ambient Occlusion (脚印/轮胎痕迹压实暗部遮蔽)
                // Darkens the base albedo based on depth of indentation, making footprint channels beautifully and deeply visible!
                float footprintAO = saturate(1.0 + hCenter * 3.8); // hCenter is negative, so footprints get a dark, soft self-shadowing look!
                albedo.rgb *= footprintAO;

                // 3. High-quality Triplanar Normal Map Blending (Adds rich micro sand ripple texture!)
                float2 uvX = input.positionWS.zy * _TriplanarScale;
                float2 uvY = input.positionWS.xz * _TriplanarScale;
                float2 uvZ = input.positionWS.xy * _TriplanarScale;
                
                float3 nX = SAMPLE_TEXTURE2D(_NormalMap, sampler_NormalMap, uvX).rgb * 2.0 - 1.0;
                float3 nY = SAMPLE_TEXTURE2D(_NormalMap, sampler_NormalMap, uvY).rgb * 2.0 - 1.0;
                float3 nZ = SAMPLE_TEXTURE2D(_NormalMap, sampler_NormalMap, uvZ).rgb * 2.0 - 1.0;
                
                float3 triplanarNormal = normalize(
                    float3(0.0, nX.y, nX.x) * blendWeights.x + 
                    float3(nY.x, 0.0, nY.y) * blendWeights.y + 
                    float3(nZ.x, nZ.y, 0.0) * blendWeights.z
                );
                
                // Blend Triplanar normal map with our deformed normal
                normalWS = normalize(normalWS + triplanarNormal * 0.35);

                // Simple diffuse lighting
                Light mainLight = GetMainLight(TransformWorldToShadowCoord(input.positionWS));
                float NdotL = saturate(dot(normalWS, mainLight.direction));
                
                // --- JOURNEY STYLE CLEAN SOFT GRADIENT TINT ---
                // Blends a velvety warm sun-kissed gradient exactly like Journey's beautiful stylized look
                float3 baseWarmColor = lerp(float3(0.92, 0.68, 0.40), float3(0.96, 0.82, 0.58), NdotL);
                albedo.rgb = albedo.rgb * baseWarmColor;

                // Calculate dynamic cloud shadow factor
                float cloudShadow = GetCloudShadow(input.positionWS, mainLight.direction);

                float3 diffuse = mainLight.color * mainLight.distanceAttenuation * mainLight.shadowAttenuation * cloudShadow * NdotL;
                
                // Add ambient
                float3 ambient = SampleSH(normalWS);

                // 5. Specular Shading & Premium Shimmer Glints
                float3 viewDir = normalize(GetCameraPositionWS() - input.positionWS);
                float3 halfDir = normalize(mainLight.direction + viewDir);
                float NdotH = saturate(dot(normalWS, halfDir));

                // 🌟 First Layer: Velvety sand diffuse specular sheen (Journey-style warm polarization)
                float sandSpec = pow(NdotH, 12.0) * 0.12 * saturate(dot(mainLight.direction, normalWS));
                float3 specColor = mainLight.color * sandSpec * mainLight.distanceAttenuation * mainLight.shadowAttenuation * cloudShadow;

                // 🌟 Second Layer: Stylized Fresnel rim lighting (风之旅人标志性偏振边缘发光)
                float smoothFresnel = pow(1.0 - saturate(dot(normalWS, viewDir)), 4.0);
                float3 fresnelGlow = mainLight.color * smoothFresnel * 0.28 * saturate(dot(mainLight.direction, normalWS)) * mainLight.shadowAttenuation * cloudShadow;

                // 🌟 Third Layer: Stylized Golden needle glints (风之旅人微粒黄金碎钻闪烁)
                // Generates extremely sparse, premium golden sparkles only catching direct sunlight reflection
                float glintNoise = frac(sin(dot(floor(input.positionWS.xz * 16.0), float2(12.9898, 78.233))) * 43758.5453);
                float glint = pow(NdotH, 1200.0) * step(0.993, glintNoise) * saturate(dot(mainLight.direction, normalWS));
                float3 sparkleColor = float3(1.0, 0.86, 0.52) * glint * 4.5 * mainLight.shadowAttenuation * cloudShadow;

                float3 finalColor = albedo.rgb * (diffuse + ambient) + specColor + fresnelGlow + sparkleColor;

                return float4(finalColor, 1.0);
            }
            ENDHLSL
        }

        // --- NEW DEPTH PRE-PASS FOR URP DEPTH TEXTURE (深度写入通道) ---
        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode"="DepthOnly" }

            ZWrite On
            ColorMask 0

            HLSLPROGRAM
            #pragma vertex depthVert
            #pragma fragment depthFrag
            #pragma target 3.0

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
            };

            CBUFFER_START(UnityPerMaterial)
                float _TriplanarScale;
                float _BlendSharpness;
                float4 _Color;
            CBUFFER_END

            float4 _DeformerPositions[128];
            float4 _DeformerParams[128];

            float GetSandDeformation(float3 posWS)
            {
                float totalDisp = 0.0;
                for (int i = 0; i < 128; i++)
                {
                    float radius = _DeformerPositions[i].w;
                    if (radius <= 0.01) continue;
                    
                    float3 defPos = _DeformerPositions[i].xyz;
                    float4 defParam = _DeformerParams[i];
                    
                    float d = distance(posWS.xz, defPos.xz);
                    float fade = defParam.w;
                    if (fade > 0.0 && d < (radius + defParam.y))
                    {
                        float disp = 0.0;
                        if (d < radius)
                        {
                            float k = cos((d / radius) * 1.570796);
                            disp = -defParam.x * k * k;
                        }
                        else
                        {
                            float t = (d - radius) / defParam.y;
                            disp = defParam.z * sin(t * 3.1415926);
                        }
                        totalDisp += disp * fade;
                    }
                }
                return totalDisp;
            }

            Varyings depthVert(Attributes input)
            {
                Varyings output;
                float3 positionWS = TransformObjectToWorld(input.positionOS.xyz);
                float disp = GetSandDeformation(positionWS);
                positionWS.y += disp;
                output.positionCS = TransformWorldToHClip(positionWS);
                return output;
            }

            half4 depthFrag(Varyings input) : SV_Target
            {
                return 0;
            }
            ENDHLSL
        }

        // --- NEW DEPTH NORMALS PASS FOR URP DEPTH+NORMALS TEXTURE (SSAO/深度法线通道) ---
        Pass
        {
            Name "DepthNormals"
            Tags { "LightMode"="DepthNormals" }

            ZWrite On
            Cull Back

            HLSLPROGRAM
            #pragma vertex depthNormalsVert
            #pragma fragment depthNormalsFrag
            #pragma target 3.0

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 normalWS : TEXCOORD0;
            };

            CBUFFER_START(UnityPerMaterial)
                float _TriplanarScale;
                float _BlendSharpness;
                float4 _Color;
            CBUFFER_END

            float4 _DeformerPositions[128];
            float4 _DeformerParams[128];

            float GetSandDeformation(float3 posWS)
            {
                float totalDisp = 0.0;
                for (int i = 0; i < 128; i++)
                {
                    float radius = _DeformerPositions[i].w;
                    if (radius <= 0.01) continue;
                    
                    float3 defPos = _DeformerPositions[i].xyz;
                    float4 defParam = _DeformerParams[i];
                    
                    float d = distance(posWS.xz, defPos.xz);
                    float fade = defParam.w;
                    if (fade > 0.0 && d < (radius + defParam.y))
                    {
                        float disp = 0.0;
                        if (d < radius)
                        {
                            float k = cos((d / radius) * 1.570796);
                            disp = -defParam.x * k * k;
                        }
                        else
                        {
                            float t = (d - radius) / defParam.y;
                            disp = defParam.z * sin(t * 3.1415926);
                        }
                        totalDisp += disp * fade;
                    }
                }
                return totalDisp;
            }

            Varyings depthNormalsVert(Attributes input)
            {
                Varyings output;
                float3 positionWS = TransformObjectToWorld(input.positionOS.xyz);
                float disp = GetSandDeformation(positionWS);
                positionWS.y += disp;
                output.positionCS = TransformWorldToHClip(positionWS);
                
                // Normal offset due to footprints
                float delta = 0.15;
                float hRight = GetSandDeformation(positionWS + float3(delta, 0, 0));
                float hUp = GetSandDeformation(positionWS + float3(0, 0, delta));
                float3 normalOffset = float3((disp - hRight) / delta, 0.0, (disp - hUp) / delta);
                output.normalWS = normalize(TransformObjectToWorldNormal(input.normalOS) + normalOffset * 1.5);
                
                return output;
            }

            half4 depthNormalsFrag(Varyings input) : SV_Target
            {
                float3 normalWS = normalize(input.normalWS);
                float3 normalVS = TransformWorldToViewNormal(normalWS, true);
                return half4(PackNormalOctRectEncode(normalVS), 0.0, 0.0);
            }
            ENDHLSL
        }

        // --- NEW SHADOW CASTER PASS FOR TERRAIN & GROOVES (阴影投影通道) ---
        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode"="ShadowCaster" }

            ZWrite On
            ZTest LEqual
            ColorMask 0

            HLSLPROGRAM
            #pragma vertex shadowVert
            #pragma fragment shadowFrag
            #pragma target 3.0

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/CommonMaterial.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
            };

            CBUFFER_START(UnityPerMaterial)
                float _TriplanarScale;
                float _BlendSharpness;
                float4 _Color;
            CBUFFER_END

            float4 _DeformerPositions[128];
            float4 _DeformerParams[128];

            float GetSandDeformation(float3 posWS)
            {
                float totalDisp = 0.0;
                for (int i = 0; i < 128; i++)
                {
                    float radius = _DeformerPositions[i].w;
                    if (radius <= 0.01) continue;
                    
                    float3 defPos = _DeformerPositions[i].xyz;
                    float4 defParam = _DeformerParams[i];
                    
                    float d = distance(posWS.xz, defPos.xz);
                    float fade = defParam.w;
                    if (fade > 0.0 && d < (radius + defParam.y))
                    {
                        float disp = 0.0;
                        if (d < radius)
                        {
                            float k = cos((d / radius) * 1.570796);
                            disp = -defParam.x * k * k;
                        }
                        else
                        {
                            float t = (d - radius) / defParam.y;
                            disp = defParam.z * sin(t * 3.1415926);
                        }
                        totalDisp += disp * fade;
                    }
                }
                return totalDisp;
            }

            float3 _LightDirection;

            float4 GetShadowPositionHClip(float3 positionWS, float3 normalWS)
            {
                float4 positionCS = TransformWorldToHClip(ApplyShadowBias(positionWS, normalWS, _LightDirection));
                #if UNITY_REVERSED_Z
                positionCS.z = min(positionCS.z, positionCS.w * UNITY_NEAR_CLIP_VALUE);
                #else
                positionCS.z = max(positionCS.z, positionCS.w * UNITY_NEAR_CLIP_VALUE);
                #endif
                return positionCS;
            }

            Varyings shadowVert(Attributes input)
            {
                Varyings output;
                float3 positionWS = TransformObjectToWorld(input.positionOS.xyz);
                float disp = GetSandDeformation(positionWS);
                positionWS.y += disp;
                
                float3 normalWS = TransformObjectToWorldNormal(input.normalOS);
                output.positionCS = GetShadowPositionHClip(positionWS, normalWS);
                return output;
            }

            half4 shadowFrag(Varyings input) : SV_Target
            {
                return 0;
            }
            ENDHLSL
        }
    }
    FallBack "Hidden/Universal Render Pipeline/FallbackError"
}
