using UnityEngine;

/// <summary>
/// 3D背包摄像头与容器切换控制器，实现背包场景的独立渲染、激活/关闭、摄像头切换等。
/// </summary>
public class InventoryCameraController : MonoBehaviour
{
    [Header("主相机 (Main Camera)")]
    public Camera mainCamera;
    [Header("背包专用相机 (Inventory Camera)")]
    public Camera inventoryCamera;
    [Header("背包容器根节点 (Inventory Root)")]
    public GameObject inventoryRoot;

    [Header("鼠标状态")]
    [SerializeField] private bool unlockCursorWhenInventoryOpen = true;
    [SerializeField] private bool lockCursorWhenInventoryClosed = true;

    [Header("背包取景")]
    [SerializeField] private bool autoFrameCameraOnOpen = true;
    [SerializeField] private GridInventorySystem inventorySystem;
    [SerializeField] private Vector3 cameraViewDirection = new Vector3(1f, 0.75f, -1f);
    [SerializeField] private float framingDistanceMultiplier = 1.8f;
    [SerializeField] private float minFramingDistance = 3f;

    [Header("背包默认物品")]
    [SerializeField] private ItemData fallbackPreviewItem;

    [Header("快捷键")]
    [SerializeField] private bool allowToggleInventoryKey = true;
    [SerializeField] private KeyCode toggleInventoryKey = KeyCode.E;

    [Header("Runtime Links")]
    [SerializeField] private InventoryRaycastPlacer inventoryRaycastPlacer;
    [SerializeField] private InteractionSystem interactionSystem;

    private static InventoryCameraController _primaryController;
    private bool inventoryActive = false;
    private ItemData lastPreviewItem;
    public bool IsInventoryActive
    {
        get
        {
            InventoryCameraController primary = GetPrimaryController();
            if (primary != null && primary != this)
                return primary.inventoryActive;

            return inventoryActive;
        }
    }

    void Awake()
    {
        EnsurePrimaryController();
        if (!IsPrimaryController())
            allowToggleInventoryKey = false;
    }

    void Start()
    {
        if (!IsPrimaryController())
            return;

        SetInventoryActive(false);
    }

    void Update()
    {
        if (!IsPrimaryController())
            return;

        if (allowToggleInventoryKey && Input.GetKeyDown(toggleInventoryKey))
        {
            if (inventoryActive)
                CloseInventoryFromKey();
            else
                EnterInventoryMode(null);

            return;
        }

        // 仅允许Enter退出背包
        if (inventoryActive && Input.GetKeyDown(KeyCode.Return))
        {
            CloseInventoryFromKey();
        }
    }

    void CloseInventoryFromKey()
    {
        InventoryRaycastPlacer placer = GetInventoryPlacer();
        bool hadPreviewInHand = placer != null && placer.HasActivePreviewItem;

        SetInventoryActive(false);

        if (!hadPreviewInHand)
            return;

        InteractionSystem interaction = GetInteractionSystem();
        if (interaction != null)
            interaction.RestorePendingCollectedObjectToCarry();
    }

    /// <summary>
    /// 进入背包模式，传入当前收纳物品
    /// </summary>
    public void EnterInventoryMode(ItemData item)
    {
        InventoryCameraController primary = GetPrimaryController();
        if (primary != null && primary != this)
        {
            primary.EnterInventoryMode(item);
            return;
        }

        if (item == null)
            item = lastPreviewItem != null ? lastPreviewItem : fallbackPreviewItem;

        if (item != null)
            lastPreviewItem = item;

        SetInventoryActive(true);
        // 通知背包预览系统
        InventoryRaycastPlacer placer = GetInventoryPlacer();
        if (placer != null)
            placer.SetPreviewItem(item);
    }

    /// <summary>
    /// 激活/关闭背包视图（切换摄像头和背包物体）
    /// </summary>
    public void SetInventoryActive(bool active)
    {
        InventoryCameraController primary = GetPrimaryController();
        if (primary != null && primary != this)
        {
            primary.SetInventoryActive(active);
            inventoryActive = active;
            return;
        }

        inventoryActive = active;
        if (mainCamera != null) mainCamera.enabled = !active;
        if (inventoryCamera != null) inventoryCamera.enabled = active;
        if (inventoryRoot != null) inventoryRoot.SetActive(active);

        if (active)
            AutoFrameInventoryCamera();

        ApplyCursorState(active);

        if (!active)
        {
            // 退出背包时清除预览
            InventoryRaycastPlacer placer = GetInventoryPlacer();
            if (placer != null)
                placer.ClearPreview();
        }
    }

    public static InventoryCameraController GetPrimaryController()
    {
        if (_primaryController == null)
            _primaryController = SelectPrimaryController();

        return _primaryController;
    }

    static InventoryCameraController SelectPrimaryController()
    {
        InventoryCameraController[] controllers = FindObjectsOfType<InventoryCameraController>(true);
        if (controllers == null || controllers.Length == 0)
            return null;

        InventoryCameraController best = controllers[0];
        int bestScore = ScoreController(best);
        for (int i = 1; i < controllers.Length; i++)
        {
            InventoryCameraController candidate = controllers[i];
            int score = ScoreController(candidate);
            if (score > bestScore)
            {
                best = candidate;
                bestScore = score;
            }
        }

        return best;
    }

    static int ScoreController(InventoryCameraController controller)
    {
        if (controller == null)
            return int.MinValue;

        int score = 0;
        if (controller.enabled) score += 2;
        if (controller.inventorySystem != null) score += 4;
        if (controller.inventoryRoot != null) score += 2;
        if (controller.inventoryCamera != null) score += 1;
        if (controller.mainCamera != null) score += 1;
        return score;
    }

    void EnsurePrimaryController()
    {
        if (_primaryController == null)
            _primaryController = SelectPrimaryController();
    }

    bool IsPrimaryController()
    {
        EnsurePrimaryController();
        return _primaryController == this;
    }

    InventoryRaycastPlacer GetInventoryPlacer()
    {
        if (inventoryRaycastPlacer == null)
            inventoryRaycastPlacer = FindObjectOfType<InventoryRaycastPlacer>();

        return inventoryRaycastPlacer;
    }

    InteractionSystem GetInteractionSystem()
    {
        if (interactionSystem == null)
            interactionSystem = FindObjectOfType<InteractionSystem>();

        return interactionSystem;
    }

    void ApplyCursorState(bool inventoryOpen)
    {
        if (inventoryOpen)
        {
            if (!unlockCursorWhenInventoryOpen)
                return;

            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
            return;
        }

        if (!lockCursorWhenInventoryClosed)
            return;

        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;
    }

    void AutoFrameInventoryCamera()
    {
        if (!autoFrameCameraOnOpen || inventoryCamera == null || inventoryRoot == null)
            return;

        if (inventorySystem == null)
            inventorySystem = inventoryRoot.GetComponent<GridInventorySystem>();

        int width = inventorySystem != null ? inventorySystem.gridWidth : 6;
        int height = inventorySystem != null ? inventorySystem.gridHeight : 4;
        int depth = inventorySystem != null ? inventorySystem.gridDepth : 6;

        Transform root = inventoryRoot.transform;
        Vector3 centerLocal = new Vector3((width - 1) * 0.5f, (height - 1) * 0.5f, (depth - 1) * 0.5f);
        Vector3 centerWorld = root.TransformPoint(centerLocal);

        Vector3 localDir = cameraViewDirection.sqrMagnitude > 0.0001f
            ? cameraViewDirection.normalized
            : new Vector3(0.58f, 0.5f, -0.64f);
        Vector3 worldDir = root.TransformDirection(localDir).normalized;

        float size = Mathf.Max(width, Mathf.Max(height, depth));
        float distance = Mathf.Max(minFramingDistance, size * framingDistanceMultiplier);
        Vector3 cameraPos = centerWorld + worldDir * distance;

        inventoryCamera.transform.position = cameraPos;
        inventoryCamera.transform.rotation = Quaternion.LookRotation(centerWorld - cameraPos, Vector3.up);
    }

    void OnDestroy()
    {
        if (_primaryController == this)
            _primaryController = null;
    }
}
