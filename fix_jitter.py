import re

file_path = "/Users/ra/Documents/Cementery/Assets/Shaders/VolumetricCloud.shader"
with open(file_path, 'r') as f:
    content = f.read()

# Replace the jitter logic to use full step jitter
pattern = r"float t = tNear \+ \(float\(i\) \+ jitter \* 0\.25\) \* stepSize;"
replacement = r"float t = tNear + (float(i) + jitter * max(0.5, _JitterStrength * 2.0)) * stepSize;"
new_content = re.sub(pattern, replacement, content)

with open(file_path, 'w') as f:
    f.write(new_content)

print("Fixed jitter in shader.")
