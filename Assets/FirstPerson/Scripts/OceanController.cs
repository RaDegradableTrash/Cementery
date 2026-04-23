using UnityEngine;

/// <summary>
/// Generates and animates a large ocean surface using Gerstner waves (same model used in
/// Sea of Thieves, Rust, etc.). Wave directions, steepness, and wavelength are all tunable.
///
/// Assign a URP Lit or custom water material for best visuals.
/// Call GetWaveHeight(worldPos) for buoyancy, boat bobbing, or swimming detection.
/// </summary>
[RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
public class OceanController : MonoBehaviour
{
    [System.Serializable]
    public struct WaveLayer
    {
        [Range(0f, 360f)] public float directionDeg;
        /// <summary>Controls peak sharpness. Keep total steepness sum below 1 to avoid cusps.</summary>
        [Range(0f, 1f)]   public float steepness;
        public float wavelength;   // metres
        public float speed;        // metres per second
    }

    [Header("Mesh")]
    [Tooltip("Vertices per axis. 64 → smooth waves; 96+ → fine chop visible up close.")]
    public int   resolution = 64;
    [Tooltip("World-space size of the ocean plane in metres.")]
    public float size       = 400f;

    [Header("Gerstner Waves")]
    public WaveLayer[] waveLayers = new WaveLayer[]
    {
        new WaveLayer { directionDeg =   0f, steepness = 0.22f, wavelength = 60f,  speed = 7f  },
        new WaveLayer { directionDeg =  40f, steepness = 0.18f, wavelength = 30f,  speed = 5f  },
        new WaveLayer { directionDeg = -25f, steepness = 0.13f, wavelength = 14f,  speed = 3f  },
        new WaveLayer { directionDeg =  70f, steepness = 0.07f, wavelength =  6f,  speed = 1.8f},
    };

    [Header("Material")]
    [Tooltip("Assign a URP Lit, URP Transparent, or custom ocean material.")]
    public Material oceanMaterial;
    [ColorUsage(true, true)]
    public Color deepColor    = new Color(0.02f, 0.12f, 0.30f, 0.95f);
    [Range(0f, 1f)]
    public float smoothness   = 0.93f;

    // ── Internal ─────────────────────────────────────────────────────────────
    private Mesh      _mesh;
    private Vector3[] _baseVerts;   // undisplaced flat positions (local space)
    private Vector3[] _animVerts;   // displaced positions written each frame
    private MeshRenderer _rend;

    private static readonly int s_BaseColor  = Shader.PropertyToID("_BaseColor");
    private static readonly int s_Smoothness = Shader.PropertyToID("_Smoothness");
    private MaterialPropertyBlock _mpb;

    // ── Lifecycle ─────────────────────────────────────────────────────────────
    void Start()
    {
        _rend = GetComponent<MeshRenderer>();
        _mpb  = new MaterialPropertyBlock();
        BuildMesh();
        SetupMaterial();
    }

    void Update()
    {
        AnimateWaves(Time.time);
    }

    // ── Public API ────────────────────────────────────────────────────────────

    /// <summary>
    /// Returns the ocean surface Y position (world space) directly above or below worldPos.
    /// Iterates Gerstner equations the same way as the mesh; use for buoyancy scripts.
    /// </summary>
    public float GetWaveHeight(Vector3 worldPos)
    {
        // Convert to local XZ, solve Gerstner, convert Y back to world
        Vector3 local     = transform.InverseTransformPoint(worldPos);
        Vector3 displaced = GerstnerDisplace(new Vector3(local.x, 0f, local.z), Time.time);
        return transform.TransformPoint(displaced).y;
    }

    // ── Mesh Construction ─────────────────────────────────────────────────────
    void BuildMesh()
    {
        int vw  = resolution + 1;
        int vc  = vw * vw;
        _baseVerts = new Vector3[vc];
        _animVerts = new Vector3[vc];
        var uvs  = new Vector2[vc];
        var tris = new int[resolution * resolution * 6];

        float cellSize = size / resolution;
        float half     = size * 0.5f;

        for (int zi = 0; zi <= resolution; zi++)
        for (int xi = 0; xi <= resolution; xi++)
        {
            int i = zi * vw + xi;
            _baseVerts[i] = new Vector3(xi * cellSize - half, 0f, zi * cellSize - half);
            uvs[i]        = new Vector2((float)xi / resolution, (float)zi / resolution);
        }

        int t = 0;
        for (int zi = 0; zi < resolution; zi++)
        for (int xi = 0; xi < resolution; xi++)
        {
            int bl = zi * vw + xi;
            tris[t++] = bl;       tris[t++] = bl + vw;     tris[t++] = bl + vw + 1;
            tris[t++] = bl;       tris[t++] = bl + vw + 1; tris[t++] = bl + 1;
        }

        _mesh = new Mesh { name = "Ocean" };
        if (vc > 65535) _mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
        _mesh.vertices  = _baseVerts;
        _mesh.uv        = uvs;
        _mesh.triangles = tris;
        GetComponent<MeshFilter>().sharedMesh = _mesh;
    }

    // ── Wave Animation ────────────────────────────────────────────────────────
    void AnimateWaves(float t)
    {
        for (int i = 0; i < _baseVerts.Length; i++)
            _animVerts[i] = GerstnerDisplace(_baseVerts[i], t);

        _mesh.vertices = _animVerts;
        _mesh.RecalculateNormals();
    }

    /// <summary>Gerstner wave displacement in local space.</summary>
    Vector3 GerstnerDisplace(Vector3 p, float t)
    {
        float dx = 0f, dy = 0f, dz = 0f;

        foreach (var w in waveLayers)
        {
            if (w.wavelength <= 0f) continue;

            float rad   = w.directionDeg * Mathf.Deg2Rad;
            float Dx    = Mathf.Cos(rad);
            float Dz    = Mathf.Sin(rad);
            float k     = 2f * Mathf.PI / w.wavelength;   // angular frequency
            float A     = w.steepness / k;                 // amplitude
            float phi   = w.speed * k;                     // phase speed
            float theta = k * (Dx * p.x + Dz * p.z) + phi * t;

            dx += w.steepness * A * Dx * Mathf.Cos(theta);
            dz += w.steepness * A * Dz * Mathf.Cos(theta);
            dy += A * Mathf.Sin(theta);
        }

        return new Vector3(p.x + dx, p.y + dy, p.z + dz);
    }

    // ── Material ──────────────────────────────────────────────────────────────
    void SetupMaterial()
    {
        if (oceanMaterial != null)
        {
            _rend.sharedMaterial = oceanMaterial;
        }
        else
        {
            // Fallback: basic URP Lit transparent material
            var mat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            mat.SetFloat("_Surface",    1f);          // 0=opaque, 1=transparent
            mat.SetFloat("_Blend",      0f);          // Alpha blend
            mat.SetFloat("_Smoothness", smoothness);
            mat.color = deepColor;
            _rend.sharedMaterial = mat;
        }

        // Push colour & smoothness each frame via property block (avoids material instance)
        _rend.GetPropertyBlock(_mpb);
        _mpb.SetColor(s_BaseColor,  deepColor);
        _mpb.SetFloat(s_Smoothness, smoothness);
        _rend.SetPropertyBlock(_mpb);
    }
}
