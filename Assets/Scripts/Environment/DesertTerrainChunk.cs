using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace EnvironmentSystem
{
    /// <summary>
    /// Generates a highly detailed desert terrain mesh dynamically or in the editor.
    /// Supports both asymmetrical sand dunes (with organic chaotic modulation) and rugged, terraced, steep Minecraft-like desert hills.
    /// Features 100% seamless analytical normal calculation and interactive designer tools.
    /// Decouples macroscopic base height trend (50% blend zone) from high-frequency dune details to ensure constant local contrast.
    /// Uses Coons Patch Transfinite Interpolation to preserve natural rolling boundary profiles and organic slopes.
    /// </summary>
    [RequireComponent(typeof(MeshFilter), typeof(MeshRenderer), typeof(MeshCollider))]
    public class DesertTerrainChunk : MonoBehaviour
    {
        [Header("Base Grid Settings")]
        public int width = 64;       // Vertices along X
        public int depth = 64;       // Vertices along Z
        public float cellSize = 2f;  // Space between vertices (meters)

        [Header("Global Seed")]
        public int seed = 42;

        [Header("Minecraft-style Hilly Terrain (MC风凹凸峡谷丘陵)")]
        public bool enableMinecraftHills = false; // Set to false by default to focus on sand dunes
        public float hillMaxHeight = 35f;      // Maximum height of the rugged hills
        public float hillNoiseScale = 120f;    // Base horizontal scale for the hills
        [Range(1, 8)]
        public int octaves = 5;                // Complexity octaves (FBM noise layers)
        public float lacunarity = 2.1f;        // Frequency multiplier between octaves
        public float gain = 0.48f;             // Amplitude multiplier between octaves

        [Header("MC Stepped Stratification (MC风侵蚀阶梯)")]
        public bool enableTerracing = true;
        public float terraceStep = 5f;         // Vertical height of each step/block level (meters)
        [Range(0.4f, 0.95f)]
        public float terraceFlatness = 0.8f;   // Percentage of the step that is flat plateau before the cliff drop

        [Header("Legacy Rolling Dunes (传统沙丘 - 升级后混沌起伏)")]
        public float baseScale = 200f;       // Scale of large base hills
        public float baseHeight = 10f;       // Height of large base hills (now acts as absolute elevation offset!)
        public float baseNoiseHeight = 15f;  // Wave amplitude of the base hills
        public float duneSpacing = 70f;      // Base spacing of sand dunes
        public float duneHeight = 5.5f;      // Base height of sand dunes
        [Range(0f, 360f)]
        public float duneDirection = 45f;    // Wind direction angle
        public float duneWarpScale = 100f;   // Scale of dune waviness curves
        public float duneWarpStrength = 18f; // Wave curve distortion strength
        [Range(0.5f, 0.95f)]
        public float crestPosition = 0.8f;   // Dune sharp crest offset
        [Range(1f, 3f)]
        public float windwardExponent = 2f;  // Windward slope curve curvature

        [Header("Fine Sand Ripples (微观风沙纹)")]
        public float rippleSpacing = 5f;
        public float rippleHeight = 0.35f;
        [Range(0f, 360f)]
        public float rippleDirection = 55f;

        [Header("Fine Micro-Detail Settings (按钮2细化微调)")]
        public float detailScale = 10f;
        public float detailHeight = 0.3f;
        [Range(2, 24)]
        public int blendWidth = 8;            // Boundary seam weld zone (for high frequency details)

        [Header("Materials & Physics")]
        public Material terrainMaterial;

        // ── Lifecycle ──────────────────────────────────────────────────────────
        private void Start()
        {
            MeshFilter filter = GetComponent<MeshFilter>();
            if (filter == null || filter.sharedMesh == null)
            {
                Build();
            }
            EnsureShaderMigration();
        }

        private void EnsureShaderMigration()
        {
            if (!Application.isPlaying) return;

            MeshRenderer mr = GetComponent<MeshRenderer>();
            if (mr == null)
            {
                Debug.LogWarning($"[DesertTerrainChunk] '{gameObject.name}' has no MeshRenderer found!");
                return;
            }

            if (mr.sharedMaterial == null)
            {
                Debug.LogWarning($"[DesertTerrainChunk] '{gameObject.name}' has no sharedMaterial assigned at Start!");
                return;
            }

            string currentShaderName = mr.sharedMaterial.shader != null ? mr.sharedMaterial.shader.name : "NULL";
            Debug.LogWarning($"[DesertTerrainChunk] '{gameObject.name}' active shader at Start is '{currentShaderName}'");

            if (currentShaderName != "Environment/URPTriplanarEnvironment")
            {
                Shader customTriplanar = Shader.Find("Environment/URPTriplanarEnvironment");
                if (customTriplanar != null)
                {
                    Material runtimeMat = new Material(customTriplanar);
                    
                    if (mr.sharedMaterial.HasProperty("_BaseMap")) runtimeMat.SetTexture("_MainTex", mr.sharedMaterial.GetTexture("_BaseMap"));
                    else if (mr.sharedMaterial.HasProperty("_MainTex")) runtimeMat.SetTexture("_MainTex", mr.sharedMaterial.GetTexture("_MainTex"));
                    
                    if (mr.sharedMaterial.HasProperty("_BumpMap")) runtimeMat.SetTexture("_NormalMap", mr.sharedMaterial.GetTexture("_BumpMap"));
                    else if (mr.sharedMaterial.HasProperty("_NormalMap")) runtimeMat.SetTexture("_NormalMap", mr.sharedMaterial.GetTexture("_NormalMap"));
                    
                    if (mr.sharedMaterial.HasProperty("_Color")) runtimeMat.SetColor("_Color", mr.sharedMaterial.GetColor("_Color"));
                    else if (mr.sharedMaterial.HasProperty("_BaseColor")) runtimeMat.SetColor("_Color", mr.sharedMaterial.GetColor("_BaseColor"));
                    
                    mr.sharedMaterial = runtimeMat;
                    Debug.LogWarning($"[DesertTerrainChunk] SUCCESS: Auto-migrated '{gameObject.name}' material to custom Triplanar shader on scene start!");
                }
                else
                {
                    Debug.LogWarning($"[DesertTerrainChunk] WARNING: Custom triplanar shader 'Environment/URPTriplanarEnvironment' could not be found via Shader.Find!");
                }
            }
        }

        private void OnEnable()
        {
            SceneManager.sceneLoaded += OnSceneLoaded;
        }

        private void OnDisable()
        {
            SceneManager.sceneLoaded -= OnSceneLoaded;
        }

        private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            if (Application.isPlaying)
            {
                BuildSeamlessWithNeighbors(propagate: false);
            }
        }

        // ── Public API ─────────────────────────────────────────────────────────
        [ContextMenu("Rebuild Desert Chunk")]
        public void Build()
        {
            Mesh mesh = GenerateMesh();
            ApplyMesh(mesh);
        }

        /// <summary>
        /// [Button 1]: Scans neighboring chunks, negotiates base地势 in absolute world space, and builds a seamless mesh.
        /// </summary>
        public void BuildSeamlessWithNeighbors(bool propagate = true)
        {
            List<DesertTerrainChunk> neighbors = FindLoadedNeighbors(
                out var left, out var right, out var down, out var up,
                out var leftDown, out var leftUp, out var rightDown, out var rightUp
            );

            Debug.Log($"[DesertTerrainChunk] Decoupled seamless stitching scan on '{name}': Found {neighbors.Count} active neighbors.");

            Mesh mesh = GenerateSeamlessMesh(left, right, down, up, leftDown, leftUp, rightDown, rightUp);
            ApplyMesh(mesh);

            if (propagate)
            {
                foreach (var neighbor in neighbors)
                {
                    if (neighbor != null)
                    {
                        neighbor.BuildSeamlessWithNeighbors(propagate: false);

#if UNITY_EDITOR
                        if (!Application.isPlaying)
                        {
                            UnityEditor.EditorUtility.SetDirty(neighbor);
                            if (neighbor.TryGetComponent<MeshFilter>(out var filter) && filter.sharedMesh != null)
                            {
                                UnityEditor.EditorUtility.SetDirty(filter.sharedMesh);
                            }
                        }
#endif
                    }
                }
            }
        }

        public List<DesertTerrainChunk> FindLoadedNeighbors(
            out DesertTerrainChunk left, out DesertTerrainChunk right, out DesertTerrainChunk down, out DesertTerrainChunk up,
            out DesertTerrainChunk leftDown, out DesertTerrainChunk leftUp, out DesertTerrainChunk rightDown, out DesertTerrainChunk rightUp)
        {
            List<DesertTerrainChunk> neighbors = new List<DesertTerrainChunk>();
            left = right = down = up = leftDown = leftUp = rightDown = rightUp = null;

            DesertTerrainChunk[] allChunks = FindObjectsOfType<DesertTerrainChunk>();
            
            // Try robust naming-based grid coordinate resolution first
            bool parsedMe = ParseGridCoords(this.name, out int myGridX, out int myGridZ);

            float wSize = width * cellSize;
            float dSize = depth * cellSize;
            Vector3 myPos = transform.position;

            foreach (var chunk in allChunks)
            {
                if (chunk == this) continue;

                if (parsedMe && ParseGridCoords(chunk.name, out int targetGridX, out int targetGridZ))
                {
                    int dGridX = targetGridX - myGridX;
                    int dGridZ = targetGridZ - myGridZ;

                    if (dGridX == -1 && dGridZ == 0) { left = chunk; neighbors.Add(chunk); }
                    else if (dGridX == 1 && dGridZ == 0) { right = chunk; neighbors.Add(chunk); }
                    else if (dGridX == 0 && dGridZ == -1) { down = chunk; neighbors.Add(chunk); }
                    else if (dGridX == 0 && dGridZ == 1) { up = chunk; neighbors.Add(chunk); }
                    else if (dGridX == -1 && dGridZ == -1) { leftDown = chunk; neighbors.Add(chunk); }
                    else if (dGridX == -1 && dGridZ == 1) { leftUp = chunk; neighbors.Add(chunk); }
                    else if (dGridX == 1 && dGridZ == -1) { rightDown = chunk; neighbors.Add(chunk); }
                    else if (dGridX == 1 && dGridZ == 1) { rightUp = chunk; neighbors.Add(chunk); }
                }
                else
                {
                    // Fallback to coordinates physical distance check with safe 5.0f tolerance
                    Vector3 targetPos = chunk.transform.position;
                    float dX = targetPos.x - myPos.x;
                    float dZ = targetPos.z - myPos.z;

                    if (Mathf.Abs(dX - (-wSize)) < 5.0f && Mathf.Abs(dZ) < 5.0f) { left = chunk; neighbors.Add(chunk); }
                    else if (Mathf.Abs(dX - wSize) < 5.0f && Mathf.Abs(dZ) < 5.0f) { right = chunk; neighbors.Add(chunk); }
                    else if (Mathf.Abs(dX) < 5.0f && Mathf.Abs(dZ - (-dSize)) < 5.0f) { down = chunk; neighbors.Add(chunk); }
                    else if (Mathf.Abs(dX) < 5.0f && Mathf.Abs(dZ - dSize) < 5.0f) { up = chunk; neighbors.Add(chunk); }
                    else if (Mathf.Abs(dX - (-wSize)) < 5.0f && Mathf.Abs(dZ - (-dSize)) < 5.0f) { leftDown = chunk; neighbors.Add(chunk); }
                    else if (Mathf.Abs(dX - (-wSize)) < 5.0f && Mathf.Abs(dZ - dSize) < 5.0f) { leftUp = chunk; neighbors.Add(chunk); }
                    else if (Mathf.Abs(dX - wSize) < 5.0f && Mathf.Abs(dZ - (-dSize)) < 5.0f) { rightDown = chunk; neighbors.Add(chunk); }
                    else if (Mathf.Abs(dX - wSize) < 5.0f && Mathf.Abs(dZ - dSize) < 5.0f) { rightUp = chunk; neighbors.Add(chunk); }
                }
            }

            return neighbors;
        }

        private bool ParseGridCoords(string chunkName, out int gridX, out int gridZ)
        {
            gridX = 0;
            gridZ = 0;

            string cleanName = chunkName;
            int cloneIdx = cleanName.IndexOf('(');
            if (cloneIdx >= 0)
            {
                cleanName = cleanName.Substring(0, cloneIdx);
            }
            cleanName = cleanName.Trim();

            string[] parts = cleanName.Split('_');
            if (parts.Length >= 4)
            {
                if (int.TryParse(parts[2], out int x) && int.TryParse(parts[3], out int z))
                {
                    gridX = x;
                    gridZ = z;
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// [Button 2]: Overlays high-frequency micro-details on top of existing vertex heights.
        /// </summary>
        public void RefineExistingTerrain()
        {
            MeshFilter filter = GetComponent<MeshFilter>();
            if (filter == null || filter.sharedMesh == null)
            {
                Debug.LogWarning("[DesertTerrainChunk] No existing mesh to refine! Generating new mesh.");
                BuildSeamlessWithNeighbors();
                return;
            }

            Mesh mesh = filter.sharedMesh;
            Vector3[] vertices = mesh.vertices;
            Vector3[] normals = new Vector3[vertices.Length];
            Vector3 chunkOffset = transform.position;

            int vw = width + 1;
            int vd = depth + 1;

            if (vertices.Length != vw * vd)
            {
                Debug.LogWarning("[DesertTerrainChunk] Existing mesh vertex count mismatch. Regenerating seamless mesh.");
                BuildSeamlessWithNeighbors();
                return;
            }

            // Apply fine detail noise
            float ox = seed * 4.1f;
            float oz = seed * 5.9f;

            for (int z = 0; z < vd; z++)
            {
                for (int x = 0; x < vw; x++)
                {
                    int index = z * vw + x;
                    Vector3 vertex = vertices[index];

                    float worldX = chunkOffset.x + vertex.x;
                    float worldZ = chunkOffset.z + vertex.z;

                    float detail = Mathf.PerlinNoise((worldX + ox) / detailScale, (worldZ + oz) / detailScale) * detailHeight;
                    vertex.y += detail;
                    vertices[index] = vertex;
                }
            }

            // Seamless analytical normals based on updated vertices
            for (int z = 0; z < vd; z++)
            {
                for (int x = 0; x < vw; x++)
                {
                    int index = z * vw + x;

                    float hL = GetHeightFromArray(x - 1, z, vertices);
                    float hR = GetHeightFromArray(x + 1, z, vertices);
                    float hD = GetHeightFromArray(x, z - 1, vertices);
                    float hU = GetHeightFromArray(x, z + 1, vertices);

                    Vector3 tangentX = new Vector3(cellSize * 2f, hR - hL, 0);
                    Vector3 tangentZ = new Vector3(0, hU - hD, cellSize * 2f);
                    normals[index] = Vector3.Cross(tangentZ, tangentX).normalized;
                }
            }

            mesh.vertices = vertices;
            mesh.normals = normals;
            mesh.RecalculateBounds();

            if (TryGetComponent<MeshCollider>(out var col))
            {
                col.sharedMesh = null;
                col.sharedMesh = mesh;
            }

            Debug.Log("[DesertTerrainChunk] Refined existing terrain with fine micro-details.");
        }

        public float GetVertexHeightAtLocal(int x, int z)
        {
            MeshFilter filter = GetComponent<MeshFilter>();
            if (filter == null || filter.sharedMesh == null)
            {
                Vector3 chunkOffset = transform.position;
                return SampleHeight(chunkOffset.x + x * cellSize, chunkOffset.z + z * cellSize);
            }

            int vw = width + 1;
            Vector3[] verts = filter.sharedMesh.vertices;
            int idx = z * vw + x;
            if (idx >= 0 && idx < verts.Length)
            {
                return transform.position.y + verts[idx].y;
            }

            return transform.position.y + SampleHeight(transform.position.x + x * cellSize, transform.position.z + z * cellSize);
        }

        /// <summary>
        /// Samples only the macroscopic base height trend (low frequency).
        /// </summary>
        public float SampleBaseHeight(float worldX, float worldZ)
        {
            if (enableMinecraftHills)
            {
                float fbmVal = 0f;
                float amplitude = 1f;
                float frequency = 1f / hillNoiseScale;
                float maxAmp = 0f;
                float ox = seed * 1.7f;
                float oz = seed * 2.3f;

                for (int i = 0; i < octaves; i++)
                {
                    float n = Mathf.PerlinNoise((worldX * frequency + ox), (worldZ * frequency + oz)) * 2f - 1f;
                    float ridge = 1.0f - Mathf.Abs(n);
                    float mixedVal = Mathf.Lerp(n, ridge * 2f - 1f, 0.42f);
                    fbmVal += mixedVal * amplitude;
                    maxAmp += amplitude;
                    amplitude *= gain;
                    frequency *= lacunarity;
                }

                float normalizedHeight = (fbmVal / maxAmp) * 0.5f + 0.5f;
                float rawHeight = baseHeight + normalizedHeight * hillMaxHeight;

                if (enableTerracing && terraceStep > 0.1f)
                {
                    float y = rawHeight / terraceStep;
                    float floorY = Mathf.Floor(y);
                    float fract = y - floorY;
                    float smoothFract = fract < terraceFlatness ? 0f : Mathf.SmoothStep(0f, 1f, (fract - terraceFlatness) / (1f - terraceFlatness));
                    return (floorY + smoothFract) * terraceStep;
                }
                return rawHeight;
            }
            else
            {
                float ox = seed * 1.7f;
                float oz = seed * 2.3f;
                return baseHeight + Mathf.PerlinNoise((worldX + ox) / baseScale, (worldZ + oz) / baseScale) * baseNoiseHeight;
            }
        }

        /// <summary>
        /// Samples only the high-frequency sand dunes and wind-swept ripples.
        /// </summary>
        public float SampleDetailHeight(float worldX, float worldZ, float baseHForRipples)
        {
            float detailH = 0f;

            if (!enableMinecraftHills)
            {
                float ox = seed * 1.7f;
                float oz = seed * 2.3f;

                // 2. Wind Direction Wandering
                float angleWander = (Mathf.PerlinNoise((worldX + seed * 8.7f) / 380f, (worldZ + seed * 12.3f) / 380f) * 2f - 1f) * 30f;
                float theta = (duneDirection + angleWander) * Mathf.Deg2Rad;
                Vector2 windDir = new Vector2(Mathf.Cos(theta), Mathf.Sin(theta));

                // 3. Spacing Modulation
                float spacingMod = Mathf.PerlinNoise((worldX + seed * 19.3f) / 450f, (worldZ + seed * 23.7f) / 450f);
                float activeDuneSpacing = duneSpacing * Mathf.Lerp(0.65f, 1.45f, spacingMod);

                // 4. Height Modulation
                float heightMod = Mathf.PerlinNoise((worldX - seed * 31.1f) / 280f, (worldZ - seed * 27.9f) / 280f);
                float activeDuneHeight = duneHeight * Mathf.Lerp(0.4f, 1.4f, heightMod);

                // 5. Asymmetrical Main Sand Wave
                float coord = worldX * windDir.x + worldZ * windDir.y;
                float warp = Mathf.PerlinNoise((worldX + seed * 3.1f) / duneWarpScale, (worldZ + seed * 4.7f) / duneWarpScale) * duneWarpStrength;
                float duneCoord = (coord + warp) / activeDuneSpacing;
                float fraction = duneCoord - Mathf.Floor(duneCoord);

                float duneH = fraction < crestPosition 
                    ? Mathf.Pow(fraction / crestPosition, windwardExponent) * activeDuneHeight
                    : ((1f - fraction) / (1f - crestPosition)) * activeDuneHeight;

                // 6. Secondary Cross dunes
                float secondaryTheta = (duneDirection + 35f) * Mathf.Deg2Rad;
                Vector2 secWindDir = new Vector2(Mathf.Cos(secondaryTheta), Mathf.Sin(secondaryTheta));
                float secCoord = worldX * secWindDir.x + worldZ * secWindDir.y;
                float secWarp = Mathf.PerlinNoise((worldX + seed * 14.1f) / (duneWarpScale * 0.5f), (worldZ - seed * 11.7f) / (duneWarpScale * 0.5f)) * (duneWarpStrength * 0.4f);
                float secDuneCoord = (secCoord + secWarp) / (activeDuneSpacing * 0.45f);
                float secFraction = secDuneCoord - Mathf.Floor(secDuneCoord);

                float secDuneH = secFraction < 0.75f
                    ? Mathf.Pow(secFraction / 0.75f, 2f) * (activeDuneHeight * 0.35f)
                    : ((1f - secFraction) / 0.25f) * (activeDuneHeight * 0.35f);

                detailH = duneH + secDuneH;
            }

            // Wind-swept ripples
            float rippleTheta = rippleDirection * Mathf.Deg2Rad;
            Vector2 rippleDir = new Vector2(Mathf.Cos(rippleTheta), Mathf.Sin(rippleTheta));
            float rippleCoord = (worldX * rippleDir.x + worldZ * rippleDir.y) / rippleSpacing;
            float rippleWarp = Mathf.PerlinNoise(worldX / 8f, worldZ / 8f) * 0.3f;
            float rippleVal = Mathf.Sin((rippleCoord + rippleWarp) * Mathf.PI * 2f);

            float rippleFactor = 1.0f;
            if (enableMinecraftHills && enableTerracing)
            {
                float yVal = baseHForRipples / terraceStep;
                float fractVal = yVal - Mathf.Floor(yVal);
                if (fractVal > 0.05f) rippleFactor = 0.05f;
            }

            float rippleH = (rippleVal * 0.5f + 0.5f) * rippleHeight * rippleFactor;
            return detailH + rippleH;
        }

        public float SampleHeight(float worldX, float worldZ)
        {
            float baseH = SampleBaseHeight(worldX, worldZ);
            return baseH + SampleDetailHeight(worldX, worldZ, baseH);
        }

        // ── Mesh Generation ────────────────────────────────────────────────────
        public Mesh GenerateMesh()
        {
            return GenerateSeamlessMesh(null, null, null, null, null, null, null, null);
        }

        public Mesh GenerateSeamlessMesh(
            DesertTerrainChunk left, DesertTerrainChunk right, DesertTerrainChunk down, DesertTerrainChunk up,
            DesertTerrainChunk leftDown, DesertTerrainChunk leftUp, DesertTerrainChunk rightDown, DesertTerrainChunk rightUp)
        {
            int vw = width + 1;
            int vd = depth + 1;

            Vector3[] vertices = new Vector3[vw * vd];
            Vector2[] uvs = new Vector2[vw * vd];
            Vector3[] normals = new Vector3[vw * vd];
            int[] triangles = new int[width * depth * 6];

            // 1. Calculate stitched heights for all vertices
            for (int z = 0; z < vd; z++)
            {
                for (int x = 0; x < vw; x++)
                {
                    float localX = x * cellSize;
                    float localZ = z * cellSize;

                    float worldX = transform.position.x + localX;
                    float worldZ = transform.position.z + localZ;

                    float worldH = GetStitchedWorldHeightAt(worldX, worldZ, left, right, down, up, leftDown, leftUp, rightDown, rightUp);
                    float h = worldH - transform.position.y;

                    int index = z * vw + x;
                    vertices[index] = new Vector3(localX, h, localZ);
                    uvs[index] = new Vector2((float)x / width, (float)z / depth);
                }
            }

            // 2. Compute seamless analytical normals
            for (int z = 0; z < vd; z++)
            {
                for (int x = 0; x < vw; x++)
                {
                    float localX = x * cellSize;
                    float localZ = z * cellSize;

                    float worldX = transform.position.x + localX;
                    float worldZ = transform.position.z + localZ;

                    int index = z * vw + x;

                    float hL = GetStitchedWorldHeightAt(worldX - cellSize, worldZ, left, right, down, up, leftDown, leftUp, rightDown, rightUp);
                    float hR = GetStitchedWorldHeightAt(worldX + cellSize, worldZ, left, right, down, up, leftDown, leftUp, rightDown, rightUp);
                    float hD = GetStitchedWorldHeightAt(worldX, worldZ - cellSize, left, right, down, up, leftDown, leftUp, rightDown, rightUp);
                    float hU = GetStitchedWorldHeightAt(worldX, worldZ + cellSize, left, right, down, up, leftDown, leftUp, rightDown, rightUp);

                    Vector3 tangentX = new Vector3(cellSize * 2f, hR - hL, 0);
                    Vector3 tangentZ = new Vector3(0, hU - hD, cellSize * 2f);
                    normals[index] = Vector3.Cross(tangentZ, tangentX).normalized;
                }
            }

            // 3. Build triangles
            int t = 0;
            for (int z = 0; z < depth; z++)
            {
                for (int x = 0; x < width; x++)
                {
                    int bl = z * vw + x;
                    int br = bl + 1;
                    int tl = bl + vw;
                    int tr = tl + 1;

                    triangles[t++] = bl;
                    triangles[t++] = tl;
                    triangles[t++] = tr;

                    triangles[t++] = bl;
                    triangles[t++] = tr;
                    triangles[t++] = br;
                }
            }

            Mesh mesh = new Mesh { name = "DesertTerrainChunk" };

            if (vertices.Length > 65535)
                mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;

            mesh.vertices = vertices;
            mesh.uv = uvs;
            mesh.normals = normals;
            mesh.triangles = triangles;
            mesh.RecalculateBounds();

            return mesh;
        }

        public float GetStitchedWorldHeightAt(
            float wX, float wZ,
            DesertTerrainChunk l, DesertTerrainChunk r, DesertTerrainChunk d, DesertTerrainChunk u,
            DesertTerrainChunk ld, DesertTerrainChunk lu, DesertTerrainChunk rd, DesertTerrainChunk ru)
        {
            float localX = wX - transform.position.x;
            float localZ = wZ - transform.position.z;

            float wSize = width * cellSize;
            float dSize = depth * cellSize;

            // ── PART 1: Base Elevation Blending ────────────────────────────────────
            float A = this.baseHeight;

            // Edge weights
            float blendRangeX = wSize * 0.5f;
            float blendRangeZ = dSize * 0.5f;

            float w_L = localX < blendRangeX ? Mathf.SmoothStep(1f, 0f, localX / blendRangeX) : 0f;
            float w_R = localX > wSize - blendRangeX ? Mathf.SmoothStep(1f, 0f, (wSize - localX) / blendRangeX) : 0f;
            float w_D = localZ < blendRangeZ ? Mathf.SmoothStep(1f, 0f, localZ / blendRangeZ) : 0f;
            float w_U = localZ > dSize - blendRangeZ ? Mathf.SmoothStep(1f, 0f, (dSize - localZ) / blendRangeZ) : 0f;

            // Corner weights (intersection of edge weights)
            float w_LD = w_L * w_D;
            float w_LU = w_L * w_U;
            float w_RD = w_R * w_D;
            float w_RU = w_R * w_U;

            // Subtract corner weights from edge weights to ensure strict transition regions
            float w_left_only = Mathf.Max(0f, w_L - (w_LD + w_LU));
            float w_right_only = Mathf.Max(0f, w_R - (w_RD + w_RU));
            float w_down_only = Mathf.Max(0f, w_D - (w_LD + w_RD));
            float w_up_only = Mathf.Max(0f, w_U - (w_LU + w_RU));

            // Base elevation target values
            float hL = l != null ? (A + l.baseHeight) * 0.5f : A;
            float hR = r != null ? (A + r.baseHeight) * 0.5f : A;
            float hD = d != null ? (A + d.baseHeight) * 0.5f : A;
            float hU = u != null ? (A + u.baseHeight) * 0.5f : A;

            // Robust LD corner base average
            float sumLD = A;
            float countLD = 1f;
            if (l != null) { sumLD += l.baseHeight; countLD += 1f; }
            if (d != null) { sumLD += d.baseHeight; countLD += 1f; }
            if (ld != null) { sumLD += ld.baseHeight; countLD += 1f; }
            float hLD = sumLD / countLD;

            // Robust LU corner base average
            float sumLU = A;
            float countLU = 1f;
            if (l != null) { sumLU += l.baseHeight; countLU += 1f; }
            if (u != null) { sumLU += u.baseHeight; countLU += 1f; }
            if (lu != null) { sumLU += lu.baseHeight; countLU += 1f; }
            float hLU = sumLU / countLU;

            // Robust RD corner base average
            float sumRD = A;
            float countRD = 1f;
            if (r != null) { sumRD += r.baseHeight; countRD += 1f; }
            if (d != null) { sumRD += d.baseHeight; countRD += 1f; }
            if (rd != null) { sumRD += rd.baseHeight; countRD += 1f; }
            float hRD = sumRD / countRD;

            // Robust RU corner base average
            float sumRU = A;
            float countRU = 1f;
            if (r != null) { sumRU += r.baseHeight; countRU += 1f; }
            if (u != null) { sumRU += u.baseHeight; countRU += 1f; }
            if (ru != null) { sumRU += ru.baseHeight; countRU += 1f; }
            float hRU = sumRU / countRU;

            float elevSum = 0f;
            float elevWeightSum = 0f;

            // Accumulate active edge regions
            if (l != null && w_left_only > 0f) { elevSum += hL * w_left_only; elevWeightSum += w_left_only; }
            if (r != null && w_right_only > 0f) { elevSum += hR * w_right_only; elevWeightSum += w_right_only; }
            if (d != null && w_down_only > 0f) { elevSum += hD * w_down_only; elevWeightSum += w_down_only; }
            if (u != null && w_up_only > 0f) { elevSum += hU * w_up_only; elevWeightSum += w_up_only; }

            // Accumulate active corner regions
            if ((l != null || d != null || ld != null) && w_LD > 0f) { elevSum += hLD * w_LD; elevWeightSum += w_LD; }
            if ((l != null || u != null || lu != null) && w_LU > 0f) { elevSum += hLU * w_LU; elevWeightSum += w_LU; }
            if ((r != null || d != null || rd != null) && w_RD > 0f) { elevSum += hRD * w_RD; elevWeightSum += w_RD; }
            if ((r != null || u != null || ru != null) && w_RU > 0f) { elevSum += hRU * w_RU; elevWeightSum += w_RU; }

            float elev = A;
            if (elevWeightSum > 0f)
            {
                float finalWeight = Mathf.Min(elevWeightSum, 1f);
                float averageBlend = elevSum / elevWeightSum;
                elev = Mathf.Lerp(A, averageBlend, finalWeight);
            }

            // ── PART 2: Local Features Blending ────────────────────────────────────
            float featNatural = SampleHeight(wX, wZ) - A;

            float weldRangeX = blendWidth * cellSize;
            float weldRangeZ = blendWidth * cellSize;

            float w_L_feat = localX < weldRangeX ? Mathf.SmoothStep(1f, 0f, localX / weldRangeX) : 0f;
            float w_R_feat = localX > wSize - weldRangeX ? Mathf.SmoothStep(1f, 0f, (wSize - localX) / weldRangeX) : 0f;
            float w_D_feat = localZ < weldRangeZ ? Mathf.SmoothStep(1f, 0f, localZ / weldRangeZ) : 0f;
            float w_U_feat = localZ > dSize - weldRangeZ ? Mathf.SmoothStep(1f, 0f, (dSize - localZ) / weldRangeZ) : 0f;

            float w_LD_feat = w_L_feat * w_D_feat;
            float w_LU_feat = w_L_feat * w_U_feat;
            float w_RD_feat = w_R_feat * w_D_feat;
            float w_RU_feat = w_R_feat * w_U_feat;

            float w_left_feat = Mathf.Max(0f, w_L_feat - (w_LD_feat + w_LU_feat));
            float w_right_feat = Mathf.Max(0f, w_R_feat - (w_RD_feat + w_RU_feat));
            float w_down_feat = Mathf.Max(0f, w_D_feat - (w_LD_feat + w_RD_feat));
            float w_up_feat = Mathf.Max(0f, w_U_feat - (w_LU_feat + w_RU_feat));

            float fL = l != null ? l.SampleHeight(wX, wZ) - l.baseHeight : featNatural;
            float fR = r != null ? r.SampleHeight(wX, wZ) - r.baseHeight : featNatural;
            float fD = d != null ? d.SampleHeight(wX, wZ) - d.baseHeight : featNatural;
            float fU = u != null ? u.SampleHeight(wX, wZ) - u.baseHeight : featNatural;

            float fLD = ld != null ? ld.SampleHeight(wX, wZ) - ld.baseHeight : featNatural;
            float fLU = lu != null ? lu.SampleHeight(wX, wZ) - lu.baseHeight : featNatural;
            float fRD = rd != null ? rd.SampleHeight(wX, wZ) - rd.baseHeight : featNatural;
            float fRU = ru != null ? ru.SampleHeight(wX, wZ) - ru.baseHeight : featNatural;

            float featSum = 0f;
            float featWeightSum = 0f;

            if (l != null && w_left_feat > 0f) { featSum += (featNatural + fL) * 0.5f * w_left_feat; featWeightSum += w_left_feat; }
            if (r != null && w_right_feat > 0f) { featSum += (featNatural + fR) * 0.5f * w_right_feat; featWeightSum += w_right_feat; }
            if (d != null && w_down_feat > 0f) { featSum += (featNatural + fD) * 0.5f * w_down_feat; featWeightSum += w_down_feat; }
            if (u != null && w_up_feat > 0f) { featSum += (featNatural + fU) * 0.5f * w_up_feat; featWeightSum += w_up_feat; }

            // Robust LD corner feature average
            float fSumLD = featNatural;
            float fCountLD = 1f;
            if (l != null) { fSumLD += fL; fCountLD += 1f; }
            if (d != null) { fSumLD += fD; fCountLD += 1f; }
            if (ld != null) { fSumLD += fLD; fCountLD += 1f; }
            float fLD_corn = fSumLD / fCountLD;

            // Robust LU corner feature average
            float fSumLU = featNatural;
            float fCountLU = 1f;
            if (l != null) { fSumLU += fL; fCountLU += 1f; }
            if (u != null) { fSumLU += fU; fCountLU += 1f; }
            if (lu != null) { fSumLU += fLU; fCountLU += 1f; }
            float fLU_corn = fSumLU / fCountLU;

            // Robust RD corner feature average
            float fSumRD = featNatural;
            float fCountRD = 1f;
            if (r != null) { fSumRD += fR; fCountRD += 1f; }
            if (d != null) { fSumRD += fD; fCountRD += 1f; }
            if (rd != null) { fSumRD += fRD; fCountRD += 1f; }
            float fRD_corn = fSumRD / fCountRD;

            // Robust RU corner feature average
            float fSumRU = featNatural;
            float fCountRU = 1f;
            if (r != null) { fSumRU += fR; fCountRU += 1f; }
            if (u != null) { fSumRU += fU; fCountRU += 1f; }
            if (ru != null) { fSumRU += fRU; fCountRU += 1f; }
            float fRU_corn = fSumRU / fCountRU;

            if ((l != null || d != null || ld != null) && w_LD_feat > 0f) { featSum += fLD_corn * w_LD_feat; featWeightSum += w_LD_feat; }
            if ((l != null || u != null || lu != null) && w_LU_feat > 0f) { featSum += fLU_corn * w_LU_feat; featWeightSum += w_LU_feat; }
            if ((r != null || d != null || rd != null) && w_RD_feat > 0f) { featSum += fRD_corn * w_RD_feat; featWeightSum += w_RD_feat; }
            if ((r != null || u != null || ru != null) && w_RU_feat > 0f) { featSum += fRU_corn * w_RU_feat; featWeightSum += w_RU_feat; }

            float features = featNatural;
            if (featWeightSum > 0f)
            {
                float finalWeight = Mathf.Min(featWeightSum, 1f);
                float averageBlend = featSum / featWeightSum;
                features = Mathf.Lerp(featNatural, averageBlend, finalWeight);
            }

            return elev + features;
        }

        public float GetWorldHeightOfVertex(int x, int z)
        {
            MeshFilter filter = GetComponent<MeshFilter>();
            if (filter == null || filter.sharedMesh == null)
            {
                return transform.position.y + SampleHeight(transform.position.x + x * cellSize, transform.position.z + z * cellSize);
            }

            int vw = width + 1;
            Vector3[] verts = filter.sharedMesh.vertices;
            int idx = z * vw + x;
            if (idx >= 0 && idx < verts.Length)
            {
                return transform.position.y + verts[idx].y;
            }

            return transform.position.y + SampleHeight(transform.position.x + x * cellSize, transform.position.z + z * cellSize);
        }

        private float GetHeightFromArray(int x, int z, Vector3[] vertices)
        {
            int vw = width + 1;
            int cx = Mathf.Clamp(x, 0, width);
            int cz = Mathf.Clamp(z, 0, depth);
            return vertices[cz * vw + cx].y;
        }

        private void ApplyMesh(Mesh mesh)
        {
            if (TryGetComponent<MeshFilter>(out var filter))
            {
#if UNITY_EDITOR
                string assetPath = UnityEditor.AssetDatabase.GetAssetPath(filter.sharedMesh);
                bool isWriteableCustomAsset = !string.IsNullOrEmpty(assetPath) && assetPath.EndsWith(".asset") && assetPath.Contains("/Meshes/");

                if (filter.sharedMesh != null && filter.sharedMesh != mesh && isWriteableCustomAsset)
#else
                if (filter.sharedMesh != null && filter.sharedMesh != mesh)
#endif
                {
                    // 🌟 In-place mutate the existing persistent mesh asset to preserve Unity GUID & Scene references!
                    filter.sharedMesh.Clear();
                    filter.sharedMesh.indexFormat = mesh.indexFormat;
                    filter.sharedMesh.vertices = mesh.vertices;
                    filter.sharedMesh.uv = mesh.uv;
                    filter.sharedMesh.normals = mesh.normals;
                    filter.sharedMesh.triangles = mesh.triangles;
                    filter.sharedMesh.RecalculateBounds();

                    // Force CPU-side changes to be uploaded to GPU memory immediately!
                    filter.sharedMesh.UploadMeshData(false);

                    // Destroy the temporary intermediate mesh to prevent memory leaks in the editor
                    DestroyImmediate(mesh);

                    // Re-assign the mutated persistent sharedMesh for downstream components (like collider)
                    mesh = filter.sharedMesh;

                    // Trigger Unity's internal setter modification event to force immediate Scene View repaint!
                    filter.sharedMesh = null;
                    filter.sharedMesh = mesh;
                }
                else
                {
                    filter.sharedMesh = mesh;
                }
            }

            if (TryGetComponent<MeshCollider>(out var col))
            {
                col.sharedMesh = null;
                col.sharedMesh = mesh;
            }

            if (TryGetComponent<MeshRenderer>(out var mr))
            {
                if (terrainMaterial != null)
                {
                    mr.sharedMaterial = terrainMaterial;
                }
                else
                {
                    bool needsFallback = mr.sharedMaterial == null || 
                                         mr.sharedMaterial.shader.name == "Standard" || 
                                         mr.sharedMaterial.shader.name == "Hidden/InternalErrorShader" ||
                                         (!mr.sharedMaterial.shader.name.Contains("Universal Render Pipeline") && 
                                          !mr.sharedMaterial.shader.name.Contains("URP") && 
                                          !mr.sharedMaterial.shader.name.Contains("Custom") && 
                                          !mr.sharedMaterial.shader.name.Contains("Triplanar") && 
                                          !mr.sharedMaterial.shader.name.Contains("Terrain"));

                    if (needsFallback)
                    {
                        Shader customTriplanar = Shader.Find("Environment/URPTriplanarEnvironment");
                        if (customTriplanar != null)
                        {
                            mr.sharedMaterial = new Material(customTriplanar);
                        }
                        else
                        {
                            mr.sharedMaterial = new Material(Shader.Find("Universal Render Pipeline/Lit"));
                        }
                    }
                }

                // 🌟 First Insurance: Force migrate any assigned material to the custom Triplanar sand shader in Play Mode.
                // Keeps their existing textures (Albedo, Bump/Normal) and color tint completely intact!
                if (Application.isPlaying && mr.sharedMaterial != null && mr.sharedMaterial.shader.name != "Environment/URPTriplanarEnvironment")
                {
                    Shader customTriplanar = Shader.Find("Environment/URPTriplanarEnvironment");
                    if (customTriplanar != null)
                    {
                        Material runtimeMat = new Material(customTriplanar);
                        
                        if (mr.sharedMaterial.HasProperty("_BaseMap")) runtimeMat.SetTexture("_MainTex", mr.sharedMaterial.GetTexture("_BaseMap"));
                        else if (mr.sharedMaterial.HasProperty("_MainTex")) runtimeMat.SetTexture("_MainTex", mr.sharedMaterial.GetTexture("_MainTex"));
                        
                        if (mr.sharedMaterial.HasProperty("_BumpMap")) runtimeMat.SetTexture("_NormalMap", mr.sharedMaterial.GetTexture("_BumpMap"));
                        else if (mr.sharedMaterial.HasProperty("_NormalMap")) runtimeMat.SetTexture("_NormalMap", mr.sharedMaterial.GetTexture("_NormalMap"));
                        
                        if (mr.sharedMaterial.HasProperty("_Color")) runtimeMat.SetColor("_Color", mr.sharedMaterial.GetColor("_Color"));
                        else if (mr.sharedMaterial.HasProperty("_BaseColor")) runtimeMat.SetColor("_Color", mr.sharedMaterial.GetColor("_BaseColor"));
                        
                        mr.sharedMaterial = runtimeMat;
                    }
                }
            }
        }

        private void OnValidate()
        {
            if (width < 2) width = 2;
            if (depth < 2) depth = 2;
            if (cellSize < 0.1f) cellSize = 0.1f;
            if (baseScale < 1f) baseScale = 1f;
            if (duneSpacing < 1f) duneSpacing = 1f;
            if (duneWarpScale < 1f) duneWarpScale = 1f;
            if (rippleSpacing < 0.1f) rippleSpacing = 0.1f;
            if (crestPosition <= 0.05f) crestPosition = 0.05f;
            if (crestPosition >= 0.95f) crestPosition = 0.95f;
            if (windwardExponent < 1f) windwardExponent = 1f;
            if (blendWidth < 1) blendWidth = 1;
            if (detailScale < 0.1f) detailScale = 0.1f;

            if (hillNoiseScale < 1f) hillNoiseScale = 1f;
            if (octaves < 1) octaves = 1;
            if (octaves > 8) octaves = 8;
            if (terraceStep < 0.1f) terraceStep = 0.1f;
            if (terraceFlatness < 0.1f) terraceFlatness = 0.1f;
            if (terraceFlatness >= 0.99f) terraceFlatness = 0.99f;
        }
    }
}
