// Legacy backup of cloud generation and rendering logic — alpha v0.2.2
// Copied from Assets/Scripts/Rendering/VolumetricCloudFeature.cs
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

public class VolumetricCloudFeature_Legacy_v0_2_2 : ScriptableRendererFeature
{
    [System.Serializable]
    public class CloudSettings
    {
        [Header("Heights")]
        public float minHeight = 1000f;
        public float maxHeight = 2000f;

        [Header("Densities & Shapes")]
        [Range(0.1f, 10.0f)] public float densityScale = 1.5f;
        [Range(0.0f, 1.0f)] public float threshold = 0.25f;
        public float baseNoiseScale = 0.0005f;
        public float detailNoiseScale = 0.003f;
        [Range(0.0f, 1.0f)] public float detailInfluence = 0.35f;
        [Range(0.1f, 10.0f)] public float verticalStretch = 1.0f;

        [Header("Convective Shapes")]
        [Range(0.0f, 2.0f)] public float convectiveWarp = 0.8f;
        [Range(0.0f, 1.0f)] public float verticalRandomness = 0.5f;
        [Range(0.0f, 1.0f)] public float puffiness = 0.6f;
        [Range(0.0f, 1.0f)] public float cloudBaseFlatness = 0.8f;
        [Range(0.001f, 0.5f)] public float edgeSoftness = 0.02f;

        [Header("Lighting & Color")]
        [Range(0.1f, 10.0f)] public float lightAbsorption = 2.5f;
        [Range(0.0f, 2.0f)] public float backlitGlow = 0.5f;
        public Color shadowColor = new Color(0.2f, 0.25f, 0.35f, 1.0f);
        public Color maxLightColor = new Color(1.0f, 0.95f, 0.85f, 1.0f);

        [Header("Wind & Movement")]
        public Vector3 baseWindSpeed = new Vector3(2.0f, 0.0f, 1.0f);
        public Vector3 detailWindSpeed = new Vector3(1.0f, 1.0f, 1.0f);

        public enum ResolutionScale { Full = 1, Half = 2, Quarter = 4 }

        [Header("Performance Settings")]
        public ResolutionScale resolutionScale = ResolutionScale.Quarter; 
        [Range(4, 64)] public int maxSteps = 16;
        [Range(0.0f, 1.0f)] public float jitterStrength = 0.2f;
        public float shadowSampleDistance = 40.0f;

        [Header("Optimization Settings")]
        public float maxRenderDistance = 4000.0f;
        public float farDistanceOptimization = 4000.0f;
        [Range(1, 16)] public int farStepCount = 4;

        [Header("Shader Reference")]
        public Shader cloudShader;
    }

    public CloudSettings settings = new CloudSettings();
    public static VolumetricCloudFeature_Legacy_v0_2_2 Instance { get; private set; }

    private VolumetricCloudPass _cloudPass;
    private Texture3D _baseNoiseTex;
    private Texture3D _detailNoiseTex;

    public override void Create()
    {
        Instance = this;
        settings.cloudShader = Shader.Find("Hidden/Universal Render Pipeline/VolumetricCloud");

        if (_baseNoiseTex == null) _baseNoiseTex = GenerateBaseNoise();
        if (_detailNoiseTex == null) _detailNoiseTex = GenerateDetailNoise();

        if (_cloudPass != null)
        {
            _cloudPass.Dispose();
            _cloudPass = null;
        }

        _cloudPass = new VolumetricCloudPass(settings);
        _cloudPass.renderPassEvent = RenderPassEvent.BeforeRenderingTransparents;
    }

    public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
    {
        if (settings.cloudShader == null) return;
        if (_baseNoiseTex == null) _baseNoiseTex = GenerateBaseNoise();
        if (_detailNoiseTex == null) _detailNoiseTex = GenerateDetailNoise();

        _cloudPass.Setup(settings, _baseNoiseTex, _detailNoiseTex);
        renderer.EnqueuePass(_cloudPass);
    }

    protected override void Dispose(bool disposing)
    {
        _cloudPass?.Dispose();
        if (_baseNoiseTex != null) { DestroyImmediate(_baseNoiseTex); _baseNoiseTex = null; }
        if (_detailNoiseTex != null) { DestroyImmediate(_detailNoiseTex); _detailNoiseTex = null; }
    }

    #region Procedural 3D Noise Generation
    // 这里是完整的、真实的云层噪声生成算法（备份）
    private Texture3D GenerateBaseNoise()
    {
        int size = 64;
        Texture3D tex = new Texture3D(size, size, size, TextureFormat.R8, false);
        tex.name = "VolumetricCloudBaseNoise3D";
        tex.wrapMode = TextureWrapMode.Repeat;
        tex.filterMode = FilterMode.Bilinear;
        
        Color32[] colors = new Color32[size * size * size];
        int gridSize = 5;
        
        Vector3[,,] gridPoints = new Vector3[gridSize, gridSize, gridSize];
        Random.State oldState = Random.state;
        Random.InitState(1337);
        for (int z = 0; z < gridSize; z++)
            for (int y = 0; y < gridSize; y++)
                for (int x = 0; x < gridSize; x++)
                    gridPoints[x, y, z] = new Vector3(Random.value, Random.value, Random.value);
        Random.state = oldState;
        
        for (int z = 0; z < size; z++)
        {
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float fx = (float)x / size * gridSize;
                    float fy = (float)y / size * gridSize;
                    float fz = (float)z / size * gridSize;
                    
                    int cellX = Mathf.FloorToInt(fx);
                    int cellY = Mathf.FloorToInt(fy);
                    int cellZ = Mathf.FloorToInt(fz);
                    
                    float minDist = 1e9f;
                    for (int dz = -1; dz <= 1; dz++)
                    {
                        for (int dy = -1; dy <= 1; dy++)
                        {
                            for (int dx = -1; dx <= 1; dx++)
                            {
                                int nx = cellX + dx;
                                int ny = cellY + dy;
                                int nz = cellZ + dz;
                                
                                int wx = (nx % gridSize + gridSize) % gridSize;
                                int wy = (ny % gridSize + gridSize) % gridSize;
                                int wz = (nz % gridSize + gridSize) % gridSize;
                                
                                Vector3 pointPos = new Vector3(nx, ny, nz) + gridPoints[wx, wy, wz];
                                float dist = Vector3.Distance(new Vector3(fx, fy, fz), pointPos);
                                if (dist < minDist) minDist = dist;
                            }
                        }
                    }
                    
                    float noiseVal = Mathf.Clamp01(1.0f - minDist);
                    float px = fx * 2.0f, py = fy * 2.0f, pz = fz * 2.0f;
                    float perlinVal = Noise3D.Perlin(px, py, pz, gridSize * 2) * 0.5f + 0.5f;
                    
                    noiseVal = noiseVal * 0.85f + perlinVal * 0.15f;
                    byte val = (byte)(Mathf.Clamp01(noiseVal) * 255.0f);
                    colors[x + y * size + z * size * size] = new Color32(val, 0, 0, 255);
                }
            }
        }
        tex.SetPixels32(colors);
        tex.Apply();
        return tex;
    }

    private Texture3D GenerateDetailNoise()
    {
        int size = 32;
        Texture3D tex = new Texture3D(size, size, size, TextureFormat.R8, false);
        tex.name = "VolumetricCloudDetailNoise3D";
        tex.wrapMode = TextureWrapMode.Repeat;
        tex.filterMode = FilterMode.Bilinear;
        
        Color32[] colors = new Color32[size * size * size];
        int gridSize = 8;
        
        Vector3[,,] gridPoints = new Vector3[gridSize, gridSize, gridSize];
        Random.State oldState = Random.state;
        Random.InitState(42);
        for (int z = 0; z < gridSize; z++)
            for (int y = 0; y < gridSize; y++)
                for (int x = 0; x < gridSize; x++)
                    gridPoints[x, y, z] = new Vector3(Random.value, Random.value, Random.value);
        Random.state = oldState;
        
        for (int z = 0; z < size; z++)
        {
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float fx = (float)x / size * gridSize;
                    float fy = (float)y / size * gridSize;
                    float fz = (float)z / size * gridSize;
                    
                    int cellX = Mathf.FloorToInt(fx);
                    int cellY = Mathf.FloorToInt(fy);
                    int cellZ = Mathf.FloorToInt(fz);
                    
                    float minDist = 1e9f;
                    for (int dz = -1; dz <= 1; dz++)
                    {
                        for (int dy = -1; dy <= 1; dy++)
                        {
                            for (int dx = -1; dx <= 1; dx++)
                            {
                                int nx = cellX + dx;
                                int ny = cellY + dy;
                                int nz = cellZ + dz;
                                
                                int wx = (nx % gridSize + gridSize) % gridSize;
                                int wy = (ny % gridSize + gridSize) % gridSize;
                                int wz = (nz % gridSize + gridSize) % gridSize;
                                
                                Vector3 pointPos = new Vector3(nx, ny, nz) + gridPoints[wx, wy, wz];
                                float dist = Vector3.Distance(new Vector3(fx, fy, fz), pointPos);
                                if (dist < minDist) minDist = dist;
                            }
                        }
                    }
                    
                    float noiseVal = Mathf.Clamp01(1.0f - minDist);
                    byte val = (byte)(noiseVal * 255.0f);
                    colors[x + y * size + z * size * size] = new Color32(val, 0, 0, 255);
                }
            }
        }
        tex.SetPixels32(colors);
        tex.Apply();
        return tex;
    }

    private static class Noise3D
    {
        private static readonly int[] p = new int[512];
        private static readonly int[] permutation = {
            151,160,137,91,90,15,131,13,201,95,96,53,194,233,7,225,140,36,103,30,69,142,8,99,37,240,21,10,23,
            190,6,148,247,120,234,75,0,26,197,62,94,252,219,203,117,35,11,32,57,177,33,88,237,149,56,87,174,20,125,136,171,168,68,175,74,165,71,134,139,48,27,166,
            77,146,158,231,83,111,229,122,60,211,133,230,220,105,92,41,55,46,245,40,244,102,143,54,65,25,63,161,1,216,80,73,209,76,132,187,208,89,18,169,200,196,
            135,130,116,188,159,86,164,100,109,198,173,186,3,64,52,217,226,250,124,123,5,202,38,147,118,126,255,82,85,212,207,206,59,227,47,162,119,171,112,75,
            224,181,159,101,232,45,2,249,189,24,210,130,21,67,250,8,19,244,223,50,104,180,242,29,28,248,30,140,189,202,110,72,137,252,245,40,244,25,149,2,19,79,
            232,244,116,78,117,28,161,252,129,236,3,121,13,90,230,220,179,37,50,207,172,176,170,187,191,123,74,211,48,166,47,54,84,8,190,224,120,120,137,225,36,
            36,125,103,130,22,192,172,201,150,24,188,148,229,172,173,2,67,72,130,26,124,119,215,119,24,210,216,131,209,198,173,193,193,23,210,89,90,14,18,101,122,
            19,200,223,2,194,233,7,225,140,36,103,30,69,142,8,99,37,240,21,10,23,190,6,148,247,120,234,75,0,26,197,62,94,252,219,203,117,35,11,32,57,177,33,
            88,237,149,56,87,174,20,125,136,171,168,68,175,74,165,71,134,139,48,27,166,77,146,158,231,83,111,229,122,60,211,133,230,220,105,92,41,55,46,245,40,
            244,102,143,54,65,25,63,161,1,216,80,73,209,76,132,187,208,89,18,169,200,196,135,130,116,188,159,86,164,100,109,198,173,186,3,64,52,217,226,250,
            124,123,5,202,38,147,118,126,255,82,85,212,207,206,59,227,47,162,119,171,112,75,224,181,159,101,232,45,2,249,189,24,210,130,21,67,250,8,19,244,223,
            50,104,180,242,29,28,248,30,140,189,202,110,72,137,252,245,40,244,25,149,2,19,79,232,244,116,78,117,28,161,252,129,236,3,121,13,90,230,220,179,37,
            50,207,172,176,170,187,191,123,74,211,48,166,47,54,84,8,190,224,120,120,137,225,36,36,125,103,130,22,192,172,201,150,24,188,148,229,172,173,2,67,
            72,130,26,124,119,215,119,24,210,216,131,209,198,173,193
        };

        static Noise3D()
        {
            for (int i = 0; i < 256; i++) { p[i] = permutation[i]; p[256 + i] = permutation[i]; }
        }

        private static float Fade(float t) { return t * t * t * (t * (t * 6f - 15f) + 10f); }
        private static float Lerp(float t, float a, float b) { return a + t * (b - a); }
        private static float Grad(int hash, float x, float y, float z)
        {
            int h = hash & 15;
            float u = h < 8 ? x : y;
            float v = h < 4 ? y : h == 12 || h == 14 ? x : z;
            return ((h & 1) == 0 ? u : -u) + ((h & 2) == 0 ? v : -v);
        }

        public static float Perlin(float x, float y, float z, int tile)
        {
            int X = Mathf.FloorToInt(x), Y = Mathf.FloorToInt(y), Z = Mathf.FloorToInt(z);
            float fx = x - X, fy = y - Y, fz = z - Z;

            if (tile > 0)
            {
                X = (X % tile + tile) % tile; Y = (Y % tile + tile) % tile; Z = (Z % tile + tile) % tile;
            }

            int X1 = (tile > 0) ? (X + 1) % tile : X + 1;
            int Y1 = (tile > 0) ? (Y + 1) % tile : Y + 1;
            int Z1 = (tile > 0) ? (Z + 1) % tile : Z + 1;

            X &= 255; Y &= 255; Z &= 255; X1 &= 255; Y1 &= 255; Z1 &= 255;

            float u = Fade(fx), v = Fade(fy), w = Fade(fz);
            int A = p[X] + Y, AA = p[A & 255] + Z, AB = p[(A + 1) & 255] + Z, B = p[X1] + Y, BA = p[B & 255] + Z, BB = p[(B + 1) & 255] + Z;

            return Lerp(w, Lerp(v, Lerp(u, Grad(p[AA & 255], fx, fy, fz), Grad(p[BA & 255], fx - 1, fy, fz)),
                                   Lerp(u, Grad(p[AB & 255], fx, fy - 1, fz), Grad(p[BB & 255], fx - 1, fy - 1, fz))),
                           Lerp(v, Lerp(u, Grad(p[(AA + 1) & 255], fx, fy, fz - 1), Grad(p[(BA + 1) & 255], fx - 1, fy, fz - 1)),
                                   Lerp(u, Grad(p[(AB + 1) & 255], fx, fy - 1, fz - 1), Grad(p[(BB + 1) & 255], fx - 1, fy - 1, fz - 1))));
        }
    }
    #endregion
}

// Note: VolumetricCloudPass class is intentionally not duplicated here; this file preserves
// the procedural noise generation and settings for legacy reference. See original
// Assets/Scripts/Rendering/VolumetricCloudFeature.cs for full runtime pass implementation.
