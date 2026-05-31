using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 鼠标射线检测与物品预览吸附，适配InventoryCamera和3D网格。
/// </summary>
public class InventoryRaycastPlacer : MonoBehaviour
{
    private static InventoryRaycastPlacer _primaryInstance;
    
    public class TempItem
    {
        public ItemData itemData;
        public Transform transform;
        public Quaternion rotation;
        public Coroutine shakeCoroutine;
    }

    class PlacedItemMarker : MonoBehaviour
    {
        public ItemData itemData;
        public Vector3Int anchor;
        public Quaternion rotation;
    }

    class CellTile
    {
        public int x;
        public int z;
        public Transform transform;
        public LineRenderer lineRenderer;
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
    [SerializeField] private InteractionSystem interactionSystem;
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
    [SerializeField] private float cellFrameLineWidth = 0.05f;
    [SerializeField] private Color cellValidColor = new Color(0.2f, 0.9f, 0.35f, 0.52f);
    [SerializeField] private Color cellInvalidColor = new Color(0.9f, 0.2f, 0.2f, 0.58f);
    [SerializeField] private Color cellHoverValidColor = new Color(0.26f, 1f, 0.45f, 0.7f);
    [SerializeField] private Color cellHoverInvalidColor = new Color(1f, 0.3f, 0.25f, 0.72f);
    [SerializeField] private Color cellNeutralColor = new Color(0.6f, 0.6f, 0.6f, 0.3f);
    [SerializeField] private Color cellOccupiedTransparentColor = new Color(0f, 0f, 0f, 0f);
    [SerializeField] private Material cellOverlayMaterial;

    [Header("已放置物品")]
    [SerializeField] private bool keepPlacedItemsInInventory = true;
    [SerializeField] private bool closeInventoryOnPlace = false;

    [Header("拖拽延迟效果")]
    [SerializeField] private float dragSmoothTime = 0.05f;

    [Header("放置失败反馈")]
    [SerializeField] private bool flashOccupiedCellsOnPlaceFail = true;
    [SerializeField] private float occupiedFailFlashDuration = 0.45f;
    [SerializeField] private float occupiedFailFlashFrequency = 7.5f;
    [SerializeField] private Color occupiedFailFlashColor = new Color(1f, 0.18f, 0.18f, 0.92f);

    private Transform previewObject;
    private ItemData previewItemData;
    private Renderer[] previewRenderers;
    private MaterialPropertyBlock previewColorBlock;
    private MaterialPropertyBlock cellColorBlock;
    private Renderer gridRenderer;
    private readonly List<CellTile> _cellTiles = new List<CellTile>();
    private readonly HashSet<Vector2Int> _previewFootprintCells = new HashSet<Vector2Int>();
    private readonly Dictionary<Vector2Int, bool> _previewFootprintBlocked = new Dictionary<Vector2Int, bool>();
    private readonly HashSet<Vector2Int> _occupiedFailFlashCells = new HashSet<Vector2Int>();
    private Transform _cellRoot;
    private Transform _placedItemsRoot;
    private Material _runtimeCellMaterial;
    private int _cachedCellWidth = -1;
    private int _cachedCellDepth = -1;
    private float _occupiedFailFlashStartTime = -1f;
    private bool _wasInventoryOpen;
    
    private readonly List<TempItem> _tempItems = new List<TempItem>();
    private Vector3 _dragOffset = Vector3.zero;
    private Vector3 _previewVelocity = Vector3.zero;
    private bool _snapPreviewNextFrame;

    public bool HasActivePreviewItem => previewItemData != null;
    public List<TempItem> GetTempItems() => _tempItems;
    public void ClearTempItems() => _tempItems.Clear();

    void Awake()
    {
        if (_primaryInstance == null)
            _primaryInstance = this;

        previewColorBlock = new MaterialPropertyBlock();
        cellColorBlock = new MaterialPropertyBlock();
        CacheGridRenderer();
        SetGridVisible(false);
        SetCellOverlayVisible(false);
    }

    void Update()
    {
        if (!IsPrimaryInstance())
            return;

        if (inventoryCamera == null || inventoryRoot == null || gridPlane == null || inventorySystem == null)
            return;

        bool inventoryOpen = inventoryCamera.enabled;
        if (!inventoryOpen)
        {
            if (_wasInventoryOpen)
            {
                SetCellOverlayVisible(false);
                SetGridVisible(false);
                SetPreviewVisible(false);
            }

            _wasInventoryOpen = false;
            return;
        }

        _wasInventoryOpen = true;

        EnsureCellTiles();

        if (Input.GetKeyDown(KeyCode.W))
            inventorySystem.currentLayer = Mathf.Min(inventorySystem.currentLayer + 1, inventorySystem.gridHeight - 1);
        else if (Input.GetKeyDown(KeyCode.S))
            inventorySystem.currentLayer = Mathf.Max(inventorySystem.currentLayer - 1, 0);

        bool hasPlaneHit = TryGetGridAnchorUnderMouse(out Vector3Int gridPos, out Vector3 localPlaneHit, out bool anchorInBounds);

        if (previewItemData != null && previewObject != null)
        {
            Vector3 targetPos = localPlaneHit + _dragOffset;
            int gX = Mathf.FloorToInt(targetPos.x + 0.5f);
            int gZ = Mathf.FloorToInt(targetPos.z + 0.5f);
            gridPos = new Vector3Int(gX, inventorySystem.currentLayer, gZ);
            anchorInBounds = inventorySystem.InBounds(gridPos);
        }

        UpdateCellOverlay(inventoryOpen, hasPlaneHit && anchorInBounds, gridPos);

        bool showBasePlane = showGridWhenInventoryActive && inventoryOpen && !(showPlacementCells && hideBasePlaneRenderer);
        SetGridVisible(showBasePlane);

        if (previewItemData == null)
        {
            if (Input.GetMouseButtonDown(0))
            {
                TryPickTempItemUnderMouse();
                if (previewItemData == null)
                    TryPickPlacedItemUnderMouse();

                if (previewItemData != null && hasPlaneHit)
                {
                    _dragOffset = previewObject.localPosition - localPlaneHit;
                }
            }
            return;
        }

        // Dragging mode requires holding left click
        if (Input.GetMouseButton(0))
        {
            if (previewObject != null)
                previewObject.localRotation = previewRotation;

            if (!hasPlaneHit)
            {
                if (hidePreviewWhenNoHit)
                    SetPreviewVisible(false);
                SetPreviewColor(invalidPreviewColor);
            }
            else
            {
                SetPreviewVisible(true);
                if (previewObject != null)
                {
                    Vector3 targetPos = anchorInBounds ? (Vector3)gridPos : (localPlaneHit + _dragOffset);
                    
                    if (_snapPreviewNextFrame)
                    {
                        previewObject.localPosition = targetPos;
                        _snapPreviewNextFrame = false;
                        _previewVelocity = Vector3.zero;
                    }
                    else
                    {
                        previewObject.localPosition = Vector3.SmoothDamp(previewObject.localPosition, targetPos, ref _previewVelocity, dragSmoothTime);
                    }
                }

                bool canPlaceObj = anchorInBounds && inventorySystem.CanPlace(previewItemData, gridPos, previewRotation);
                SetPreviewColor(canPlaceObj ? validPreviewColor : invalidPreviewColor);
            }
            return;
        }

        // Released left click (or wasn't holding it)
        if (!hasPlaneHit)
        {
            // Drop to temp storage outside grid bounds
            Vector3 dropPos = previewObject != null ? previewObject.localPosition : Vector3.zero;
            ConvertToTempItem(previewObject, previewItemData, dropPos, previewRotation);
            ClearPreviewReferenceOnly();
            return;
        }

        bool canPlace = anchorInBounds && inventorySystem.CanPlace(previewItemData, gridPos, previewRotation);
        
        if (!canPlace)
        {
            if (anchorInBounds)
            {
                // In bounds but blocked -> Shake
                TryTriggerOccupiedFailFlash(gridPos, anchorInBounds);
                if (previewObject != null)
                {
                    Coroutine c = StartCoroutine(ShakeAndReject(previewObject, previewItemData, (Vector3)gridPos, previewRotation));
                    TempItem ti = ConvertToTempItem(previewObject, previewItemData, (Vector3)gridPos, previewRotation);
                    if (ti != null) ti.shakeCoroutine = c;
                }
                ClearPreviewReferenceOnly();
            }
            else
            {
                // Out of bounds -> Temp Item
                ConvertToTempItem(previewObject, previewItemData, localPlaneHit, previewRotation);
                ClearPreviewReferenceOnly();
            }
            return;
        }

        // Success Place
        if (inventorySystem.Place(previewItemData, gridPos, previewRotation))
        {
            CommitPlacedItemVisual(gridPos, previewRotation);

            InteractionSystem interaction = GetInteractionSystem();
            if (interaction != null)
            {
                interaction.DropCarriedObjectIfAny();
            }

            ClearPreview();

            if (closeInventoryOnPlace)
            {
                InventoryCameraController camCtrl = GetInventoryCameraController();
                if (camCtrl != null)
                    camCtrl.SetInventoryActive(false);
            }
        }
    }

    void TryPickPlacedItemUnderMouse()
    {
        if (inventoryCamera == null || inventorySystem == null)
            return;

        EnsurePlacedItemsRoot();
        if (_placedItemsRoot == null)
            return;

        Ray ray = inventoryCamera.ScreenPointToRay(Input.mousePosition);
        PlacedItemMarker marker = FindNearestPlacedItemMarker(ray);
        if (marker == null || marker.itemData == null)
            return;

        if (inventorySystem.InBounds(marker.anchor))
            inventorySystem.Remove(marker.anchor);

        previewItemData = marker.itemData;
        previewRotation = marker.rotation;
        previewObject = marker.transform;
        previewRenderers = previewObject != null
            ? previewObject.GetComponentsInChildren<Renderer>(true)
            : null;

        if (previewObject != null && previewObject.parent != inventoryRoot)
            previewObject.SetParent(inventoryRoot, false);

        if (marker != null)
        {
            if (Application.isPlaying) Destroy(marker);
            else UnityEngine.Object.DestroyImmediate(marker);
        }

        SetPreviewVisible(true);
        SetPreviewColor(validPreviewColor);

        InteractionSystem interaction = GetInteractionSystem();
        if (interaction != null)
        {
            // Removed SetPendingInventoryCarryItem logic
        }
    }

    PlacedItemMarker FindNearestPlacedItemMarker(Ray ray)
    {
        if (_placedItemsRoot == null)
            return null;

        PlacedItemMarker[] markers = _placedItemsRoot.GetComponentsInChildren<PlacedItemMarker>(true);
        if (markers == null || markers.Length == 0)
            return null;

        float nearestDistance = float.PositiveInfinity;
        PlacedItemMarker nearest = null;

        for (int i = 0; i < markers.Length; i++)
        {
            PlacedItemMarker marker = markers[i];
            if (marker == null || marker.itemData == null)
                continue;

            Renderer[] renderers = marker.GetComponentsInChildren<Renderer>(true);
            if (renderers == null || renderers.Length == 0)
                continue;

            float markerNearest = float.PositiveInfinity;
            bool hit = false;
            for (int r = 0; r < renderers.Length; r++)
            {
                Renderer renderer = renderers[r];
                if (renderer == null || !renderer.enabled)
                    continue;

                if (!renderer.bounds.IntersectRay(ray, out float hitDistance))
                    continue;

                if (hitDistance >= markerNearest)
                    continue;

                markerNearest = hitDistance;
                hit = true;
            }

            if (!hit || markerNearest >= nearestDistance)
                continue;

            nearestDistance = markerNearest;
            nearest = marker;
        }

        return nearest;
    }

    void TryTriggerOccupiedFailFlash(Vector3Int anchor, bool anchorInBounds)
    {
        if (!flashOccupiedCellsOnPlaceFail || previewItemData == null || !anchorInBounds)
            return;

        _occupiedFailFlashCells.Clear();

        int layer = inventorySystem.currentLayer;
        foreach (Vector3Int offset in previewItemData.GetRotatedOffsets(previewRotation))
        {
            Vector3Int pos = anchor + offset;
            if (!inventorySystem.InBounds(pos) || pos.y != layer)
                continue;

            if (!inventorySystem.IsOccupied(pos))
                continue;

            _occupiedFailFlashCells.Add(new Vector2Int(pos.x, pos.z));
        }

        if (_occupiedFailFlashCells.Count <= 0)
            return;

        _occupiedFailFlashStartTime = Time.unscaledTime;
    }

    public void SetPreviewItem(ItemData item)
    {
        ClearOccupiedFailFlash();
        _dragOffset = Vector3.zero;
        _snapPreviewNextFrame = true;

        previewItemData = item;
        if (previewObject != null)
        {
            if (Application.isPlaying) Destroy(previewObject.gameObject);
            else UnityEngine.Object.DestroyImmediate(previewObject.gameObject);
        }

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

    void TryPickTempItemUnderMouse()
    {
        if (inventoryCamera == null || _tempItems.Count == 0) return;

        Ray ray = inventoryCamera.ScreenPointToRay(Input.mousePosition);
        TempItem nearest = null;
        float nearestDist = float.PositiveInfinity;

        for (int i = 0; i < _tempItems.Count; i++)
        {
            TempItem ti = _tempItems[i];
            if (ti == null || ti.transform == null) continue;
            
            Renderer[] renderers = ti.transform.GetComponentsInChildren<Renderer>(true);
            foreach (Renderer r in renderers)
            {
                if (!r.enabled) continue;
                if (r.bounds.IntersectRay(ray, out float hitDist))
                {
                    if (hitDist < nearestDist)
                    {
                        nearestDist = hitDist;
                        nearest = ti;
                    }
                }
            }
        }

        if (nearest != null)
        {
            if (nearest.shakeCoroutine != null)
                StopCoroutine(nearest.shakeCoroutine);

            _tempItems.Remove(nearest);
            previewItemData = nearest.itemData;
            previewObject = nearest.transform;
            previewRotation = nearest.rotation;
            previewRenderers = previewObject.GetComponentsInChildren<Renderer>(true);

            SetPreviewVisible(true);
            SetPreviewColor(validPreviewColor);
        }
    }

    TempItem ConvertToTempItem(Transform obj, ItemData data, Vector3 pos, Quaternion rot)
    {
        if (obj == null) return null;

        obj.localPosition = pos;
        obj.localRotation = rot;
        obj.name = "TempItem_" + (data != null ? data.name : "Unknown");

        Renderer[] renderers = obj.GetComponentsInChildren<Renderer>(true);
        foreach (Renderer r in renderers)
        {
            if (r == null) continue;
            r.SetPropertyBlock(null);
        }

        TempItem ti = new TempItem { itemData = data, transform = obj, rotation = rot };
        _tempItems.Add(ti);
        return ti;
    }

    void ClearPreviewReferenceOnly()
    {
        _dragOffset = Vector3.zero;
        previewItemData = null;
        previewObject = null;
        previewRenderers = null;
    }

    private System.Collections.IEnumerator ShakeAndReject(Transform obj, ItemData itemData, Vector3 startPos, Quaternion rotation)
    {
        if (obj == null) yield break;

        float duration = 0.4f;
        float elapsed = 0f;
        float shakeAmount = 0.25f;
        float speed = 50f;

        while (elapsed < duration)
        {
            if (obj == null) yield break;
            elapsed += Time.unscaledDeltaTime;

            float xOffset = Mathf.Sin(elapsed * speed) * shakeAmount;
            obj.localPosition = startPos + new Vector3(xOffset, 0, 0);

            yield return null;
        }

        if (obj != null)
            obj.localPosition = startPos;
    }

    public void ForceDropPreviewToTemp()
    {
        if (previewItemData == null || previewObject == null) return;
        ConvertToTempItem(previewObject, previewItemData, previewObject.localPosition, previewRotation);
        ClearPreviewReferenceOnly();
    }

    public void ClearPreview()
    {
        ClearOccupiedFailFlash();

        previewItemData = null;
        if (previewObject != null)
        {
            if (Application.isPlaying) Destroy(previewObject.gameObject);
            else UnityEngine.Object.DestroyImmediate(previewObject.gameObject);
        }
        previewObject = null;
        previewRenderers = null;
    }

    bool TryGetGridAnchorUnderMouse(out Vector3Int gridPos, out Vector3 localHit, out bool inBackpackRange)
    {
        gridPos = default;
        localHit = Vector3.zero;
        inBackpackRange = false;

        Ray ray = inventoryCamera.ScreenPointToRay(Input.mousePosition);

        Vector3 layerOffset = new Vector3(0, inventorySystem.currentLayer, 0);
        Vector3 planePoint = inventoryRoot.TransformPoint(layerOffset);
        Vector3 planeNormal = inventoryRoot.up;
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
                GameObject tile = new GameObject($"Cell_{x}_{z}");
                tile.name = $"Cell_{x}_{z}";
                tile.transform.SetParent(_cellRoot, false);
                tile.transform.localRotation = Quaternion.identity;
                tile.layer = layer;

                LineRenderer lr = tile.AddComponent<LineRenderer>();
                lr.useWorldSpace = false;
                lr.loop = false;
                lr.positionCount = 5;
                lr.alignment = LineAlignment.View;
                lr.numCornerVertices = 2;
                lr.numCapVertices = 2;
                lr.sortingOrder = 20;
                lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                lr.receiveShadows = false;
                if (cellMat != null)
                    lr.sharedMaterial = cellMat;

                _cellTiles.Add(new CellTile
                {
                    x = x,
                    z = z,
                    transform = tile.transform,
                    lineRenderer = lr
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

            AttachPlacedItemMarker(placed.gameObject, previewItemData, anchor, rotation);

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

        AttachPlacedItemMarker(placedGo, previewItemData, anchor, rotation);

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
            ConfigureCellFrame(tile.lineRenderer, clampedFill);

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

            if (TryGetOccupiedFailFlashColor(tile.x, tile.z, color, out Color flashColor))
                color = flashColor;

            SetLineColor(tile.lineRenderer, color);
        }
    }

    bool TryGetOccupiedFailFlashColor(int x, int z, Color baseColor, out Color flashColor)
    {
        flashColor = baseColor;
        if (!IsOccupiedFailFlashActive())
            return false;

        Vector2Int key = new Vector2Int(x, z);
        if (!_occupiedFailFlashCells.Contains(key))
            return false;

        float elapsed = Time.unscaledTime - _occupiedFailFlashStartTime;
        float wave = 0.5f + 0.5f * Mathf.Sin(elapsed * Mathf.PI * 2f * Mathf.Max(0.1f, occupiedFailFlashFrequency));
        flashColor = Color.Lerp(baseColor, occupiedFailFlashColor, wave);
        return true;
    }

    bool IsOccupiedFailFlashActive()
    {
        if (_occupiedFailFlashStartTime < 0f)
            return false;

        float duration = Mathf.Max(0.05f, occupiedFailFlashDuration);
        if (Time.unscaledTime - _occupiedFailFlashStartTime <= duration)
            return true;

        ClearOccupiedFailFlash();
        return false;
    }

    void ClearOccupiedFailFlash()
    {
        _occupiedFailFlashStartTime = -1f;
        _occupiedFailFlashCells.Clear();
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

    void SetLineColor(LineRenderer lr, Color color)
    {
        if (lr == null)
            return;

        Material shared = lr.sharedMaterial;
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
            lr.SetPropertyBlock(cellColorBlock);
    }

    void ConfigureCellFrame(LineRenderer lr, float fill)
    {
        if (lr == null)
            return;

        float half = Mathf.Clamp(fill, 0.1f, 1f) * 0.5f;
        Vector3 p0 = new Vector3(-half, 0f, -half);
        Vector3 p1 = new Vector3(-half, 0f, half);
        Vector3 p2 = new Vector3(half, 0f, half);
        Vector3 p3 = new Vector3(half, 0f, -half);

        lr.startWidth = cellFrameLineWidth;
        lr.endWidth = cellFrameLineWidth;
        lr.SetPosition(0, p0);
        lr.SetPosition(1, p1);
        lr.SetPosition(2, p2);
        lr.SetPosition(3, p3);
        lr.SetPosition(4, p0);
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
        {
            if (Application.isPlaying) Destroy(col);
            else UnityEngine.Object.DestroyImmediate(col);
        }

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

    void AttachPlacedItemMarker(GameObject go, ItemData itemData, Vector3Int anchor, Quaternion rotation)
    {
        if (go == null)
            return;

        PlacedItemMarker marker = go.GetComponent<PlacedItemMarker>();
        if (marker == null)
            marker = go.AddComponent<PlacedItemMarker>();

        marker.itemData = itemData;
        marker.anchor = anchor;
        marker.rotation = rotation;
    }

    public static InventoryRaycastPlacer GetPrimaryPlacer()
    {
        if (_primaryInstance == null)
            _primaryInstance = FindObjectOfType<InventoryRaycastPlacer>(true);

        return _primaryInstance;
    }

    bool IsPrimaryInstance()
    {
        if (_primaryInstance == null)
            _primaryInstance = this;

        return _primaryInstance == this;
    }

    InventoryCameraController GetInventoryCameraController()
    {
        InventoryCameraController primary = InventoryCameraController.GetPrimaryController();
        if (primary != null)
            inventoryCameraController = primary;
        else if (inventoryCameraController == null)
            inventoryCameraController = FindObjectOfType<InventoryCameraController>();

        return inventoryCameraController;
    }

    InteractionSystem GetInteractionSystem()
    {
        if (interactionSystem == null)
            interactionSystem = FindObjectOfType<InteractionSystem>();

        return interactionSystem;
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
