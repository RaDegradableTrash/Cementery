import re

with open('Assets/Scripts/Environment/DesertTerrainChunk.cs', 'r') as f:
    content = f.read()

# Inject into ApplyMesh
content = re.sub(
    r'(col\.sharedMesh = mesh;\s*\n\s*\})',
    r'\1\n\n            UpdateSnowLayer(mesh);',
    content
)

# Fix the method we just added at the bottom which is correct, but we need to ensure the calls are correctly placed
with open('Assets/Scripts/Environment/DesertTerrainChunk.cs', 'w') as f:
    f.write(content)

