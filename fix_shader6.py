import re

with open('Assets/Shaders/URPTriplanarEnvironment.shader', 'r') as f:
    content = f.read()

# 1. Remove Vertex Displacement entirely
content = content.replace('return GetSandDeformation(posWS) + GetSnowDisplacement(posWS);', 'return GetSandDeformation(posWS);')

with open('Assets/Shaders/URPTriplanarEnvironment.shader', 'w') as f:
    f.write(content)

