import re

file_path = "/Users/ra/Documents/Cementery/Assets/Shaders/VolumetricCloud.shader"
with open(file_path, 'r') as f:
    content = f.read()

# 1. Update distanceFade in the loop
pattern_distance = r"float distanceFade = saturate\(\(160000\.0 - t\) / 64000\.0\);"
replacement_distance = r"float distanceFade = saturate((_MaxRenderDist - t) / (_MaxRenderDist * 0.4));"
content = re.sub(pattern_distance, replacement_distance, content)

# 2. Add distToCam and distRatio right before localThreshold calculation in SampleCloudDensity
pattern_threshold = r"float localThreshold = _CloudThreshold \* 0\.5 \+ baseSpread \+ \(1\.0 - coverageMask\) \* 0\.35;"
replacement_threshold = r"""
                float distToCam = length(pos - _WorldSpaceCameraPos);
                float distRatio = saturate(distToCam / _MaxRenderDist);
                // 距离越远，阈值越高，从而过滤掉远处稀碎的小云块
                float localThreshold = _CloudThreshold * 0.5 + baseSpread + (1.0 - coverageMask) * 0.35 + distRatio * 0.15;"""
content = re.sub(pattern_threshold, replacement_threshold, content)

# 3. Fade out detail noise over distance
pattern_detail = r"float boundaryWarp = detailNoise \* _Puffiness \* 0\.15 \* heightFactor \* \(1\.0 - baseShape\);\s+float edgeCarving = \(1\.0 - detailNoise\) \* _DetailInfluence \* 0\.12 \* \(1\.0 - baseShape\);"
replacement_detail = r"""
                // 近实远虚：远处细节噪声衰减，减少噪点和破碎感，保留大块平滑的体积
                float detailFade = saturate(1.0 - distRatio * 1.5);
                float boundaryWarp = detailNoise * (_Puffiness * detailFade) * 0.15 * heightFactor * (1.0 - baseShape);
                float edgeCarving = (1.0 - detailNoise) * (_DetailInfluence * detailFade) * 0.12 * (1.0 - baseShape);"""
content = re.sub(pattern_detail, replacement_detail, content)

with open(file_path, 'w') as f:
    f.write(content)

print("LOD and Atmospheric fading applied.")
