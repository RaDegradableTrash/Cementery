import re

file_path = "/Users/ra/Documents/Cementery/Assets/Scripts/Environment/Editor/DesertTerrainChunkEditor.cs"
with open(file_path, 'r') as f:
    content = f.read()

button_logic = """            // Draw Button 2 (Pastel Blue)
            GUI.backgroundColor = new Color(0.4f, 0.7f, 0.9f);
            if (GUILayout.Button("按钮 2：基于已有地形微调细化", GUILayout.Height(36)))
            {
                Undo.RecordObject(chunk, "Refine Existing Terrain");
                if (chunk.TryGetComponent<MeshFilter>(out var filter) && filter.sharedMesh != null)
                {
                    Undo.RecordObject(filter.sharedMesh, "Refine Existing Terrain Mesh");
                }

                chunk.RefineExistingTerrain();

                MarkDirtyAndSave(chunk);
            }

            EditorGUILayout.Space();

            // Draw Biome Button (Pastel Yellow/Orange)
            GUI.backgroundColor = new Color(0.9f, 0.75f, 0.4f);
            if (GUILayout.Button("🌍 仅重新生成群系颜色 (Regenerate Biome Colormap)", GUILayout.Height(36)))
            {
                Undo.RecordObject(chunk, "Regenerate Biome Colors");
                if (chunk.TryGetComponent<MeshFilter>(out var filter) && filter.sharedMesh != null)
                {
                    Undo.RecordObject(filter.sharedMesh, "Regenerate Biome Colors Mesh");
                }

                chunk.RegenerateBiomeColors();

                MarkDirtyAndSave(chunk);
            }"""

content = content.replace("""            // Draw Button 2 (Pastel Blue)
            GUI.backgroundColor = new Color(0.4f, 0.7f, 0.9f);
            if (GUILayout.Button("按钮 2：基于已有地形微调细化", GUILayout.Height(36)))
            {
                Undo.RecordObject(chunk, "Refine Existing Terrain");
                if (chunk.TryGetComponent<MeshFilter>(out var filter) && filter.sharedMesh != null)
                {
                    Undo.RecordObject(filter.sharedMesh, "Refine Existing Terrain Mesh");
                }

                chunk.RefineExistingTerrain();

                MarkDirtyAndSave(chunk);
            }""", button_logic)

with open(file_path, 'w') as f:
    f.write(content)

print("Editor script updated.")
