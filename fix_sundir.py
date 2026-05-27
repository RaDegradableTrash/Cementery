import re

file_path = "/Users/ra/Documents/Cementery/Assets/Shaders/VolumetricCloud.shader"
with open(file_path, 'r') as f:
    content = f.read()

# Fix the sun direction logic
content = content.replace("if (sunDir.y >= -0.01) return 0.0; // Sun is below horizon or horizontal", "if (sunDir.y <= 0.01) return 0.0; // Sun is below horizon")

with open(file_path, 'w') as f:
    f.write(content)

print("Sun direction fixed.")
