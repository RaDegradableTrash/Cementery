import re

file_path = "/Users/ra/Documents/Cementery/Assets/Shaders/VolumetricCloud.shader"
with open(file_path, 'r') as f:
    content = f.read()

old_logic = """                // === GROUND SHADOWS AND TYNDALL EFFECT (GREEN FOR DEBUGGING) ===
                if (!isSkybox) 
                {
                    // 1. Project ground shadow
                    float groundShadow = SampleCloudShadowFast(sceneWorldPos, mainLight.direction);
                    if (groundShadow > 0.05) 
                    {
                        // Tint ground green with shadow intensity
                        finalColor.rgb += float3(0.0, 1.0, 0.0) * groundShadow * 0.8;
                        finalColor.a += groundShadow * 0.8;
                    }
                    
                    // 2. Volumetric Tyndall Light Shafts (march towards ground)
                    int tyndallSteps = 8;
                    float tyndallStepSize = sceneDist / (float)tyndallSteps;
                    float tyndallDensity = 0.0;
                    
                    // Jitter starting position
                    float tTyndall = jitter * tyndallStepSize;
                    
                    [loop]
                    for (int j = 0; j < tyndallSteps; j++)
                    {
                        float3 pos = rayOrigin + rayDir * tTyndall;
                        float shadowAtPos = SampleCloudShadowFast(pos, mainLight.direction);
                        
                        // If NOT in shadow, air is illuminated by sun (Tyndall ray!)
                        float illumination = 1.0 - shadowAtPos;
                        tyndallDensity += illumination;
                        
                        tTyndall += tyndallStepSize;
                    }
                    
                    // Average Tyndall intensity
                    float godRayIntensity = (tyndallDensity / (float)tyndallSteps);
                    // Add bright green godrays
                    if (godRayIntensity > 0.0) {
                        float phase = lerp(0.8, 2.5, saturate(dot(rayDir, mainLight.direction) * 0.5 + 0.5));
                        float3 godRayColor = float3(0.1, 1.0, 0.1) * godRayIntensity * phase * 0.6;
                        
                        finalColor.rgb += godRayColor * (1.0 - finalColor.a);
                        finalColor.a = saturate(finalColor.a + godRayIntensity * 0.6);
                    }
                }"""

new_logic = """                // === GROUND SHADOWS AND TYNDALL EFFECT (GREEN FOR DEBUGGING) ===
                
                // 1. Project ground shadow (only for non-skybox pixels)
                if (!isSkybox) 
                {
                    float groundShadow = SampleCloudShadowFast(sceneWorldPos, mainLight.direction);
                    if (groundShadow > 0.05) 
                    {
                        finalColor.rgb += float3(0.0, 1.0, 0.0) * groundShadow * 0.8;
                        finalColor.a += groundShadow * 0.8;
                    }
                }
                
                // 2. Volumetric Tyndall Light Shafts (for all pixels, up to geometry or clouds)
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

content = content.replace(old_logic, new_logic)

with open(file_path, 'w') as f:
    f.write(content)

print("Skybox God Rays fixed.")
