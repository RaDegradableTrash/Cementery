Shader "Environment/URPSeaLevelOcean"
{
    Properties
    {
        _ShallowColor ("Shallow Color", Color) = (0.10, 0.42, 0.52, 0.62)
        _DeepColor ("Deep Color", Color) = (0.02, 0.12, 0.28, 0.78)
        _SeaLevel ("Sea Level", Float) = 0
        _WaveAmplitude ("Wave Amplitude", Range(0.05, 3.0)) = 0.55
        _WaveSpeed ("Wave Speed", Range(0.05, 6.0)) = 0.65
        _PrimaryWaveLength ("Primary Wave Length", Range(8.0, 160.0)) = 62
        _SecondaryWaveLength ("Secondary Wave Length", Range(4.0, 80.0)) = 24
        _ShimmerStrength ("Shimmer Strength", Range(0.0, 3.0)) = 1.2
        _SpecularPower ("Specular Power", Range(8.0, 256.0)) = 96
        _Alpha ("Alpha", Range(0.0, 1.0)) = 0.72
        _FoamColor ("Foam Color", Color) = (0.92, 0.98, 1.0, 0.88)
        _FoamIntensity ("Foam Intensity", Range(0.0, 4.0)) = 1.35
        _CrestFoamThreshold ("Crest Foam Threshold", Range(0.0, 1.0)) = 0.68
        _ShorelineFoamDepth ("Shoreline Foam Depth", Range(0.25, 8.0)) = 2.4
        _FoamNoiseScale ("Foam Noise Scale", Range(0.005, 0.2)) = 0.045
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Transparent"
            "Queue" = "Transparent-50"
            "RenderPipeline" = "UniversalPipeline"
        }

        LOD 150
        Blend SrcAlpha OneMinusSrcAlpha
        ZWrite Off
        Cull Off

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_fog
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float2 uv : TEXCOORD1;
                float fogCoord : TEXCOORD2;
                float4 screenPos : TEXCOORD3;
                float waveHeight : TEXCOORD4;
            };

            CBUFFER_START(UnityPerMaterial)
                half4 _ShallowColor;
                half4 _DeepColor;
                float _SeaLevel;
                float _WaveAmplitude;
                float _WaveSpeed;
                float _PrimaryWaveLength;
                float _SecondaryWaveLength;
                float _ShimmerStrength;
                float _SpecularPower;
                float _Alpha;
                half4 _FoamColor;
                float _FoamIntensity;
                float _CrestFoamThreshold;
                float _ShorelineFoamDepth;
                float _FoamNoiseScale;
            CBUFFER_END

            static const float OceanTwoPi = 6.28318530718;

            float WavePhase(float2 position, float2 direction, float length, float speedScale)
            {
                float safeLength = max(length, 0.001);
                return dot(position, normalize(direction)) * (OceanTwoPi / safeLength) + _Time.y * _WaveSpeed * speedScale;
            }

            float WaveHeight(float2 position)
            {
                float primary = sin(WavePhase(position, float2(0.83, 0.56), _PrimaryWaveLength, 1.0));
                float secondary = sin(WavePhase(position, float2(-0.38, 0.92), _SecondaryWaveLength, 1.75));
                float chop = sin(WavePhase(position, float2(0.14, 0.99), max(5.0, _SecondaryWaveLength * 0.42), 2.4));
                return (primary * 0.62 + secondary * 0.28 + chop * 0.10) * _WaveAmplitude;
            }

            float FoamNoise(float2 position)
            {
                float2 p = position * _FoamNoiseScale;
                float a = sin(dot(p, float2(12.9898, 78.233)) + _Time.y * _WaveSpeed * 2.1);
                float b = sin(dot(p, float2(-39.3468, 11.135)) - _Time.y * _WaveSpeed * 1.4);
                return saturate((a + b) * 0.25 + 0.5);
            }

            float2 WaveSlope(float2 position)
            {
                float2 dirA = normalize(float2(0.83, 0.56));
                float2 dirB = normalize(float2(-0.38, 0.92));
                float2 dirC = normalize(float2(0.14, 0.99));

                float kA = OceanTwoPi / max(_PrimaryWaveLength, 0.001);
                float kB = OceanTwoPi / max(_SecondaryWaveLength, 0.001);
                float kC = OceanTwoPi / max(max(5.0, _SecondaryWaveLength * 0.42), 0.001);

                float phaseA = WavePhase(position, dirA, _PrimaryWaveLength, 1.0);
                float phaseB = WavePhase(position, dirB, _SecondaryWaveLength, 1.75);
                float phaseC = WavePhase(position, dirC, max(5.0, _SecondaryWaveLength * 0.42), 2.4);

                return _WaveAmplitude * (
                    cos(phaseA) * 0.62 * kA * dirA +
                    cos(phaseB) * 0.28 * kB * dirB +
                    cos(phaseC) * 0.10 * kC * dirC);
            }

            Varyings vert(Attributes input)
            {
                Varyings output;
                float3 positionWS = TransformObjectToWorld(input.positionOS.xyz);
                positionWS.y = _SeaLevel + WaveHeight(positionWS.xz);

                output.positionWS = positionWS;
                output.positionCS = TransformWorldToHClip(positionWS);
                output.uv = input.uv;
                output.fogCoord = ComputeFogFactor(output.positionCS.z);
                output.screenPos = ComputeScreenPos(output.positionCS);
                output.waveHeight = positionWS.y - _SeaLevel;
                return output;
            }

            half4 frag(Varyings input) : SV_Target
            {
                float2 slope = WaveSlope(input.positionWS.xz);
                half3 normalWS = normalize(half3(-slope.x, 1.0h, -slope.y));

                Light mainLight = GetMainLight();
                half3 lightDir = normalize(mainLight.direction);
                half3 viewDir = normalize(GetWorldSpaceViewDir(input.positionWS));
                half3 halfDir = normalize(lightDir + viewDir);

                half ndotl = saturate(dot(normalWS, lightDir));
                half fresnel = pow(1.0h - saturate(dot(normalWS, viewDir)), 4.0h);
                half specular = pow(saturate(dot(normalWS, halfDir)), _SpecularPower) * _ShimmerStrength;
                half ripple = saturate(sin((input.positionWS.x + input.positionWS.z) * 0.09 + _Time.y * _WaveSpeed * 3.1) * 0.5h + 0.5h);
                float normalizedCrest = saturate(input.waveHeight / max(_WaveAmplitude, 0.001));
                float crestFoam = smoothstep(_CrestFoamThreshold, 1.0, normalizedCrest);
                float2 screenUv = input.screenPos.xy / input.screenPos.w;
                float rawDepth = SampleSceneDepth(screenUv);
                float sceneEyeDepth = LinearEyeDepth(rawDepth, _ZBufferParams);
                float waterEyeDepth = max(0.001, input.screenPos.w);
                float shorelineFoam = saturate(1.0 - abs(sceneEyeDepth - waterEyeDepth) / max(0.001, _ShorelineFoamDepth));
                shorelineFoam *= step(waterEyeDepth, sceneEyeDepth + _ShorelineFoamDepth);
                float foamNoise = FoamNoise(input.positionWS.xz);
                half foam = saturate((crestFoam * (0.55h + foamNoise * 0.45h) + shorelineFoam * (0.35h + foamNoise * 0.65h)) * _FoamIntensity);

                half3 waterColor = lerp(_DeepColor.rgb, _ShallowColor.rgb, saturate(fresnel + ndotl * 0.25h));
                waterColor += mainLight.color * specular * (0.65h + ripple * 0.35h);
                waterColor += fresnel * 0.18h;
                waterColor = lerp(waterColor, _FoamColor.rgb, foam * _FoamColor.a);
                waterColor = MixFog(waterColor, input.fogCoord);

                half alpha = saturate(lerp(_DeepColor.a, _ShallowColor.a, fresnel) * _Alpha + fresnel * 0.12h + foam * 0.18h);
                return half4(waterColor, alpha);
            }
            ENDHLSL
        }
    }

    FallBack "Hidden/Universal Render Pipeline/FallbackError"
}
