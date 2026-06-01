import re

with open('Assets/Shaders/URPTriplanarEnvironment.shader', 'r') as f:
    content = f.read()

# Replace saturate(snowHeight * 2.0) with saturate(snowHeight * 3.0)
content = content.replace('saturate(snowHeight * 2.0)', 'saturate(snowHeight * 3.0)')

with open('Assets/Shaders/URPTriplanarEnvironment.shader', 'w') as f:
    f.write(content)

