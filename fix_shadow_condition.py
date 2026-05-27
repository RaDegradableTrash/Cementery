import re

file_path = "/Users/ra/Documents/Cementery/Assets/Shaders/VolumetricCloud.shader"
with open(file_path, 'r') as f:
    content = f.read()

# Replace the condition `if (!isSkybox && sceneDist < tNear)` with `if (!isSkybox)`
content = content.replace("if (!isSkybox && sceneDist < tNear)", "if (!isSkybox)")

with open(file_path, 'w') as f:
    f.write(content)

print("Condition fixed.")
