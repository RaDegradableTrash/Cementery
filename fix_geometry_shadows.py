import re

file_path = "/Users/ra/Documents/Cementery/Assets/Shaders/VolumetricCloud.shader"
with open(file_path, 'r') as f:
    content = f.read()

# Add shadow pragmas
pragma_inject = """            #pragma vertex FullscreenVert
            #pragma fragment Fragment

            // Added shadow keywords to receive geometry shadows
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE
            #pragma multi_compile _ _SHADOWS_SOFT"""

content = re.sub(r"            #pragma vertex FullscreenVert\s*#pragma fragment Fragment", pragma_inject, content)

# Modify shadow logic inside Fragment
old_logic = r"// === GROUND SHADOWS AND TYNDALL EFFECT \(GREEN FOR DEBUGGING\) ===\s*// 1\. Ground Shadow\s*if \(!isSkybox && sceneDist < 20000\.0\) // Don't shadow infinite far ground\s*\{\s*float shadow = SampleShadowMask\(sceneWorldPos, mainLight\.direction\);\s*if \(shadow > 0\.05\) \s*\{\s*// Ground shadow is green\s*finalColor\.rgb = lerp\(finalColor\.rgb, float3\(0\.0, 1\.0, 0\.0\), shadow \* 0\.6\);\s*finalColor\.a = saturate\(finalColor\.a \+ shadow \* 0\.6\);\s*\}\s*\}"

new_logic = """// === GROUND SHADOWS AND TYNDALL EFFECT (GREEN FOR DEBUGGING) ===
                
                // Get geometry shadow attenuation from main light
                float4 shadowCoord = TransformWorldToShadowCoord(sceneWorldPos);
                Light mainLightShadowed = GetMainLight(shadowCoord);
                float geomAtten = mainLightShadowed.shadowAttenuation;
                
                // 1. Ground Shadow (now represents "Light")
                if (!isSkybox && sceneDist < 20000.0)
                {
                    float shadow = SampleShadowMask(sceneWorldPos, mainLight.direction);
                    
                    // We want the GREEN to represent the LIGHT passing through the clouds!
                    // If shadow is 0, it means it's illuminated!
                    float cloudIllumination = pow(1.0 - shadow, 2.0); 
                    
                    if (cloudIllumination > 0.05) 
                    {
                        // ONLY add light if geometry is NOT blocking the sun (geomAtten > 0)
                        float finalIllum = cloudIllumination * geomAtten;
                        if (finalIllum > 0.01) {
                            finalColor.rgb = lerp(finalColor.rgb, float3(0.0, 1.0, 0.0), finalIllum * 0.6);
                            finalColor.a = saturate(finalColor.a + finalIllum * 0.6);
                        }
                    }
                }"""

content = re.sub(old_logic, new_logic, content)

# Modify Tyndall logic to respect geometry shadows
old_tyndall = r"// 强化对比度：无云遮挡（shadowAtPos=0）则光照为1\s*float illumination = pow\(1\.0 - shadowAtPos, 4\.0\);"
new_tyndall = """// 强化对比度：无云遮挡（shadowAtPos=0）则光照为1
                        float illumination = pow(1.0 - shadowAtPos, 4.0);
                        
                        // Check geometry shadow for Tyndall ray
                        float4 rayShadowCoord = TransformWorldToShadowCoord(pos);
                        float rayGeomAtten = MainLightRealtimeShadow(rayShadowCoord);
                        illumination *= rayGeomAtten; // God rays shouldn't appear inside truck shadows!"""
                        
content = re.sub(old_tyndall, new_tyndall, content)

with open(file_path, 'w') as f:
    f.write(content)

print("Geometry shadows added.")
