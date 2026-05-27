import re

file_path = "/Users/ra/Documents/Cementery/Assets/Shaders/VolumetricCloud.shader"
with open(file_path, 'r') as f:
    content = f.read()

old_logic = r"// === GROUND SHADOWS AND TYNDALL EFFECT \(GREEN FOR DEBUGGING\) ===.*?if \(!hitClouds\)"

new_logic = """// === REALISTIC GROUND SHADOWS AND TYNDALL RAYS ===
                
                // 1. Realistic Ground Cloud Shadows
                if (!isSkybox && sceneDist < 20000.0)
                {
                    float shadow = SampleShadowMask(sceneWorldPos, mainLight.direction);
                    
                    if (shadow > 0.01) 
                    {
                        // Darken the ground using the shadow color
                        finalColor.rgb = lerp(finalColor.rgb, _ShadowColor.rgb, shadow * 0.6);
                        finalColor.a = saturate(finalColor.a + shadow * 0.6);
                    }
                }
                
                // 2. Volumetric Tyndall God Rays
                float maxTyndallDist = isSkybox ? (hitClouds ? min(tNear, 3000.0) : 3000.0) : min(sceneDist, 3000.0);
                
                if (maxTyndallDist > 20.0)
                {
                    int tyndallSteps = 16; // 高精度步数
                    float stepSize = maxTyndallDist / (float)tyndallSteps;
                    float accumulatedLight = 0.0;
                    
                    float t = jitter * stepSize;
                    
                    [loop]
                    for (int j = 0; j < tyndallSteps; j++)
                    {
                        float3 pos = rayOrigin + rayDir * t;
                        float shadowAtPos = SampleShadowMask(pos, mainLight.direction);
                        
                        // 1.0 means full sunlight, 0.0 means shadowed by clouds
                        float illumination = pow(1.0 - shadowAtPos, 2.0);
                        
                        // Add light based on step distance (Riemann sum)
                        accumulatedLight += illumination * stepSize * 0.0005;
                        
                        t += stepSize;
                    }
                    
                    if (accumulatedLight > 0.01)
                    {
                        // Phase function gives sun glare
                        float phase = lerp(0.5, 3.0, pow(saturate(dot(rayDir, mainLight.direction) * 0.5 + 0.5), 3.0));
                        float3 godRayColor = _MaxLightColor.rgb * accumulatedLight * phase;
                        
                        // Additive blending for realistic light shafts
                        finalColor.rgb += godRayColor * (1.0 - finalColor.a);
                        finalColor.a = saturate(finalColor.a + accumulatedLight * 0.3);
                    }
                }
                
                if (!hitClouds)"""

content = re.sub(old_logic, new_logic, content, flags=re.DOTALL)

with open(file_path, 'w') as f:
    f.write(content)

print("Converted to realistic shadows and rays.")
