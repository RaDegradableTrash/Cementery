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
    private const float DefaultThirdPersonMinPitch = -35f;

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

    [Header("Audio")]
    [SerializeField] private bool manageAudioListener = true;

    [Header("Perspective")]
    [SerializeField] private KeyCode perspectiveToggleKey = KeyCode.V;
    [SerializeField] private bool startInThirdPerson = true;
    [SerializeField] private float thirdPersonDistance = 6.5f;
    [SerializeField] private float thirdPersonMinDistance = 3.5f;
    [SerializeField] private float thirdPersonMaxDistance = 10f;
    [SerializeField] private float thirdPersonZoomSpeed = 2.5f;
    [SerializeField] private float thirdPersonHeight = 1.2f;
    [SerializeField] private float thirdPersonLookAtHeight = 1.1f;
    [SerializeField] private float thirdPersonShoulderOffset = 0f;
    [SerializeField] private float thirdPersonDefaultPitch = 32f;
    [SerializeField] private float thirdPersonMinPitch = DefaultThirdPersonMinPitch;
    [SerializeField] private float thirdPersonMaxPitch = 58f;
    [SerializeField] private float thirdPersonPositionSharpness = 9f;
    [SerializeField] private float thirdPersonRotationSharpness = 14f;
    [SerializeField] private float thirdPersonMaxRoll = 8f;
    [SerializeField] private float thirdPersonRollFromMouse = 0.45f;
    [SerializeField] private float thirdPersonRollReturnSharpness = 7f;
    [SerializeField] private float thirdPersonCollisionRadius = 0.28f;
    [SerializeField] private float thirdPersonCollisionPadding = 0.12f;
    [SerializeField] private LayerMask thirdPersonCollisionMask = ~0;
    [SerializeField] private QueryTriggerInteraction thirdPersonTriggerInteraction = QueryTriggerInteraction.Ignore;
    [SerializeField] private bool showPlayerModelInThirdPerson = true;

    public bool IsThirdPersonActive => _thirdPersonActive;
    public Transform PlayerTarget => player;

    public void SetupCamera(Transform newPlayer, Transform newCameraHolder)
    {
        player = newPlayer;
        cameraHolder = newCameraHolder;
        _firstPersonParent = cameraHolder;
        
        transform.SetParent(cameraHolder);
        transform.localPosition = Vector3.zero;
        transform.localRotation = Quaternion.identity;
        InvalidateAppliedLookCache();

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
        CachePlayerRenderers();
        NormalizeThirdPersonSettings();
        InitializeThirdPersonOrbitPitch();
        ApplyPerspectiveImmediate();
    }

    public void ForceFirstPersonView()
    {
        EnsureFirstPersonParent();
        if (!_thirdPersonActive)
        {
            RestoreFirstPersonTransform();
            return;
        }

        _thirdPersonActive = false;
        RestoreFirstPersonTransform();
        ApplyPlayerRendererVisibility();
    }

    public void ResetRotation()
    {
        _pitch = transform.localEulerAngles.x;
        if (_pitch > 180f) _pitch -= 360f;
        if (player != null)
        {
            _yaw = player.eulerAngles.y;
        }
        InvalidateAppliedLookCache();
    }

    /// <summary>
/// 设置镜头的基础朝向（例如上床或上车时同步方向）
/// </summary>
public void SetBaseRotation(Quaternion targetRotation)
{
    // 提取目标旋转的 Y 轴角度（水平方向）
    float targetYAngle = targetRotation.eulerAngles.y;
    
    // 假设你的 MouseLook 维护水平旋转的变量叫 m_CharacterTargetRot 或 xRotation
    // 这里将其强制同步为目标角度，防止视角计算叠加导致闪回
    // 示例（如果使用的是 Euler 角度数值）：
    // rotationX = targetYAngle; 
    // rotationY = 0f; // 躺下时视线水平看前方
    
    // 如果你的 MouseLook 是直接控制 transform 的：
    transform.rotation = targetRotation;
    InvalidateAppliedLookCache();
    _yaw = targetYAngle;
    _pitch = 0f;
    
    // 💡 记得同时重置你脚本内部用于累加鼠标输入的 pitch 和 yaw（或 lookAngles）变量！
    // 例如：
    // _currentYaw = targetYAngle;
    // _currentPitch = 0f;
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
    private float _thirdPersonPitch;
    private float _thirdPersonRoll;
    private float _lastAppliedPitch = float.NaN;
    private float _lastAppliedYaw = float.NaN;
    private int _inventoryModeCacheFrame = -1;
    private bool _cachedInventoryModeActive;
    private bool _thirdPersonActive;
    private Renderer[] _playerRenderers;
    private readonly RaycastHit[] _thirdPersonCollisionHits = new RaycastHit[8];
    private Camera _ownCamera;
    private AudioListener _ownAudioListener;
    private Transform _firstPersonParent;
    private float _nextAudioListenerSyncTime;
    private const float AudioListenerSyncInterval = 0.5f;

    private void InvalidateAppliedLookCache()
    {
        _lastAppliedPitch = float.NaN;
        _lastAppliedYaw = float.NaN;
    }

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

        _ownCamera = GetComponent<Camera>();
        _ownAudioListener = GetComponent<AudioListener>();
        _firstPersonParent = transform.parent;

        _pitch = transform.eulerAngles.x;
        if (_pitch > 180f) _pitch -= 360f;

        ResolvePlayerReferences();

        if (cameraHolder != null)
            _holderDefaultLocalPos = cameraHolder.localPosition;

        ResolveAttractPivot();
        InitializeAttractOrbitBaseline();
        CachePlayerRenderers();
        NormalizeThirdPersonSettings();
        _thirdPersonActive = startInThirdPerson || ShouldDefaultToThirdPerson();
        InitializeThirdPersonOrbitPitch();
        ApplyPerspectiveImmediate();
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
        {
            EnsureFirstPersonParent();
            RestoreFirstPersonTransform();
            return;
        }

        HandleCursorToggle();
        // 联机控制：如果网络已启动且你不是主人，则禁止旋转
        bool isNetworkActive = NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening;
        if (isNetworkActive && (_playerController != null && (!_playerController.IsSpawned || !_playerController.IsOwner))) return;

        HandlePerspectiveToggle();
        ApplyMouseLook();
    }

    // LateUpdate: runs after CharacterController.Move() — apply bob to CameraHolderEmpty.
    void LateUpdate()
    {
        if (PlayerDeathFlowController.IsPlayerDead || GameMenuManager.IsMenuOpen || IsInventoryModeActive())
            return;

        SyncOwnedAudioListener(false);
        ResolvePlayerReferences();

        if (cameraHolder != null)
        {
            Vector3 bob = _playerController != null
                ? _playerController.BobOffset
                : Vector3.zero;

            cameraHolder.localPosition = _holderDefaultLocalPos + bob;
        }

        if (ShouldUseAttractOrbit())
            ApplyAttractOrbit();
        else if (_thirdPersonActive)
        {
            ApplyPlayerRendererVisibility();
            ApplyThirdPersonCamera(false);
        }
        else
            RestoreFirstPersonTransform();
    }



    // ── Look ──────────────────────────────────────────────────────────────────
    void ApplyMouseLook()
    {
        if (suspendMouseLook) return;

        float mouseX = (useRawMouseInput ? Input.GetAxisRaw("Mouse X") : Input.GetAxis("Mouse X")) * sensitivityX;
        float mouseY = (useRawMouseInput ? Input.GetAxisRaw("Mouse Y") : Input.GetAxis("Mouse Y")) * sensitivityY;

        if (!ShouldUseAttractOrbit() && _thirdPersonActive)
        {
            ApplyThirdPersonLookDelta(mouseX, mouseY);
            ApplyThirdPersonZoom();
            return;
        }


        // Vertical pitch
        _pitch -= mouseY;
        _pitch = Mathf.Clamp(_pitch, minVertical, maxVertical);
        
        if (!ShouldUseAttractOrbit() && !_thirdPersonActive)
        {
            if (!Mathf.Approximately(_lastAppliedPitch, _pitch))
            {
                transform.localRotation = Quaternion.Euler(_pitch, 0f, 0f);
                _lastAppliedPitch = _pitch;
            }
        }

        // Horizontal yaw — rotates the player body so movement stays aligned with the view
        if (player != null)
        {
            _yaw += mouseX;
            if (!Mathf.Approximately(_lastAppliedYaw, _yaw))
            {
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
                _lastAppliedYaw = _yaw;
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

    void HandlePerspectiveToggle()
    {
        KeyCode toggleKey = GetPerspectiveToggleKey();
        if (toggleKey == KeyCode.None || !Input.GetKeyDown(toggleKey))
            return;

        _thirdPersonActive = !_thirdPersonActive;
        ApplyPlayerRendererVisibility();

        if (_thirdPersonActive)
        {
            InitializeThirdPersonOrbitPitch();
            ApplyThirdPersonCamera(true);
        }
        else
        {
            EnsureFirstPersonParent();
            RestoreFirstPersonTransform();
        }

        SyncOwnedAudioListener(true);
    }

    void ApplyPerspectiveImmediate()
    {
        ApplyPlayerRendererVisibility();
        if (_thirdPersonActive)
        {
            InitializeThirdPersonOrbitPitch();
            ApplyThirdPersonCamera(true);
        }
        else
        {
            EnsureFirstPersonParent();
            RestoreFirstPersonTransform();
        }

        SyncOwnedAudioListener(true);
    }

    KeyCode GetPerspectiveToggleKey()
    {
        return perspectiveToggleKey == KeyCode.None ? KeyCode.V : perspectiveToggleKey;
    }

    void ApplyThirdPersonLookDelta(float mouseX, float mouseY)
    {
        _yaw += mouseX;
        _thirdPersonPitch = Mathf.Clamp(
            _thirdPersonPitch - mouseY,
            Mathf.Min(thirdPersonMinPitch, thirdPersonMaxPitch),
            Mathf.Max(thirdPersonMinPitch, thirdPersonMaxPitch));
        _thirdPersonRoll = Mathf.Clamp(
            _thirdPersonRoll - mouseX * thirdPersonRollFromMouse,
            -Mathf.Abs(thirdPersonMaxRoll),
            Mathf.Abs(thirdPersonMaxRoll));
    }

    void ApplyThirdPersonZoom()
    {
        float scroll = Input.mouseScrollDelta.y;
        if (Mathf.Approximately(scroll, 0f))
            return;

        thirdPersonDistance = Mathf.Clamp(
            thirdPersonDistance - scroll * thirdPersonZoomSpeed,
            Mathf.Min(thirdPersonMinDistance, thirdPersonMaxDistance),
            Mathf.Max(thirdPersonMinDistance, thirdPersonMaxDistance));
    }

    void NormalizeThirdPersonSettings()
    {
        if (thirdPersonDistance <= 0.01f)
            thirdPersonDistance = 6.5f;
        if (thirdPersonMinDistance <= 0.01f)
            thirdPersonMinDistance = 3.5f;
        if (thirdPersonMaxDistance <= thirdPersonMinDistance)
            thirdPersonMaxDistance = Mathf.Max(thirdPersonMinDistance + 0.5f, 10f);
        thirdPersonDistance = Mathf.Clamp(thirdPersonDistance, thirdPersonMinDistance, thirdPersonMaxDistance);
        if (thirdPersonZoomSpeed <= 0.01f)
            thirdPersonZoomSpeed = 2.5f;
        if (thirdPersonLookAtHeight <= 0.01f)
            thirdPersonLookAtHeight = 1.1f;
        if (thirdPersonDefaultPitch <= 0.01f)
            thirdPersonDefaultPitch = 32f;
        if (thirdPersonMinPitch > -0.01f)
            thirdPersonMinPitch = DefaultThirdPersonMinPitch;
        if (thirdPersonMaxPitch <= 0.01f)
        {
            thirdPersonMaxPitch = 58f;
        }
        if (thirdPersonPositionSharpness <= 0.01f)
            thirdPersonPositionSharpness = 14f;
        if (thirdPersonRotationSharpness <= 0.01f)
            thirdPersonRotationSharpness = 18f;
        if (thirdPersonMaxRoll <= 0.01f)
            thirdPersonMaxRoll = 8f;
        if (thirdPersonRollFromMouse <= 0.01f)
            thirdPersonRollFromMouse = 0.45f;
        if (thirdPersonRollReturnSharpness <= 0.01f)
            thirdPersonRollReturnSharpness = 7f;
        if (thirdPersonCollisionRadius <= 0.01f)
            thirdPersonCollisionRadius = 0.28f;
        if (thirdPersonCollisionPadding <= 0.01f)
            thirdPersonCollisionPadding = 0.12f;
    }

    void InitializeThirdPersonOrbitPitch()
    {
        float minPitch = Mathf.Min(thirdPersonMinPitch, thirdPersonMaxPitch);
        float maxPitch = Mathf.Max(thirdPersonMinPitch, thirdPersonMaxPitch);
        if (_thirdPersonPitch < minPitch || _thirdPersonPitch > maxPitch)
            _thirdPersonPitch = Mathf.Clamp(thirdPersonDefaultPitch, minPitch, maxPitch);
    }

    void ApplyThirdPersonCamera(bool snap)
    {
        ResolvePlayerReferences();
        if (player == null)
            return;

        if (transform.parent != null)
            transform.SetParent(null, true);

        Transform reference = player;
        float minPitch = Mathf.Min(thirdPersonMinPitch, thirdPersonMaxPitch);
        float maxPitch = Mathf.Max(thirdPersonMinPitch, thirdPersonMaxPitch);
        float orbitPitch = Mathf.Clamp(_thirdPersonPitch, minPitch, maxPitch);
        float distance = Mathf.Clamp(
            thirdPersonDistance,
            Mathf.Min(thirdPersonMinDistance, thirdPersonMaxDistance),
            Mathf.Max(thirdPersonMinDistance, thirdPersonMaxDistance));
        float height = Mathf.Clamp(thirdPersonHeight, 0f, 3f);
        _thirdPersonRoll = Mathf.Lerp(
            _thirdPersonRoll,
            0f,
            1f - Mathf.Exp(-thirdPersonRollReturnSharpness * Mathf.Max(0f, Time.deltaTime)));

        Vector3 lookTarget = reference.position + Vector3.up * Mathf.Max(0.1f, thirdPersonLookAtHeight);
        Quaternion orbitRotation = Quaternion.Euler(orbitPitch, _yaw, 0f);
        Vector3 orbitOffset = orbitRotation * Vector3.back * distance;
        Vector3 shoulder = Quaternion.Euler(0f, _yaw, 0f) * Vector3.right * thirdPersonShoulderOffset;
        Vector3 desiredPosition = lookTarget + orbitOffset + Vector3.up * height + shoulder;
        Vector3 correctedPosition = ResolveThirdPersonCollision(lookTarget, desiredPosition);
        Vector3 lookDirection = lookTarget - correctedPosition;
        if (lookDirection.sqrMagnitude < 0.001f)
            lookDirection = reference.forward;

        Quaternion desiredRotation = Quaternion.LookRotation(lookDirection.normalized, Vector3.up)
            * Quaternion.Euler(0f, 0f, _thirdPersonRoll);

        if (snap || Time.deltaTime <= 0f)
        {
            transform.position = correctedPosition;
            transform.rotation = desiredRotation;
            return;
        }

        float positionLerp = 1f - Mathf.Exp(-thirdPersonPositionSharpness * Time.deltaTime);
        float rotationLerp = 1f - Mathf.Exp(-thirdPersonRotationSharpness * Time.deltaTime);
        transform.position = Vector3.Lerp(transform.position, correctedPosition, positionLerp);
        transform.rotation = Quaternion.Slerp(transform.rotation, desiredRotation, rotationLerp);
    }

    Vector3 ResolveThirdPersonCollision(Vector3 pivot, Vector3 desiredPosition)
    {
        Vector3 toCamera = desiredPosition - pivot;
        float distance = toCamera.magnitude;
        if (distance <= 0.001f)
            return desiredPosition;

        Vector3 direction = toCamera / distance;
        int hitCount = Physics.SphereCastNonAlloc(
            pivot,
            Mathf.Max(0.01f, thirdPersonCollisionRadius),
            direction,
            _thirdPersonCollisionHits,
            distance,
            thirdPersonCollisionMask,
            thirdPersonTriggerInteraction);

        float nearest = distance;
        for (int i = 0; i < hitCount; i++)
        {
            RaycastHit hit = _thirdPersonCollisionHits[i];
            if (hit.collider == null || (player != null && hit.collider.transform.IsChildOf(player)))
                continue;

            nearest = Mathf.Min(nearest, hit.distance);
        }

        if (nearest >= distance)
            return desiredPosition;

        return pivot + direction * Mathf.Max(0.05f, nearest - thirdPersonCollisionPadding);
    }

    void RestoreFirstPersonTransform()
    {
        if (cameraHolder == null)
            return;

        EnsureFirstPersonParent();
        transform.localPosition = Vector3.zero;
        if (!ShouldUseAttractOrbit())
            transform.localRotation = Quaternion.Euler(_pitch, 0f, 0f);
    }

    void CachePlayerRenderers()
    {
        _playerRenderers = player != null ? player.GetComponentsInChildren<Renderer>(true) : null;
    }

    void ApplyPlayerRendererVisibility()
    {
        if (!showPlayerModelInThirdPerson && !_thirdPersonActive)
            return;

        if (_playerRenderers == null || _playerRenderers.Length == 0)
            CachePlayerRenderers();

        if (_playerRenderers == null)
            return;

        UnityEngine.Rendering.ShadowCastingMode mode = _thirdPersonActive
            ? UnityEngine.Rendering.ShadowCastingMode.On
            : UnityEngine.Rendering.ShadowCastingMode.ShadowsOnly;

        for (int i = 0; i < _playerRenderers.Length; i++)
        {
            Renderer renderer = _playerRenderers[i];
            if (renderer != null)
                renderer.shadowCastingMode = mode;
        }
    }

    void SyncOwnedAudioListener(bool force)
    {
        if (!manageAudioListener)
            return;

        if (!force && Time.unscaledTime < _nextAudioListenerSyncTime)
            return;

        _nextAudioListenerSyncTime = Time.unscaledTime + AudioListenerSyncInterval;

        if (_ownCamera == null)
            _ownCamera = GetComponent<Camera>();
        if (_ownAudioListener == null)
            _ownAudioListener = GetComponent<AudioListener>();

        if (_ownCamera == null || _ownAudioListener == null || !_ownCamera.enabled || !_ownCamera.gameObject.activeInHierarchy)
            return;

        SetExclusiveAudioListener(_ownAudioListener);
    }

    public static void SetExclusiveAudioListener(AudioListener activeListener)
    {
        if (activeListener == null)
            return;

        AudioListener[] listeners = FindObjectsByType<AudioListener>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        for (int i = 0; i < listeners.Length; i++)
        {
            AudioListener listener = listeners[i];
            if (listener == null)
                continue;

            bool shouldEnable = listener == activeListener && listener.gameObject.activeInHierarchy;
            if (listener.enabled != shouldEnable)
                listener.enabled = shouldEnable;
        }
    }

    void ResolvePlayerReferences()
    {
        if (player == null)
        {
            PlayerController controller = GetComponentInParent<PlayerController>();
            if (controller == null)
                controller = FindObjectOfType<PlayerController>();

            if (controller != null)
                player = controller.transform;
        }

        if (cameraHolder == null)
        {
            if (_firstPersonParent != null)
                cameraHolder = _firstPersonParent;
            else if (player != null)
                cameraHolder = FindChildByName(player, "CameraHolderEmpty");
            else if (transform.parent != null)
                cameraHolder = transform.parent;
        }

        if (_firstPersonParent == null && cameraHolder != null)
            _firstPersonParent = cameraHolder;

        if (player != null)
        {
            if (_playerController == null)
                _playerController = player.GetComponent<PlayerController>();
            if (_playerRb == null)
                _playerRb = player.GetComponent<Rigidbody>();
            if (_playerRenderers == null || _playerRenderers.Length == 0)
                CachePlayerRenderers();
        }
    }

    void EnsureFirstPersonParent()
    {
        if (cameraHolder == null)
            ResolvePlayerReferences();

        if (cameraHolder != null && transform.parent != cameraHolder)
            transform.SetParent(cameraHolder, true);
    }

    bool ShouldDefaultToThirdPerson()
    {
        return player != null && !ShouldUseAttractOrbit();
    }

    static Transform FindChildByName(Transform root, string childName)
    {
        if (root == null || string.IsNullOrEmpty(childName))
            return null;

        if (root.name == childName)
            return root;

        for (int i = 0; i < root.childCount; i++)
        {
            Transform match = FindChildByName(root.GetChild(i), childName);
            if (match != null)
                return match;
        }

        return null;
    }

    void HandleCursorToggle()
    {
        // If the menu was closed on this exact frame, skip cursor releasing
        if (Time.frameCount == GameMenuManager.ClosedFrameCount) return;

        if (Input.GetKeyDown(KeyCode.Escape) || Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter))
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
        int frame = Time.frameCount;
        if (_inventoryModeCacheFrame == frame)
            return _cachedInventoryModeActive;

        InventoryCameraController primary = InventoryCameraController.GetPrimaryController();
        if (primary != null)
            inventoryCameraController = primary;
        else if (inventoryCameraController == null)
            inventoryCameraController = FindObjectOfType<InventoryCameraController>();

        _cachedInventoryModeActive = inventoryCameraController != null && inventoryCameraController.IsInventoryActive;
        _inventoryModeCacheFrame = frame;
        return _cachedInventoryModeActive;
    }
}
