using UnityEngine;

namespace EnvironmentSystem
{
    [ExecuteInEditMode]
    [RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
    public class DesertTerrainRoadOverlay : MonoBehaviour
    {
        public float yOffset = 0.02f;
        public Material roadMaterial;
        [SerializeField, Min(0.1f)]
        private float runtimeSyncCheckInterval = 1f;

        // Keep a serialized mesh reference to avoid lost mesh references in build/scene save
        [SerializeField]
        private Mesh localOverlayMesh;
        private MeshFilter meshFilter;
        private MeshRenderer meshRenderer;
        private Transform cachedParent;
        private MeshFilter cachedTerrainFilter;
        private float nextRuntimeSyncCheckTime;

        public Mesh GetMesh()
        {
            CacheOwnComponents();
            if (meshFilter != null)
            {
                return meshFilter.sharedMesh;
            }
            return null;
        }

        public void SetMesh(Mesh mesh)
        {
            CacheOwnComponents();
            if (meshFilter != null)
            {
                meshFilter.sharedMesh = mesh;
            }
            localOverlayMesh = mesh;
        }

        /// <summary>
        /// Synchronizes the road overlay's vertices and topology with the terrain mesh,
        /// while carefully preserving any painted vertex colors (the road mask).
        /// </summary>
        public void SyncWithTerrain(Mesh terrainMesh)
        {
            if (terrainMesh == null) return;

            CacheOwnComponents();

            if (meshFilter == null)
            {
                meshFilter = gameObject.AddComponent<MeshFilter>();
            }

            if (meshRenderer == null)
            {
                meshRenderer = gameObject.AddComponent<MeshRenderer>();
            }

            // Setup Material
            if (roadMaterial == null)
            {
                Shader roadShader = Shader.Find("Environment/RoadOverlay");
                if (roadShader != null)
                {
                    roadMaterial = new Material(roadShader);
                    roadMaterial.name = "RoadOverlayMaterial";
                }
            }

            if (meshRenderer.sharedMaterial != roadMaterial && roadMaterial != null)
            {
                meshRenderer.sharedMaterial = roadMaterial;
            }

            meshRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            meshRenderer.receiveShadows = true;

            // Retrieve or create our unique local mesh
            Mesh currentMesh = meshFilter.sharedMesh;
            bool isNewMesh = false;

            // We must instantiate/create a standalone mesh if we don't have one or if it's shared with the terrain
            if (currentMesh == null || currentMesh == terrainMesh)
            {
                currentMesh = new Mesh();
                currentMesh.name = "RoadOverlayMesh_" + transform.parent.name;
                isNewMesh = true;
            }

            // Copy topology from the terrain mesh
            Vector3[] terrainVerts = terrainMesh.vertices;
            Vector2[] terrainUVs = terrainMesh.uv;
            Vector3[] terrainNormals = terrainMesh.normals;
            int[] terrainTriangles = terrainMesh.triangles;

            Vector3[] newVerts = new Vector3[terrainVerts.Length];
            Color[] newColors = new Color[terrainVerts.Length];

            // Offset vertices slightly along normals or vertically to avoid z-fighting
            for (int i = 0; i < terrainVerts.Length; i++)
            {
                newVerts[i] = terrainVerts[i] + new Vector3(0, yOffset, 0);
            }

            // Preserve vertex colors if they are matching in length
            if (!isNewMesh && currentMesh.colors != null && currentMesh.colors.Length == terrainVerts.Length)
            {
                System.Array.Copy(currentMesh.colors, newColors, terrainVerts.Length);
            }
            else
            {
                // Initialize to fully transparent (no road)
                for (int i = 0; i < newColors.Length; i++)
                {
                    newColors[i] = new Color(0, 0, 0, 0);
                }
            }

            // Flatten skirt vertices to have 0 area triangles (preventing rendering on skirts)
            int vertexCount = terrainVerts.Length;
            int S = Mathf.RoundToInt(-2f + Mathf.Sqrt(4f + vertexCount));
            if (S * S + 4 * S == vertexCount)
            {
                int vw = S;
                int vd = S;
                int mainVerts = vw * vd;

                int leftSB = mainVerts;
                int rightSB = mainVerts + vd;
                int bottomSB = mainVerts + 2 * vd;
                int topSB = mainVerts + 2 * vd + vw;

                // Left skirt (x=0 column)
                for (int z = 0; z < vd; z++)
                {
                    int mi = z * vw;
                    newVerts[leftSB + z] = newVerts[mi];
                }
                // Right skirt (x=width column)
                for (int z = 0; z < vd; z++)
                {
                    int mi = z * vw + (vw - 1);
                    newVerts[rightSB + z] = newVerts[mi];
                }
                // Bottom skirt (z=0 row)
                for (int x = 0; x < vw; x++)
                {
                    int mi = x;
                    newVerts[bottomSB + x] = newVerts[mi];
                }
                // Top skirt (z=depth row)
                for (int x = 0; x < vw; x++)
                {
                    int mi = (vd - 1) * vw + x;
                    newVerts[topSB + x] = newVerts[mi];
                }
            }

            if (newVerts.Length > 65535)
                currentMesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;

            currentMesh.vertices = newVerts;
            currentMesh.uv = terrainUVs;
            currentMesh.normals = terrainNormals;
            currentMesh.colors = newColors;
            currentMesh.triangles = terrainTriangles;
            currentMesh.RecalculateBounds();

            meshFilter.sharedMesh = currentMesh;
            localOverlayMesh = currentMesh;

#if UNITY_EDITOR
            if (!Application.isPlaying)
            {
                UnityEditor.EditorUtility.SetDirty(this);
                UnityEditor.EditorUtility.SetDirty(currentMesh);
            }
#endif
        }

        private Mesh lastTerrainMesh;
        private Vector3[] lastTerrainVertices;
        private Bounds lastTerrainBounds;

        private void Awake()
        {
            CacheOwnComponents();
        }

        private void OnValidate()
        {
            CacheOwnComponents();
        }

        private void CacheOwnComponents()
        {
            if (meshFilter == null)
            {
                TryGetComponent(out meshFilter);
            }

            if (meshRenderer == null)
            {
                TryGetComponent(out meshRenderer);
            }
        }

        private MeshFilter GetTerrainFilter()
        {
            Transform parent = transform.parent;
            if (parent == null)
            {
                cachedParent = null;
                cachedTerrainFilter = null;
                return null;
            }

            if (cachedParent != parent || cachedTerrainFilter == null)
            {
                cachedParent = parent;
                cachedTerrainFilter = parent.GetComponent<MeshFilter>();
            }

            return cachedTerrainFilter;
        }

        private void Update()
        {
            if (Application.isPlaying && Time.unscaledTime < nextRuntimeSyncCheckTime)
            {
                return;
            }

            if (Application.isPlaying)
            {
                nextRuntimeSyncCheckTime = Time.unscaledTime + runtimeSyncCheckInterval;
            }

            MeshFilter terrainFilter = GetTerrainFilter();
            if (terrainFilter == null || terrainFilter.sharedMesh == null) return;

            Mesh terrainMesh = terrainFilter.sharedMesh;
            bool needSync = false;
            Mesh filterMesh = GetMesh();

            if (filterMesh == null)
            {
                needSync = true;
            }
            else if (terrainMesh != lastTerrainMesh)
            {
                needSync = true;
            }
            else if (!Application.isPlaying)
            {
                Vector3[] currentVerts = terrainMesh.vertices;
                if (lastTerrainVertices == null || lastTerrainVertices.Length != currentVerts.Length)
                {
                    needSync = true;
                }
                else if (terrainMesh.bounds != lastTerrainBounds)
                {
                    needSync = true;
                }
            }

            if (needSync)
            {
                SyncWithTerrain(terrainMesh);
                lastTerrainMesh = terrainMesh;
                if (terrainMesh != null)
                {
                    lastTerrainVertices = terrainMesh.vertices;
                    lastTerrainBounds = terrainMesh.bounds;
                }
            }
        }
    }
}
