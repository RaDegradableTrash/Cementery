import re

file_path = "/Users/ra/Documents/Cementery/Assets/Shaders/VolumetricCloud.shader"
with open(file_path, 'r') as f:
    content = f.read()

# 1. Modify SampleShadowMask to return a softer, varying density instead of a hard block
old_mask = r"// Simplest possible thresholding for binary shadow mask\s*float threshold = _CloudThreshold \* 0\.5 \+ \(1\.0 - coverageMask\) \* 1\.5;\s*return saturate\(\(baseNoise - threshold\) \* 10\.0\);"
new_mask = """// Use a softer multiplier to preserve the natural density gradients of the cloud
                float threshold = _CloudThreshold * 0.5 + (1.0 - coverageMask) * 1.5;
                float density = saturate(baseNoise - threshold);
                
                // Return a smooth gradient (0 to 1) instead of a hard binary block
                // pow helps concentrate the darkest parts in the center
                return saturate(pow(density * 2.5, 1.5));"""
content = re.sub(old_mask, new_mask, content)

# 2. Modify Fragment ground shadow to increase contrast
old_shadow = r"// 不使用灰蒙蒙的颜色覆盖，而是利用纯黑色的 Alpha 混合直接“压暗”底图！\s*// 这能完美保留地表的贴图颜色和原本的光照质感，实现类似卡车阴影的真实“块状”阴影效果\s*finalColor\.rgb = float3\(0\.0, 0\.0, 0\.0\);\s*finalColor\.a = shadow \* 0\.65; // 0\.65 是模拟阳光被遮挡的暗度系数"
new_shadow = """// 使用更大的系数提高阴影的最暗处的深度（增强反差），同时因为 shadow 变量现在是渐变的，
                        // 所以阴影边缘会自然过渡，同一片云也会呈现出深浅不一的质感。
                        finalColor.rgb = float3(0.0, 0.0, 0.0);
                        finalColor.a = shadow * 0.85; // 0.85 提供极高的内外反差"""
content = re.sub(old_shadow, new_shadow, content)

with open(file_path, 'w') as f:
    f.write(content)

print("Shadow softness and contrast fixed.")
