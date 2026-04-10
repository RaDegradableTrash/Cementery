using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 鼠标射线检测与物品预览吸附，适配InventoryCamera和3D网格。
/// </summary>
public class InventoryRaycastPlacer : MonoBehaviour
{
    class CellTile
    {
        public int x;
        public int z;
        public Transform transform;
        public Renderer renderer;
    }

    [Header("背包摄像头")]
    public Camera inventoryCamera;
    [Header("背包容器根节点")]
    public Transform inventoryRoot;
    [Header("高亮网格平面")]
    public Transform gridPlane;
    [Header("背包系统")]
    public GridInventorySystem inventorySystem;
    [Header("背包控制器")]
    [SerializeField] private InventoryCameraController inventoryCameraController;
    [Header("当前旋转")]
    public Quaternion previewRotation = Quaternion.identity;

    [Header("预览反馈")]
    [SerializeField] private Color validPreviewColor = new Color(0.2f, 0.9f, 0.35f, 0.68f);
    [SerializeField] private Color invalidPreviewColor = new Color(0.92f, 0.2f, 0.2f, 0.78f);
    [SerializeField] private bool hidePreviewWhenNoHit = true;
    [SerializeField] private bool showGridWhenInventoryActive = true;

    [Header("格子覆盖")]
    [SerializeField] private bool showPlacementCells = true;
    [SerializeField] private bool hideBasePlaneRenderer = true;
    [Range(0.1f, 1f)]
    [SerializeField] private float cellFill = 0.92f;
    [SerializeField] private float cellHeightOffset = 0.01f;
    [SerializeField] private Color cellValidColor = new Color(0.2f, 0.9f, 0.35f, 0.52f);
    [SerializeField] private Color cellInvalidColor = new Color(0.9f, 0.2f, 0.2f, 0.58f);
    [SerializeField] private Color cellHoverValidColor = new Color(0.26f, 1f, 0.45f, 0.7f);
    [SerializeField] private Color cellHoverInvalidColor = new Color(1f, 0.3f, 0.25f, 0.72f);
    [SerializeField] private Color cellNeutralColor = new Color(0.6f, 0.6f, 0.6f, 0.1f);
    [SerializeField] private Color cellOccupiedTransparentColor = new Color(0f, 0f, 0f, 0f);
    [SerializeField] private Material cellOverlayMaterial;

    [Header("已放置物品")]
    [SerializeField] private bool keepPlacedItemsInInventory = true;
    [SerializeField] private bool closeInventoryOnPlace = false;

    private Transform previewObject;
    private ItemData previewItemData;
    private Renderer[] previewRenderers;
    private MaterialPropertyBlock previewColorBlock;
    private MaterialPropertyBlock cellColorBlock;
    private Renderer gridRenderer;
    private readonly List<CellTile> _cellTiles = new List<CellTile>();
    private readonly HashSet<Vector2Int> _previewFootprintCells = new HashSet<Vector2Int>();
    private readonly Dictionary<Vector2Int, bool> _previewFootprintBlocked = new Dictionary<Vector2Int, bool>();
    private Transform _cellRoot;
    private Transform _placedItemsRoot;
    private Material _runtimeCellMaterial;
    private int _cachedCellWidth = -1;
    private int _cachedCellDepth = -1;

    void Awake()
    {
        previewColorBlock = new MaterialPropertyBlock();
        cellColorBlock = new MaterialPropertyBlock();
        CacheGridRenderer();
        SetGridVisible(false);
        SetCellOverlayVisible(false);
    }

    void Update()
    {
        if (inventoryCamera == null || inventoryRoot == null || gridPlane == null || inventorySystem == null)
            return;

        bool inventoryOpen = inventoryCamera.enabled;
        EnsureCellTiles();

        bool hasPlaneHit = TryGetGridAnchorUnderMouse(out Vector3Int gridPos, out Vector3 localPlaneHit, out bool anchorInBounds);
        UpdateCellOverlay(inventoryOpen, hasPlaneHit && anchorInBounds, gridPos);

        bool showBasePlane = showGridWhenInventoryActive && inventoryOpen && !(showPlacementCells && hideBasePlaneRenderer);
        SetGridVisible(showBasePlane);

        if (!inventoryOpen || previewItemData == null)
        {
            if (!inventoryOpen)
                SetPreviewVisible(false);
            return;
        }

        if (previewObject != null)
            previewObject.localRotation = previewRotation;

        if (!hasPlaneHit)
        {
            if (hidePreviewWhenNoHit)
                SetPreviewVisible(false);

            SetPreviewColor(invalidPreviewColor);
            return;
        }

        SetPreviewVisible(true);

        // 预览物体吸附
        if (previewObject != null)
            previewObject.localPosition = anchorInBounds ? (Vector3)gridPos : localPlaneHit;

        bool canPlace = anchorInBounds && inventorySystem.CanPlace(previewItemData, gridPos, previewRotation);
        SetPreviewColor(canPlace ? validPreviewColor : invalidPreviewColor);

        // 左键放置
        if (Input.GetMouseButtonDown(0) && canPlace)
        {
            if (!inventorySystem.Place(previewItemData, gridPos, previewRotation))
                return;

            CommitPlacedItemVisual(gridPos, previewRotation);

            if (closeInventoryOnPlace)
            {
                // 放置后退出背包
                InventoryCameraController camCtrl = GetInventoryCameraController();
                if (camCtrl != null)
                    camCtrl.SetInventoryActive(false);
            }
            else
            {
                // 保持背包开启，继续同类型物品放置
                SetPreviewItem(previewItemData);
            }
        }
    }

    public void SetPreviewItem(ItemData item)
    {
        previewItemData = item;
        if (previewObject != null)
            Destroy(previewObject.gameObject);

        previewObject = null;
        previewRenderers = null;

        if (item == null)
            return;

        GameObject previewGo = item.previewPrefab != null
            ? Instantiate(item.previewPrefab, inventoryRoot)
            : CreateFallbackPreview(item);

        if (previewGo != null)
        {
            previewObject = previewGo.transform;
            previewRenderers = previewGo.GetComponentsInChildren<Renderer>(true);
            DisablePreviewPhysics(previewGo);
            SetLayerRecursively(previewGo, gridPlane.gameObject.layer);
            SetPreviewVisible(true);
            SetPreviewColor(validPreviewColor);
        }
    }

    public void ClearPreview()
    {
        previewItemData = null;
        if (previewObject != null)
            Destroy(previewObject.gameObject);
        previewObject = null;
        previewRenderers = null;
    }

    bool TryGetGridAnchorUnderMouse(out Vector3Int gridPos, out Vector3 localHit, out bool inBackpackRange)
    {
        gridPos = default;
        localHit = Vector3.zero;
        inBackpackRange = false;

        Ray ray = inventoryCamera.ScreenPointToRay(Input.mousePosition);

        Vector3 planePoint = gridPlane.position;
        Vector3 planeNormal = gridPlane.up;
        Plane layerPlane = new Plane(planeNormal, planePoint);

        if (!layerPlane.Raycast(ray, out float enter) || enter < 0f)
            return false;

        Vector3 worldHit = ray.GetPoint(enter);
        localHit = inventoryRoot.InverseTransformPoint(worldHit);

        // Grid coordinates are centered on integer anchors (..,-1,0,1,..),
        // so offset by 0.5 before floor to map edge hits to the nearest cell.
        int gridX = Mathf.FloorToInt(localHit.x + 0.5f);
        int gridZ = Mathf.FloorToInt(localHit.z + 0.5f);

        gridPos = new Vector3Int(
            gridX,
            inventorySystem.currentLayer,
            gridZ
        );

        inBackpackRange = inventorySystem.InBounds(gridPos);

        return true;
    }

    void SetPreviewColor(Color color)
    {
        if (previewRenderers == null)
            return;

        for (int i = 0; i < previewRenderers.Length; i++)
        {
            Renderer r = previewRenderers[i];
            if (r == null)
                continue;

            Material shared = r.sharedMaterial;
            if (shared == null)
                continue;

            previewColorBlock.Clear();

            bool wroteColor = false;
            if (shared.HasProperty("_BaseColor"))
            {
                previewColorBlock.SetColor("_BaseColor", color);
                wroteColor = true;
            }

            if (shared.HasProperty("_Color"))
            {
                previewColorBlock.SetColor("_Color", color);
                wroteColor = true;
            }

            if (wroteColor)
                r.SetPropertyBlock(previewColorBlock);
        }
    }

    void SetPreviewVisible(bool visible)
    {
        if (previewRenderers == null)
            return;

        for (int i = 0; i < previewRenderers.Length; i++)
        {
            if (previewRenderers[i] != null)
                previewRenderers[i].enabled = visible;
        }
    }

    void CacheGridRenderer()
    {
        if (gridPlane != null)
            gridRenderer = gridPlane.GetComponent<Renderer>();
    }

    void SetGridVisible(bool visible)
    {
        if (gridRenderer == null)
            CacheGridRenderer();

        if (gridRenderer != null)
            gridRenderer.enabled = visible;
    }

    void EnsureCellTiles()
    {
        if (!showPlacementCells)
            return;

        int width = Mathf.Max(1, inventorySystem.gridWidth);
        int depth = Mathf.Max(1, inventorySystem.gridDepth);

        if (_cellRoot == null)
        {
            Transform existing = inventoryRoot.Find("PlacementCells_Runtime");
            if (existing != null)
                _cellRoot = existing;
            else
            {
                GameObject root = new GameObject("PlacementCells_Runtime");
                _cellRoot = root.transform;
                _cellRoot.SetParent(inventoryRoot, false);
                _cellRoot.localPosition = Vector3.zero;
                _cellRoot.localRotation = Quaternion.identity;
            }
        }

        if (_cachedCellWidth == width && _cachedCellDepth == depth && _cellTiles.Count == width * depth)
            return;

        ClearCellTiles();

        Material cellMat = GetCellOverlayMaterial();
        int layer = gridPlane.gameObject.layer;
        for (int x = 0; x < width; x++)
        {
            for (int z = 0; z < depth; z++)
            {
                GameObject tile = GameObject.CreatePrimitive(PrimitiveType.Quad);
                tile.name = $"Cell_{x}_{z}";
                tile.transform.SetParent(_cellRoot, false);
                tile.transform.localRotation = Quaternion.Euler(-90f, 0f, 0f);
                tile.layer = layer;

                Collider c = tile.GetComponent<Collider>();
                if (c != null)
                    Destroy(c);

                Renderer r = tile.GetComponent<Renderer>();
                if (r != null && cellMat != null)
                    r.sharedMaterial = cellMat;

                _cellTiles.Add(new CellTile
                {
                    x = x,
                    z = z,
                    transform = tile.transform,
                    renderer = r
                });
            }
        }

        _cachedCellWidth = width;
        _cachedCellDepth = depth;
    }

    void EnsurePlacedItemsRoot()
    {
        if (_placedItemsRoot != null)
            return;

        Transform existing = inventoryRoot.Find("PlacedItems_Runtime");
        if (existing != null)
        {
            _placedItemsRoot = existing;
            return;
        }

        GameObject root = new GameObject("PlacedItems_Runtime");
        _placedItemsRoot = root.transform;
        _placedItemsRoot.SetParent(inventoryRoot, false);
        _placedItemsRoot.localPosition = Vector3.zero;
        _placedItemsRoot.localRotation = Quaternion.identity;
    }

    void CommitPlacedItemVisual(Vector3Int anchor, Quaternion rotation)
    {
        if (!keepPlacedItemsInInventory)
            return;

        EnsurePlacedItemsRoot();
        if (_placedItemsRoot == null)
            return;

        if (previewObject != null)
        {
            Transform placed = previewObject;
            Renderer[] placedRenderers = previewRenderers;

            if (placed.parent != _placedItemsRoot)
                placed.SetParent(_placedItemsRoot, false);

            placed.localPosition = anchor;
            placed.localRotation = rotation;
            placed.name = $"Placed_{(previewItemData != null ? previewItemData.name : "Item")}";

            if (placedRenderers != null)
            {
                for (int i = 0; i < placedRenderers.Length; i++)
                {
                    Renderer r = placedRenderers[i];
                    if (r == null) continue;
                    r.enabled = true;
                    r.SetPropertyBlock(null);
                }
            }

            previewObject = null;
            previewRenderers = null;
            return;
        }

        if (previewItemData == null)
            return;

        GameObject placedGo = previewItemData.previewPrefab != null
            ? Instantiate(previewItemData.previewPrefab, _placedItemsRoot)
            : CreateFallbackPreview(previewItemData);
        if (placedGo == null)
            return;

        if (placedGo.transform.parent != _placedItemsRoot)
            placedGo.transform.SetParent(_placedItemsRoot, false);

        placedGo.transform.localPosition = anchor;
        placedGo.transform.localRotation = rotation;
        placedGo.name = $"Placed_{previewItemData.name}";

        DisablePreviewPhysics(placedGo);
        SetLayerRecursively(placedGo, gridPlane.gameObject.layer);

        Renderer[] renderers = placedGo.GetComponentsInChildren<Renderer>(true);
        for (int i = 0; i < renderers.Length; i++)
        {
            Renderer r = renderers[i];
            if (r == null) continue;
            r.enabled = true;
            r.SetPropertyBlock(null);
        }
    }

    void ClearCellTiles()
    {
        for (int i = 0; i < _cellTiles.Count; i++)
        {
            CellTile tile = _cellTiles[i];
            if (tile == null || tile.transform == null)
                continue;

            if (Application.isPlaying)
                Destroy(tile.transform.gameObject);
            else
                DestroyImmediate(tile.transform.gameObject);
        }

        _cellTiles.Clear();
        _cachedCellWidth = -1;
        _cachedCellDepth = -1;
    }

    void UpdateCellOverlay(bool inventoryOpen, bool hasHover, Vector3Int hoverGridPos)
    {
        if (!showPlacementCells || !inventoryOpen || _cellTiles.Count == 0)
        {
            SetCellOverlayVisible(false);
            return;
        }

        SetCellOverlayVisible(true);

        int layer = inventorySystem.currentLayer;
        float clampedFill = Mathf.Clamp(cellFill, 0.1f, 1f);
        bool hasPreviewItem = previewItemData != null && hasHover;

        _previewFootprintCells.Clear();
        _previewFootprintBlocked.Clear();
        if (hasPreviewItem)
        {
            foreach (Vector3Int offset in previewItemData.GetRotatedOffsets(previewRotation))
            {
                Vector3Int occupied = hoverGridPos + offset;
                if (!inventorySystem.InBounds(occupied) || occupied.y != layer)
                    continue;

                Vector2Int key = new Vector2Int(occupied.x, occupied.z);
                bool blocked = inventorySystem.IsOccupied(occupied);

                _previewFootprintCells.Add(key);
                if (_previewFootprintBlocked.TryGetValue(key, out bool existing))
                    _previewFootprintBlocked[key] = existing || blocked;
                else
                    _previewFootprintBlocked.Add(key, blocked);
            }
        }

        for (int i = 0; i < _cellTiles.Count; i++)
        {
            CellTile tile = _cellTiles[i];
            if (tile == null || tile.transform == null)
                continue;

            tile.transform.localPosition = new Vector3(tile.x, layer + cellHeightOffset, tile.z);
            tile.transform.localScale = new Vector3(clampedFill, clampedFill, 1f);

            Vector3Int tileGridPos = new Vector3Int(tile.x, layer, tile.z);
            bool tileOccupied = inventorySystem.IsOccupied(tileGridPos);
            Color color = tileOccupied ? cellOccupiedTransparentColor : cellNeutralColor;
            if (hasPreviewItem)
            {
                Vector2Int key = new Vector2Int(tile.x, tile.z);
                if (_previewFootprintCells.Contains(key))
                {
                    bool blocked = _previewFootprintBlocked.TryGetValue(key, out bool isBlocked) && isBlocked;
                    color = blocked ? cellInvalidColor : cellValidColor;
                }
            }

            SetRendererColor(tile.renderer, color);
        }
    }

    void SetCellOverlayVisible(bool visible)
    {
        if (_cellRoot != null)
            _cellRoot.gameObject.SetActive(visible);
    }

    Material GetCellOverlayMaterial()
    {
        if (cellOverlayMaterial != null)
            return cellOverlayMaterial;

        if (_runtimeCellMaterial != null)
            return _runtimeCellMaterial;

        Shader shader = Shader.Find("Sprites/Default");
        if (shader == null)
            shader = Shader.Find("Unlit/Color");
        if (shader == null)
            return null;

        _runtimeCellMaterial = new Material(shader)
        {
            hideFlags = HideFlags.HideAndDontSave
        };

        if (_runtimeCellMaterial.HasProperty("_Color"))
            _runtimeCellMaterial.SetColor("_Color", Color.white);

        return _runtimeCellMaterial;
    }

    void SetRendererColor(Renderer renderer, Color color)
    {
        if (renderer == null)
            return;

        Material shared = renderer.sharedMaterial;
        if (shared == null)
            return;

        cellColorBlock.Clear();
        bool wrote = false;

        if (shared.HasProperty("_BaseColor"))
        {
            cellColorBlock.SetColor("_BaseColor", color);
            wrote = true;
        }

        if (shared.HasProperty("_Color"))
        {
            cellColorBlock.SetColor("_Color", color);
            wrote = true;
        }

        if (wrote)
            renderer.SetPropertyBlock(cellColorBlock);
    }

    void DisablePreviewPhysics(GameObject go)
    {
        Collider[] cols = go.GetComponentsInChildren<Collider>(true);
        for (int i = 0; i < cols.Length; i++)
            cols[i].enabled = false;

        Rigidbody[] rigidbodies = go.GetComponentsInChildren<Rigidbody>(true);
        for (int i = 0; i < rigidbodies.Length; i++)
        {
            rigidbodies[i].isKinematic = true;
            rigidbodies[i].useGravity = false;
            rigidbodies[i].detectCollisions = false;
        }

        WorldObject[] worldObjects = go.GetComponentsInChildren<WorldObject>(true);
        for (int i = 0; i < worldObjects.Length; i++)
            worldObjects[i].enabled = false;
    }

    GameObject CreateFallbackPreview(ItemData item)
    {
        GameObject root = new GameObject("FallbackPreview");
        root.transform.SetParent(inventoryRoot, false);

        GameObject cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
        cube.transform.SetParent(root.transform, false);

        Collider col = cube.GetComponent<Collider>();
        if (col != null)
            Destroy(col);

        Vector3Int min = Vector3Int.zero;
        Vector3Int max = Vector3Int.zero;
        if (item.localOffsets != null && item.localOffsets.Count > 0)
        {
            min = item.localOffsets[0];
            max = item.localOffsets[0];

            for (int i = 1; i < item.localOffsets.Count; i++)
            {
                Vector3Int cell = item.localOffsets[i];
                min = Vector3Int.Min(min, cell);
                max = Vector3Int.Max(max, cell);
            }
        }

        cube.transform.localScale = new Vector3(
            Mathf.Max(1f, max.x - min.x + 1f),
            Mathf.Max(1f, max.y - min.y + 1f),
            Mathf.Max(1f, max.z - min.z + 1f)
        );
        cube.transform.localPosition = new Vector3(
            (min.x + max.x) * 0.5f,
            (min.y + max.y) * 0.5f,
            (min.z + max.z) * 0.5f
        );

        return root;
    }

    static void SetLayerRecursively(GameObject go, int layer)
    {
        if (go == null)
            return;

        Transform[] all = go.GetComponentsInChildren<Transform>(true);
        for (int i = 0; i < all.Length; i++)
            all[i].gameObject.layer = layer;
    }

    InventoryCameraController GetInventoryCameraController()
    {
        if (inventoryCameraController == null)
            inventoryCameraController = FindObjectOfType<InventoryCameraController>();

        return inventoryCameraController;
    }

    void OnDestroy()
    {
        if (_runtimeCellMaterial != null)
        {
            if (Application.isPlaying)
                Destroy(_runtimeCellMaterial);
            else
                DestroyImmediate(_runtimeCellMaterial);
        }
    }
}
