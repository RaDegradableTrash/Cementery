import re

file_path = "/Users/ra/Documents/Cementery/Assets/Shaders/VolumetricCloud.shader"
with open(file_path, 'r') as f:
    content = f.read()

old_logic = r"// 1\. Realistic Ground Cloud Shadows\s*if \(!isSkybox && sceneDist < 20000\.0\)\s*\{\s*float shadow = SampleShadowMask\(sceneWorldPos, mainLight\.direction\);\s*if \(shadow > 0\.01\) \s*\{\s*// Darken the ground using the shadow color\s*finalColor\.rgb = lerp\(finalColor\.rgb, _ShadowColor\.rgb, shadow \* 0\.6\);\s*finalColor\.a = saturate\(finalColor\.a \+ shadow \* 0\.6\);\s*\}\s*\}"

new_logic = """// 1. Realistic Ground Cloud Shadows
                if (!isSkybox && sceneDist < 20000.0)
                {
                    float shadow = SampleShadowMask(sceneWorldPos, mainLight.direction);
                    
                    if (shadow > 0.01) 
                    {
                        // 不使用灰蒙蒙的颜色覆盖，而是利用纯黑色的 Alpha 混合直接“压暗”底图！
                        // 这能完美保留地表的贴图颜色和原本的光照质感，实现类似卡车阴影的真实“块状”阴影效果
                        finalColor.rgb = float3(0.0, 0.0, 0.0);
                        finalColor.a = shadow * 0.65; // 0.65 是模拟阳光被遮挡的暗度系数
                    }
                }"""

content = re.sub(old_logic, new_logic, content)

with open(file_path, 'w') as f:
    f.write(content)

print("Shadow color fixed.")
