using UnityEngine;
using UnityEngine.UI;

// 如果是在编辑器环境下，才引入 UnityEditor 命名空间
#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.UI;
#endif

namespace HP
{
    [RequireComponent(typeof(Image))]
    public class LiquidProgressBar : Image
    {
        [Range(-1f, 1f)]
        public new float fillAmount = 1f; // 血条填充比例
        public float waveSpeed = 1f;      // 液体波动速度
        public float waveHeight = 0.1f;    // 液体波动高度
        public float waveTime = 0f;       // 波浪的时间种子
        [Min(0.02f)]
        public float meshRefreshInterval = 0.033f;

        [Header("曲面细分")]
        public Vector2Int SubdivisionSurface = new Vector2Int(1, 1);

        public float waveOffset = 0f;    // 液体波动偏移量
        public Color color2;             // 中间/顶层液体颜色
        private float _meshRefreshTimer;

        protected override void OnEnable()
        {
            base.OnEnable();
            _meshRefreshTimer = 0f;
            SetVerticesDirty();
        }

        private void Update()
        {
            if (canvasRenderer == null || canvasRenderer.GetAlpha() <= 0.001f)
                return;

            _meshRefreshTimer += Time.deltaTime;
            float interval = Mathf.Max(0.02f, meshRefreshInterval);
            if (_meshRefreshTimer < interval)
                return;

            float elapsed = _meshRefreshTimer;
            _meshRefreshTimer = 0f;
            waveTime += elapsed * waveSpeed;
            SetVerticesDirty();
        }

        // 核心：动态网格重建
        protected override void OnPopulateMesh(VertexHelper vh)
        {
            Rect r = GetPixelAdjustedRect();
            Vector4 v = new Vector4(r.xMin, r.yMin, r.xMax, r.yMin + r.height * Mathf.Clamp01(fillAmount));

            vh.Clear();

            // 1. 生成细分顶点
            for (int y = 0; y <= SubdivisionSurface.y; y++)
            {
                for (int x = 0; x <= SubdivisionSurface.x; x++)
                {
                    float xPos = Mathf.Lerp(v.x, v.z, (float)x / SubdivisionSurface.x);
                    float yPos = Mathf.Lerp(v.y, v.w, (float)y / SubdivisionSurface.y);
                    
                    Color finalColor = color; 

                    // 顶层波浪顶点计算
                    if (y == SubdivisionSurface.y)
                    {
                        float amplitudeFactor = 1f - (Mathf.Abs(fillAmount / 2f) + 0.5f);
                        yPos += Mathf.Sin(waveTime + ((float)x / SubdivisionSurface.x) * Mathf.PI) * waveHeight * 100f * amplitudeFactor; 
                        finalColor = color2; 
                    }
                    // 次顶层（中间）波浪顶点计算
                    else if (y == SubdivisionSurface.y - 1 && SubdivisionSurface.y > 1)
                    {
                        float amplitudeFactor = 1f - (Mathf.Abs(fillAmount / 2f) + 0.5f);
                        yPos += Mathf.Sin(waveTime + ((float)x / SubdivisionSurface.x) * Mathf.PI + waveOffset) * waveHeight * 100f * amplitudeFactor;
                    }

                    Vector2 uv = new Vector2((float)x / SubdivisionSurface.x, (float)y / SubdivisionSurface.y);
                    vh.AddVert(new Vector3(xPos, yPos, 0f), finalColor, uv);
                }
            }

            // 2. 拓扑连接三角形面片
            int columns = SubdivisionSurface.x + 1;
            for (int y = 0; y < SubdivisionSurface.y; y++)
            {
                for (int x = 0; x < SubdivisionSurface.x; x++)
                {
                    int vertexIndex = x + y * columns;

                    vh.AddTriangle(vertexIndex, vertexIndex + columns, vertexIndex + 1);
                    vh.AddTriangle(vertexIndex + 1, vertexIndex + columns, vertexIndex + columns + 1);
                }
            }
        }
    }
}

// ============================================================================
// 以下是编辑器扩展部分：使用预编译指令包裹，确保打包时这部分代码会被编译器自动忽略
// ============================================================================
#if UNITY_EDITOR
namespace HP
{
    [CustomEditor(typeof(LiquidProgressBar))]
    public class LiquidProgressBarEditor : ImageEditor
    {
        private SerializedProperty m_SubdivisionSurface;
        private SerializedProperty m_fillAmount;
        private SerializedProperty m_waveSpeed;
        private SerializedProperty m_waveHeight;
        private SerializedProperty m_waveTime;
        private SerializedProperty m_waveOffset;
        private SerializedProperty m_color2;

        protected override void OnEnable()
        {
            base.OnEnable();
            m_SubdivisionSurface = serializedObject.FindProperty("SubdivisionSurface");
            m_fillAmount = serializedObject.FindProperty("fillAmount");
            m_waveSpeed = serializedObject.FindProperty("waveSpeed");
            m_waveHeight = serializedObject.FindProperty("waveHeight");
            m_waveTime = serializedObject.FindProperty("waveTime");
            m_waveOffset = serializedObject.FindProperty("waveOffset");
            m_color2 = serializedObject.FindProperty("color2");
        }

        public override UnityEngine.UIElements.VisualElement CreateInspectorGUI()
        {
            return base.CreateInspectorGUI();
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            EditorGUILayout.PropertyField(m_SubdivisionSurface);
            EditorGUILayout.PropertyField(m_fillAmount);
            EditorGUILayout.PropertyField(m_waveSpeed);
            EditorGUILayout.PropertyField(m_waveHeight);
            EditorGUILayout.PropertyField(m_waveTime);
            EditorGUILayout.PropertyField(m_waveOffset);
            EditorGUILayout.PropertyField(m_color2);

            serializedObject.ApplyModifiedProperties();

            base.OnInspectorGUI(); // 保留 Image 原生的属性面板（如 Material, Raycast Target 等）
        }
    }
}
#endif
