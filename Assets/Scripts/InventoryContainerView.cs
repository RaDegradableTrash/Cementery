using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 背包容器的3D表现与交互入口，负责网格高亮、层级切换、物品预览等。
/// </summary>
public class InventoryContainerView : MonoBehaviour
{
    [Header("背包系统")]
    public GridInventorySystem inventorySystem;
    [Header("高亮网格平面")]
    public Transform gridPlane;
    [Header("层级切换步长")]
    public int layerStep = 1;
    [Header("背包控制器")]
    [SerializeField] private InventoryCameraController inventoryCameraController;

    [Header("平面贴合")]
    [SerializeField] private float planeInset = 0f;
    [SerializeField] private float planeHeightOffset = 0.005f;
    [SerializeField] private Material planeMaterialOverride;

    [Header("网格线显示")]
    [SerializeField] private bool showGridLines = true;
    [SerializeField] private Color gridLineColor = new Color(0.72f, 0.72f, 0.72f, 0.75f);
    [SerializeField] private float gridLineWidth = 0.02f;
    [SerializeField] private float gridLineHeightOffset = 0.012f;
    [SerializeField] private Material gridLineMaterial;

    private readonly List<LineRenderer> _gridLines = new List<LineRenderer>();
    private Transform _gridLineRoot;
    private Material _runtimeGridLineMaterial;
    private MeshFilter _gridMeshFilter;
    private Renderer _gridRenderer;
    private int _cachedWidth = -1;
    private int _cachedDepth = -1;
    private float _cachedInset = -1f;

    void Start()
    {
        InitVisualRefs();
        UpdateGridPlane();
    }

    void OnEnable()
    {
        InitVisualRefs();
        UpdateGridPlane();
    }

    void Update()
    {
        if (!IsInventoryActive())
            return;

        // 滚轮切换层级
        float scroll = Input.mouseScrollDelta.y;
        if (Mathf.Abs(scroll) > 0.01f)
        {
            int newLayer = Mathf.Clamp(inventorySystem.currentLayer + (int)Mathf.Sign(scroll) * layerStep, 0, inventorySystem.gridHeight - 1);
            if (newLayer != inventorySystem.currentLayer)
            {
                inventorySystem.currentLayer = newLayer;
                UpdateGridPlane();
            }
        }

        SetGridLineVisible(showGridLines && IsInventoryActive());
    }

    void UpdateGridPlane()
    {
        if (inventorySystem == null || gridPlane == null)
            return;

        FitPlaneToInventoryCrossSection();
        RebuildGridLinesIfNeeded();
        UpdateGridLineHeight();
        SetGridLineVisible(showGridLines && IsInventoryActive());
    }

    void InitVisualRefs()
    {
        if (gridPlane == null)
            return;

        if (_gridRenderer == null)
            _gridRenderer = gridPlane.GetComponent<Renderer>();
        if (_gridMeshFilter == null)
            _gridMeshFilter = gridPlane.GetComponent<MeshFilter>();

        if (planeMaterialOverride != null && _gridRenderer != null)
            _gridRenderer.sharedMaterial = planeMaterialOverride;

        EnsureGridLineRoot();
    }

    void FitPlaneToInventoryCrossSection()
    {
        if (inventorySystem == null || gridPlane == null)
            return;

        float inset = Mathf.Max(0f, planeInset);

        Vector3 pos = gridPlane.localPosition;
        pos.x = (inventorySystem.gridWidth - 1f) * 0.5f;
        pos.y = inventorySystem.currentLayer + planeHeightOffset;
        pos.z = (inventorySystem.gridDepth - 1f) * 0.5f;
        gridPlane.localPosition = pos;

        float targetWidth = Mathf.Max(0.1f, inventorySystem.gridWidth - inset * 2f);
        float targetDepth = Mathf.Max(0.1f, inventorySystem.gridDepth - inset * 2f);

        float meshWidth = 10f;
        float meshDepth = 10f;
        if (_gridMeshFilter != null && _gridMeshFilter.sharedMesh != null)
        {
            Vector3 meshSize = _gridMeshFilter.sharedMesh.bounds.size;
            if (meshSize.x > 0.0001f) meshWidth = meshSize.x;
            if (meshSize.z > 0.0001f) meshDepth = meshSize.z;
        }

        Vector3 scale = gridPlane.localScale;
        scale.x = targetWidth / meshWidth;
        scale.z = targetDepth / meshDepth;
        gridPlane.localScale = scale;
    }

    void EnsureGridLineRoot()
    {
        if (_gridLineRoot != null || gridPlane == null)
            return;

        Transform existing = transform.Find("InventoryGridLines_Runtime");
        if (existing != null)
        {
            _gridLineRoot = existing;
            return;
        }

        GameObject root = new GameObject("InventoryGridLines_Runtime");
        _gridLineRoot = root.transform;
        _gridLineRoot.SetParent(transform, false);
    }

    void RebuildGridLinesIfNeeded()
    {
        if (!showGridLines || inventorySystem == null)
            return;

        EnsureGridLineRoot();
        if (_gridLineRoot == null)
            return;

        int width = Mathf.Max(1, inventorySystem.gridWidth);
        int depth = Mathf.Max(1, inventorySystem.gridDepth);
        float inset = Mathf.Max(0f, planeInset);

        bool shouldRebuild = _gridLines.Count == 0 ||
                             width != _cachedWidth ||
                             depth != _cachedDepth ||
                             !Mathf.Approximately(inset, _cachedInset);

        if (!shouldRebuild)
        {
            ApplyLineStyle();
            return;
        }

        ClearGridLines();

        float minX = -0.5f + inset;
        float maxX = width - 0.5f - inset;
        float minZ = -0.5f + inset;
        float maxZ = depth - 0.5f - inset;

        for (int i = 0; i <= width; i++)
        {
            float t = width > 0 ? i / (float)width : 0f;
            float x = Mathf.Lerp(minX, maxX, t);
            CreateGridLine(new Vector3(x, 0f, minZ), new Vector3(x, 0f, maxZ), $"GridLine_X_{i}");
        }

        for (int i = 0; i <= depth; i++)
        {
            float t = depth > 0 ? i / (float)depth : 0f;
            float z = Mathf.Lerp(minZ, maxZ, t);
            CreateGridLine(new Vector3(minX, 0f, z), new Vector3(maxX, 0f, z), $"GridLine_Z_{i}");
        }

        _cachedWidth = width;
        _cachedDepth = depth;
        _cachedInset = inset;

        ApplyLineStyle();
    }

    void CreateGridLine(Vector3 a, Vector3 b, string name)
    {
        GameObject go = new GameObject(name);
        go.transform.SetParent(_gridLineRoot, false);

        LineRenderer lr = go.AddComponent<LineRenderer>();
        lr.useWorldSpace = false;
        lr.positionCount = 2;
        lr.SetPosition(0, a);
        lr.SetPosition(1, b);
        lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        lr.receiveShadows = false;

        _gridLines.Add(lr);
    }

    void ApplyLineStyle()
    {
        Material lineMat = GetLineMaterial();
        for (int i = 0; i < _gridLines.Count; i++)
        {
            LineRenderer lr = _gridLines[i];
            if (lr == null)
                continue;

            lr.startWidth = gridLineWidth;
            lr.endWidth = gridLineWidth;
            lr.startColor = gridLineColor;
            lr.endColor = gridLineColor;
            if (lineMat != null)
                lr.sharedMaterial = lineMat;
        }
    }

    void UpdateGridLineHeight()
    {
        if (_gridLineRoot == null || inventorySystem == null)
            return;

        Vector3 pos = _gridLineRoot.localPosition;
        pos.y = inventorySystem.currentLayer + planeHeightOffset + gridLineHeightOffset;
        _gridLineRoot.localPosition = pos;
    }

    Material GetLineMaterial()
    {
        if (gridLineMaterial != null)
            return gridLineMaterial;

        if (_runtimeGridLineMaterial != null)
            return _runtimeGridLineMaterial;

        Shader shader = Shader.Find("Sprites/Default");
        if (shader == null)
            shader = Shader.Find("Unlit/Color");
        if (shader == null)
            return null;

        _runtimeGridLineMaterial = new Material(shader)
        {
            hideFlags = HideFlags.HideAndDontSave
        };
        return _runtimeGridLineMaterial;
    }

    void SetGridLineVisible(bool visible)
    {
        for (int i = 0; i < _gridLines.Count; i++)
        {
            LineRenderer lr = _gridLines[i];
            if (lr != null)
                lr.enabled = visible;
        }
    }

    void ClearGridLines()
    {
        for (int i = 0; i < _gridLines.Count; i++)
        {
            LineRenderer lr = _gridLines[i];
            if (lr == null)
                continue;

            if (Application.isPlaying)
                Destroy(lr.gameObject);
            else
                DestroyImmediate(lr.gameObject);
        }

        _gridLines.Clear();
    }

    void OnDestroy()
    {
        if (_runtimeGridLineMaterial == null)
            return;

        if (Application.isPlaying)
            Destroy(_runtimeGridLineMaterial);
        else
            DestroyImmediate(_runtimeGridLineMaterial);
    }

    bool IsInventoryActive()
    {
        if (inventoryCameraController == null)
            inventoryCameraController = FindObjectOfType<InventoryCameraController>();

        return inventoryCameraController != null && inventoryCameraController.IsInventoryActive;
    }
}
