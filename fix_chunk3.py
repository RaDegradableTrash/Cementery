import re

with open('Assets/Scripts/Environment/DesertTerrainChunk.cs', 'r') as f:
    content = f.read()

# Strip out everything after and including the malformed UpdateSnowLayer
idx = content.find('// ── Snow Layer Management')
if idx != -1:
    content = content[:idx]

# Clean up trailing braces and whitespace
content = content.rstrip()
while content.endswith('}'):
    content = content[:-1].rstrip()

method_code = """
        // ── Snow Layer Management ────────────────────────────────────────────────
        private void UpdateSnowLayer(Mesh originalMesh)
        {
            if (originalMesh == null) return;

            Transform snowLayerTransform = transform.Find("SnowLayer");
            GameObject snowLayerObj;

            if (snowLayerTransform == null)
            {
                snowLayerObj = new GameObject("SnowLayer");
                snowLayerObj.transform.SetParent(transform, false);
                snowLayerObj.transform.localPosition = Vector3.zero;
                snowLayerObj.transform.localRotation = Quaternion.identity;
                snowLayerObj.transform.localScale = Vector3.one;
            }
            else
            {
                snowLayerObj = snowLayerTransform.gameObject;
            }

            MeshFilter filter = snowLayerObj.GetComponent<MeshFilter>();
            if (filter == null) filter = snowLayerObj.AddComponent<MeshFilter>();
            filter.sharedMesh = originalMesh;

            MeshRenderer renderer = snowLayerObj.GetComponent<MeshRenderer>();
            if (renderer == null) renderer = snowLayerObj.AddComponent<MeshRenderer>();

            // Setup shadows so the snow blanket casts soft shadows
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
            renderer.receiveShadows = true;

            // Load SnowBlanket shader
            Shader snowShader = Shader.Find("Environment/SnowBlanket");
            if (snowShader != null)
            {
                // To avoid leaking materials in Editor, check if we already have it
                if (renderer.sharedMaterial == null || renderer.sharedMaterial.shader != snowShader)
                {
                    Material snowMat = new Material(snowShader);
                    snowMat.name = "SnowBlanketMaterial";
                    renderer.sharedMaterial = snowMat;
                }
            }
            else
            {
                Debug.LogWarning("[DesertTerrainChunk] Could not find Environment/SnowBlanket shader for SnowLayer.");
            }
        }
"""

new_content = content + "\n        }\n" + method_code + "    }\n}\n"

with open('Assets/Scripts/Environment/DesertTerrainChunk.cs', 'w') as f:
    f.write(new_content)

