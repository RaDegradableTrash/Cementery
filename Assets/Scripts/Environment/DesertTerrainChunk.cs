using System.Collections;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace EnvironmentSystem
{
    /// <summary>
    /// Global O(1) registry for all active DesertTerrainChunk instances, keyed by grid coordinate.
    /// Eliminates FindObjectsOfType&lt;DesertTerrainChunk&gt;() calls, which are O(n) over all scene objects.
    /// Each chunk self-registers on OnEnable and unregisters on OnDisable.
    /// </summary>
    public static class ChunkRegistry
    {
        private static readonly Dictionary<Vector2Int, DesertTerrainChunk> _chunks
            = new Dictionary<Vector2Int, DesertTerrainChunk>();

        public static void Register(DesertTerrainChunk chunk)
        {
            if (chunk == null) return;
            _chunks[chunk.GridCoord] = chunk;
        }

        public static void Unregister(DesertTerrainChunk chunk)
        {
            if (chunk == null) return;
            Vector2Int key = chunk.GridCoord;
            if (_chunks.TryGetValue(key, out var existing) && existing == chunk)
                _chunks.Remove(key);
        }

        public static DesertTerrainChunk Get(int gridX, int gridZ)
        {
            _chunks.TryGetValue(new Vector2Int(gridX, gridZ), out var chunk);
            return chunk;
        }

        public static DesertTerrainChunk Get(Vector2Int coord)
        {
            _chunks.TryGetValue(coord, out var chunk);
            return chunk;
        }

        public static IReadOnlyDictionary<Vector2Int, DesertTerrainChunk> All => _chunks;

        public static void Clear() => _chunks.Clear();
    }

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
        public int snowResolutionMultiplier = 4; // Modifier for high-res snow mesh
        
        [Tooltip("If true, the chunk will lock its current saved mesh asset and skip rebuilding from noise at runtime.")]
        public bool useSavedMeshAsset = true;

        [Header("Global Seed")]
        public int seed = 42;

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

        /// <summary>Parsed grid coordinate for this chunk (from scene name e.g. Desert_Chunk_3_-2 → (3,-2)).</summary>
        public Vector2Int GridCoord
        {
            get
            {
                if (ParseGridCoords(name, out int gx, out int gz))
                    return new Vector2Int(gx, gz);
                // Fallback: derive from world position
                float wx = width * cellSize;
                float wz = depth * cellSize;
                return new Vector2Int(
                    Mathf.RoundToInt(transform.position.x / wx),
                    Mathf.RoundToInt(transform.position.z / wz));
            }
        }

        // Tracks whether an async build is running; queued flag ensures a re-build fires
        // when a new neighbor loads while this chunk is mid-build (prevents missed stitches).
        private bool _asyncBuildRunning = false;
        private bool _asyncBuildQueued  = false;

        private Coroutine _lodCoroutine = null;
        private Material _lodMaterial = null;
        private Material _highQualityMaterial = null;
        private bool _isUsingLOD = false;



    public void UpdateResolution()
    {
        if (useSavedMeshAsset) return; // 🚀 Skip rebuilding if sculpted
        if (_asyncBuildRunning) return;

        Transform snowLayerTransform = transform.Find("SnowLayer");
        if (snowLayerTransform != null)
        {
            MeshFilter filter = snowLayerTransform.GetComponent<MeshFilter>();
            if (filter != null && filter.sharedMesh != null)
            {
                // Check if current mesh matches expected high-res vertex count
                int expectedVertices = (width * snowResolutionMultiplier + 1) * (depth * snowResolutionMultiplier + 1);
                // Also account for skirts
                int expectedTotal = expectedVertices + 2 * (depth * snowResolutionMultiplier + 1) + 2 * (width * snowResolutionMultiplier + 1);
                
                if (filter.sharedMesh.vertexCount == expectedTotal)
                {
                    return; // Already correct resolution, no need to rebuild
                }
            }
        }
        
        // Needs rebuild
        StartCoroutine(BuildAsyncCoroutine(delayFrames: 1));
    }

        private void OnEnable()
        {
            SceneManager.sceneLoaded += OnSceneLoaded;
            ChunkRegistry.Register(this);
            UpdateResolution();
        }

        private void Start()
        {
            MeshFilter filter = GetComponent<MeshFilter>();
            if (filter == null || filter.sharedMesh == null)
            {
                Build();
            }
            else if (!useSavedMeshAsset) // 🚀 Skip rebuilding if sculpted
            {
                UpdateResolution(); // Ensure high-res mesh is built if missing
            }
            else
            {
                // 🚀 For sculpted chunks, explicitly assign the mesh to the collider and sync snow layer at startup
                if (TryGetComponent<MeshCollider>(out var col))
                {
                    col.sharedMesh = null;
                    col.sharedMesh = filter.sharedMesh;
                }
                SyncSnowLayer();
                SyncRoadOverlay();
            }

            _highQualityMaterial = terrainMaterial;
            if (_highQualityMaterial == null)
            {
                _highQualityMaterial = Resources.Load<Material>("GravelTerrain");
                terrainMaterial = _highQualityMaterial;
            }
            _lodMaterial = Resources.Load<Material>("GravelTerrain_LOD");

            if (TryGetComponent<MeshRenderer>(out var mr))
            {
                if (_highQualityMaterial != null)
                {
                    mr.sharedMaterial = _highQualityMaterial;
                }
            }

            if (Application.isPlaying)
            {
                _lodCoroutine = StartCoroutine(LODCheckLoop());
            }
        }

        private IEnumerator LODCheckLoop()
        {
            var wait = new WaitForSeconds(1.5f + Random.Range(0f, 0.5f)); // staggered to prevent spikes
            MeshRenderer mr = GetComponent<MeshRenderer>();
            if (mr == null) yield break;

            while (true)
            {
                yield return wait;
                
                Camera mainCam = Camera.main;
                if (mainCam != null)
                {
                    float dist = Vector3.Distance(transform.position + new Vector3(width * cellSize * 0.5f, 0, depth * cellSize * 0.5f), mainCam.transform.position);
                    // Standard LOD threshold: 220 meters
                    if (dist > 220f)
                    {
                        if (!_isUsingLOD && _lodMaterial != null)
                        {
                            mr.sharedMaterial = _lodMaterial;
                            _isUsingLOD = true;
                        }
                    }
                    else
                    {
                        if (_isUsingLOD && _highQualityMaterial != null)
                        {
                            mr.sharedMaterial = _highQualityMaterial;
                            _isUsingLOD = false;
                        }
                    }
                }
            }
        }

        private void OnDisable()
        {
            SceneManager.sceneLoaded -= OnSceneLoaded;
            ChunkRegistry.Unregister(this);
            _asyncBuildRunning = false;
            if (_lodCoroutine != null)
            {
                StopCoroutine(_lodCoroutine);
                _lodCoroutine = null;
            }
            _isUsingLOD = false;
        }

        private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            if (!Application.isPlaying) return;
            if (useSavedMeshAsset) return; // 🚀 Skip rebuilding if sculpted
            if (!_asyncBuildRunning)
            {
                StartCoroutine(BuildAsyncCoroutine(delayFrames: 2));
            }
            else
            {
                // A build is already in flight; flag it so a re-stitch runs when it finishes.
                _asyncBuildQueued = true;
            }
        }

        // ── Public API ─────────────────────────────────────────────────────────
        [ContextMenu("Rebuild Desert Chunk")]
        public void Build()
        {
            if (useSavedMeshAsset) return; // 🚀 Skip rebuilding if sculpted
            Mesh mesh = GenerateMesh();
            ApplyMesh(mesh);
        }

        /// <summary>
        /// [Button 1]: Scans neighboring chunks, negotiates base地势 in absolute world space, and builds a seamless mesh.
        /// </summary>
        public void BuildSeamlessWithNeighbors(bool propagate = true)
        {
            if (useSavedMeshAsset) return; // 🚀 Skip rebuilding if sculpted

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

            // O(1) ChunkRegistry lookups instead of FindObjectsOfType (O(n) scene scan)
            if (ParseGridCoords(name, out int myGridX, out int myGridZ))
            {
                left      = ChunkRegistry.Get(myGridX - 1, myGridZ);
                right     = ChunkRegistry.Get(myGridX + 1, myGridZ);
                down      = ChunkRegistry.Get(myGridX,     myGridZ - 1);
                up        = ChunkRegistry.Get(myGridX,     myGridZ + 1);
                leftDown  = ChunkRegistry.Get(myGridX - 1, myGridZ - 1);
                leftUp    = ChunkRegistry.Get(myGridX - 1, myGridZ + 1);
                rightDown = ChunkRegistry.Get(myGridX + 1, myGridZ - 1);
                rightUp   = ChunkRegistry.Get(myGridX + 1, myGridZ + 1);
            }
            else
            {
                // Fallback: position-based search (only when name parsing fails)
                float wSize = width * cellSize;
                float dSize = depth * cellSize;
                Vector3 myPos = transform.position;

                foreach (var kv in ChunkRegistry.All)
                {
                    var chunk = kv.Value;
                    if (chunk == null || chunk == this) continue;
                    Vector3 targetPos = chunk.transform.position;
                    float dX = targetPos.x - myPos.x;
                    float dZ = targetPos.z - myPos.z;

                    if (Mathf.Abs(dX - (-wSize)) < 5.0f && Mathf.Abs(dZ) < 5.0f)       { left      = chunk; }
                    else if (Mathf.Abs(dX - wSize) < 5.0f && Mathf.Abs(dZ) < 5.0f)    { right     = chunk; }
                    else if (Mathf.Abs(dX) < 5.0f && Mathf.Abs(dZ - (-dSize)) < 5.0f) { down      = chunk; }
                    else if (Mathf.Abs(dX) < 5.0f && Mathf.Abs(dZ - dSize) < 5.0f)    { up        = chunk; }
                    else if (Mathf.Abs(dX - (-wSize)) < 5.0f && Mathf.Abs(dZ - (-dSize)) < 5.0f) { leftDown  = chunk; }
                    else if (Mathf.Abs(dX - (-wSize)) < 5.0f && Mathf.Abs(dZ - dSize) < 5.0f)    { leftUp    = chunk; }
                    else if (Mathf.Abs(dX - wSize) < 5.0f && Mathf.Abs(dZ - (-dSize)) < 5.0f)    { rightDown = chunk; }
                    else if (Mathf.Abs(dX - wSize) < 5.0f && Mathf.Abs(dZ - dSize) < 5.0f)       { rightUp   = chunk; }
                }
            }

            if (left      != null) neighbors.Add(left);
            if (right     != null) neighbors.Add(right);
            if (down      != null) neighbors.Add(down);
            if (up        != null) neighbors.Add(up);
            if (leftDown  != null) neighbors.Add(leftDown);
            if (leftUp    != null) neighbors.Add(leftUp);
            if (rightDown != null) neighbors.Add(rightDown);
            if (rightUp   != null) neighbors.Add(rightUp);

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
            int mainVerts = vw * vd;

            if (vertices.Length < mainVerts)
            {
                Debug.LogWarning("[DesertTerrainChunk] Existing mesh vertex count is too small. Regenerating seamless mesh.");
                BuildSeamlessWithNeighbors();
                return;
            }

            // Apply fine detail noise only to surface vertices
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

            // Rebuild/update skirt vertices to match the refined surface vertices
            int leftSB = mainVerts;
            int rightSB = mainVerts + vd;
            int bottomSB = mainVerts + 2 * vd;
            int topSB = mainVerts + 2 * vd + vw;
            const float SkirtDepth = 40f;

            if (vertices.Length == mainVerts + 2 * vd + 2 * vw)
            {
                // Left skirt (x=0 column)
                for (int z = 0; z < vd; z++)
                {
                    int mi = z * vw;
                    vertices[leftSB + z] = vertices[mi] + new Vector3(0f, -SkirtDepth, 0f);
                }
                // Right skirt (x=width column)
                for (int z = 0; z < vd; z++)
                {
                    int mi = z * vw + width;
                    vertices[rightSB + z] = vertices[mi] + new Vector3(0f, -SkirtDepth, 0f);
                }
                // Bottom skirt (z=0 row)
                for (int x = 0; x < vw; x++)
                {
                    int mi = x;
                    vertices[bottomSB + x] = vertices[mi] + new Vector3(0f, -SkirtDepth, 0f);
                }
                // Top skirt (z=depth row)
                for (int x = 0; x < vw; x++)
                {
                    int mi = depth * vw + x;
                    vertices[topSB + x] = vertices[mi] + new Vector3(0f, -SkirtDepth, 0f);
                }
            }

            // Seamless analytical normals based on updated surface vertices
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

            // Set skirt normals to face outward
            if (vertices.Length == mainVerts + 2 * vd + 2 * vw)
            {
                for (int z = 0; z < vd; z++) normals[leftSB + z] = Vector3.left;
                for (int z = 0; z < vd; z++) normals[rightSB + z] = Vector3.right;
                for (int x = 0; x < vw; x++) normals[bottomSB + x] = Vector3.back;
                for (int x = 0; x < vw; x++) normals[topSB + x] = Vector3.forward;
            }

            mesh.vertices = vertices;
            mesh.normals = normals;
            mesh.RecalculateBounds();

            useSavedMeshAsset = true;

            if (TryGetComponent<MeshCollider>(out var col))
            {
                col.sharedMesh = null;
                col.sharedMesh = mesh;
            }

            UpdateSnowLayer(mesh);
            SyncRoadOverlay();

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
            float ox = seed * 1.7f;
            float oz = seed * 2.3f;
            return baseHeight + Mathf.PerlinNoise((worldX + ox) / baseScale, (worldZ + oz) / baseScale) * baseNoiseHeight;
        }

        /// <summary>
        /// Samples only the high-frequency sand dunes and wind-swept ripples.
        /// </summary>
        public float SampleDetailHeight(float worldX, float worldZ)
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

            float duneRaw = fraction < crestPosition 
                ? Mathf.Pow(fraction / crestPosition, windwardExponent)
                : ((1f - fraction) / (1f - crestPosition));
            // 平滑圆润化沙丘尖峰，彻底消除因为法线突变和低模网格连接处产生的黑色锯齿折线！
            float duneH = (duneRaw * duneRaw * (3f - 2f * duneRaw)) * activeDuneHeight;

            // 6. Secondary Cross dunes
            float secondaryTheta = (duneDirection + 35f) * Mathf.Deg2Rad;
            Vector2 secWindDir = new Vector2(Mathf.Cos(secondaryTheta), Mathf.Sin(secondaryTheta));
            float secCoord = worldX * secWindDir.x + worldZ * secWindDir.y;
            float secWarp = Mathf.PerlinNoise((worldX + seed * 14.1f) / (duneWarpScale * 0.5f), (worldZ - seed * 11.7f) / (duneWarpScale * 0.5f)) * (duneWarpStrength * 0.4f);
            float secDuneCoord = (secCoord + secWarp) / (activeDuneSpacing * 0.45f);
            float secFraction = secDuneCoord - Mathf.Floor(secDuneCoord);

            float secDuneRaw = secFraction < 0.75f
                ? Mathf.Pow(secFraction / 0.75f, 2f)
                : ((1f - secFraction) / 0.25f);
            float secDuneH = (secDuneRaw * secDuneRaw * (3f - 2f * secDuneRaw)) * (activeDuneHeight * 0.35f);

            float detailH = duneH + secDuneH;

            // Wind-swept ripples
            float rippleTheta = rippleDirection * Mathf.Deg2Rad;
            Vector2 rippleDir = new Vector2(Mathf.Cos(rippleTheta), Mathf.Sin(rippleTheta));
            float rippleCoord = (worldX * rippleDir.x + worldZ * rippleDir.y) / rippleSpacing;
            float rippleWarp = Mathf.PerlinNoise(worldX / 8f, worldZ / 8f) * 0.3f;
            float rippleVal = Mathf.Sin((rippleCoord + rippleWarp) * Mathf.PI * 2f);

            float rippleH = (rippleVal * 0.5f + 0.5f) * rippleHeight;
            return detailH + rippleH;
        }

        public float SampleHeight(float worldX, float worldZ)
        {
            float baseH = SampleBaseHeight(worldX, worldZ);
            return baseH + SampleDetailHeight(worldX, worldZ);
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

            Color[] colors = new Color[vw * vd];
            // Colors will be generated by the Global Biome Tool. For now, default to zero alpha.
            for (int i = 0; i < colors.Length; i++) colors[i] = new Color(1, 1, 1, 0);

            Mesh mesh = new Mesh { name = "DesertTerrainChunk" };

            if (vertices.Length > 65535)
                mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;

            mesh.vertices = vertices;
            mesh.uv = uvs;
            mesh.normals = normals;
            mesh.colors = colors;
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

            UpdateSnowLayer(mesh);
            SyncRoadOverlay();

            if (TryGetComponent<MeshRenderer>(out var mr))
            {
                if (terrainMaterial != null)
                {
                    mr.sharedMaterial = terrainMaterial;
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

        public void SyncSnowLayer()
        {
            MeshFilter filter = GetComponent<MeshFilter>();
            if (filter != null && filter.sharedMesh != null)
            {
                UpdateSnowLayer(filter.sharedMesh);
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
            
            // Hot-swap when changing resolution in Inspector
            if (Application.isPlaying && gameObject.activeInHierarchy)
            {
                UpdateResolution();
            }
            if (blendWidth < 1) blendWidth = 1;
            if (detailScale < 0.1f) detailScale = 0.1f;
        }

        // ── Async Build System ─────────────────────────────────────────────────
        //
        // Snapshot of all terrain parameters required for pure (thread-safe) height sampling.
        // All fields are value types so the struct is safely copyable to a background thread.
        //
        private struct TerrainHeightParams
        {
            public bool isValid;
            public int seed;
            public float baseScale, baseHeight, baseNoiseHeight;
            public float duneSpacing, duneHeight, duneDirection;
            public float duneWarpScale, duneWarpStrength;
            public float crestPosition, windwardExponent;
            public float rippleSpacing, rippleHeight, rippleDirection;
            public float cellSize;
            public int width, depth, blendWidth;
            public Vector3 chunkPosition; // captured on main thread before Task.Run

            /// <summary>Capture all params from a chunk on the main thread.</summary>
            public static TerrainHeightParams Capture(DesertTerrainChunk c)
            {
                if (c == null) return new TerrainHeightParams { isValid = false };
                return new TerrainHeightParams
                {
                    isValid         = true,
                    seed            = c.seed,
                    baseScale       = c.baseScale,
                    baseHeight      = c.baseHeight,
                    baseNoiseHeight = c.baseNoiseHeight,
                    duneSpacing     = c.duneSpacing,
                    duneHeight      = c.duneHeight,
                    duneDirection   = c.duneDirection,
                    duneWarpScale   = c.duneWarpScale,
                    duneWarpStrength = c.duneWarpStrength,
                    crestPosition   = c.crestPosition,
                    windwardExponent = c.windwardExponent,
                    rippleSpacing   = c.rippleSpacing,
                    rippleHeight    = c.rippleHeight,
                    rippleDirection = c.rippleDirection,
                    cellSize        = c.cellSize,
                    width           = c.width,
                    depth           = c.depth,
                    blendWidth      = c.blendWidth,
                    chunkPosition   = c.transform.position,
                };
            }
        }

    private struct MeshBuildResult
    {
        public Vector3[] vertices;
        public Vector2[] uvs;
        public Vector3[] normals;
        public int[] triangles;
    }

    private struct ChunkBuildResult
    {
        public MeshBuildResult terrainMesh;
        public MeshBuildResult snowMesh;
    }

        // ── Static thread-safe height sampling (mirror of instance methods, no Unity API) ──

        private static float SampleBaseHeightS(in TerrainHeightParams p, float wx, float wz)
        {
            float ox = p.seed * 1.7f, oz = p.seed * 2.3f;
            return p.baseHeight + Mathf.PerlinNoise((wx + ox) / p.baseScale, (wz + oz) / p.baseScale) * p.baseNoiseHeight;
        }

        private static float SampleDetailHeightS(in TerrainHeightParams p, float wx, float wz)
        {
            float detailH = 0f;

            float angleWander = (Mathf.PerlinNoise((wx + p.seed * 8.7f) / 380f, (wz + p.seed * 12.3f) / 380f) * 2f - 1f) * 30f;
            float theta = (p.duneDirection + angleWander) * Mathf.Deg2Rad;
            var windDir = new Vector2(Mathf.Cos(theta), Mathf.Sin(theta));

            float spacingMod = Mathf.PerlinNoise((wx + p.seed * 19.3f) / 450f, (wz + p.seed * 23.7f) / 450f);
            float activeDuneSpacing = p.duneSpacing * Mathf.Lerp(0.65f, 1.45f, spacingMod);
            float heightMod = Mathf.PerlinNoise((wx - p.seed * 31.1f) / 280f, (wz - p.seed * 27.9f) / 280f);
            float activeDuneHeight = p.duneHeight * Mathf.Lerp(0.4f, 1.4f, heightMod);

            float coord = wx * windDir.x + wz * windDir.y;
            float warp  = Mathf.PerlinNoise((wx + p.seed * 3.1f) / p.duneWarpScale, (wz + p.seed * 4.7f) / p.duneWarpScale) * p.duneWarpStrength;
            float duneCoord = (coord + warp) / activeDuneSpacing;
            float fraction  = duneCoord - Mathf.Floor(duneCoord);
            float duneRaw = fraction < p.crestPosition
                ? Mathf.Pow(fraction / p.crestPosition, p.windwardExponent)
                : ((1f - fraction) / (1f - p.crestPosition));
            // 平滑圆润化沙丘尖峰，彻底消除因为法线突变和低模网格连接处产生的黑色锯齿折线！
            float duneH = (duneRaw * duneRaw * (3f - 2f * duneRaw)) * activeDuneHeight;

            float secTheta = (p.duneDirection + 35f) * Mathf.Deg2Rad;
            var secWindDir = new Vector2(Mathf.Cos(secTheta), Mathf.Sin(secTheta));
            float secCoord = wx * secWindDir.x + wz * secWindDir.y;
            float secWarp  = Mathf.PerlinNoise((wx + p.seed * 14.1f) / (p.duneWarpScale * 0.5f), (wz - p.seed * 11.7f) / (p.duneWarpScale * 0.5f)) * (p.duneWarpStrength * 0.4f);
            float secDuneCoord = (secCoord + secWarp) / (activeDuneSpacing * 0.45f);
            float secFraction  = secDuneCoord - Mathf.Floor(secDuneCoord);
            float secDuneRaw = secFraction < 0.75f
                ? Mathf.Pow(secFraction / 0.75f, 2f)
                : ((1f - secFraction) / 0.25f);
            float secDuneH = (secDuneRaw * secDuneRaw * (3f - 2f * secDuneRaw)) * (activeDuneHeight * 0.35f);
            detailH = duneH + secDuneH;

            float rippleTheta = p.rippleDirection * Mathf.Deg2Rad;
            var rippleDir = new Vector2(Mathf.Cos(rippleTheta), Mathf.Sin(rippleTheta));
            float rippleCoord = (wx * rippleDir.x + wz * rippleDir.y) / p.rippleSpacing;
            float rippleWarp  = Mathf.PerlinNoise(wx / 8f, wz / 8f) * 0.3f;
            float rippleVal   = Mathf.Sin((rippleCoord + rippleWarp) * Mathf.PI * 2f);

            return detailH + (rippleVal * 0.5f + 0.5f) * p.rippleHeight;
        }

        private static float SampleHeightS(in TerrainHeightParams p, float wx, float wz)
        {
            float baseH = SampleBaseHeightS(in p, wx, wz);
            return baseH + SampleDetailHeightS(in p, wx, wz);
        }

        private static float GetStitchedHeightS(
            in TerrainHeightParams self, float wX, float wZ,
            in TerrainHeightParams l, in TerrainHeightParams r,
            in TerrainHeightParams d, in TerrainHeightParams u,
            in TerrainHeightParams ld, in TerrainHeightParams lu,
            in TerrainHeightParams rd, in TerrainHeightParams ru)
        {
            float localX = wX - self.chunkPosition.x;
            float localZ = wZ - self.chunkPosition.z;
            float wSize  = self.width  * self.cellSize;
            float dSize  = self.depth  * self.cellSize;

            // ── Part 1: Base Elevation Blending ─────────────────────────────────
            float A = self.baseHeight;
            float blendRangeX = wSize * 0.5f, blendRangeZ = dSize * 0.5f;
            float w_L  = localX < blendRangeX           ? Mathf.SmoothStep(1f, 0f, localX / blendRangeX) : 0f;
            float w_R  = localX > wSize - blendRangeX   ? Mathf.SmoothStep(1f, 0f, (wSize - localX) / blendRangeX) : 0f;
            float w_D  = localZ < blendRangeZ           ? Mathf.SmoothStep(1f, 0f, localZ / blendRangeZ) : 0f;
            float w_U  = localZ > dSize - blendRangeZ   ? Mathf.SmoothStep(1f, 0f, (dSize - localZ) / blendRangeZ) : 0f;
            float w_LD = w_L * w_D, w_LU = w_L * w_U, w_RD = w_R * w_D, w_RU = w_R * w_U;
            float w_left_only  = Mathf.Max(0f, w_L - (w_LD + w_LU));
            float w_right_only = Mathf.Max(0f, w_R - (w_RD + w_RU));
            float w_down_only  = Mathf.Max(0f, w_D - (w_LD + w_RD));
            float w_up_only    = Mathf.Max(0f, w_U - (w_LU + w_RU));

            float hL = l.isValid  ? (A + l.baseHeight)  * 0.5f : A;
            float hR = r.isValid  ? (A + r.baseHeight)  * 0.5f : A;
            float hD = d.isValid  ? (A + d.baseHeight)  * 0.5f : A;
            float hU = u.isValid  ? (A + u.baseHeight)  * 0.5f : A;

            float sumLD = A; float cntLD = 1f;
            if (l.isValid)  { sumLD += l.baseHeight;  cntLD++; }
            if (d.isValid)  { sumLD += d.baseHeight;  cntLD++; }
            if (ld.isValid) { sumLD += ld.baseHeight; cntLD++; }
            float hLD = sumLD / cntLD;

            float sumLU = A; float cntLU = 1f;
            if (l.isValid)  { sumLU += l.baseHeight;  cntLU++; }
            if (u.isValid)  { sumLU += u.baseHeight;  cntLU++; }
            if (lu.isValid) { sumLU += lu.baseHeight; cntLU++; }
            float hLU = sumLU / cntLU;

            float sumRD = A; float cntRD = 1f;
            if (r.isValid)  { sumRD += r.baseHeight;  cntRD++; }
            if (d.isValid)  { sumRD += d.baseHeight;  cntRD++; }
            if (rd.isValid) { sumRD += rd.baseHeight; cntRD++; }
            float hRD = sumRD / cntRD;

            float sumRU = A; float cntRU = 1f;
            if (r.isValid)  { sumRU += r.baseHeight;  cntRU++; }
            if (u.isValid)  { sumRU += u.baseHeight;  cntRU++; }
            if (ru.isValid) { sumRU += ru.baseHeight; cntRU++; }
            float hRU = sumRU / cntRU;

            float elevSum = 0f, elevW = 0f;
            if (l.isValid  && w_left_only  > 0f) { elevSum += hL  * w_left_only;  elevW += w_left_only; }
            if (r.isValid  && w_right_only > 0f) { elevSum += hR  * w_right_only; elevW += w_right_only; }
            if (d.isValid  && w_down_only  > 0f) { elevSum += hD  * w_down_only;  elevW += w_down_only; }
            if (u.isValid  && w_up_only    > 0f) { elevSum += hU  * w_up_only;    elevW += w_up_only; }
            if ((l.isValid || d.isValid || ld.isValid) && w_LD > 0f) { elevSum += hLD * w_LD; elevW += w_LD; }
            if ((l.isValid || u.isValid || lu.isValid) && w_LU > 0f) { elevSum += hLU * w_LU; elevW += w_LU; }
            if ((r.isValid || d.isValid || rd.isValid) && w_RD > 0f) { elevSum += hRD * w_RD; elevW += w_RD; }
            if ((r.isValid || u.isValid || ru.isValid) && w_RU > 0f) { elevSum += hRU * w_RU; elevW += w_RU; }
            float elev = elevW > 0f ? Mathf.Lerp(A, elevSum / elevW, Mathf.Min(elevW, 1f)) : A;

            // ── Part 2: Local Feature Blending ───────────────────────────────────
            float featNatural = SampleHeightS(in self, wX, wZ) - A;
            float weldRangeX  = self.blendWidth * self.cellSize;
            float weldRangeZ  = self.blendWidth * self.cellSize;

            float w_L_f  = localX < weldRangeX         ? Mathf.SmoothStep(1f, 0f, localX / weldRangeX) : 0f;
            float w_R_f  = localX > wSize - weldRangeX ? Mathf.SmoothStep(1f, 0f, (wSize - localX) / weldRangeX) : 0f;
            float w_D_f  = localZ < weldRangeZ         ? Mathf.SmoothStep(1f, 0f, localZ / weldRangeZ) : 0f;
            float w_U_f  = localZ > dSize - weldRangeZ ? Mathf.SmoothStep(1f, 0f, (dSize - localZ) / weldRangeZ) : 0f;
            float w_LD_f = w_L_f * w_D_f, w_LU_f = w_L_f * w_U_f, w_RD_f = w_R_f * w_D_f, w_RU_f = w_R_f * w_U_f;
            float w_lf = Mathf.Max(0f, w_L_f - (w_LD_f + w_LU_f));
            float w_rf = Mathf.Max(0f, w_R_f - (w_RD_f + w_RU_f));
            float w_df = Mathf.Max(0f, w_D_f - (w_LD_f + w_RD_f));
            float w_uf = Mathf.Max(0f, w_U_f - (w_LU_f + w_RU_f));

            float fL  = l.isValid  ? SampleHeightS(in l,  wX, wZ) - l.baseHeight   : featNatural;
            float fR  = r.isValid  ? SampleHeightS(in r,  wX, wZ) - r.baseHeight   : featNatural;
            float fD  = d.isValid  ? SampleHeightS(in d,  wX, wZ) - d.baseHeight   : featNatural;
            float fU  = u.isValid  ? SampleHeightS(in u,  wX, wZ) - u.baseHeight   : featNatural;
            float fLD = ld.isValid ? SampleHeightS(in ld, wX, wZ) - ld.baseHeight  : featNatural;
            float fLU = lu.isValid ? SampleHeightS(in lu, wX, wZ) - lu.baseHeight  : featNatural;
            float fRD = rd.isValid ? SampleHeightS(in rd, wX, wZ) - rd.baseHeight  : featNatural;
            float fRU = ru.isValid ? SampleHeightS(in ru, wX, wZ) - ru.baseHeight  : featNatural;

            float fSumLD = featNatural; float fcLD = 1f;
            if (l.isValid)  { fSumLD += fL;  fcLD++; } if (d.isValid)  { fSumLD += fD;  fcLD++; }
            if (ld.isValid) { fSumLD += fLD; fcLD++; }
            float fSumLU = featNatural; float fcLU = 1f;
            if (l.isValid)  { fSumLU += fL;  fcLU++; } if (u.isValid)  { fSumLU += fU;  fcLU++; }
            if (lu.isValid) { fSumLU += fLU; fcLU++; }
            float fSumRD = featNatural; float fcRD = 1f;
            if (r.isValid)  { fSumRD += fR;  fcRD++; } if (d.isValid)  { fSumRD += fD;  fcRD++; }
            if (rd.isValid) { fSumRD += fRD; fcRD++; }
            float fSumRU = featNatural; float fcRU = 1f;
            if (r.isValid)  { fSumRU += fR;  fcRU++; } if (u.isValid)  { fSumRU += fU;  fcRU++; }
            if (ru.isValid) { fSumRU += fRU; fcRU++; }

            float featSum = 0f, featW = 0f;
            if (l.isValid  && w_lf  > 0f) { featSum += (featNatural + fL) * 0.5f * w_lf;  featW += w_lf; }
            if (r.isValid  && w_rf  > 0f) { featSum += (featNatural + fR) * 0.5f * w_rf;  featW += w_rf; }
            if (d.isValid  && w_df  > 0f) { featSum += (featNatural + fD) * 0.5f * w_df;  featW += w_df; }
            if (u.isValid  && w_uf  > 0f) { featSum += (featNatural + fU) * 0.5f * w_uf;  featW += w_uf; }
            if ((l.isValid || d.isValid || ld.isValid) && w_LD_f > 0f) { featSum += (fSumLD / fcLD) * w_LD_f; featW += w_LD_f; }
            if ((l.isValid || u.isValid || lu.isValid) && w_LU_f > 0f) { featSum += (fSumLU / fcLU) * w_LU_f; featW += w_LU_f; }
            if ((r.isValid || d.isValid || rd.isValid) && w_RD_f > 0f) { featSum += (fSumRD / fcRD) * w_RD_f; featW += w_RD_f; }
            if ((r.isValid || u.isValid || ru.isValid) && w_RU_f > 0f) { featSum += (fSumRU / fcRU) * w_RU_f; featW += w_RU_f; }

            float features = featW > 0f ? Mathf.Lerp(featNatural, featSum / featW, Mathf.Min(featW, 1f)) : featNatural;
            return elev + features;
        }

        /// <summary>
        /// Pure background-thread mesh computation. Receives captured param snapshots;
        /// uses no Unity API (Mathf.PerlinNoise and Vector3/Vector2 math are thread-safe).
        /// </summary>
        private static MeshBuildResult ComputeMeshData(
            TerrainHeightParams self,
            TerrainHeightParams left, TerrainHeightParams right,
            TerrainHeightParams down, TerrainHeightParams up,
            TerrainHeightParams leftDown, TerrainHeightParams leftUp,
            TerrainHeightParams rightDown, TerrainHeightParams rightUp)
        {
            int vw = self.width + 1;
            int vd = self.depth + 1;
            float cs = self.cellSize;
            Vector3 pos = self.chunkPosition;

            // ── Sizing: main surface + 4 edge skirts ──────────────────────────────
            // Skirts hide seams at any camera angle; each edge gets one strip of verts
            // dropped SkirtDepth units below the edge, forming a downward apron.
            const float SkirtDepth = 40f;
            int mainVerts  = vw * vd;
            // 4 edges: left=vd, right=vd, bottom=vw, top=vw
            int skirtVerts = 2 * vd + 2 * vw;
            int totalVerts = mainVerts + skirtVerts;

            int mainTriIdx  = self.width * self.depth * 6;
            // Each edge gets (n-1) quads = 2 tris each
            int skirtTriIdx = (2 * (vd - 1) + 2 * (vw - 1)) * 6;
            int totalTriIdx = mainTriIdx + skirtTriIdx;

            var vertices  = new Vector3[totalVerts];
            var uvs       = new Vector2[totalVerts];
            var normals   = new Vector3[totalVerts];
            var triangles = new int[totalTriIdx];

            // ── 1. Main surface heights ───────────────────────────────────────────
            for (int z = 0; z < vd; z++)
            {
                for (int x = 0; x < vw; x++)
                {
                    float wx = pos.x + x * cs;
                    float wz = pos.z + z * cs;
                    float worldH = GetStitchedHeightS(in self, wx, wz,
                        in left, in right, in down, in up,
                        in leftDown, in leftUp, in rightDown, in rightUp);
                    int idx = z * vw + x;
                    vertices[idx] = new Vector3(x * cs, worldH - pos.y, z * cs);
                    uvs[idx]      = new Vector2((float)x / self.width, (float)z / self.depth);
                }
            }

            // ── 2. Main surface normals (4-neighbor finite difference) ────────────
            for (int z = 0; z < vd; z++)
            {
                for (int x = 0; x < vw; x++)
                {
                    float wx = pos.x + x * cs;
                    float wz = pos.z + z * cs;
                    float hL = GetStitchedHeightS(in self, wx - cs, wz,
                        in left, in right, in down, in up, in leftDown, in leftUp, in rightDown, in rightUp);
                    float hR = GetStitchedHeightS(in self, wx + cs, wz,
                        in left, in right, in down, in up, in leftDown, in leftUp, in rightDown, in rightUp);
                    float hD = GetStitchedHeightS(in self, wx, wz - cs,
                        in left, in right, in down, in up, in leftDown, in leftUp, in rightDown, in rightUp);
                    float hU = GetStitchedHeightS(in self, wx, wz + cs,
                        in left, in right, in down, in up, in leftDown, in leftUp, in rightDown, in rightUp);
                    var tangX = new Vector3(cs * 2f, hR - hL, 0f);
                    var tangZ = new Vector3(0f, hU - hD, cs * 2f);
                    normals[z * vw + x] = Vector3.Normalize(new Vector3(
                        tangZ.y * tangX.z - tangZ.z * tangX.y,
                        tangZ.z * tangX.x - tangZ.x * tangX.z,
                        tangZ.x * tangX.y - tangZ.y * tangX.x));
                }
            }

            // ── 3. Main surface triangles ─────────────────────────────────────────
            int t = 0;
            for (int z = 0; z < self.depth; z++)
            {
                for (int x = 0; x < self.width; x++)
                {
                    int bl = z * vw + x, br = bl + 1, tl = bl + vw, tr = tl + 1;
                    triangles[t++] = bl; triangles[t++] = tl; triangles[t++] = tr;
                    triangles[t++] = bl; triangles[t++] = tr; triangles[t++] = br;
                }
            }

            // ── 4. Skirt vertices ─────────────────────────────────────────────────
            // Layout: [left vd] [right vd] [bottom vw] [top vw]
            int leftSB   = mainVerts;
            int rightSB  = mainVerts + vd;
            int bottomSB = mainVerts + 2 * vd;
            int topSB    = mainVerts + 2 * vd + vw;

            // Left skirt (x=0 column) – outward normal → -X
            for (int z = 0; z < vd; z++)
            {
                int mi = z * vw;
                vertices[leftSB + z] = vertices[mi] + new Vector3(0f, -SkirtDepth, 0f);
                uvs[leftSB + z]      = uvs[mi];
                normals[leftSB + z]  = Vector3.left;
            }
            // Right skirt (x=width column) – outward normal → +X
            for (int z = 0; z < vd; z++)
            {
                int mi = z * vw + self.width;
                vertices[rightSB + z] = vertices[mi] + new Vector3(0f, -SkirtDepth, 0f);
                uvs[rightSB + z]      = uvs[mi];
                normals[rightSB + z]  = Vector3.right;
            }
            // Bottom skirt (z=0 row) – outward normal → -Z
            for (int x = 0; x < vw; x++)
            {
                int mi = x;
                vertices[bottomSB + x] = vertices[mi] + new Vector3(0f, -SkirtDepth, 0f);
                uvs[bottomSB + x]      = uvs[mi];
                normals[bottomSB + x]  = Vector3.back;
            }
            // Top skirt (z=depth row) – outward normal → +Z
            for (int x = 0; x < vw; x++)
            {
                int mi = self.depth * vw + x;
                vertices[topSB + x] = vertices[mi] + new Vector3(0f, -SkirtDepth, 0f);
                uvs[topSB + x]      = uvs[mi];
                normals[topSB + x]  = Vector3.forward;
            }

            // ── 5. Skirt triangles (winding verified to face outward per edge) ────
            int ti = mainTriIdx;

            // LEFT skirt: face –X  (CCW from –X)  winding: mA, sA, mB | mB, sA, sB
            for (int z = 0; z < vd - 1; z++)
            {
                int mA = z * vw,        mB = (z + 1) * vw;
                int sA = leftSB + z,    sB = leftSB + z + 1;
                triangles[ti++] = mA; triangles[ti++] = sA; triangles[ti++] = mB;
                triangles[ti++] = mB; triangles[ti++] = sA; triangles[ti++] = sB;
            }
            // RIGHT skirt: face +X  winding: mA, mB, sA | mB, sB, sA
            for (int z = 0; z < vd - 1; z++)
            {
                int mA = z * vw + self.width,       mB = (z + 1) * vw + self.width;
                int sA = rightSB + z,               sB = rightSB + z + 1;
                triangles[ti++] = mA; triangles[ti++] = mB; triangles[ti++] = sA;
                triangles[ti++] = mB; triangles[ti++] = sB; triangles[ti++] = sA;
            }
            // BOTTOM skirt: face –Z  winding: mA, mB, sB | mA, sB, sA
            for (int x = 0; x < vw - 1; x++)
            {
                int mA = x,             mB = x + 1;
                int sA = bottomSB + x,  sB = bottomSB + x + 1;
                triangles[ti++] = mA; triangles[ti++] = mB; triangles[ti++] = sB;
                triangles[ti++] = mA; triangles[ti++] = sB; triangles[ti++] = sA;
            }
            // TOP skirt: face +Z  winding: mA, sA, mB | mB, sA, sB
            for (int x = 0; x < vw - 1; x++)
            {
                int mA = self.depth * vw + x,   mB = self.depth * vw + x + 1;
                int sA = topSB + x,             sB = topSB + x + 1;
                triangles[ti++] = mA; triangles[ti++] = sA; triangles[ti++] = mB;
                triangles[ti++] = mB; triangles[ti++] = sA; triangles[ti++] = sB;
            }

            return new MeshBuildResult { vertices = vertices, uvs = uvs, normals = normals, triangles = triangles };
        }

        /// <summary>
        /// Async coroutine: captures params on the main thread, offloads vertex/normal
        /// computation to a background Task, then applies the result on the main thread
        /// in &lt;2 ms with no stalling.
        /// </summary>
        private IEnumerator BuildAsyncCoroutine(int delayFrames = 2)
        {
            if (useSavedMeshAsset)
            {
                _asyncBuildRunning = false;
                yield break;
            }

            _asyncBuildRunning = true;

            // Stagger across frames to prevent 25 chunks all computing at frame 0
            for (int i = 0; i < delayFrames; i++)
                yield return null;

            if (!this || !gameObject.activeInHierarchy || useSavedMeshAsset)
            {
                _asyncBuildRunning = false;
                yield break;
            }

            // ── 1. Capture ALL params on main thread (must happen before Task.Run) ──
            var selfP = TerrainHeightParams.Capture(this);
            var gc    = GridCoord;

            DesertTerrainChunk lc  = ChunkRegistry.Get(gc.x - 1, gc.y);
            DesertTerrainChunk rc  = ChunkRegistry.Get(gc.x + 1, gc.y);
            DesertTerrainChunk dc  = ChunkRegistry.Get(gc.x,     gc.y - 1);
            DesertTerrainChunk uc  = ChunkRegistry.Get(gc.x,     gc.y + 1);
            DesertTerrainChunk ldc = ChunkRegistry.Get(gc.x - 1, gc.y - 1);
            DesertTerrainChunk luc = ChunkRegistry.Get(gc.x - 1, gc.y + 1);
            DesertTerrainChunk rdc = ChunkRegistry.Get(gc.x + 1, gc.y - 1);
            DesertTerrainChunk ruc = ChunkRegistry.Get(gc.x + 1, gc.y + 1);

            var lP  = TerrainHeightParams.Capture(lc);
            var rP  = TerrainHeightParams.Capture(rc);
            var dP  = TerrainHeightParams.Capture(dc);
            var uP  = TerrainHeightParams.Capture(uc);
            var ldP = TerrainHeightParams.Capture(ldc);
            var luP = TerrainHeightParams.Capture(luc);
            var rdP = TerrainHeightParams.Capture(rdc);
            var ruP = TerrainHeightParams.Capture(ruc);

            var snowSelfP = selfP;
            snowSelfP.width *= snowResolutionMultiplier;
            snowSelfP.depth *= snowResolutionMultiplier;
            snowSelfP.cellSize /= snowResolutionMultiplier;

            // ── 2. Launch background computation or run synchronously on WebGL ──────
#if UNITY_WEBGL && !UNITY_EDITOR
            var result = new ChunkBuildResult();
            result.terrainMesh = ComputeMeshData(selfP, lP, rP, dP, uP, ldP, luP, rdP, ruP);
            result.snowMesh = ComputeMeshData(snowSelfP, lP, rP, dP, uP, ldP, luP, rdP, ruP);
            ApplyMeshFromResult(result);
            _asyncBuildRunning = false;
#else
            var task = Task.Run(() => {
                var res = new ChunkBuildResult();
                res.terrainMesh = ComputeMeshData(selfP, lP, rP, dP, uP, ldP, luP, rdP, ruP);
                res.snowMesh = ComputeMeshData(snowSelfP, lP, rP, dP, uP, ldP, luP, rdP, ruP);
                return res;
            });

            // ── 3. Yield each frame until done (main thread stays free) ──────────
            while (!task.IsCompleted)
                yield return null;

            if (!this || !gameObject.activeInHierarchy)
            {
                _asyncBuildRunning = false;
                yield break;
            }

            if (task.IsFaulted)
            {
                Debug.LogError($"[DesertTerrainChunk] Async build failed on '{name}': {task.Exception?.GetBaseException()?.Message}");
                _asyncBuildRunning = false;
                yield break;
            }

            // ── 4. Apply result on main thread in <2 ms ───────────────────────────
            ChunkBuildResult result = task.Result;
            ApplyMeshFromResult(result);
            _asyncBuildRunning = false;
#endif

            // If a rebuild was queued while we were running (new neighbor loaded), do it now.
            if (_asyncBuildQueued)
            {
                _asyncBuildQueued = false;
                StartCoroutine(BuildAsyncCoroutine(delayFrames: 1));
                yield break;
            }

            // Notify immediate neighbors to re-stitch (propagate=false to prevent chain)
            DesertTerrainChunk[] immediateNeighbors = { lc, rc, dc, uc };
            foreach (var nb in immediateNeighbors)
            {
                if (nb != null && nb.gameObject.activeInHierarchy && !nb._asyncBuildRunning)
                    nb.StartCoroutine(nb.BuildAsyncCoroutine(delayFrames: 1));
            }
        }

        /// <summary>
        /// Applies pre-computed mesh arrays to the MeshFilter and MeshCollider.
        /// Called on the main thread; runs in under 2 ms.
        /// </summary>
        private void ApplyMeshFromResult(ChunkBuildResult result)
        {
            if (!TryGetComponent<MeshFilter>(out var filter)) return;

            Mesh mesh;
            bool hasAsset = false;

#if UNITY_EDITOR
            string assetPath = UnityEditor.AssetDatabase.GetAssetPath(filter.sharedMesh);
            hasAsset = !string.IsNullOrEmpty(assetPath) && assetPath.EndsWith(".asset") && assetPath.Contains("/Meshes/");
#endif

            if (filter.sharedMesh != null && hasAsset)
            {
                // Reuse existing persistent mesh asset (preserves GUID / scene reference)
                mesh = filter.sharedMesh;
                mesh.Clear();
            }
            else
            {
                mesh = new Mesh { name = "DesertTerrainChunk" };
                filter.sharedMesh = mesh;
            }

            if (result.terrainMesh.vertices.Length > 65535)
                mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;

            mesh.vertices  = result.terrainMesh.vertices;
            mesh.uv        = result.terrainMesh.uvs;
            mesh.normals   = result.terrainMesh.normals;
            mesh.triangles = result.terrainMesh.triangles;
            mesh.RecalculateBounds();
            mesh.UploadMeshData(false);

            if (TryGetComponent<MeshCollider>(out var col))
            {
                col.sharedMesh = null;
                col.sharedMesh = mesh;
            }

            Mesh snowMesh = new Mesh { name = "SnowLayerMesh" };
            if (result.snowMesh.vertices.Length > 65535)
                snowMesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            snowMesh.vertices = result.snowMesh.vertices;
            snowMesh.uv = result.snowMesh.uvs;
            snowMesh.normals = result.snowMesh.normals;
            snowMesh.triangles = result.snowMesh.triangles;
            snowMesh.RecalculateBounds();
            snowMesh.UploadMeshData(false);

            UpdateSnowLayer(snowMesh);

            WakeUpDynamicProps();
        }

        private void WakeUpDynamicProps()
        {
            if (gameObject.scene.isLoaded)
            {
                foreach (GameObject root in gameObject.scene.GetRootGameObjects())
                {
                    if (root == this.gameObject) continue;
                    foreach (Rigidbody rb in root.GetComponentsInChildren<Rigidbody>(true))
                    {
                        WorldObject wo = rb.GetComponent<WorldObject>();
                        // Unfreeze items that are meant to be dynamic (e.g., Fuel Cans)
                        // now that the terrain mesh is officially solid.
                        // Cacti (environment props) will remain frozen.
                        if (wo != null && (wo.carryable || wo.collectable))
                        {
                            rb.isKinematic = false;
                            rb.useGravity = true;
                            rb.WakeUp();
                        }
                    }
                }
            }
        }

        // ── Snow Layer Management (Restored for 2D Base) ──────────────────────────
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

            // Always instantiate a copy so we don't modify the main terrain mesh asset directly!
            Mesh snowMesh = Instantiate(originalMesh);
            snowMesh.name = "SnowLayerMesh_Flattened";

            // Flatten skirt vertices to have 0 area triangles (preventing normal interpolation rendering on skirts)
            int vertexCount = snowMesh.vertexCount;
            int S = Mathf.RoundToInt(-2f + Mathf.Sqrt(4f + vertexCount));
            if (S * S + 4 * S == vertexCount)
            {
                int vw = S;
                int vd = S;
                int mainVerts = vw * vd;
                Vector3[] verts = snowMesh.vertices;

                int leftSB = mainVerts;
                int rightSB = mainVerts + vd;
                int bottomSB = mainVerts + 2 * vd;
                int topSB = mainVerts + 2 * vd + vw;

                // Left skirt (x=0 column)
                for (int z = 0; z < vd; z++)
                {
                    int mi = z * vw;
                    verts[leftSB + z] = verts[mi];
                }
                // Right skirt (x=width column)
                for (int z = 0; z < vd; z++)
                {
                    int mi = z * vw + (vw - 1);
                    verts[rightSB + z] = verts[mi];
                }
                // Bottom skirt (z=0 row)
                for (int x = 0; x < vw; x++)
                {
                    int mi = x;
                    verts[bottomSB + x] = verts[mi];
                }
                // Top skirt (z=depth row)
                for (int x = 0; x < vw; x++)
                {
                    int mi = (vd - 1) * vw + x;
                    verts[topSB + x] = verts[mi];
                }

                snowMesh.vertices = verts;
                snowMesh.RecalculateBounds();
            }

            if (filter.sharedMesh != null && filter.sharedMesh != originalMesh)
            {
                if (Application.isPlaying)
                {
                    Destroy(filter.sharedMesh);
                }
                else
                {
                    DestroyImmediate(filter.sharedMesh);
                }
            }
            filter.sharedMesh = snowMesh;

            MeshRenderer renderer = snowLayerObj.GetComponent<MeshRenderer>();
            if (renderer == null) renderer = snowLayerObj.AddComponent<MeshRenderer>();

            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; // 2D base doesn't cast shadows
            renderer.receiveShadows = true;

            Shader snowShader = Shader.Find("Environment/SnowBlanket");
            if (snowShader != null)
            {
                if (renderer.sharedMaterial == null || renderer.sharedMaterial.shader != snowShader)
                {
                    Material snowMat = new Material(snowShader);
                    snowMat.name = "SnowBlanketMaterial";
                    renderer.sharedMaterial = snowMat;
                }
            }
        }

        public void SyncRoadOverlay()
        {
            MeshFilter filter = GetComponent<MeshFilter>();
            if (filter != null && filter.sharedMesh != null)
            {
                Transform roadOverlayTransform = transform.Find("RoadOverlay");
                if (roadOverlayTransform != null)
                {
                    var roadOverlay = roadOverlayTransform.GetComponent<DesertTerrainRoadOverlay>();
                    if (roadOverlay != null)
                    {
                        roadOverlay.SyncWithTerrain(filter.sharedMesh);
                    }
                }
            }
        }
    }
}
