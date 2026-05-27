import re

file_path = "/Users/ra/Documents/Cementery/Assets/Shaders/VolumetricCloud.shader"
with open(file_path, 'r') as f:
    content = f.read()

# Remove the duplicate declarations
content = re.sub(r"float jitter = InterleavedGradientNoise\(input\.positionCS\.xy\);\s*float4 finalColor = float4\(0, 0, 0, 0\);\s*Light mainLight = GetMainLight\(\);\s*", "", content)

with open(file_path, 'w') as f:
    f.write(content)

print("Duplicates removed.")
