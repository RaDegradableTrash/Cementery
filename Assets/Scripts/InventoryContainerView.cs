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
    [SerializeField] private bool showGridLines = false;
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

    private Transform _placedItemsRoot;
    private Dictionary<ItemInstance, GameObject> _placedVisuals = new Dictionary<ItemInstance, GameObject>();

    void Start()
    {
        InitVisualRefs();
        UpdateGridPlane();
    }

    void OnEnable()
    {
        InitVisualRefs();
        UpdateGridPlane();
        if (inventorySystem != null)
        {
            inventorySystem.OnItemPlaced += HandleItemPlaced;
            inventorySystem.OnItemRemoved += HandleItemRemoved;
            RefreshAllPlacedItems();
        }
    }

    void OnDisable()
    {
        if (inventorySystem != null)
        {
            inventorySystem.OnItemPlaced -= HandleItemPlaced;
            inventorySystem.OnItemRemoved -= HandleItemRemoved;
        }
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

        // 让外围长方体 CubeSpace_PackStorage 始终包裹并对齐该网格空间
        Transform cubeSpace = transform.Find("CubeSpace_PackStorage");
        if (cubeSpace == null)
        {
            GameObject obj = GameObject.Find("CubeSpace_PackStorage");
            if (obj != null)
                cubeSpace = obj.transform;
        }

        if (cubeSpace != null)
        {
            cubeSpace.localPosition = new Vector3(
                (inventorySystem.gridWidth - 1f) * 0.5f,
                (inventorySystem.gridHeight - 1f) * 0.5f,
                (inventorySystem.gridDepth - 1f) * 0.5f
            );
            cubeSpace.localScale = new Vector3(
                inventorySystem.gridWidth,
                inventorySystem.gridHeight,
                inventorySystem.gridDepth
            );
            MakeCubeSpaceTransparent(cubeSpace);
        }
    }

    private void MakeCubeSpaceTransparent(Transform cubeSpace)
    {
        if (cubeSpace == null) return;
        Renderer r = cubeSpace.GetComponent<Renderer>();
        if (r == null) return;

        Material mat = r.material;
        if (mat == null) return;

        if (mat.HasProperty("_Surface")) mat.SetFloat("_Surface", 1f);
        if (mat.HasProperty("_Blend")) mat.SetFloat("_Blend", 0f);
        if (mat.HasProperty("_Mode")) mat.SetFloat("_Mode", 3f);

        mat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
        mat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
        mat.SetInt("_ZWrite", 0);
        mat.DisableKeyword("_ALPHATEST_ON");
        mat.EnableKeyword("_ALPHABLEND_ON");
        mat.DisableKeyword("_ALPHAPREMULTIPLY_ON");
        mat.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;

        Color semiTransColor = new Color(1f, 1f, 1f, 0.25f);
        if (mat.HasProperty("_Color")) mat.SetColor("_Color", semiTransColor);
        if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", semiTransColor);
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

        CreateGridOutline(minX, maxX, minZ, maxZ, "GridOutline");

        _cachedWidth = width;
        _cachedDepth = depth;
        _cachedInset = inset;

        ApplyLineStyle();
    }

    void CreateGridOutline(float minX, float maxX, float minZ, float maxZ, string name)
    {
        GameObject go = new GameObject(name);
        go.transform.SetParent(_gridLineRoot, false);

        LineRenderer lr = go.AddComponent<LineRenderer>();
        lr.useWorldSpace = false;
        lr.loop = true;
        lr.positionCount = 4;
        lr.SetPosition(0, new Vector3(minX, 0f, minZ));
        lr.SetPosition(1, new Vector3(minX, 0f, maxZ));
        lr.SetPosition(2, new Vector3(maxX, 0f, maxZ));
        lr.SetPosition(3, new Vector3(maxX, 0f, minZ));
        lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        lr.receiveShadows = false;

        _gridLines.Add(lr);
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
        InventoryCameraController primary = InventoryCameraController.GetPrimaryController();
        if (primary != null)
            inventoryCameraController = primary;
        else if (inventoryCameraController == null)
            inventoryCameraController = FindObjectOfType<InventoryCameraController>();

        return inventoryCameraController != null && inventoryCameraController.IsInventoryActive;
    }

    void EnsurePlacedItemsRoot()
    {
        if (_placedItemsRoot != null)
            return;

        Transform existing = transform.Find("PlacedItems_Runtime");
        if (existing != null)
        {
            _placedItemsRoot = existing;
            return;
        }

        GameObject root = new GameObject("PlacedItems_Runtime");
        _placedItemsRoot = root.transform;
        _placedItemsRoot.SetParent(transform, false);
        _placedItemsRoot.localPosition = Vector3.zero;
        _placedItemsRoot.localRotation = Quaternion.identity;
    }

    void HandleItemPlaced(ItemInstance instance)
    {
        EnsurePlacedItemsRoot();
        if (instance == null || instance.item == null) return;
        
        Vector3 size = Vector3.one;
        Vector3 offset = Vector3.zero;

        Vector3Int min = Vector3Int.zero;
        Vector3Int max = Vector3Int.zero;
        if (instance.item.localOffsets != null && instance.item.localOffsets.Count > 0)
        {
            min = instance.item.localOffsets[0];
            max = instance.item.localOffsets[0];
            for (int i = 1; i < instance.item.localOffsets.Count; i++)
            {
                Vector3Int cell = instance.item.localOffsets[i];
                min = Vector3Int.Min(min, cell);
                max = Vector3Int.Max(max, cell);
            }
            size = new Vector3(
                Mathf.Max(1f, max.x - min.x + 1f),
                Mathf.Max(1f, max.y - min.y + 1f),
                Mathf.Max(1f, max.z - min.z + 1f)
            );
            offset = new Vector3(
                (min.x + max.x) * 0.5f,
                (min.y + max.y) * 0.5f,
                (min.z + max.z) * 0.5f
            );
        }

        GameObject visual;
        if (instance.item.previewPrefab != null)
        {
            visual = Instantiate(instance.item.previewPrefab, _placedItemsRoot);
            visual.name = $"Placed_{instance.item.itemName ?? instance.item.name}";
            visual.transform.localScale = Vector3.Scale(instance.item.previewPrefab.transform.localScale, size);
            visual.transform.localPosition = instance.anchor;
            visual.transform.localRotation = instance.rotation;
        }
        else
        {
            GameObject root = new GameObject($"Placed_{instance.item.itemName ?? instance.item.name}");
            root.transform.SetParent(_placedItemsRoot, false);
            root.transform.localPosition = instance.anchor;
            root.transform.localRotation = instance.rotation;

            GameObject cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
            cube.transform.SetParent(root.transform, false);
            cube.transform.localScale = size * 0.95f;
            cube.transform.localPosition = offset;
            
            visual = root;
        }
        
        // Disable physics components as this is purely visual inside the inventory
        var colliders = visual.GetComponentsInChildren<Collider>(true);
        foreach (var c in colliders) c.enabled = false;
        var rbs = visual.GetComponentsInChildren<Rigidbody>(true);
        foreach (var rb in rbs) 
        {
            rb.isKinematic = true;
            rb.useGravity = false;
            rb.detectCollisions = false;
        }
        
        var renderers = visual.GetComponentsInChildren<Renderer>(true);
        foreach (var r in renderers)
        {
            r.enabled = true;
            r.SetPropertyBlock(null);
        }
        
        SetLayerRecursively(visual, gameObject.layer);
        _placedVisuals[instance] = visual;
    }

    void HandleItemRemoved(ItemInstance instance)
    {
        if (_placedVisuals.TryGetValue(instance, out GameObject visual))
        {
            if (visual != null)
            {
                if (Application.isPlaying) Destroy(visual);
                else DestroyImmediate(visual);
            }
            _placedVisuals.Remove(instance);
        }
    }

    void RefreshAllPlacedItems()
    {
        foreach (var visual in _placedVisuals.Values)
        {
            if (visual != null)
            {
                if (Application.isPlaying) Destroy(visual);
                else DestroyImmediate(visual);
            }
        }
        _placedVisuals.Clear();
        
        if (inventorySystem == null) return;
        
        foreach (var item in inventorySystem.GetAllItems())
        {
            HandleItemPlaced(item);
        }
    }

    void SetLayerRecursively(GameObject obj, int newLayer)
    {
        if (obj == null) return;
        obj.layer = newLayer;
        foreach (Transform child in obj.transform)
        {
            if (child != null)
                SetLayerRecursively(child.gameObject, newLayer);
        }
    }

    /// <summary>
    /// Helper for the Controller to pick up items via raycast against the visual renderers
    /// </summary>
    public ItemInstance GetItemAtRay(Ray ray)
    {
        float nearestDist = float.PositiveInfinity;
        ItemInstance nearestInstance = null;

        foreach (var kvp in _placedVisuals)
        {
            GameObject visual = kvp.Value;
            if (visual == null) continue;

            Renderer[] renderers = visual.GetComponentsInChildren<Renderer>(true);
            foreach (var r in renderers)
            {
                if (!r.enabled) continue;
                if (r.bounds.IntersectRay(ray, out float dist))
                {
                    if (dist < nearestDist)
                    {
                        nearestDist = dist;
                        nearestInstance = kvp.Key;
                    }
                }
            }
        }

        return nearestInstance;
    }
}
