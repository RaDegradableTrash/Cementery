import re

file_path = "/Users/ra/Documents/Cementery/Assets/Shaders/VolumetricCloud.shader"
with open(file_path, 'r') as f:
    content = f.read()

# 1. Strip out the buggy SampleCloudShadowFast
content = re.sub(r"// --- FAST 2D CLOUD SHADOW PROJECTION ---.*?return saturate\(density \* 10\.0\);\s*}", "", content, flags=re.DOTALL)

# 2. Add a new, robust 2D shadow function
new_shadow_func = """
            // --- ROBUST 2D CLOUD SHADOW & TYNDALL MASK ---
            float SampleShadowMask(float3 worldPos, float3 sunDir)
            {
                // Only project if sun is above horizon
                if (sunDir.y <= 0.05) return 0.0;
                
                float midHeight = (_CloudMinHeight + _CloudMaxHeight) * 0.5;
                float distToCloud = (midHeight - worldPos.y) / sunDir.y;
                if (distToCloud < 0.0) return 0.0;
                
                float3 cloudPos = worldPos + sunDir * distToCloud;
                
                // We only need the macro shape to determine if sunlight is blocked
                float3 baseScaleVec = float3(_BaseScale, _BaseScale / _VerticalStretch, _BaseScale);
                float3 uvwBase = cloudPos * baseScaleVec + _BaseWindSpeed.xyz * _Time.y;
                float baseNoise = SAMPLE_TEXTURE3D_LOD(_BaseNoiseTex, sampler_LinearRepeat, uvwBase, 0).r;
                
                float3 uvwCoverage = cloudPos * (baseScaleVec * 0.2) + _BaseWindSpeed.xyz * _Time.y * 0.1;
                uvwCoverage.y = 0.0;
                float coverage = SAMPLE_TEXTURE3D_LOD(_BaseNoiseTex, sampler_LinearRepeat, uvwCoverage, 0).r;
                float coverageMask = smoothstep(0.32, 0.58, coverage);
                
                // Simplest possible thresholding for binary shadow mask
                float threshold = _CloudThreshold * 0.5 + (1.0 - coverageMask) * 1.5;
                return saturate((baseNoise - threshold) * 10.0);
            }

            half4 Fragment(Varyings input) : SV_Target"""

content = re.sub(r"half4 Fragment\(Varyings input\) : SV_Target", new_shadow_func, content)

# 3. Rewrite the Shadow/Tyndall application in Fragment
old_apply = r"// === GROUND SHADOWS AND TYNDALL EFFECT \(GREEN FOR DEBUGGING\) ===.*?if \(!hitClouds\)"
new_apply = """// === GROUND SHADOWS AND TYNDALL EFFECT (GREEN FOR DEBUGGING) ===
                
                // 1. Ground Shadow
                if (!isSkybox && sceneDist < 20000.0) // Don't shadow infinite far ground
                {
                    float shadow = SampleShadowMask(sceneWorldPos, mainLight.direction);
                    if (shadow > 0.05) 
                    {
                        // Ground shadow is green
                        finalColor.rgb = lerp(finalColor.rgb, float3(0.0, 1.0, 0.0), shadow * 0.6);
                        finalColor.a = saturate(finalColor.a + shadow * 0.6);
                    }
                }
                
                // 2. Volumetric Tyndall Rays
                // 限制在 2000 米以内，远处不渲染！这符合要求，且极大提高性能
                float maxTyndallDist = isSkybox ? (hitClouds ? min(tNear, 2000.0) : 2000.0) : min(sceneDist, 2000.0);
                
                if (maxTyndallDist > 20.0)
                {
                    int tyndallSteps = 12; // 提高步数，减少断层
                    float stepSize = maxTyndallDist / (float)tyndallSteps;
                    float accumulatedLight = 0.0;
                    
                    float t = jitter * stepSize;
                    
                    [loop]
                    for (int j = 0; j < tyndallSteps; j++)
                    {
                        float3 pos = rayOrigin + rayDir * t;
                        float shadowAtPos = SampleShadowMask(pos, mainLight.direction);
                        
                        // 强化对比度：无云遮挡（shadowAtPos=0）则光照为1
                        float illumination = pow(1.0 - shadowAtPos, 4.0);
                        
                        // 积分累计：光照 * 步长（这是物理正确的体积光积分！）
                        // 为了避免数值爆炸，缩小一个系数
                        accumulatedLight += illumination * stepSize * 0.002;
                        
                        t += stepSize;
                    }
                    
                    if (accumulatedLight > 0.01)
                    {
                        float phase = lerp(0.8, 4.0, pow(saturate(dot(rayDir, mainLight.direction) * 0.5 + 0.5), 2.0));
                        float3 godRayColor = float3(0.0, 1.0, 0.0) * accumulatedLight * phase;
                        
                        // 物理正确的 Additive 叠加
                        finalColor.rgb += godRayColor * (1.0 - finalColor.a);
                        finalColor.a = saturate(finalColor.a + accumulatedLight * 0.3);
                    }
                }
                
                if (!hitClouds)"""

content = re.sub(old_apply, new_apply, content, flags=re.DOTALL)

with open(file_path, 'w') as f:
    f.write(content)

print("Shadows rewritten.")
