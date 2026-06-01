import re

with open('Assets/Shaders/URPTriplanarEnvironment.shader', 'r') as f:
    content = f.read()

# Replace all occurrences
old_str = "return GetSandDeformation(posWS);"
new_str = "return GetSandDeformation(posWS) + GetSnowDisplacement(posWS);"
content = content.replace(old_str, new_str)

with open('Assets/Shaders/URPTriplanarEnvironment.shader', 'w') as f:
    f.write(content)

