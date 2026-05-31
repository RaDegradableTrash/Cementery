using System.Collections.Generic;
using UnityEngine;

namespace EnvironmentSystem
{
    public class SnowMoundManager : MonoBehaviour
    {
        public static SnowMoundManager Instance { get; private set; }

        [Header("Instancing Settings")]
        public Material snowMaterial;
        public float mergeDistance = 1.2f;
        public float maxMoundScale = 4.0f;
        public float moundHeightRatio = 0.35f; 
        
        [Header("Buffer Settings")]
        public int framesBetweenMerges = 10; 

        private Mesh _sphereMesh;
        
        // Active mounds data
        private class MoundData
        {
            public Vector3 position;
            public Vector3 normal;
            public float rotationY;
            public float stretchX;
            public float stretchZ;
            public float targetScale;
            public float currentScale;
        }
        private List<MoundData> _activeMounds = new List<MoundData>();

        // Buffer for incoming snow hits
        private List<Vector3> _incomingHits = new List<Vector3>();
        private int _frameCount = 0;

        // Cached matrices for rendering
        private List<Matrix4x4[]> _instancedBatches = new List<Matrix4x4[]>();
        private List<int> _batchCounts = new List<int>();

        private void Awake()
        {
            if (Instance == null) Instance = this;
            else Destroy(gameObject);

            // Steal the built-in sphere mesh
            GameObject tempSphere = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            _sphereMesh = tempSphere.GetComponent<MeshFilter>().sharedMesh;
            Destroy(tempSphere);

            if (snowMaterial == null)
            {
                Shader shader = Shader.Find("Environment/SnowBlob");
                if (shader != null)
                {
                    snowMaterial = new Material(shader);
                    snowMaterial.enableInstancing = true;
                }
            }
        }

        public void AddSnow(Vector3 worldPos)
        {
            _incomingHits.Add(worldPos);
        }

        public void ClearArea(Vector3 center, float radius)
        {
            float sqrRadius = radius * radius;
            int removed = _activeMounds.RemoveAll(m => (m.position - center).sqrMagnitude <= sqrRadius);
            if (removed > 0)
            {
                RebuildMatrices();
            }
        }

        private void Update()
        {
            _frameCount++;
            if (_frameCount >= framesBetweenMerges)
            {
                ProcessBuffer();
                _frameCount = 0;
            }

            // Animate scales
            bool needsMatrixRebuild = false;
            foreach (var m in _activeMounds)
            {
                if (Mathf.Abs(m.currentScale - m.targetScale) > 0.01f)
                {
                    m.currentScale = Mathf.Lerp(m.currentScale, m.targetScale, Time.deltaTime * 3.0f);
                    needsMatrixRebuild = true;
                }
            }

            if (needsMatrixRebuild)
            {
                RebuildMatrices();
            }

            RenderInstancedMounds();
        }

        private void ProcessBuffer()
        {
            if (_incomingHits.Count == 0) return;

            foreach (var hitPos in _incomingHits)
            {
                bool merged = false;
                
                for (int i = 0; i < _activeMounds.Count; i++)
                {
                    MoundData mound = _activeMounds[i];
                    float distSq = (mound.position - hitPos).sqrMagnitude;
                    
                    float dynamicMergeDist = mergeDistance * (mound.targetScale / 1.0f);
                    
                    if (distSq < dynamicMergeDist * dynamicMergeDist)
                    {
                        if (mound.targetScale < maxMoundScale)
                        {
                            mound.targetScale += 0.15f;
                        }
                        merged = true;
                        break;
                    }
                }

                if (!merged)
                {
                    Vector3 groundNormal = Vector3.up;
                    if (Physics.Raycast(hitPos + Vector3.up * 5f, Vector3.down, out RaycastHit hit, 10f))
                    {
                        groundNormal = hit.normal;
                    }

                    _activeMounds.Add(new MoundData
                    {
                        position = hitPos,
                        normal = groundNormal,
                        rotationY = Random.Range(0f, 360f),
                        stretchX = Random.Range(0.8f, 1.2f),
                        stretchZ = Random.Range(0.8f, 1.2f),
                        targetScale = 0.8f,
                        currentScale = 0.0f
                    });
                }
            }

            _incomingHits.Clear();
        }

        private void RebuildMatrices()
        {
            _instancedBatches.Clear();
            _batchCounts.Clear();

            int total = _activeMounds.Count;
            int batchIndex = 0;

            while (total > 0)
            {
                int countThisBatch = Mathf.Min(total, 1023);
                Matrix4x4[] batch = new Matrix4x4[1023];

                for (int i = 0; i < countThisBatch; i++)
                {
                    MoundData data = _activeMounds[batchIndex * 1023 + i];
                    Vector3 scale = new Vector3(
                        data.currentScale * data.stretchX, 
                        data.currentScale * moundHeightRatio, 
                        data.currentScale * data.stretchZ
                    );

                    Quaternion alignRot = Quaternion.FromToRotation(Vector3.up, data.normal);
                    Quaternion randomY = Quaternion.Euler(0, data.rotationY, 0);
                    Quaternion finalRot = alignRot * randomY;

                    batch[i] = Matrix4x4.TRS(data.position, finalRot, scale);
                }

                _instancedBatches.Add(batch);
                _batchCounts.Add(countThisBatch);

                total -= 1023;
                batchIndex++;
            }
        }

        private void RenderInstancedMounds()
        {
            if (_sphereMesh == null || snowMaterial == null || _activeMounds.Count == 0) return;

            for (int i = 0; i < _instancedBatches.Count; i++)
            {
                Graphics.DrawMeshInstanced(
                    _sphereMesh,
                    0,
                    snowMaterial,
                    _instancedBatches[i],
                    _batchCounts[i],
                    null,
                    UnityEngine.Rendering.ShadowCastingMode.On,
                    true
                );
            }
        }
    }
}
