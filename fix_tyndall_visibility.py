import re

file_path = "/Users/ra/Documents/Cementery/Assets/Shaders/VolumetricCloud.shader"
with open(file_path, 'r') as f:
    content = f.read()

old_logic = """                // 2. Volumetric Tyndall Light Shafts (for all pixels, up to geometry or clouds)
                float maxTyndallDist = isSkybox ? (hitClouds ? tNear : 10000.0) : min(sceneDist, 10000.0);
                if (maxTyndallDist > 10.0)
                {
                    int tyndallSteps = 8;
                    float tyndallStepSize = maxTyndallDist / (float)tyndallSteps;
                    float tyndallDensity = 0.0;
                    
                    float tTyndall = jitter * tyndallStepSize;
                    
                    [loop]
                    for (int j = 0; j < tyndallSteps; j++)
                    {
                        float3 pos = rayOrigin + rayDir * tTyndall;
                        float shadowAtPos = SampleCloudShadowFast(pos, mainLight.direction);
                        
                        float illumination = 1.0 - shadowAtPos;
                        
                        // Distance fade for godrays
                        float fade = saturate((10000.0 - tTyndall) / 10000.0);
                        tyndallDensity += illumination * fade;
                        
                        tTyndall += tyndallStepSize;
                    }
                    
                    float godRayIntensity = (tyndallDensity / (float)tyndallSteps);
                    
                    if (godRayIntensity > 0.0) {
                        float phase = lerp(0.8, 2.5, saturate(dot(rayDir, mainLight.direction) * 0.5 + 0.5));
                        float3 godRayColor = float3(0.1, 1.0, 0.1) * godRayIntensity * phase * 0.6;
                        
                        finalColor.rgb += godRayColor * (1.0 - finalColor.a);
                        finalColor.a = saturate(finalColor.a + godRayIntensity * 0.6);
                    }
                }"""

new_logic = """                // 2. Volumetric Tyndall Light Shafts (for all pixels, up to geometry or clouds)
                // 限制最大渲染距离为 3000，过滤远处的计算，同时提高近处步进的精度
                float maxTyndallDist = isSkybox ? (hitClouds ? min(tNear, 3000.0) : 3000.0) : min(sceneDist, 3000.0);
                if (maxTyndallDist > 10.0)
                {
                    int tyndallSteps = 8;
                    float tyndallStepSize = maxTyndallDist / (float)tyndallSteps;
                    float tyndallDensity = 0.0;
                    
                    float tTyndall = jitter * tyndallStepSize;
                    
                    [loop]
                    for (int j = 0; j < tyndallSteps; j++)
                    {
                        float3 pos = rayOrigin + rayDir * tTyndall;
                        float shadowAtPos = SampleCloudShadowFast(pos, mainLight.direction);
                        
                        // 强化明暗对比！有阴影的地方几乎为0，没阴影的地方高亮
                        float illumination = pow(1.0 - shadowAtPos, 3.0);
                        
                        // 距离衰减，越远越弱
                        float fade = saturate((3000.0 - tTyndall) / 3000.0);
                        tyndallDensity += illumination * fade;
                        
                        tTyndall += tyndallStepSize;
                    }
                    
                    float godRayIntensity = (tyndallDensity / (float)tyndallSteps);
                    
                    // 增强可见度：即使只有一点强度，也暴力放大，做成纯绿色
                    if (godRayIntensity > 0.01) {
                        float phase = lerp(1.0, 4.0, pow(saturate(dot(rayDir, mainLight.direction) * 0.5 + 0.5), 2.0));
                        
                        // HDR 极高亮度的纯绿色，无视底色
                        float3 godRayColor = float3(0.0, 3.0, 0.0) * godRayIntensity * phase;
                        
                        finalColor.rgb += godRayColor; // 直接 additive 叠加到画面上
                        finalColor.a = saturate(finalColor.a + godRayIntensity);
                    }
                }"""

content = content.replace(old_logic, new_logic)

with open(file_path, 'w') as f:
    f.write(content)

print("Tyndall visibility fixed.")
