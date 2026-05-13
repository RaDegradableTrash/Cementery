using UnityEngine;
using Unity.Netcode;

/// <summary>
/// First-person camera controller. Attach to Main Camera.
///
/// Hierarchy expected:
///   PlayerEmpty
///   └── CameraHolderEmpty      ← assign to "cameraHolder"
///       └── Main Camera        ← this script lives here
///
/// PlayerEmpty handles horizontal rotation (yaw).
/// Main Camera handles vertical rotation (pitch).
/// CameraHolderEmpty localPosition is nudged for head bob.
/// </summary>
public class MouseLook : MonoBehaviour
{
    [Header("References")]
    [Tooltip("Root player transform (PlayerEmpty). Receives horizontal yaw.")]
    [SerializeField] private Transform player;
    [Tooltip("CameraHolderEmpty — its localPosition is animated for head bob.")]
    [SerializeField] private Transform cameraHolder;

    [Header("Attract Orbit")]
    [Tooltip("Camera will orbit around and look at this pivot while pitching.")]
    [SerializeField] private Transform attractPivot;
    [SerializeField] private bool useAttractOrbit = false;

    [Header("Sensitivity")]
    [SerializeField] private float sensitivityX = 2f;
    [SerializeField] private float sensitivityY = 2f;
    [Tooltip("Use raw mouse input for immediate camera response.")]
    [SerializeField] private bool useRawMouseInput = true;

    [Header("Vertical Clamp (degrees)")]
    [SerializeField] private float minVertical = -80f;
    [SerializeField] private float maxVertical =  80f;

    [Header("Inventory")]
    [SerializeField] private InventoryCameraController inventoryCameraController;

    [Header("Cursor")]
    [SerializeField] private bool autoLockCursorOnStart = true;

    public void SetupCamera(Transform newPlayer, Transform newCameraHolder)
    {
        player = newPlayer;
        cameraHolder = newCameraHolder;
        
        transform.SetParent(cameraHolder);
        transform.localPosition = Vector3.zero;
        transform.localRotation = Quaternion.identity;

        _pitch = 0f;
        
        if (player != null)
        {
            _playerController = player.GetComponent<PlayerController>();
            _playerRb = player.GetComponent<Rigidbody>();
            _yaw = player.eulerAngles.y;
        }

        if (cameraHolder != null)
            _holderDefaultLocalPos = cameraHolder.localPosition;

        ResolveAttractPivot();
        InitializeAttractOrbitBaseline();
    }



    // ── State ─────────────────────────────────────────────────────────────────
    [HideInInspector] public bool suspendMouseLook = false;
    private float _pitch;
    private PlayerController _playerController;
    private Vector3 _holderDefaultLocalPos;
    private Vector3 _attractBaseLocalOffset;
    private bool _attractOrbitInitialized;
    private bool _pendingStartCursorLock;
    private Rigidbody _playerRb;
    private float _yaw;

    // ── Lifecycle ─────────────────────────────────────────────────────────────
    void Start()
    {
        if (autoLockCursorOnStart)
        {
            LockCursor();
            // Safety lock on first Update so start-up order cannot leave cursor unlocked.
            _pendingStartCursorLock = true;
        }

        if (inventoryCameraController == null)
            inventoryCameraController = InventoryCameraController.GetPrimaryController();
        if (inventoryCameraController == null)
            inventoryCameraController = FindObjectOfType<InventoryCameraController>();

        _pitch = transform.eulerAngles.x;
        if (_pitch > 180f) _pitch -= 360f;

        if (player != null)
        {
            _playerController = player.GetComponent<PlayerController>();
            _playerRb = player.GetComponent<Rigidbody>();
            _yaw = player.eulerAngles.y;
        }

        if (cameraHolder != null)
            _holderDefaultLocalPos = cameraHolder.localPosition;

        ResolveAttractPivot();
        InitializeAttractOrbitBaseline();
    }

    void Update()
    {
        // 死亡期间或者如果菜单打开，不处理任何逻辑
        if (PlayerDeathFlowController.IsPlayerDead || GameMenuManager.IsMenuOpen) return;

        if (_pendingStartCursorLock)
        {
            _pendingStartCursorLock = false;
            if (!IsInventoryModeActive())
                LockCursor();
        }

        if (IsInventoryModeActive())
            return;

        HandleCursorToggle();
        // 联机控制：如果网络已启动且你不是主人，则禁止旋转
        bool isNetworkActive = NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening;
        if (isNetworkActive && (_playerController != null && (!_playerController.IsSpawned || !_playerController.IsOwner))) return;

        ApplyMouseLook();
    }

    // LateUpdate: runs after CharacterController.Move() — apply bob to CameraHolderEmpty.
    void LateUpdate()
    {
        if (PlayerDeathFlowController.IsPlayerDead || GameMenuManager.IsMenuOpen || IsInventoryModeActive())
            return;

        if (cameraHolder == null) return;

        Vector3 bob = _playerController != null
            ? _playerController.BobOffset
            : Vector3.zero;

        cameraHolder.localPosition = _holderDefaultLocalPos + bob;

        if (ShouldUseAttractOrbit())
            ApplyAttractOrbit();
    }



    // ── Look ──────────────────────────────────────────────────────────────────
    void ApplyMouseLook()
    {
        if (suspendMouseLook) return;

        float mouseX = (useRawMouseInput ? Input.GetAxisRaw("Mouse X") : Input.GetAxis("Mouse X")) * sensitivityX;
        float mouseY = (useRawMouseInput ? Input.GetAxisRaw("Mouse Y") : Input.GetAxis("Mouse Y")) * sensitivityY;



        // Vertical pitch
        _pitch -= mouseY;
        _pitch = Mathf.Clamp(_pitch, minVertical, maxVertical);
        
        if (!ShouldUseAttractOrbit())
        {
            transform.localRotation = Quaternion.Euler(_pitch, 0f, 0f);
        }

        // Horizontal yaw — rotates the player body so movement stays aligned with the view
        if (player != null)
        {
            _yaw += mouseX;
            Quaternion targetRot = Quaternion.Euler(0f, _yaw, 0f);
            if (_playerRb != null)
            {
                _playerRb.rotation = targetRot;
                // setting Rigidbody.rotation updates physics state immediately, preventing interpolation fighting
            }
            else
            {
                player.rotation = targetRot;
            }
        }
    }

    bool ShouldUseAttractOrbit()
    {
        return useAttractOrbit && attractPivot != null;
    }

    void ResolveAttractPivot()
    {
        if (attractPivot != null)
            return;

        if (player != null)
            attractPivot = FindChildByName(player, "Attract");

        if (attractPivot == null)
            attractPivot = FindChildByName(transform.root, "Attract");
    }

    void InitializeAttractOrbitBaseline()
    {
        if (!ShouldUseAttractOrbit())
            return;

        Transform orbitRef = GetOrbitReferenceTransform();
        Vector3 baseOffsetWorld = transform.position - attractPivot.position;
        if (baseOffsetWorld.sqrMagnitude < 0.0001f)
        {
            Vector3 fallbackForward = Vector3.ProjectOnPlane(orbitRef.forward, Vector3.up);
            if (fallbackForward.sqrMagnitude < 0.0001f)
                fallbackForward = Vector3.forward;

            baseOffsetWorld = -fallbackForward.normalized * 1.5f + Vector3.up * 0.15f;
        }

        Vector3 localOffset = orbitRef.InverseTransformDirection(baseOffsetWorld);
        _attractBaseLocalOffset = Quaternion.AngleAxis(-_pitch, Vector3.right) * localOffset;
        if (_attractBaseLocalOffset.sqrMagnitude < 0.0001f)
            _attractBaseLocalOffset = new Vector3(0f, 0f, -1.5f);

        _attractOrbitInitialized = true;
    }

    Transform GetOrbitReferenceTransform()
    {
        if (player != null)
            return player;

        if (cameraHolder != null && cameraHolder.parent != null)
            return cameraHolder.parent;

        return transform.parent != null ? transform.parent : transform;
    }

    void ApplyAttractOrbit()
    {
        if (!_attractOrbitInitialized)
            InitializeAttractOrbitBaseline();

        Transform orbitRef = GetOrbitReferenceTransform();
        Vector3 localOffset = Quaternion.AngleAxis(_pitch, Vector3.right) * _attractBaseLocalOffset;
        if (localOffset.sqrMagnitude < 0.0001f)
            localOffset = new Vector3(0f, 0f, -1f);

        Vector3 worldOffset = orbitRef.TransformDirection(localOffset);
        transform.position = attractPivot.position + worldOffset;

        Vector3 lookDir = attractPivot.position - transform.position;
        if (lookDir.sqrMagnitude > 0.0001f)
            transform.rotation = Quaternion.LookRotation(lookDir.normalized, Vector3.up);
    }

    static Transform FindChildByName(Transform root, string childName)
    {
        if (root == null || string.IsNullOrEmpty(childName))
            return null;

        Transform[] all = root.GetComponentsInChildren<Transform>(true);
        for (int i = 0; i < all.Length; i++)
        {
            Transform t = all[i];
            if (t != null && t.name == childName)
                return t;
        }

        return null;
    }

    // ── Cursor ────────────────────────────────────────────────────────────────
    void HandleCursorToggle()
    {
        if (Input.GetKeyDown(KeyCode.Escape))
            ReleaseCursor();
        else if (Input.GetMouseButtonDown(0) && Cursor.lockState != CursorLockMode.Locked)
            LockCursor();
    }

    void LockCursor()
    {
        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible   = false;
    }

    void ReleaseCursor()
    {
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible   = true;
    }

    bool IsInventoryModeActive()
    {
        InventoryCameraController primary = InventoryCameraController.GetPrimaryController();
        if (primary != null)
            inventoryCameraController = primary;
        else if (inventoryCameraController == null)
            inventoryCameraController = FindObjectOfType<InventoryCameraController>();

        return inventoryCameraController != null && inventoryCameraController.IsInventoryActive;
    }
}
