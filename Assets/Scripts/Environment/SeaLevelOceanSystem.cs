using System.Collections.Generic;
using UnityEngine;

namespace EnvironmentSystem
{
    /// <summary>
    /// Runtime sea-level surface for the streamed desert world.
    /// Uses a fixed pool of chunk-sized ocean tiles and shader-side wave animation.
    /// </summary>
    public sealed class SeaLevelOceanSystem : MonoBehaviour
    {
        private const string RuntimeObjectName = "[SYSTEM] SeaLevelOcean";
        private const string ShaderName = "Environment/URPSeaLevelOcean";
        private const float TargetRefreshInterval = 0.75f;
        private const float ChunkSizeRefreshInterval = 2f;

        [Header("Coverage")]
        [Tooltip("World-space y coordinate for the still-water baseline.")]
        public float seaLevel = 0f;
        [Tooltip("Ocean tiles extend this many grid cells around the tracked target.")]
        [Range(1, 4)] public int tileRadius = 2;
        [Tooltip("Fallback tile size. Runtime will use WorldStreamer chunk size when available.")]
        [Min(32f)] public float fallbackTileSize = 256f;
        [Tooltip("Grid resolution per ocean tile. Keep modest because wave animation runs in the shader.")]
        [Range(4, 64)] public int tileResolution = 24;

        [Header("Water Look")]
        public Color shallowColor = new Color(0.10f, 0.42f, 0.52f, 0.62f);
        public Color deepColor = new Color(0.02f, 0.12f, 0.28f, 0.78f);
        [Range(0.05f, 3f)] public float waveAmplitude = 0.55f;
        [Range(0.05f, 6f)] public float waveSpeed = 0.65f;
        [Range(8f, 160f)] public float primaryWaveLength = 62f;
        [Range(4f, 80f)] public float secondaryWaveLength = 24f;
        [Range(0f, 3f)] public float shimmerStrength = 1.2f;
        [Range(8f, 256f)] public float specularPower = 96f;
        [Range(0f, 1f)] public float alpha = 0.72f;

        [Header("Foam")]
        public Color foamColor = new Color(0.92f, 0.98f, 1f, 0.88f);
        [Range(0f, 4f)] public float foamIntensity = 1.35f;
        [Range(0f, 1f)] public float crestFoamThreshold = 0.68f;
        [Range(0.25f, 8f)] public float shorelineFoamDepth = 2.4f;
        [Range(0.005f, 0.2f)] public float foamNoiseScale = 0.045f;

        private static readonly int ShallowColorId = Shader.PropertyToID("_ShallowColor");
        private static readonly int DeepColorId = Shader.PropertyToID("_DeepColor");
        private static readonly int SeaLevelId = Shader.PropertyToID("_SeaLevel");
        private static readonly int WaveAmplitudeId = Shader.PropertyToID("_WaveAmplitude");
        private static readonly int WaveSpeedId = Shader.PropertyToID("_WaveSpeed");
        private static readonly int PrimaryWaveLengthId = Shader.PropertyToID("_PrimaryWaveLength");
        private static readonly int SecondaryWaveLengthId = Shader.PropertyToID("_SecondaryWaveLength");
        private static readonly int ShimmerStrengthId = Shader.PropertyToID("_ShimmerStrength");
        private static readonly int SpecularPowerId = Shader.PropertyToID("_SpecularPower");
        private static readonly int AlphaId = Shader.PropertyToID("_Alpha");
        private static readonly int FoamColorId = Shader.PropertyToID("_FoamColor");
        private static readonly int FoamIntensityId = Shader.PropertyToID("_FoamIntensity");
        private static readonly int CrestFoamThresholdId = Shader.PropertyToID("_CrestFoamThreshold");
        private static readonly int ShorelineFoamDepthId = Shader.PropertyToID("_ShorelineFoamDepth");
        private static readonly int FoamNoiseScaleId = Shader.PropertyToID("_FoamNoiseScale");

        private readonly List<Transform> _tiles = new List<Transform>(81);
        private Transform _target;
        private GameObject _cachedPlayer;
        private Camera _cachedCamera;
        private Material _material;
        private MaterialPropertyBlock _propertyBlock;
        private Mesh _sharedMesh;
        private float _tileSize;
        private float _nextTargetRefreshTime;
        private float _nextChunkSizeRefreshTime;
        private int _lastCenterX = int.MinValue;
        private int _lastCenterZ = int.MinValue;
        private int _lastTileRadius = -1;
        private int _lastTileResolution = -1;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Bootstrap()
        {
            if (FindFirstObjectByType<SeaLevelOceanSystem>(FindObjectsInactive.Include) != null)
                return;

            GameObject oceanObject = new GameObject(RuntimeObjectName);
            DontDestroyOnLoad(oceanObject);
            oceanObject.AddComponent<ExcludeFromOptimization>();
            oceanObject.AddComponent<SeaLevelOceanSystem>();
        }

        private void Awake()
        {
            _propertyBlock = new MaterialPropertyBlock();
            _tileSize = Mathf.Max(32f, fallbackTileSize);
            EnsureMaterial();
            RebuildTilePool();
        }

        private void OnDestroy()
        {
            if (_sharedMesh != null)
            {
                Destroy(_sharedMesh);
                _sharedMesh = null;
            }

            if (_material != null)
            {
                Destroy(_material);
                _material = null;
            }
        }

        private void Update()
        {
            RefreshTargetIfNeeded();
            RefreshChunkSizeIfNeeded();
            EnsureRuntimeShape();
            PushMaterialSettings();

            if (_target == null)
                return;

            Vector3 targetPosition = _target.position;
            int centerX = Mathf.RoundToInt(targetPosition.x / _tileSize);
            int centerZ = Mathf.RoundToInt(targetPosition.z / _tileSize);

            if (centerX == _lastCenterX && centerZ == _lastCenterZ)
                return;

            _lastCenterX = centerX;
            _lastCenterZ = centerZ;
            PositionTiles(centerX, centerZ);
        }

        private void RefreshTargetIfNeeded()
        {
            if (_target != null && _target.gameObject.activeInHierarchy && Time.time < _nextTargetRefreshTime)
                return;

            _nextTargetRefreshTime = Time.time + TargetRefreshInterval;

            WorldStreamer streamer = WorldStreamer.Instance;
            if (streamer != null && streamer.trackingTarget != null)
            {
                _target = streamer.trackingTarget;
                return;
            }

            if (_cachedPlayer == null || !_cachedPlayer.activeInHierarchy)
                _cachedPlayer = GameObject.FindGameObjectWithTag("Player");

            if (_cachedPlayer != null && _cachedPlayer.activeInHierarchy)
            {
                _target = _cachedPlayer.transform;
                return;
            }

            if (_cachedCamera == null || !_cachedCamera.gameObject.activeInHierarchy)
                _cachedCamera = Camera.main;

            _target = _cachedCamera != null ? _cachedCamera.transform : null;
        }

        private void RefreshChunkSizeIfNeeded()
        {
            if (Time.time < _nextChunkSizeRefreshTime)
                return;

            _nextChunkSizeRefreshTime = Time.time + ChunkSizeRefreshInterval;

            float nextSize = Mathf.Max(32f, fallbackTileSize);
            WorldStreamer streamer = WorldStreamer.Instance;
            if (streamer != null)
            {
                nextSize = Mathf.Max(nextSize, streamer.chunkSizeX, streamer.chunkSizeZ);
            }

            if (Mathf.Approximately(nextSize, _tileSize))
                return;

            _tileSize = nextSize;
            RebuildSharedMesh();
            _lastCenterX = int.MinValue;
            _lastCenterZ = int.MinValue;
        }

        private void EnsureRuntimeShape()
        {
            if (_lastTileRadius != tileRadius)
            {
                RebuildTilePool();
                return;
            }

            if (_lastTileResolution != tileResolution)
                RebuildSharedMesh();
        }

        private void RebuildTilePool()
        {
            for (int i = transform.childCount - 1; i >= 0; i--)
            {
                Destroy(transform.GetChild(i).gameObject);
            }

            _tiles.Clear();
            _lastTileRadius = tileRadius;

            RebuildSharedMesh();
            EnsureMaterial();

            int tileCount = (tileRadius * 2 + 1) * (tileRadius * 2 + 1);
            for (int i = 0; i < tileCount; i++)
            {
                GameObject tile = new GameObject("SeaLevelOceanTile");
                tile.transform.SetParent(transform, false);
                tile.AddComponent<ExcludeFromOptimization>();

                MeshFilter meshFilter = tile.AddComponent<MeshFilter>();
                meshFilter.sharedMesh = _sharedMesh;

                MeshRenderer meshRenderer = tile.AddComponent<MeshRenderer>();
                meshRenderer.sharedMaterial = _material;
                meshRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                meshRenderer.receiveShadows = false;
                meshRenderer.allowOcclusionWhenDynamic = false;

                _tiles.Add(tile.transform);
            }

            _lastCenterX = int.MinValue;
            _lastCenterZ = int.MinValue;
        }

        private void RebuildSharedMesh()
        {
            _lastTileResolution = tileResolution;

            if (_sharedMesh != null)
                Destroy(_sharedMesh);

            int resolution = Mathf.Clamp(tileResolution, 4, 64);
            int vertexWidth = resolution + 1;
            Vector3[] vertices = new Vector3[vertexWidth * vertexWidth];
            Vector2[] uvs = new Vector2[vertices.Length];
            int[] triangles = new int[resolution * resolution * 6];
            float halfSize = _tileSize * 0.5f;
            float step = _tileSize / resolution;

            for (int z = 0; z <= resolution; z++)
            {
                for (int x = 0; x <= resolution; x++)
                {
                    int index = z * vertexWidth + x;
                    vertices[index] = new Vector3(x * step - halfSize, 0f, z * step - halfSize);
                    uvs[index] = new Vector2((float)x / resolution, (float)z / resolution);
                }
            }

            int triangleIndex = 0;
            for (int z = 0; z < resolution; z++)
            {
                for (int x = 0; x < resolution; x++)
                {
                    int bottomLeft = z * vertexWidth + x;
                    triangles[triangleIndex++] = bottomLeft;
                    triangles[triangleIndex++] = bottomLeft + vertexWidth;
                    triangles[triangleIndex++] = bottomLeft + vertexWidth + 1;
                    triangles[triangleIndex++] = bottomLeft;
                    triangles[triangleIndex++] = bottomLeft + vertexWidth + 1;
                    triangles[triangleIndex++] = bottomLeft + 1;
                }
            }

            _sharedMesh = new Mesh { name = "SeaLevelOceanTile" };
            _sharedMesh.vertices = vertices;
            _sharedMesh.uv = uvs;
            _sharedMesh.triangles = triangles;
            _sharedMesh.RecalculateBounds();
            _sharedMesh.bounds = new Bounds(Vector3.zero, new Vector3(_tileSize, 12f, _tileSize));

            for (int i = 0; i < _tiles.Count; i++)
            {
                if (_tiles[i] == null)
                    continue;

                MeshFilter meshFilter = _tiles[i].GetComponent<MeshFilter>();
                if (meshFilter != null)
                    meshFilter.sharedMesh = _sharedMesh;
            }
        }

        private void EnsureMaterial()
        {
            if (_material != null)
                return;

            Shader shader = Shader.Find(ShaderName);
            if (shader == null)
                shader = Shader.Find("Universal Render Pipeline/Lit");

            _material = new Material(shader)
            {
                name = "SeaLevelOcean_Runtime",
                renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent
            };

            if (_material.HasProperty("_Surface"))
                _material.SetFloat("_Surface", 1f);
            if (_material.HasProperty("_Blend"))
                _material.SetFloat("_Blend", 0f);
            if (_material.HasProperty("_ZWrite"))
                _material.SetFloat("_ZWrite", 0f);
        }

        private void PushMaterialSettings()
        {
            EnsureMaterial();

            _propertyBlock.SetColor(ShallowColorId, shallowColor);
            _propertyBlock.SetColor(DeepColorId, deepColor);
            _propertyBlock.SetFloat(SeaLevelId, seaLevel);
            _propertyBlock.SetFloat(WaveAmplitudeId, waveAmplitude);
            _propertyBlock.SetFloat(WaveSpeedId, waveSpeed);
            _propertyBlock.SetFloat(PrimaryWaveLengthId, primaryWaveLength);
            _propertyBlock.SetFloat(SecondaryWaveLengthId, secondaryWaveLength);
            _propertyBlock.SetFloat(ShimmerStrengthId, shimmerStrength);
            _propertyBlock.SetFloat(SpecularPowerId, specularPower);
            _propertyBlock.SetFloat(AlphaId, alpha);
            _propertyBlock.SetColor(FoamColorId, foamColor);
            _propertyBlock.SetFloat(FoamIntensityId, foamIntensity);
            _propertyBlock.SetFloat(CrestFoamThresholdId, crestFoamThreshold);
            _propertyBlock.SetFloat(ShorelineFoamDepthId, shorelineFoamDepth);
            _propertyBlock.SetFloat(FoamNoiseScaleId, foamNoiseScale);

            for (int i = 0; i < _tiles.Count; i++)
            {
                if (_tiles[i] == null)
                    continue;

                MeshRenderer meshRenderer = _tiles[i].GetComponent<MeshRenderer>();
                if (meshRenderer != null)
                    meshRenderer.SetPropertyBlock(_propertyBlock);
            }
        }

        private void PositionTiles(int centerX, int centerZ)
        {
            int index = 0;
            for (int z = -tileRadius; z <= tileRadius; z++)
            {
                for (int x = -tileRadius; x <= tileRadius; x++)
                {
                    if (index >= _tiles.Count)
                        return;

                    Transform tile = _tiles[index++];
                    if (tile == null)
                        continue;

                    tile.position = new Vector3((centerX + x) * _tileSize, seaLevel, (centerZ + z) * _tileSize);
                }
            }
        }
    }
}
