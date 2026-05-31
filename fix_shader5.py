import re

with open('Assets/Shaders/URPTriplanarEnvironment.shader', 'r') as f:
    content = f.read()

# 1. Increase height from 0.4 to 1.5 to make it visibly 3D
content = content.replace('return snowHeight * 0.4;', 'return snowHeight * 1.5;')

# 2. Fix the normal calculation offset strength (currently 0.4, let's restore it to 1.2 for proper shading)
# Note: delta was 0.8. Let's keep delta = 0.8 so normals are smooth.
content = content.replace('normalOffset * 0.4);', 'normalOffset * 1.2);')

with open('Assets/Shaders/URPTriplanarEnvironment.shader', 'w') as f:
    f.write(content)

