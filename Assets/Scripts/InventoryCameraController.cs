using UnityEngine;
using UnityEngine.Rendering;

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

    [Header("Render Culling")]
    [SerializeField] private bool aggressivelyCullOutOfViewRenderers = true;
    [SerializeField] private bool cullOnlyWhenInventoryOpen = true;
    [SerializeField] private bool preserveFrameInfluencingRenderers = true;
    [SerializeField] private bool preserveShadowCastingRenderers = true;
    [Min(16f)]
    [SerializeField] private int cullingBatchSize = 256;
    [Min(0.01f)]
    [SerializeField] private float cullingUpdateInterval = 0.08f;
    [Min(0.2f)]
    [SerializeField] private float cullingCacheRefreshInterval = 1.2f;

    private static InventoryCameraController _primaryController;
    private bool inventoryActive = false;
    private int _openedFrameCount = -1;
    private ItemData lastPreviewItem;
    private Renderer[] _cachedRenderers;
    private float _nextCullingTickTime;
    private float _nextCullingCacheRefreshTime;
    private int _cullingCursor;
    private bool _hasForcedRenderersOff;
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
        // 强制启用快捷键，防止编辑器序列化丢失或被意外设为 false
        allowToggleInventoryKey = true;

        // 动态补全相机与背包根节点引用
        if (mainCamera == null)
        {
            mainCamera = Camera.main;
            if (mainCamera == null)
            {
                GameObject mainCamObj = GameObject.FindWithTag("MainCamera");
                if (mainCamObj != null)
                    mainCamera = mainCamObj.GetComponent<Camera>();
            }
        }

        if (inventoryCamera == null)
        {
            Camera[] allCameras = FindObjectsOfType<Camera>(true);
            foreach (Camera cam in allCameras)
            {
                if (cam.name.Contains("PackStorage") || cam.name.Contains("Inventory"))
                {
                    inventoryCamera = cam;
                    break;
                }
            }
        }

        if (inventoryRoot == null)
        {
            GridInventorySystem sys = FindObjectOfType<GridInventorySystem>(true);
            if (sys != null)
            {
                inventoryRoot = sys.gameObject;
                inventorySystem = sys;
            }
            else
            {
                GameObject rootObj = GameObject.Find("InventoryRootEmpty_PackStorage");
                if (rootObj == null) rootObj = GameObject.Find("InventoryRoot");
                if (rootObj != null)
                {
                    inventoryRoot = rootObj;
                    inventorySystem = rootObj.GetComponent<GridInventorySystem>();
                }
            }
        }
        else if (inventorySystem == null)
        {
            inventorySystem = inventoryRoot.GetComponent<GridInventorySystem>();
            if (inventorySystem == null)
                inventorySystem = FindObjectOfType<GridInventorySystem>(true);
        }

        EnsurePrimaryController();
    }

    void Start()
    {
        if (!IsPrimaryController())
            return;

        SetInventoryActive(false);
        if (aggressivelyCullOutOfViewRenderers)
            RebuildRendererCache();
    }

void Update()
{
    if (!IsPrimaryController())
        return;

    if (allowToggleInventoryKey && Input.GetKeyDown(toggleInventoryKey))
    {
        InteractionSystem interaction = GetInteractionSystem();
        
        if (inventoryActive)
        {
            if (Time.frameCount == _openedFrameCount)
                return;
            CloseInventoryFromKey();
        }
        else
        {
            // 修改这里：如果手上拿着物品，先获取这个物品的数据，然后带入背包
            if (interaction != null && interaction.HasCarriedObject())
            {
                // 假设你的 interactionSystem 有方法可以获取当前拿着的 ItemData
                // 请根据你 InteractionSystem 的实际变量名替换 GetCarriedItemData()
                ItemData carriedItem = interaction.GetCarriedItemData(); 
                
                if (carriedItem != null)
                {
                    EnterInventoryMode(carriedItem);
                    return;
                }
            }

            // 如果没拿东西，正常打开空背包
            EnterInventoryMode(null);
        }

        return;
    }

    // 仅允许Enter退出背包
    if (inventoryActive && Input.GetKeyDown(KeyCode.Return))
    {
        CloseInventoryFromKey();
    }

    UpdateAggressiveFrustumCulling();
}

    void CloseInventoryFromKey()
    {
        InventoryRaycastPlacer placer = GetInventoryPlacer();
        InteractionSystem interaction = GetInteractionSystem();

        if (placer != null && placer.HasActivePreviewItem)
        {
            placer.ForceDropPreviewToWorld();
        }

        SetInventoryActive(false);

        if (interaction == null)
            return;

        interaction.DropCarriedObjectIfAny();
    }

    /// <summary>
    /// 进入背包模式，传入当前收纳物品
    /// </summary>
    public void EnterInventoryMode(ItemData item, Color customColor = default)
    {
        InventoryCameraController primary = GetPrimaryController();
        if (primary != null && primary != this)
        {
            primary.EnterInventoryMode(item, customColor);
            return;
        }

        bool hasExplicitItem = item != null;
        if (hasExplicitItem)
            lastPreviewItem = item;

        SetInventoryActive(true);
        // 通知背包预览系统
        InventoryRaycastPlacer placer = GetInventoryPlacer();
        if (placer == null)
            return;

        if (hasExplicitItem)
            placer.SetPreviewItem(item, customColor);
        else
            placer.ClearPreview();
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
        if (active)
        {
            _openedFrameCount = Time.frameCount;
        }
        if (mainCamera != null) mainCamera.enabled = !active;
        if (inventoryCamera != null) inventoryCamera.enabled = active;
        if (inventoryRoot != null) inventoryRoot.SetActive(active);

        if (active)
            AutoFrameInventoryCamera();

        ApplyCursorState(active);

        if (!active)
        {
            // 退出背包时清除预览并释放临时/未成功放置的物品到世界中
            InventoryRaycastPlacer placer = GetInventoryPlacer();
            if (placer != null)
            {

                placer.ClearPreview();
            }
        }
    }

    public static InventoryCameraController GetPrimaryController()
    {
        if (_primaryController != null && _primaryController.inventoryActive)
            return _primaryController;

        if (_primaryController == null || (_primaryController.mainCamera != null && !_primaryController.mainCamera.enabled))
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
        if (controller.mainCamera != null)
        {
            score += 1;
            if (controller.mainCamera.enabled)
                score += 10; // Prioritize controllers with an active main camera (local player)
        }
        return score;
    }

    void EnsurePrimaryController()
    {
        if (_primaryController == null || (_primaryController.mainCamera != null && !_primaryController.mainCamera.enabled))
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
            inventoryRaycastPlacer = InventoryRaycastPlacer.GetPrimaryPlacer();

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
        // Hard requirement: inventory camera -> unlocked cursor; main camera -> locked cursor.
        Cursor.lockState = inventoryOpen ? CursorLockMode.None : CursorLockMode.Locked;
        Cursor.visible = inventoryOpen;
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

    void UpdateAggressiveFrustumCulling()
    {
        bool shouldCull = aggressivelyCullOutOfViewRenderers && (!cullOnlyWhenInventoryOpen || inventoryActive);
        if (!shouldCull)
        {
            RestoreForcedRenderingIfNeeded();
            return;
        }

        float now = Time.unscaledTime;
        if (_cachedRenderers == null || _cachedRenderers.Length == 0 || now >= _nextCullingCacheRefreshTime)
        {
            RebuildRendererCache();
            _nextCullingCacheRefreshTime = now + Mathf.Max(0.2f, cullingCacheRefreshInterval);
        }

        if (now < _nextCullingTickTime)
            return;

        _nextCullingTickTime = now + Mathf.Max(0.01f, cullingUpdateInterval);

        Camera activeCamera = inventoryActive ? inventoryCamera : mainCamera;
        if (activeCamera == null || !activeCamera.enabled)
        {
            RestoreForcedRenderingIfNeeded();
            return;
        }

        if (_cachedRenderers == null || _cachedRenderers.Length == 0)
            return;

        int rendererCount = _cachedRenderers.Length;
        int batch = Mathf.Clamp(cullingBatchSize, 16, rendererCount);
        Plane[] planes = GeometryUtility.CalculateFrustumPlanes(activeCamera);
        bool forcedAnyOffThisTick = false;
        for (int i = 0; i < batch; i++)
        {
            int rendererIndex = (_cullingCursor + i) % rendererCount;
            Renderer r = _cachedRenderers[rendererIndex];
            if (r == null)
                continue;

            if (!r.gameObject.activeInHierarchy || !r.enabled)
            {
                if (r.forceRenderingOff)
                    r.forceRenderingOff = false;
                continue;
            }

            bool inView = GeometryUtility.TestPlanesAABB(planes, r.bounds);
            bool forceOff = !inView && CanForceHideRenderer(r);

            if (r.forceRenderingOff != forceOff)
                r.forceRenderingOff = forceOff;

            if (forceOff)
                forcedAnyOffThisTick = true;
        }

        _cullingCursor = (_cullingCursor + batch) % rendererCount;
        if (forcedAnyOffThisTick)
            _hasForcedRenderersOff = true;
    }

    bool CanForceHideRenderer(Renderer renderer)
    {
        if (renderer == null || !preserveFrameInfluencingRenderers)
            return true;

        // If renderer can still affect the current frame (for example by casting shadows), keep it visible.
        if (preserveShadowCastingRenderers && renderer.shadowCastingMode != ShadowCastingMode.Off)
            return false;

        // Visible by any camera/pass (reflection/portal/etc.) should not be force-hidden.
        if (renderer.isVisible)
            return false;

        return true;
    }

    void RebuildRendererCache()
    {
        _cachedRenderers = FindObjectsOfType<Renderer>(true);
        _cullingCursor = 0;
    }

    void RestoreForcedRendering()
    {
        if (_cachedRenderers == null)
            return;

        for (int i = 0; i < _cachedRenderers.Length; i++)
        {
            Renderer r = _cachedRenderers[i];
            if (r != null && r.forceRenderingOff)
                r.forceRenderingOff = false;
        }
    }

    void RestoreForcedRenderingIfNeeded()
    {
        if (!_hasForcedRenderersOff)
            return;

        RestoreForcedRendering();
        _hasForcedRenderersOff = false;
    }

    void OnDestroy()
    {
        RestoreForcedRendering();

        if (_primaryController == this)
            _primaryController = null;
    }
}
