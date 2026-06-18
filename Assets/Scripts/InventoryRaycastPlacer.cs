using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 鼠标射线检测与物品预览吸附，适配InventoryCamera和3D网格。
/// </summary>
public class InventoryRaycastPlacer : MonoBehaviour
{
    private static InventoryRaycastPlacer _primaryInstance;
    
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

    [SerializeField] private bool hasHover = false;
    private Transform previewObject;
    private ItemData previewItemData;
    private Color previewCustomColor = Color.clear;
    private Renderer[] previewRenderers;
    private MaterialPropertyBlock previewColorBlock;
    private MaterialPropertyBlock cellColorBlock;
    private Renderer gridRenderer;
    private readonly List<CellTile> _cellTiles = new List<CellTile>();
    private readonly HashSet<Vector2Int> _previewFootprintCells = new HashSet<Vector2Int>();
    private readonly Dictionary<Vector2Int, bool> _previewFootprintBlocked = new Dictionary<Vector2Int, bool>();
    private readonly HashSet<Vector2Int> _occupiedFailFlashCells = new HashSet<Vector2Int>();
    private Transform _cellRoot;
    private Material _runtimeCellMaterial;
    private int _cachedCellWidth = -1;
    private int _cachedCellDepth = -1;
    private float _occupiedFailFlashStartTime = -1f;
    private bool _wasInventoryOpen;
    private bool _hasLastValidGridPos;
    private Vector3 _lastValidGridPos;
    private Vector3 _dragOffset = Vector3.zero;
    private Vector3 _previewVelocity = Vector3.zero;
    private bool _snapPreviewNextFrame;

    public bool HasActivePreviewItem => previewItemData != null;

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
                TryPickPlacedItemUnderMouse();

                if (previewItemData != null && hasPlaneHit)
                {
                    _dragOffset = previewObject.localPosition - localPlaneHit;
                }
            }
            return;
        }

        bool canPlaceObj = false;
        if (hasPlaneHit && anchorInBounds)
        {
            canPlaceObj = inventorySystem.CanPlace(previewItemData, gridPos, previewRotation);
        }

        // Always update preview position to follow mouse
        if (previewObject != null)
        {
            previewObject.localRotation = previewRotation;

            if (!hasPlaneHit)
            {
                if (hidePreviewWhenNoHit)
                    SetPreviewVisible(false);
                SetPreviewColor(invalidPreviewColor);
                
                // When completely outside the plane, fully follow the mouse
                Vector3 targetPos = localPlaneHit + _dragOffset;
                _hasLastValidGridPos = false; // We left the grid completely

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
            else
                SetPreviewVisible(true);
                Vector3 targetPos;
                if (anchorInBounds)
                {
                    if (canPlaceObj)
                    {
                        // Perfectly valid grid spot
                        targetPos = (Vector3)gridPos;
                        _hasLastValidGridPos = true;
                        _lastValidGridPos = targetPos;
                    }
                    else
                    {
                        // Invalid spot (e.g., sticking out of bounds or colliding). 
                        // Stick to the last valid grid position if we have one.
                        if (_hasLastValidGridPos)
                        {
                            targetPos = _lastValidGridPos;
                        }
                        else
                        {
                            targetPos = (Vector3)gridPos;
                        }
                    }

                    // Constrain the target to the grid bounds
                    targetPos.x = Mathf.Clamp(targetPos.x, 0f, inventorySystem.gridWidth - 1f);
                    targetPos.y = Mathf.Clamp(targetPos.y, 0f, inventorySystem.gridHeight - 1f);
                    targetPos.z = Mathf.Clamp(targetPos.z, 0f, inventorySystem.gridDepth - 1f);
                }
                else
                {
                    // The mouse is over the infinite plane, but the anchor is outside the grid bounds.
                    // This means we dragged it fully off the grid edge. Stop snapping and follow the mouse fully.
                    targetPos = localPlaneHit + _dragOffset;
                    _hasLastValidGridPos = false;
                }
                
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

                bool visualValid = canPlaceObj || (anchorInBounds && _hasLastValidGridPos);
                SetPreviewColor(visualValid ? validPreviewColor : invalidPreviewColor);
            }
        }

        // Try to place or drop on click or release
        if (Input.GetMouseButtonUp(0) || Input.GetMouseButtonDown(0))
        {
            if (!hasPlaneHit)
            {
                // Drop to real world outside grid bounds
                if (previewObject != null)
                {
                    StartCoroutine(AnimateAndDropToWorld(previewObject.gameObject, previewItemData));
                }
                ClearPreviewReferenceOnly();
                return;
            }

            Vector3Int placePos = gridPos;
            bool attemptPlace = canPlaceObj;

            if (!canPlaceObj && anchorInBounds && _hasLastValidGridPos)
            {
                placePos = new Vector3Int(
                    Mathf.RoundToInt(_lastValidGridPos.x),
                    Mathf.RoundToInt(_lastValidGridPos.y),
                    Mathf.RoundToInt(_lastValidGridPos.z)
                );
                attemptPlace = true;
            }

            if (!attemptPlace)
            {
                if (anchorInBounds)
                {
                    // In bounds but blocked -> Shake
                    TryTriggerOccupiedFailFlash(gridPos, anchorInBounds);
                    if (previewObject != null)
                    {
                        StartCoroutine(ShakeAndReject(previewObject, previewItemData, (Vector3)gridPos, previewRotation));
                    }
                    // Continue holding the item (do not clear reference or convert to TempItem)
                }
                else
                {
                    // Out of bounds -> Drop to real world
                    if (previewObject != null)
                    {
                        StartCoroutine(AnimateAndDropToWorld(previewObject.gameObject, previewItemData));
                    }
                    ClearPreviewReferenceOnly();
                }
                return;
            }

            // Success Place
            if (inventorySystem.Place(previewItemData, placePos, previewRotation))
            {
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
    }

    void TryPickPlacedItemUnderMouse()
    {
        if (inventoryCamera == null || inventorySystem == null)
            return;

        InventoryContainerView view = FindObjectOfType<InventoryContainerView>();
        if (view == null) return;

        Ray ray = inventoryCamera.ScreenPointToRay(Input.mousePosition);
        ItemInstance instance = view.GetItemAtRay(ray);
        
        if (instance == null || instance.item == null)
            return;

        if (inventorySystem.InBounds(instance.anchor))
            inventorySystem.Remove(instance.anchor);

        // Recreate the preview since the View destroyed the placed visual
        previewItemData = instance.item;
        previewRotation = instance.rotation;
        _hasLastValidGridPos = true;
        _lastValidGridPos = instance.anchor;
        
        GameObject previewGo;
        if (instance.item.previewPrefab != null)
        {
            GameObject root = new GameObject("PreviewRoot");
            root.transform.SetParent(inventoryRoot, false);
            GameObject prefabInstance = Instantiate(instance.item.previewPrefab, root.transform);
            prefabInstance.transform.localScale = instance.item.previewPrefab.transform.localScale;
            
            Vector3 offset = Vector3.zero;
            if (instance.item.localOffsets != null && instance.item.localOffsets.Count > 0)
            {
                Vector3Int min = instance.item.localOffsets[0];
                Vector3Int max = instance.item.localOffsets[0];
                for (int i = 1; i < instance.item.localOffsets.Count; i++)
                {
                    min = Vector3Int.Min(min, instance.item.localOffsets[i]);
                    max = Vector3Int.Max(max, instance.item.localOffsets[i]);
                }
                offset = new Vector3(
                    (min.x + max.x) * 0.5f,
                    (min.y + max.y) * 0.5f,
                    (min.z + max.z) * 0.5f
                );
            prefabInstance.transform.localPosition = instance.item.previewPrefab.transform.localPosition + offset;
            prefabInstance.transform.localRotation = instance.item.previewPrefab.transform.localRotation;
            previewGo = root;
        }
        else
        {
            previewGo = CreateFallbackPreview(instance.item);
        }

        if (previewGo != null)
        {
            previewObject = previewGo.transform;
            previewObject.localPosition = instance.anchor;
            previewObject.localRotation = instance.rotation;
            previewRenderers = previewGo.GetComponentsInChildren<Renderer>(true);
            DisablePreviewPhysics(previewGo);
            SetLayerRecursively(previewGo, gridPlane.gameObject.layer);
            SetPreviewVisible(true);
            SetPreviewColor(validPreviewColor);
        }
        
        InteractionSystem interaction = GetInteractionSystem();
        if (interaction != null)
        {
            // Pending carry item logic
        }
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

    public void SetPreviewItem(ItemData item, Color customColor = default)
    {
        ClearOccupiedFailFlash();
        _dragOffset = Vector3.zero;
        _snapPreviewNextFrame = true;

        previewItemData = item;
        previewCustomColor = customColor;
        if (previewObject != null)
        {
            if (Application.isPlaying) Destroy(previewObject.gameObject);
            else UnityEngine.Object.DestroyImmediate(previewObject.gameObject);
        }

        previewObject = null;
        previewRenderers = null;

        if (item == null)
            return;

        GameObject previewGo;
        if (item.previewPrefab != null)
        {
            GameObject root = new GameObject("PreviewRoot");
            root.transform.SetParent(inventoryRoot, false);
            GameObject prefabInstance = Instantiate(item.previewPrefab, root.transform);
            prefabInstance.transform.localScale = item.previewPrefab.transform.localScale;
            
            Vector3 offset = Vector3.zero;
            if (item.localOffsets != null && item.localOffsets.Count > 0)
            {
                Vector3Int min = item.localOffsets[0];
                Vector3Int max = item.localOffsets[0];
                for (int i = 1; i < item.localOffsets.Count; i++)
                {
                    min = Vector3Int.Min(min, item.localOffsets[i]);
                    max = Vector3Int.Max(max, item.localOffsets[i]);
                }
                offset = new Vector3(
                    (min.x + max.x) * 0.5f,
                    (min.y + max.y) * 0.5f,
                    (min.z + max.z) * 0.5f
                );
            }
            prefabInstance.transform.localPosition = item.previewPrefab.transform.localPosition + offset;
            prefabInstance.transform.localRotation = item.previewPrefab.transform.localRotation;
            previewGo = root;
        }
        else
        {
            previewGo = CreateFallbackPreview(item);
        }

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


    private System.Collections.IEnumerator AnimateAndDropToWorld(GameObject obj, ItemData itemData)
    {
        if (obj == null || itemData == null) yield break;

        // Reset color to normal for animation
        Renderer[] renderers = obj.GetComponentsInChildren<Renderer>(true);
        foreach (Renderer r in renderers)
        {
            if (r == null) continue;
            r.SetPropertyBlock(null);
        }

        Vector3 initialScale = obj.transform.localScale;
        Vector3 targetScale = initialScale * 1.2f;

        float durationUp = 0.1f;
        float elapsed = 0f;
        while (elapsed < durationUp)
        {
            if (obj == null) yield break;
            elapsed += Time.unscaledDeltaTime;
            obj.transform.localScale = Vector3.Lerp(initialScale, targetScale, elapsed / durationUp);
            yield return null;
        }

        float durationDown = 0.2f;
        elapsed = 0f;
        while (elapsed < durationDown)
        {
            if (obj == null) yield break;
            elapsed += Time.unscaledDeltaTime;
            obj.transform.localScale = Vector3.Lerp(targetScale, Vector3.zero, elapsed / durationDown);
            yield return null;
        }

        if (obj != null)
        {
            if (Application.isPlaying) Destroy(obj);
            else DestroyImmediate(obj);
        }

        // Spawn real world object
        if (itemData.worldPrefab != null)
        {
            Camera mainCam = Camera.main;
            Vector3 spawnBase = mainCam != null ? mainCam.transform.position : Vector3.zero;
            Vector3 spawnFwd = mainCam != null ? mainCam.transform.forward : Vector3.forward;

            Vector3 spawnPos = spawnBase + spawnFwd * 1.2f + Vector3.up * 0.25f;
            GameObject realWorldObj = Instantiate(itemData.worldPrefab, spawnPos, Quaternion.identity);
            
            Rigidbody rb = realWorldObj.GetComponent<Rigidbody>();
            if (rb != null)
            {
                rb.isKinematic = false;
                rb.useGravity = true;
                rb.velocity = Vector3.zero;
                rb.angularVelocity = Vector3.zero;
                rb.AddForce(spawnFwd * 1.5f + Vector3.up * 1.0f, ForceMode.Impulse);
            }
        }
    }

    public void ForceDropPreviewToWorld()
    {
        if (previewItemData == null || previewObject == null) return;
        StartCoroutine(AnimateAndDropToWorld(previewObject.gameObject, previewItemData));
        ClearPreviewReferenceOnly();
    }

    void ClearPreviewReferenceOnly()
    {
        _dragOffset = Vector3.zero;
        previewItemData = null;
        previewObject = null;
        previewRenderers = null;
        _hasLastValidGridPos = false;
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



    public void CreatePreviewObject(ItemData item)
    {
        ClearPreview();
        if (item == null) return;
        
        previewItemData = item;
        _hasLastValidGridPos = false;
        if (previewObject != null)
        {
            if (Application.isPlaying) Destroy(previewObject.gameObject);
            else UnityEngine.Object.DestroyImmediate(previewObject.gameObject);
        }
        
        GameObject previewGo;
        if (item.previewPrefab != null)
        {
            GameObject root = new GameObject("PreviewRoot");
            root.transform.SetParent(inventoryRoot, false);
            GameObject prefabInstance = Instantiate(item.previewPrefab, root.transform);
            prefabInstance.transform.localScale = item.previewPrefab.transform.localScale;
            
            Vector3 offset = Vector3.zero;
            if (item.localOffsets != null && item.localOffsets.Count > 0)
            {
                Vector3Int min = item.localOffsets[0];
                Vector3Int max = item.localOffsets[0];
                for (int i = 1; i < item.localOffsets.Count; i++)
                {
                    min = Vector3Int.Min(min, item.localOffsets[i]);
                    max = Vector3Int.Max(max, item.localOffsets[i]);
                }
                offset = new Vector3(
                    (min.x + max.x) * 0.5f,
                    (min.y + max.y) * 0.5f,
                    (min.z + max.z) * 0.5f
                );
            }
            prefabInstance.transform.localPosition = item.previewPrefab.transform.localPosition + offset;
            prefabInstance.transform.localRotation = item.previewPrefab.transform.localRotation;
            previewGo = root;
        }
        else
        {
            previewGo = CreateFallbackPreview(item);
        }

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

    // Ensure PlacedItemsRoot and CommitPlacedItemVisual logic was completely moved to View.


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

    // Removed AttachPlacedItemMarker

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
