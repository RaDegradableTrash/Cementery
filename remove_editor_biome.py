import re

file_path = "/Users/ra/Documents/Cementery/Assets/Scripts/Environment/Editor/DesertTerrainChunkEditor.cs"
with open(file_path, 'r') as f:
    content = f.read()

content = re.sub(r'            EditorGUILayout\.Space\(\);\s*// Draw Biome Button.*?MarkDirtyAndSave\(chunk\);\s*\}', '', content, flags=re.DOTALL)

with open(file_path, 'w') as f:
    f.write(content)
