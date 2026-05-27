import re

file_path = "/Users/ra/Documents/Cementery/Assets/Shaders/VolumetricCloud.shader"
with open(file_path, 'r') as f:
    content = f.read()

# Fix the Threshold logic so that 0.55 doesn't delete clouds
pattern_threshold = r"float localThreshold = _CloudThreshold \+ baseSpread \+ \(1\.0 - coverageMask\) \* 0\.65;"
# If we scale _CloudThreshold by 0.5, then 0.55 becomes 0.275, which leaves enough room.
# Also reduce coverage mask penalty so it doesn't wipe clouds.
replacement_threshold = r"float localThreshold = _CloudThreshold * 0.5 + baseSpread + (1.0 - coverageMask) * 0.35;"
content = re.sub(pattern_threshold, replacement_threshold, content)

# Fix the Alpha banding by replacing the linear alpha with Beer's law
pattern_alpha = r"float alpha = density \* stepSize \* 0\.05;"
replacement_alpha = r"float alpha = 1.0 - exp(-density * stepSize * 0.005);"
content = re.sub(pattern_alpha, replacement_alpha, content)

# Fix the Jitter banding by replacing 0.25 jitter with full jitter
pattern_jitter = r"float t = tNear \+ \(float\(i\) \+ jitter \* 0\.25\) \* stepSize;"
replacement_jitter = r"float t = tNear + (float(i) + jitter * max(0.5, _JitterStrength * 2.0)) * stepSize;"
content = re.sub(pattern_jitter, replacement_jitter, content)

with open(file_path, 'w') as f:
    f.write(content)

print("Shader logic fixed.")
