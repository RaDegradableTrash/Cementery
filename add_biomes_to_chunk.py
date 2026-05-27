import re

file_path = "/Users/ra/Documents/Cementery/Assets/Scripts/Environment/DesertTerrainChunk.cs"
with open(file_path, 'r') as f:
    content = f.read()

# 1. Add fields
fields_str = """        [Header("Biome Settings")]
        public bool enableBiomes = true;
        public float biomeNoiseScale = 800f; // Global scale for biomes
        public Gradient biomeGradient = CreateDefaultBiomeGradient();

        private static Gradient CreateDefaultBiomeGradient()
        {
            Gradient g = new Gradient();
            GradientColorKey[] gck = new GradientColorKey[7];
            ColorUtility.TryParseHtmlString("#5E716A", out Color c0);
            ColorUtility.TryParseHtmlString("#78866B", out Color c1);
            ColorUtility.TryParseHtmlString("#88937B", out Color c2);
            ColorUtility.TryParseHtmlString("#CCD67F", out Color c3);
            ColorUtility.TryParseHtmlString("#F5F5DC", out Color c4);
            ColorUtility.TryParseHtmlString("#E8E1D5", out Color c5);
            ColorUtility.TryParseHtmlString("#CEB59E", out Color c6);
            
            gck[0] = new GradientColorKey(c0, 0.00f);
            gck[1] = new GradientColorKey(c1, 0.16f);
            gck[2] = new GradientColorKey(c2, 0.33f);
            gck[3] = new GradientColorKey(c3, 0.50f);
            gck[4] = new GradientColorKey(c4, 0.66f);
            gck[5] = new GradientColorKey(c5, 0.83f);
            gck[6] = new GradientColorKey(c6, 1.00f);
            
            GradientAlphaKey[] gak = new GradientAlphaKey[2];
            gak[0] = new GradientAlphaKey(1.0f, 0.0f);
            gak[1] = new GradientAlphaKey(1.0f, 1.0f);
            
            g.SetKeys(gck, gak);
            return g;
        }

        [Header("Minecraft-style Hilly Terrain (MC风凹凸峡谷丘陵)")]"""

content = content.replace('        [Header("Minecraft-style Hilly Terrain (MC风凹凸峡谷丘陵)")]', fields_str)

# 2. Add RegenerateBiomeColors
method_str = """        /// <summary>
        /// Regenerates and bakes ONLY the biome colors into the existing mesh.
        /// Does not alter the geometry or topology.
        /// </summary>
        public void RegenerateBiomeColors()
        {
            MeshFilter filter = GetComponent<MeshFilter>();
            if (filter == null || filter.sharedMesh == null)
            {
                Debug.LogWarning("[DesertTerrainChunk] No mesh found. Please generate the terrain first.");
                return;
            }
            
            Mesh mesh = filter.sharedMesh;
            Vector3[] vertices = mesh.vertices;
            Color[] colors = new Color[vertices.Length];
            
            int vw = width + 1;
            int vd = depth + 1;
            
            if (vertices.Length != vw * vd)
            {
                Debug.LogWarning("[DesertTerrainChunk] Vertex count mismatch. Please rebuild the terrain.");
                return;
            }
            
            for (int z = 0; z < vd; z++)
            {
                for (int x = 0; x < vw; x++)
                {
                    int index = z * vw + x;
                    if (enableBiomes && biomeGradient != null)
                    {
                        float worldX = transform.position.x + vertices[index].x;
                        float worldZ = transform.position.z + vertices[index].z;
                        
                        float bx = (worldX + seed * 99.1f) / biomeNoiseScale;
                        float bz = (worldZ + seed * 77.3f) / biomeNoiseScale;
                        
                        float noise = Mathf.PerlinNoise(bx, bz) * 0.5f 
                                    + Mathf.PerlinNoise(bx * 2f, bz * 2f) * 0.25f 
                                    + Mathf.PerlinNoise(bx * 4f, bz * 4f) * 0.125f;
                        noise = Mathf.Clamp01(noise * 1.15f);
                        
                        colors[index] = biomeGradient.Evaluate(noise);
                        colors[index].a = 1.0f;
                    }
                    else
                    {
                        colors[index] = new Color(1, 1, 1, 0);
                    }
                }
            }
            
            mesh.colors = colors;
            
#if UNITY_EDITOR
            if (!Application.isPlaying)
            {
                UnityEditor.EditorUtility.SetDirty(mesh);
            }
#endif
            Debug.Log($"[DesertTerrainChunk] Regenerated Biome Colormap for '{name}'.");
        }

        public float GetVertexHeightAtLocal(int x, int z)"""

content = content.replace('        public float GetVertexHeightAtLocal(int x, int z)', method_str)


# 3. Add to GenerateSeamlessMesh
mesh_logic_old = """            Mesh mesh = new Mesh { name = "DesertTerrainChunk" };

            if (vertices.Length > 65535)
                mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;

            mesh.vertices = vertices;
            mesh.uv = uvs;
            mesh.normals = normals;
            mesh.triangles = triangles;
            mesh.RecalculateBounds();"""

mesh_logic_new = """            Color[] colors = new Color[vw * vd];
            for (int z = 0; z < vd; z++)
            {
                for (int x = 0; x < vw; x++)
                {
                    int index = z * vw + x;
                    if (enableBiomes && biomeGradient != null)
                    {
                        float worldX = transform.position.x + vertices[index].x;
                        float worldZ = transform.position.z + vertices[index].z;
                        
                        float bx = (worldX + seed * 99.1f) / biomeNoiseScale;
                        float bz = (worldZ + seed * 77.3f) / biomeNoiseScale;
                        
                        float noise = Mathf.PerlinNoise(bx, bz) * 0.5f 
                                    + Mathf.PerlinNoise(bx * 2f, bz * 2f) * 0.25f 
                                    + Mathf.PerlinNoise(bx * 4f, bz * 4f) * 0.125f;
                        noise = Mathf.Clamp01(noise * 1.15f);
                        
                        colors[index] = biomeGradient.Evaluate(noise);
                        colors[index].a = 1.0f;
                    }
                    else
                    {
                        colors[index] = new Color(1, 1, 1, 0);
                    }
                }
            }

            Mesh mesh = new Mesh { name = "DesertTerrainChunk" };

            if (vertices.Length > 65535)
                mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;

            mesh.vertices = vertices;
            mesh.uv = uvs;
            mesh.normals = normals;
            mesh.colors = colors;
            mesh.triangles = triangles;
            mesh.RecalculateBounds();"""

content = content.replace(mesh_logic_old, mesh_logic_new)

with open(file_path, 'w') as f:
    f.write(content)

print("DesertTerrainChunk updated.")
