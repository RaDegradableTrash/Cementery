using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Generates a floating island mesh: a Perlin-noise terrain top, a rocky carved underside,
/// and seamless skirt walls that stitch them together — all in one combined mesh.
///
/// Right-click the component header → "Rebuild Island" to preview in Edit Mode.
/// Pair with a rock/cliff material for best visual results.
/// </summary>
[RequireComponent(typeof(MeshFilter), typeof(MeshRenderer), typeof(MeshCollider))]
public class FloatingIsland : MonoBehaviour
{
    [Header("Shape")]
    [Tooltip("Grid vertices per axis. 48 is a good balance.")]
    public int   resolution  = 48;
    [Tooltip("Island radius in world metres.")]
    public float radius      = 20f;
    [Range(0.05f, 0.9f)]
    [Tooltip("Fraction of radius used for the tapered falloff edge (0.3 = wide ramp, 0.1 = sharp cliff).")]
    public float edgeFalloff = 0.30f;

    [Header("Top Surface")]
    public float topHeight     = 10f;
    public float topNoiseScale = 0.07f;

    [Header("Underside")]
    [Tooltip("How far below the island centre the belly hangs.")]
    public float undersideDepth     = 14f;
    public float undersideNoiseScale = 0.09f;
    public float undersideRoughness  = 2.5f;

    [Header("Seed")]
    public int  seed       = 0;
    public bool randomSeed = true;

    [Header("Visual")]
    public Material islandMaterial;

    // ── Lifecycle ─────────────────────────────────────────────────────────────
    void Start()
    {
        if (randomSeed) seed = Random.Range(0, 100000);
        Build();
    }

    [ContextMenu("Rebuild Island")]
    public void Build()
    {
        Mesh mesh = GenerateMesh();
        GetComponent<MeshFilter>().sharedMesh   = mesh;
        GetComponent<MeshCollider>().sharedMesh = mesh;

        MeshRenderer mr = GetComponent<MeshRenderer>();
        if (islandMaterial != null)
            mr.sharedMaterial = islandMaterial;
        else if (mr.sharedMaterial == null)
            mr.sharedMaterial = new Material(Shader.Find("Universal Render Pipeline/Lit"));
    }

    // ── Mesh Generation ───────────────────────────────────────────────────────
    Mesh GenerateMesh()
    {
        int   vw   = resolution + 1;
        int   vc   = vw * vw;
        float step = (radius * 2f) / resolution;
        float ox   = seed * 0.73f;
        float oz   = seed * 0.31f;

        // ── Sample height fields ──────────────────────────────────────────────
        var topH = new float[vc];
        var botH = new float[vc];
        var fo   = new float[vc];   // radial falloff [0..1]

        for (int zi = 0; zi < vw; zi++)
        for (int xi = 0; xi < vw; xi++)
        {
            int   i  = zi * vw + xi;
            float x  = xi * step - radius;
            float z  = zi * step - radius;
            float d  = Mathf.Sqrt(x * x + z * z) / radius;

            // Smooth radial falloff
            float inner = 1f - edgeFalloff;
            fo[i] = d < inner ? 1f
                  : d > 1f   ? 0f
                  : 1f - Mathf.SmoothStep(0f, 1f, (d - inner) / edgeFalloff);

            // Top: layered Perlin noise
            float px = (x + ox) * topNoiseScale;
            float pz = (z + oz) * topNoiseScale;
            float n  = Mathf.PerlinNoise(px,      pz)      * 0.60f
                     + Mathf.PerlinNoise(px * 2f, pz * 2f) * 0.30f
                     + Mathf.PerlinNoise(px * 4f, pz * 4f) * 0.10f;
            topH[i] = n * topHeight * fo[i];

            // Bottom: concave belly with roughness noise
            float bpx = (x + ox * 1.37f) * undersideNoiseScale;
            float bpz = (z + oz * 1.37f) * undersideNoiseScale;
            float bn  = Mathf.PerlinNoise(bpx, bpz);
            botH[i]   = -(undersideDepth * fo[i]) - bn * undersideRoughness * fo[i];
        }

        // ── Allocate vertex arrays ────────────────────────────────────────────
        // Top layer  : indices 0 … vc-1
        // Bottom layer: indices vc … 2*vc-1
        var verts = new Vector3[2 * vc];
        var uvs   = new Vector2[2 * vc];

        for (int zi = 0; zi < vw; zi++)
        for (int xi = 0; xi < vw; xi++)
        {
            int   i  = zi * vw + xi;
            float x  = xi * step - radius;
            float z  = zi * step - radius;
            // Planar UV (0-1 range centred on island)
            var   uv = new Vector2(x / (radius * 2f) + 0.5f, z / (radius * 2f) + 0.5f);

            verts[i]      = new Vector3(x, topH[i], z);
            verts[vc + i] = new Vector3(x, botH[i], z);
            uvs[i]        = uv;
            uvs[vc + i]   = uv;
        }

        // ── Helpers ───────────────────────────────────────────────────────────
        bool Active(int i) => fo[i] > 0.01f;

        bool QuadActive(int xi2, int zi2)
        {
            if (xi2 < 0 || xi2 >= resolution || zi2 < 0 || zi2 >= resolution) return false;
            int bl = zi2 * vw + xi2;
            return Active(bl) && Active(bl + 1) && Active(bl + vw) && Active(bl + vw + 1);
        }

        var tris = new List<int>(vc * 4);

        // ── Top face ──────────────────────────────────────────────────────────
        // Winding: CW from above → normal points +Y
        for (int zi = 0; zi < resolution; zi++)
        for (int xi = 0; xi < resolution; xi++)
        {
            int bl = zi * vw + xi;
            if (!Active(bl) || !Active(bl+1) || !Active(bl+vw) || !Active(bl+vw+1)) continue;

            tris.Add(bl);     tris.Add(bl+vw);   tris.Add(bl+vw+1);
            tris.Add(bl);     tris.Add(bl+vw+1); tris.Add(bl+1);
        }

        // ── Bottom face ───────────────────────────────────────────────────────
        // Reversed winding → normal points -Y
        for (int zi = 0; zi < resolution; zi++)
        for (int xi = 0; xi < resolution; xi++)
        {
            int bl  = zi * vw + xi;
            if (!Active(bl) || !Active(bl+1) || !Active(bl+vw) || !Active(bl+vw+1)) continue;
            int bbl = vc + bl;

            tris.Add(bbl);     tris.Add(bbl+vw+1); tris.Add(bbl+vw);
            tris.Add(bbl);     tris.Add(bbl+1);    tris.Add(bbl+vw+1);
        }

        // ── Skirt walls ───────────────────────────────────────────────────────
        // Horizontal edges (zi constant, running along +X)
        // Wall appears where one adjacent Z quad is rendered and the other is not.
        for (int zi = 0; zi <= resolution; zi++)
        for (int xi = 0; xi < resolution; xi++)
        {
            int v0 = zi * vw + xi;
            int v1 = v0 + 1;
            if (!Active(v0) || !Active(v1)) continue;

            bool hasAbove = QuadActive(xi, zi);       // quad in +Z direction
            bool hasBelow = QuadActive(xi, zi - 1);   // quad in -Z direction
            if (hasAbove == hasBelow) continue;

            int bv0 = vc + v0, bv1 = vc + v1;

            if (hasAbove) // wall faces -Z (outward)
            {
                tris.Add(v0); tris.Add(v1);  tris.Add(bv1);
                tris.Add(v0); tris.Add(bv1); tris.Add(bv0);
            }
            else          // wall faces +Z (outward)
            {
                tris.Add(v0); tris.Add(bv1); tris.Add(v1);
                tris.Add(v0); tris.Add(bv0); tris.Add(bv1);
            }
        }

        // Vertical edges (xi constant, running along +Z)
        for (int zi = 0; zi < resolution; zi++)
        for (int xi = 0; xi <= resolution; xi++)
        {
            int v0 = zi * vw + xi;
            int v1 = v0 + vw;
            if (!Active(v0) || !Active(v1)) continue;

            bool hasRight = QuadActive(xi,     zi);   // quad in +X direction
            bool hasLeft  = QuadActive(xi - 1, zi);   // quad in -X direction
            if (hasRight == hasLeft) continue;

            int bv0 = vc + v0, bv1 = vc + v1;

            if (hasRight) // wall faces -X (outward)
            {
                tris.Add(v0); tris.Add(bv1); tris.Add(v1);
                tris.Add(v0); tris.Add(bv0); tris.Add(bv1);
            }
            else          // wall faces +X (outward)
            {
                tris.Add(v0); tris.Add(v1);  tris.Add(bv1);
                tris.Add(v0); tris.Add(bv1); tris.Add(bv0);
            }
        }

        // ── Build mesh ────────────────────────────────────────────────────────
        var mesh = new Mesh { name = "FloatingIsland" };
        if (verts.Length > 65535)
            mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;

        mesh.vertices  = verts;
        mesh.uv        = uvs;
        mesh.triangles = tris.ToArray();
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        return mesh;
    }
}
