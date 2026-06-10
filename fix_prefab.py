import re

with open('Assets/Prefabs/KelpLeaf.prefab', 'r') as f:
    content = f.read()

# Remove the component references in m_Component list
# e.g. "- component: {fileID: 5020495328218343160}"
# where the fileID matches a MeshCollider.
# First, find all MeshCollider IDs.
mesh_colliders = re.findall(r'--- !u!64 &(-?\d+)\nMeshCollider:', content)

for mc_id in mesh_colliders:
    # remove the reference
    content = re.sub(r'\s*- component: \{fileID: ' + mc_id + r'\}', '', content)

# Now remove the entire YAML blocks for the MeshColliders
for mc_id in mesh_colliders:
    # Match the block starting with "--- !u!64 &mc_id\nMeshCollider:" up to the next "--- !u!" or EOF
    pattern = r'--- !u!64 &' + mc_id + r'\nMeshCollider:.*?(?=\n--- !u!|\Z)'
    content = re.sub(pattern, '', content, flags=re.DOTALL)

with open('Assets/Prefabs/KelpLeaf.prefab', 'w') as f:
    f.write(content)

print(f"Removed MeshColliders: {mesh_colliders}")
