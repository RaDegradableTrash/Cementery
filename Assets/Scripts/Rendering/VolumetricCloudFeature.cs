using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

public class VolumetricCloudFeature : ScriptableRendererFeature
{
    [System.Serializable]
    public class CloudSettings
    {
        [Header("Heights")]
        [Tooltip("Minimum altitude of the clouds in world space.")]
        public float minHeight = 1000f;
        [Tooltip("Maximum altitude of the clouds in world space.")]
        public float maxHeight = 2000f;

        [Header("Densities & Shapes")]
        [Range(0.1f, 10.0f)] public float densityScale = 1.5f;
        [Range(0.0f, 1.0f)] public float threshold = 0.25f;
        public float baseNoiseScale = 0.0005f;
        public float detailNoiseScale = 0.003f;
        [Range(0.0f, 1.0f)] public float detailInfluence = 0.35f;
        [Tooltip("Vertical stretch factor. Value < 1.0 compresses the noise vertically to create more vertical layers and rich vertical details; value > 1.0 stretches the noise vertically.")]
        [Range(0.1f, 10.0f)] public float verticalStretch = 1.0f;

        [Header("Convective Shapes")]
        [Tooltip("Controls the outward convective bulging/spreading at the top of the clouds.")]
        [Range(0.0f, 2.0f)] public float convectiveWarp = 0.8f;

        [Tooltip("Controls the vertical waviness and irregularity of the cloud columns.")]
        [Range(0.0f, 1.0f)] public float verticalRandomness = 0.5f;

        [Tooltip("Controls the strength of the bulging cauliflower bubble structures on the boundaries.")]
        [Range(0.0f, 1.0f)] public float puffiness = 0.6f;

        [Tooltip("Controls how flat and clean the bottom of the cloud deck is.")]
        [Range(0.0f, 1.0f)] public float cloudBaseFlatness = 0.8f;

        [Tooltip("Controls the sharpness of the cloud boundary. Lower values make the edge extremely crisp and stylized (anime style), while higher values make it soft and wispy.")]
        [Range(0.001f, 0.5f)] public float edgeSoftness = 0.02f;

        [Header("Lighting & Color")]
        [Range(0.1f, 10.0f)] public float lightAbsorption = 2.5f;

        [Tooltip("Controls the intensity of the sunlight bleeding through thin cloud boundaries, making them look light and translucent.")]
        [Range(0.0f, 2.0f)] public float backlitGlow = 0.5f;
        public Color shadowColor = new Color(0.2f, 0.25f, 0.35f, 1.0f);
        public Color maxLightColor = new Color(1.0f, 0.95f, 0.85f, 1.0f);

        [Header("Wind & Movement")]
        public Vector3 baseWindSpeed = new Vector3(2.0f, 0.0f, 1.0f);
        public Vector3 detailWindSpeed = new Vector3(1.0f, 1.0f, 1.0f);

        public enum ResolutionScale
        {
            Full = 1,
            Half = 2,
            Quarter = 4
        }

        [Header("Performance Settings")]
        [Tooltip("Resolution scale of the volumetric clouds rendering target buffer. Full is the safest. Half/Quarter can improve performance but may have platform-specific blit issues.")]
        public ResolutionScale resolutionScale = ResolutionScale.Full;
        [Range(4, 64)] public int maxSteps = 16;
        [Tooltip("Controls the screen-space dither jitter grain strength. Set this to a lower value (e.g. 0.2) to completely remove the powdery/sandy grain and make the clouds silky-smooth.")]
        [Range(0.0f, 1.0f)] public float jitterStrength = 0.2f;
        public float shadowSampleDistance = 40.0f;

        [Header("Optimization Settings")]
        [Tooltip("The maximum distance from the camera up to which clouds will be rendered. Chunks beyond this are completely clipped for zero rendering cost.")]
        public float maxRenderDistance = 4000.0f;
        [Tooltip("Beyond this distance, cloud sampling steps are dynamically scaled down to optimize GPU fill-rate.")]
        public float farDistanceOptimization = 4000.0f;
        [Tooltip("The minimum raymarching step count enforced at far distances (lower steps = faster rendering).")]
        [Range(1, 16)] public int farStepCount = 4;

        [Header("Shader Reference")]
        public Shader cloudShader;
    }

    public CloudSettings settings = new CloudSettings();
    
    public static VolumetricCloudFeature Instance { get; private set; }

    private VolumetricCloudPass _cloudPass;
    private Texture3D _baseNoiseTex;
    private Texture3D _detailNoiseTex;

    public override void Create()
    {
        Instance = this;

        // Force find the shader to recover from any serialized null states
        settings.cloudShader = Shader.Find("Hidden/Universal Render Pipeline/VolumetricCloud");

        // Generate procedural 3D noises if they don't exist yet
        if (_baseNoiseTex == null)
        {
            _baseNoiseTex = GenerateBaseNoise();
        }

        if (_detailNoiseTex == null)
        {
            _detailNoiseTex = GenerateDetailNoise();
        }

        // Always recreate the pass from scratch on Create() to completely wipe out 
        // any stale internal shader/material keyword states in Unity's memory!
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
        // Always force recover shader reference
        if (settings.cloudShader == null)
        {
            settings.cloudShader = Shader.Find("Hidden/Universal Render Pipeline/VolumetricCloud");
        }

        if (settings.cloudShader == null)
        {
            Debug.LogError("Volumetric Cloud Feature: VolumetricCloud shader could not be found. Please ensure the shader is placed correctly in your project.");
            return;
        }

        // Defensive check: Regenerate procedural textures if wiped by Play Mode transitions
        if (_baseNoiseTex == null) _baseNoiseTex = GenerateBaseNoise();
        if (_detailNoiseTex == null) _detailNoiseTex = GenerateDetailNoise();

        _cloudPass.Setup(settings, _baseNoiseTex, _detailNoiseTex);
        renderer.EnqueuePass(_cloudPass);
    }

    protected override void Dispose(bool disposing)
    {
        _cloudPass?.Dispose();
        
        if (_baseNoiseTex != null)
        {
            DestroyImmediate(_baseNoiseTex);
            _baseNoiseTex = null;
        }

        if (_detailNoiseTex != null)
        {
            DestroyImmediate(_detailNoiseTex);
            _detailNoiseTex = null;
        }
    }

    #region Procedural 3D Noise Generation

    private Texture3D GenerateBaseNoise()
    {
        int size = 64;
        Texture3D tex = new Texture3D(size, size, size, TextureFormat.R8, false);
        tex.name = "VolumetricCloudBaseNoise3D";
        tex.wrapMode = TextureWrapMode.Repeat;
        tex.filterMode = FilterMode.Bilinear;
        
        Color32[] colors = new Color32[size * size * size];
        int gridSize = 5;
        
        // Precompute grid points for high performance
        Vector3[,,] gridPoints = new Vector3[gridSize, gridSize, gridSize];
        Random.State oldState = Random.state;
        Random.InitState(1337); // Use a distinct seed to prevent coordinate alignment with detail noise!
        for (int z = 0; z < gridSize; z++)
        {
            for (int y = 0; y < gridSize; y++)
            {
                for (int x = 0; x < gridSize; x++)
                {
                    gridPoints[x, y, z] = new Vector3(Random.value, Random.value, Random.value);
                }
            }
        }
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
                                
                                // Wrap coordinates for tileability
                                int wx = (nx % gridSize + gridSize) % gridSize;
                                int wy = (ny % gridSize + gridSize) % gridSize;
                                int wz = (nz % gridSize + gridSize) % gridSize;
                                
                                Vector3 pointPos = new Vector3(nx, ny, nz) + gridPoints[wx, wy, wz];
                                float dist = Vector3.Distance(new Vector3(fx, fy, fz), pointPos);
                                if (dist < minDist) minDist = dist;
                            }
                        }
                    }
                    
                    // Invert cell distance to get cellular Worley shape
                    float noiseVal = Mathf.Clamp01(1.0f - minDist);
                    
                    // Add a tiny bit of Perlin detail inside the base texture itself to make base chunks look beautifully organic!
                    // We blend 85% Worley with 15% Perlin to give the isolated chunks fluffy micro-distortions
                    float px = fx * 2.0f;
                    float py = fy * 2.0f;
                    float pz = fz * 2.0f;
                    float perlinVal = Noise3D.Perlin(px, py, pz, gridSize * 2) * 0.5f + 0.5f;
                    
                    noiseVal = noiseVal * 0.85f + perlinVal * 0.15f;
                    byte val = (byte)(Mathf.Clamp01(noiseVal) * 255.0f);
                    
                    int idx = x + y * size + z * size * size;
                    colors[idx] = new Color32(val, 0, 0, 255);
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
        
        // Precompute grid points for high performance
        Vector3[,,] gridPoints = new Vector3[gridSize, gridSize, gridSize];
        Random.State oldState = Random.state;
        Random.InitState(42);
        for (int z = 0; z < gridSize; z++)
        {
            for (int y = 0; y < gridSize; y++)
            {
                for (int x = 0; x < gridSize; x++)
                {
                    gridPoints[x, y, z] = new Vector3(Random.value, Random.value, Random.value);
                }
            }
        }
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
                                
                                // Wrap coordinates for tileability
                                int wx = (nx % gridSize + gridSize) % gridSize;
                                int wy = (ny % gridSize + gridSize) % gridSize;
                                int wz = (nz % gridSize + gridSize) % gridSize;
                                
                                Vector3 pointPos = new Vector3(nx, ny, nz) + gridPoints[wx, wy, wz];
                                float dist = Vector3.Distance(new Vector3(fx, fy, fz), pointPos);
                                if (dist < minDist) minDist = dist;
                            }
                        }
                    }
                    
                    // Invert cell distance to get cellular Worley shape
                    float noiseVal = Mathf.Clamp01(1.0f - minDist);
                    byte val = (byte)(noiseVal * 255.0f);
                    
                    int idx = x + y * size + z * size * size;
                    colors[idx] = new Color32(val, 0, 0, 255);
                }
            }
        }
        
        tex.SetPixels32(colors);
        tex.Apply();
        return tex;
    }

    #endregion

    #region Improved 3D Perlin Noise Class

    private static class Noise3D
    {
        private static readonly int[] p = new int[512];
        private static readonly int[] permutation = {
            151,160,137,91,90,15,
            131,13,201,95,96,53,194,233, 7,225,140,36,103,30,69,142,8,99,37,240,21,10,23,
            190, 6,148,247,120,234,75,0,26,197,62,94,252,219,203,117,35,11,32,57,177,33,
            88,237,149,56,87,174,20,125,136,171,168, 68,175,74,165,71,134,139,48,27,166,
            77,146,158,231,83,111,229,122, 60,211,133,230,220,105,92,41,55,46,245,40,244,
            102,143,54, 65,25,63,161, 1,216,80,73,209,76,132,187,208, 89,18,169,200,196,
            135,130,116,188,159,86,164,100,109,198,173,186, 3,64,52,217,226,250,124,123,
            5,202,38,147,118,126,255,82,85,212,207,206, 59,227,47,162,119,171,112, 75,
            224,181,159,101,232, 45, 2,249,189,24,210,130,21,67,250, 8,19,244,223, 50,
            104,180,242, 29, 28,248,30,140,189,202,110, 72,137,252,245, 40,244, 25,149,
            2, 19, 79,232,244,116, 78,117, 28,161,252,129,236,  3,121, 13, 90,230,220,
            179, 37, 50,207,172,176,170,187,191,123, 74,211, 48,166, 47, 54, 84,  8,190,
            224,120,120,137,225, 36, 36,125,103,130, 22,192,172,201,150, 24,188,148,229,
            172,173,  2, 67, 72,130, 26,124,119,215,119, 24,210,216,131,209,198,173,193,
            193, 23,210, 89, 90, 14, 18,101,122, 19,200,223, 2,194,233,  7,225,140, 36,
            103, 30, 69,142,  8, 99, 37,240, 21, 10, 23,190,  6,148,247,120,234, 75,  0,
            26,197, 62, 94,252,219,203,117, 35, 11, 32, 57,177, 33, 88,237,149, 56, 87,
            174, 20,125,136,171,168, 68,175, 74,165, 71, 134,139, 48, 27,166, 77,146,158,
            231, 83,111,229,122, 60,211,133,230,220,105, 92, 41, 55, 46,245, 40,244,102,
            143, 54, 65, 25, 63,161,  1,216, 80, 73,209, 76,132,187,208, 89, 18,169,200,
            196,135,130,116,188,159, 86,164,100,109,198,173,186,  3, 64, 52,217,226,250,
            124,123,  5,202, 38,147,118,126,255, 82, 85,212,207,206, 59,227, 47,162,119,
            171,112, 75,224,181,159,101,232, 45,  2,249,189, 24,210,130, 21, 67,250,  8,
            19,244,223, 50,104,180,242, 29, 28,248, 30,140,189,202,110, 72,137,252,245,
            40,244, 25,149,  2, 19, 79,232,244,116, 78,117, 28,161,252,129,236,  3,121,
            13, 90,230,220,179, 37, 50,207,172,176,170,187,191,123, 74,211, 48,166, 47,
            54, 84,  8,190,224,120,120,137,225, 36, 36,125,103,130, 22,192,172,201,150,
            24,188,148,229,172,173,  2, 67, 72,130, 26,124,119,215,119, 24,210,216,131,
            209,198,173,193,193, 23,210, 89, 90, 14, 18,101,122, 19,200,223,  2,194,233,
            7,225,140, 36,103, 30, 69,142,  8, 99, 37,240, 21, 10, 23,190,  6,148,247,
            120,234, 75,  0, 26,197, 62, 94,252,219,203,117, 35, 11, 32, 57,177, 33,
            88,237,149, 56, 87,174, 20,125,136,171,168, 68,175, 74,165, 71, 134,139, 48,
            27,166, 77,146,158,231, 83,111,229,122, 60,211,133,230,220,105, 92, 41, 55,
            46,245, 40,244,102,143, 54, 65, 25, 63,161,  1,216, 80, 73,209, 76,132,187,
            208, 89, 18,169,200,196,135,130,116,188,159, 86,164,100,109,198,173,186,  3,
            64, 52, 217,226,250,124,123,  5,202, 38,147,118,126,255, 82, 85,212,207,206,
            59,227, 47,162,119,171,112, 75,224,181,159,101,232, 45,  2,249,189, 24,210,
            130, 21, 67,250,  8, 19,244,223, 50,104,180,242, 29, 28,248, 30,140,189,202,
            110, 72,137,252,245, 40,244, 25,149,  2, 19, 79,232,244,116, 78,117, 28,161,
            252,129,236,  3,121, 13, 90,230,220,179, 37, 50,207,172,176,170,187,191,123,
            74,211, 48,166, 47, 54, 84,  8,190,224,120,120,137,225, 36, 36,125,103,130,
            22,192,172,201,150, 24,188,148,229,172,173,  2, 67, 72,130, 26,124,119,215,
            119, 24,210,216,131,209,198,173,193,193, 23,210, 89, 90, 14, 18,101,122, 19,
            200,223, 2,194,233,  7,225,140, 36, 103, 30, 69,142,  8, 99, 37,240, 21, 10,
            23,190,  6,148,247,120,234, 75,  0, 26,197, 62, 94,252,219,203,117, 35, 11,
            32, 57,177, 33, 88,237,149, 56, 87, 174, 20, 125, 136, 171, 168, 68, 175, 74,
            165, 71, 134, 139, 48, 27, 166, 77, 146, 158, 231, 83, 111, 229, 122, 60, 211,
            133, 230, 220, 105, 92, 41, 55, 46, 245, 40, 244, 102, 143, 54, 65, 25, 63,
            161, 1, 216, 80, 73, 209, 76, 132, 187, 208, 89, 18, 169, 200, 196, 135, 130,
            116, 188, 159, 86, 164, 100, 109, 198, 173, 186, 3, 64, 52, 217, 226, 250,
            124, 123, 5, 202, 38, 147, 118, 126, 255, 82, 85, 212, 207, 206, 59, 227, 47,
            162, 119, 171, 112, 75, 224, 181, 159, 101, 232, 45, 2, 249, 189, 24, 210,
            130, 21, 67, 250, 8, 19, 244, 223, 50, 104, 180, 242, 29, 28, 248, 30, 140,
            189, 202, 110, 72, 137, 252, 245, 40, 244, 25, 149, 2, 19, 79, 232, 244, 116,
            78, 117, 28, 161, 252, 129, 236, 3, 121, 13, 90, 230, 220, 179, 37, 50, 207,
            172, 176, 170, 187, 191, 123, 74, 211, 48, 166, 47, 54, 84, 8, 190, 224, 120,
            120, 137, 225, 36, 36, 125, 103, 130, 22, 192, 172, 201, 150, 24, 188, 148,
            229, 172, 173, 2, 67, 72, 130, 26, 124, 119, 215, 119, 24, 210, 216, 131,
            209, 198, 173, 193
        };

        static Noise3D()
        {
            for (int i = 0; i < 256; i++)
            {
                p[i] = permutation[i];
                p[256 + i] = permutation[i];
            }
        }

        private static float Fade(float t)
        {
            return t * t * t * (t * (t * 6f - 15f) + 10f);
        }

        private static float Lerp(float t, float a, float b)
        {
            return a + t * (b - a);
        }

        private static float Grad(int hash, float x, float y, float z)
        {
            int h = hash & 15;
            float u = h < 8 ? x : y;
            float v = h < 4 ? y : h == 12 || h == 14 ? x : z;
            return ((h & 1) == 0 ? u : -u) + ((h & 2) == 0 ? v : -v);
        }

        public static float Perlin(float x, float y, float z, int tile)
        {
            int X = Mathf.FloorToInt(x);
            int Y = Mathf.FloorToInt(y);
            int Z = Mathf.FloorToInt(z);

            float fx = x - X;
            float fy = y - Y;
            float fz = z - Z;

            if (tile > 0)
            {
                X = (X % tile + tile) % tile;
                Y = (Y % tile + tile) % tile;
                Z = (Z % tile + tile) % tile;
            }

            int X1 = (tile > 0) ? (X + 1) % tile : X + 1;
            int Y1 = (tile > 0) ? (Y + 1) % tile : Y + 1;
            int Z1 = (tile > 0) ? (Z + 1) % tile : Z + 1;

            X = X & 255;
            Y = Y & 255;
            Z = Z & 255;
            X1 = X1 & 255;
            Y1 = Y1 & 255;
            Z1 = Z1 & 255;

            float u = Fade(fx);
            float v = Fade(fy);
            float w = Fade(fz);

            int A = p[X] + Y;
            int AA = p[A & 255] + Z;
            int AB = p[(A + 1) & 255] + Z;
            int B = p[X1] + Y;
            int BA = p[B & 255] + Z;
            int BB = p[(B + 1) & 255] + Z;

            return Lerp(w, Lerp(v, Lerp(u, Grad(p[AA & 255], fx, fy, fz),
                                           Grad(p[BA & 255], fx - 1, fy, fz)),
                                   Lerp(u, Grad(p[AB & 255], fx, fy - 1, fz),
                                           Grad(p[BB & 255], fx - 1, fy - 1, fz))),
                           Lerp(v, Lerp(u, Grad(p[(AA + 1) & 255], fx, fy, fz - 1),
                                           Grad(p[(BA + 1) & 255], fx - 1, fy, fz - 1)),
                                   Lerp(u, Grad(p[(AB + 1) & 255], fx, fy - 1, fz - 1),
                                           Grad(p[(BB + 1) & 255], fx - 1, fy - 1, fz - 1))));
        }
    }

    #endregion
}

public class VolumetricCloudPass : ScriptableRenderPass
{
    private VolumetricCloudFeature.CloudSettings _settings;
    private Material _material;
    private Texture3D _baseNoise;
    private Texture3D _detailNoise;
    private RTHandle _lowResCloudTexture;

    private static readonly int BaseNoiseTexId = Shader.PropertyToID("_BaseNoiseTex");
    private static readonly int DetailNoiseTexId = Shader.PropertyToID("_DetailNoiseTex");
    private static readonly int CloudMinHeightId = Shader.PropertyToID("_CloudMinHeight");
    private static readonly int CloudMaxHeightId = Shader.PropertyToID("_CloudMaxHeight");
    private static readonly int CloudDensityScaleId = Shader.PropertyToID("_CloudDensityScale");
    private static readonly int CloudThresholdId = Shader.PropertyToID("_CloudThreshold");
    private static readonly int BaseScaleId = Shader.PropertyToID("_BaseScale");
    private static readonly int DetailScaleId = Shader.PropertyToID("_DetailScale");
    private static readonly int DetailInfluenceId = Shader.PropertyToID("_DetailInfluence");
    private static readonly int VerticalStretchId = Shader.PropertyToID("_VerticalStretch");
    private static readonly int ConvectiveWarpId = Shader.PropertyToID("_ConvectiveWarp");
    private static readonly int VerticalRandomnessId = Shader.PropertyToID("_VerticalRandomness");
    private static readonly int PuffinessId = Shader.PropertyToID("_Puffiness");
    private static readonly int CloudBaseFlatnessId = Shader.PropertyToID("_CloudBaseFlatness");
    private static readonly int AbsorptionId = Shader.PropertyToID("_Absorption");
    private static readonly int ShadowColorId = Shader.PropertyToID("_ShadowColor");
    private static readonly int MaxLightColorId = Shader.PropertyToID("_MaxLightColor");
    private static readonly int BaseWindSpeedId = Shader.PropertyToID("_BaseWindSpeed");
    private static readonly int DetailWindSpeedId = Shader.PropertyToID("_DetailWindSpeed");
    private static readonly int StepCountId = Shader.PropertyToID("_StepCount");
    private static readonly int JitterStrengthId = Shader.PropertyToID("_JitterStrength");
    private static readonly int LightStepDistanceId = Shader.PropertyToID("_LightStepDistance");
    private static readonly int EdgeSoftnessId = Shader.PropertyToID("_EdgeSoftness");
    private static readonly int BacklitGlowId = Shader.PropertyToID("_BacklitGlow");
    private static readonly int MaxRenderDistanceId = Shader.PropertyToID("_MaxRenderDist");
    private static readonly int FarDistanceOptimizationId = Shader.PropertyToID("_FarDist");
    private static readonly int FarStepCountId = Shader.PropertyToID("_FarSteps");

    public VolumetricCloudPass(VolumetricCloudFeature.CloudSettings settings)
    {
        _settings = settings;
    }

    public void Setup(VolumetricCloudFeature.CloudSettings settings, Texture3D baseNoise, Texture3D detailNoise)
    {
        _settings = settings;
        _baseNoise = baseNoise;
        _detailNoise = detailNoise;

        // Defensive check: If the material is null, or was bound to Unity's temporary "Internal-Loading" shader 
        // during compilation, or is missing our custom properties, or the shader asset reference changed - force recreate it.
        if (_material == null || _material.shader == null || 
            _material.shader.name.Contains("Internal-Loading") || 
            !_material.HasProperty("_BaseNoiseTex") ||
            _material.shader != _settings.cloudShader)
        {
            if (_material != null)
            {
                CoreUtils.Destroy(_material);
            }

            if (_settings.cloudShader != null)
            {
                _material = CoreUtils.CreateEngineMaterial(_settings.cloudShader);
            }
        }
    }

    public override void OnCameraSetup(CommandBuffer cmd, ref RenderingData renderingData)
    {
        // Explicitly configure the camera color target as the render target for this pass.
        // This prevents URP from optimizing the pass away and ensures depth textures are bound correctly.
        ConfigureTarget(renderingData.cameraData.renderer.cameraColorTargetHandle);

        if (_settings.resolutionScale != VolumetricCloudFeature.CloudSettings.ResolutionScale.Full)
        {
            RenderTextureDescriptor desc = renderingData.cameraData.cameraTargetDescriptor;
            desc.depthBufferBits = 0;
            desc.colorFormat = RenderTextureFormat.ARGB32;
            desc.sRGB = renderingData.cameraData.cameraTargetDescriptor.sRGB;
            
            int scale = (int)_settings.resolutionScale;
            desc.width = Mathf.Max(1, desc.width / scale);
            desc.height = Mathf.Max(1, desc.height / scale);

            RenderingUtils.ReAllocateIfNeeded(ref _lowResCloudTexture, desc, FilterMode.Bilinear, TextureWrapMode.Clamp, name: "_LowResCloudTexture");
        }
    }

    public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
    {
        // Ultimate Defensive Guard: If the shader is currently compiling, bound to 
        // Unity's placeholder shader, or missing our properties, abort execution early.
        if (_material == null || _material.shader == null || 
            _material.shader.name.Contains("Internal-Loading") || 
            !_material.HasProperty("_BaseNoiseTex") ||
            _baseNoise == null || _detailNoise == null) 
            return;

        CommandBuffer cmd = CommandBufferPool.Get("Volumetric Clouds");

        // Pass Settings to the material
        _material.SetTexture(BaseNoiseTexId, _baseNoise);
        _material.SetTexture(DetailNoiseTexId, _detailNoise);
        
        _material.SetFloat(CloudMinHeightId, _settings.minHeight);
        _material.SetFloat(CloudMaxHeightId, _settings.maxHeight);
        _material.SetFloat(CloudDensityScaleId, _settings.densityScale);
        _material.SetFloat(CloudThresholdId, _settings.threshold);
        _material.SetFloat(BaseScaleId, _settings.baseNoiseScale);
        _material.SetFloat(DetailScaleId, _settings.detailNoiseScale);
        _material.SetFloat(DetailInfluenceId, _settings.detailInfluence);
        _material.SetFloat(VerticalStretchId, _settings.verticalStretch);
        _material.SetFloat(ConvectiveWarpId, _settings.convectiveWarp);
        _material.SetFloat(VerticalRandomnessId, _settings.verticalRandomness);
        _material.SetFloat(PuffinessId, _settings.puffiness);
        _material.SetFloat(CloudBaseFlatnessId, _settings.cloudBaseFlatness);
        _material.SetFloat(AbsorptionId, _settings.lightAbsorption);
        
        _material.SetColor(ShadowColorId, _settings.shadowColor);
        _material.SetColor(MaxLightColorId, _settings.maxLightColor);
        
        _material.SetVector(BaseWindSpeedId, new Vector4(_settings.baseWindSpeed.x, _settings.baseWindSpeed.y, _settings.baseWindSpeed.z, 0));
        _material.SetVector(DetailWindSpeedId, new Vector4(_settings.detailWindSpeed.x, _settings.detailWindSpeed.y, _settings.detailWindSpeed.z, 0));
        
        _material.SetFloat(StepCountId, _settings.maxSteps);
        _material.SetFloat(JitterStrengthId, _settings.jitterStrength);
        _material.SetFloat(LightStepDistanceId, _settings.shadowSampleDistance);
        _material.SetFloat(EdgeSoftnessId, _settings.edgeSoftness);
        _material.SetFloat(BacklitGlowId, _settings.backlitGlow);
        _material.SetFloat(MaxRenderDistanceId, _settings.maxRenderDistance);
        _material.SetFloat(FarDistanceOptimizationId, _settings.farDistanceOptimization);
        _material.SetFloat(FarStepCountId, _settings.farStepCount);

        // Set global shader variables so other shaders (like the terrain) can dynamically calculate cloud shadows!
        Shader.SetGlobalTexture("_BaseNoiseTex", _baseNoise);
        Shader.SetGlobalFloat("_CloudMinHeight", _settings.minHeight);
        Shader.SetGlobalFloat("_CloudMaxHeight", _settings.maxHeight);
        Shader.SetGlobalFloat("_CloudThreshold", _settings.threshold);
        Shader.SetGlobalFloat("_CloudDensityScale", _settings.densityScale);
        Shader.SetGlobalFloat("_BaseScale", _settings.baseNoiseScale);
        Shader.SetGlobalFloat("_VerticalStretch", _settings.verticalStretch);
        Shader.SetGlobalVector("_BaseWindSpeed", new Vector4(_settings.baseWindSpeed.x, _settings.baseWindSpeed.y, _settings.baseWindSpeed.z, 0));
        Shader.SetGlobalFloat("_ConvectiveWarp", _settings.convectiveWarp);

        // Blit from built-in blackTexture using the custom material.
        if (_settings.resolutionScale == VolumetricCloudFeature.CloudSettings.ResolutionScale.Full || _lowResCloudTexture == null)
        {
            // Full resolution: direct composite using Pass 0 (Blend SrcAlpha OneMinusSrcAlpha)
            cmd.Blit(Texture2D.blackTexture, renderingData.cameraData.renderer.cameraColorTargetHandle, _material, 0);
        }
        else
        {
            // Explicitly set active target and clear
            CoreUtils.SetRenderTarget(cmd, _lowResCloudTexture, RenderBufferLoadAction.DontCare, RenderBufferStoreAction.Store);
            cmd.ClearRenderTarget(false, true, Color.clear);

            // Pass 1 (Blend Off): raw raymarching capture to low-res RT, no destination blending
            cmd.Blit(Texture2D.blackTexture, _lowResCloudTexture, _material, 1);

            // Pass 2 (Blend SrcAlpha OneMinusSrcAlpha): correct upscale-composite to camera
            cmd.Blit(_lowResCloudTexture, renderingData.cameraData.renderer.cameraColorTargetHandle, _material, 2);
        }

        context.ExecuteCommandBuffer(cmd);
        CommandBufferPool.Release(cmd);
    }

    public override void OnCameraCleanup(CommandBuffer cmd)
    {
        // No cleanup needed
    }

    public void Dispose()
    {
        if (_lowResCloudTexture != null) _lowResCloudTexture.Release();
        CoreUtils.Destroy(_material);
    }
}
