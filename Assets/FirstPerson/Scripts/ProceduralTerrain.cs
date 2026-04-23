using UnityEngine;

/// <summary>
/// Generates a terrain mesh at runtime using layered Perlin noise.
/// Add this component to an empty GameObject. Assign a Material in the Inspector.
/// </summary>
[RequireComponent(typeof(MeshFilter), typeof(MeshRenderer), typeof(MeshCollider))]
public class ProceduralTerrain : MonoBehaviour
{
    [Header("Size")]
    public int width  = 100;   // vertices along X
    public int depth  = 100;   // vertices along Z
    public float cellSize = 1f; // metres between vertices

    [Header("Height Layers")]
    [Tooltip("Large rolling hills")]
    public float hillScale     = 50f;
    public float hillHeight    = 8f;

    [Tooltip("Medium detail")]
    public float midScale      = 20f;
    public float midHeight     = 3f;

    [Tooltip("Fine surface roughness")]
    public float roughScale    = 8f;
    public float roughHeight   = 0.8f;

    [Header("Seed")]
    public int seed = 0;
    public bool randomSeed = true;

    [Header("Visual")]
    [Tooltip("Assign any material; leave empty for a default grey material.")]
    public Material terrainMaterial;

    // ── Lifecycle ──────────────────────────────────────────────────────────
    void Start()
    {
        if (randomSeed) seed = Random.Range(0, 100000);
        Build();
    }

    // ── Public API ─────────────────────────────────────────────────────────
    /// <summary>Call from editor or at runtime to regenerate the terrain.</summary>
    [ContextMenu("Rebuild Terrain")]
    public void Build()
    {
        Mesh mesh = GenerateMesh();

        GetComponent<MeshFilter>().sharedMesh   = mesh;
        GetComponent<MeshCollider>().sharedMesh = mesh;

        MeshRenderer mr = GetComponent<MeshRenderer>();
        if (terrainMaterial != null)
            mr.sharedMaterial = terrainMaterial;
        else if (mr.sharedMaterial == null)
            mr.sharedMaterial = new Material(Shader.Find("Universal Render Pipeline/Lit"));
    }

    /// <summary>
    /// Returns the world-space Y position of the terrain directly below worldPos.
    /// Useful for spawning objects on the surface.
    /// </summary>
    public float GetHeightAt(Vector3 worldPos)
    {
        Vector3 local = transform.InverseTransformPoint(worldPos);
        float nx = local.x / cellSize;
        float nz = local.z / cellSize;
        return transform.TransformPoint(new Vector3(local.x, SampleHeight(nx, nz), local.z)).y;
    }

    // ── Mesh Generation ────────────────────────────────────────────────────
    Mesh GenerateMesh()
    {
        int vw = width  + 1;
        int vd = depth  + 1;

        Vector3[] vertices  = new Vector3[vw * vd];
        Vector2[] uvs       = new Vector2[vw * vd];
        int[]     triangles = new int[width * depth * 6];

        // Build vertices
        for (int z = 0; z < vd; z++)
        {
            for (int x = 0; x < vw; x++)
            {
                float y = SampleHeight(x, z);
                vertices[z * vw + x] = new Vector3(x * cellSize, y, z * cellSize);
                uvs     [z * vw + x] = new Vector2((float)x / width, (float)z / depth);
            }
        }

        // Build triangles (two per quad)
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

        Mesh mesh = new Mesh { name = "ProceduralTerrain" };

        // Use 32-bit indices for large terrains
        if (vertices.Length > 65535)
            mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;

        mesh.vertices  = vertices;
        mesh.uv        = uvs;
        mesh.triangles = triangles;
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        return mesh;
    }

    // Layered Perlin noise
    float SampleHeight(float x, float z)
    {
        float ox = seed * 0.1f;
        float oz = seed * 0.1f;

        float h  = Mathf.PerlinNoise((x + ox) / hillScale,  (z + oz) / hillScale)  * hillHeight;
              h += Mathf.PerlinNoise((x + ox) / midScale,   (z + oz) / midScale)   * midHeight;
              h += Mathf.PerlinNoise((x + ox) / roughScale, (z + oz) / roughScale) * roughHeight;
        return h;
    }
}
