using UnityEngine;
using Unity.Netcode;

/// <summary>
/// First-person player controller. Requires a Rigidbody and CapsuleCollider component.
/// Supports: WASD movement, sprinting (Left Shift), jump force, physics-gravity falling, head bobbing.
/// </summary>
[RequireComponent(typeof(Rigidbody))]
[RequireComponent(typeof(CapsuleCollider))]
public class PlayerController : NetworkBehaviour
{
    [Header("Movement")]
    public float walkSpeed = 4f;
    public float sprintSpeed = 7f;

    [Header("Health")]
    public int hp = 10;
    public int maxHp = 10;

    [Header("Input")]
    [Tooltip("Use raw axis values for immediate response (recommended for low input latency).")]
    [SerializeField] private bool useRawMovementInput = true;
    [Range(0f, 0.25f)]
    [Tooltip("Small dead zone to avoid tiny stick/keyboard noise when using raw input.")]
    [SerializeField] private float movementInputDeadZone = 0.01f;

    [Header("Inventory")]
    [SerializeField] private InventoryCameraController inventoryCameraController;

    [Header("Jump & Fall")]
    [Tooltip("Initial upward velocity applied when jumping.")]
    public float jumpForce = 7f;
    [Tooltip("Maximum number of jumps allowed before touching ground (e.g., 2 for double jump).")]
    public int maxJumps = 2;

    [Header("Head Bob")]
    [SerializeField] private float bobFrequency = 1.8f;
    [SerializeField] private float bobAmplitude = 0.06f;

    [Header("Pushing")]
    [Tooltip("Maximum horizontal speed applied to pushable Rigidbodies when the player walks into them. " +
             "The push is clamped and never adds torque or upward impulse.")]
    [SerializeField] private float pushForce = 2f;

    /// <summary>
    /// Additive eye-space offset produced by head bobbing.
    /// MouseLook reads this each LateUpdate to nudge the camera.
    /// </summary>
    public Vector3 BobOffset { get; private set; }

    /// <summary>
    /// Global multiplier for movement speed. Managed by external systems (e.g., InteractionSystem) when dragging heavy objects.
    /// </summary>
    public float SpeedMultiplier { get; set; } = 1f;

    [Header("Climbing System")]
    [SerializeField] private float climbMaxHeight = 2.5f;
    [SerializeField] private LayerMask climbObstacleMask = ~0;


    [Header("Animation")]
    [SerializeField] private Animator playerAnimator;
    [SerializeField] private string idleAnimState = "Idle";
    [SerializeField] private string forwardMoveAnimState = "MoveForward";
    private bool _isMovingForwardAnim = false;
    [SerializeField] private MouseLook mouseLook;

    private Collider _climbCandidateCol;
    private float _climbCandidateTime;
    private bool _canClimbThisJump = true;

    // ── Internal State ──────────────────────────────────────────────────────
    private Rigidbody _rb;
    private CapsuleCollider _col;
    private PlayerStamina _stamina;
    private Collider[] _selfColliders;

    private const float JumpBufferTime = 0.12f;
    private const float CoyoteTime = 0.08f;

    private float _bobTimer;
    private bool _isClimbing = false;
    private float _activeClimbTargetY = 0f;
    private float _climbStartTime = 0f;

    private float _struggleHeightGained = 0f;
    private float _groundCheckDisabledUntil;
    private bool _isGrounded;
    private float _jumpBufferedUntil;
    private bool _isOnStairs;
    private Vector3 _stairsContactNormal = Vector3.up;

    private Vector2 _inputMove;
    private int _jumpCount;
    private Rigidbody _activePlatform;
    private Vector3 _lastPlatformPos;

    [Header("Ground Check Settings")]
    public float groundCheckOffset = 0.15f;
    public float groundCheckRadius = 0.3f;
    public LayerMask groundMask = ~0;
    public int groundCheckInterval = 2;
    private int _groundCheckFrameCounter;
    [SerializeField] private bool logJumpPeak = false;
    private const int GroundHitBufferSize = 16;
    private readonly RaycastHit[] _groundHitBuffer = new RaycastHit[GroundHitBufferSize];
    private readonly RaycastHit[] _groundFallbackHitBuffer = new RaycastHit[GroundHitBufferSize];
    private const int ClimbHitBufferSize = 16;
    private readonly RaycastHit[] _climbHitBuffer = new RaycastHit[ClimbHitBufferSize];
    private bool _hasSetupKinematic = false;
    private float _startupTime;
    private int _inventoryModeCacheFrame = -1;
    private bool _cachedInventoryModeActive;

    // 🌟 核心新增：控制玩家当前是否因使用家具（躺下/坐下）而全面冻结控制器逻辑
    public bool IsUsingFurniture { get; set; } = false;

    // ── Lifecycle ────────────────────────────────────────────────────────────
    void Awake()
    {
        // 1. 获取核心物理与状态组件
        _rb = GetComponent<Rigidbody>();
        _col = GetComponent<CapsuleCollider>();
        _stamina = GetComponent<PlayerStamina>();
        _selfColliders = GetComponentsInChildren<Collider>(true);

        // 2. 强力清除任何外部干扰脚本（特别是导致定身的 KinematicProp）
        var kp = GetComponent<EnvironmentSystem.KinematicProp>();
        if (kp != null) Destroy(kp);

        // 3. 强制物理引擎接管
        if (_rb != null)
        {
            _rb.freezeRotation = true;
            _rb.useGravity = true;        // 必须开启重力
            _rb.isKinematic = false;      // 🚀 核心：永远不要让玩家初始化为 Kinematic
            _rb.drag = 0f;                // 确保没有额外的阻力限制移动
            _rb.interpolation = RigidbodyInterpolation.Interpolate;
            _rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
        }
        else
        {
            Debug.LogError("[PlayerController] Rigidbody component not found on player!");
        }

        // 4. 初始化标志，确保 Update 不会进入任何挂起逻辑
        _startupTime = Time.time;
        _hasSetupKinematic = true; 

        // 5. 设置无摩擦材质，防止卡墙
        if (_col != null)
        {
            PhysicMaterial pm = new PhysicMaterial("PlayerMaterial") 
            { 
                dynamicFriction = 0f, 
                staticFriction = 0f, 
                frictionCombine = PhysicMaterialCombine.Minimum 
            };
            _col.material = pm;

            // 忽略自身子物体的碰撞
            for (int i = 0; i < _selfColliders.Length; i++)
            {
                Collider c = _selfColliders[i];
                if (c == null || c == _col) continue;
                Physics.IgnoreCollision(_col, c, true);
            }
        }

        // 6. 摄像机初始化逻辑（保持原样以防破坏相机栈）
        if (inventoryCameraController == null)
            inventoryCameraController = InventoryCameraController.GetPrimaryController();

        Camera[] allCameras = FindObjectsOfType<Camera>(true);
        Camera playerCamera = GetComponentInChildren<Camera>(true);
        foreach (Camera cam in allCameras)
        {
            if (playerCamera != null && cam == playerCamera)
            {
                cam.enabled = true;
                cam.gameObject.SetActive(true);
                cam.tag = "MainCamera";
                continue;
            }
            if (!cam.transform.IsChildOf(transform))
            {
                cam.enabled = false;
                if (cam.CompareTag("MainCamera")) cam.tag = "Untagged";
            }
        }

        if (mouseLook == null && Camera.main != null)
            mouseLook = Camera.main.GetComponent<MouseLook>();

        // 补充初始化 UI
        if (SimpleCircleBar.Instance != null)
            SimpleCircleBar.Instance.UpdateHealthBar(hp, maxHp);
    }

    public override void OnNetworkSpawn()
    {
        if (!IsOwner)
        {
            // 为防止远程客户端进行本地物理模拟，将其设置为Kinematic
            if (_rb != null) _rb.isKinematic = true;

            // 【关键修改】禁用其他玩家的摄像机和耳朵，否则你的屏幕会变成别人的视角！
            Camera playerCam = GetComponentInChildren<Camera>();
            if (playerCam != null)
            {
                playerCam.enabled = false;
                AudioListener listener = playerCam.GetComponent<AudioListener>();
                if (listener != null) listener.enabled = false;
            }
        }
        else
        {
            // Defer unfreezing until terrain is ready
        }
    }

    void Update()
    {
        // 1. 【家具/坐下状态拦截】：无条件切断输入，防止玩家在坐下时移动
        if (IsUsingFurniture)
        {
            _inputMove = Vector2.zero;
            BobOffset = Vector3.Lerp(BobOffset, Vector3.zero, Time.deltaTime * 12f);
            return;
        }

        // 2. 【加载状态防错】：确保初始化逻辑已完成，防止过早进入逻辑导致状态丢失
        if (!_hasSetupKinematic)
        {
            _hasSetupKinematic = true;
            if (_rb != null) _rb.isKinematic = false;
        }

        // 3. 【网络与死锁检查】：如果正在网络加载或本地玩家无效，则跳过后续逻辑
        bool isNetworkActive = NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening;
        if (isNetworkActive && (!IsSpawned || !IsOwner)) return;

        // 4. 【系统级屏蔽】：死亡、菜单打开、或者处于库存操作界面时，不处理移动逻辑
        if (hp <= 0 || PlayerDeathFlowController.IsPlayerDead || GameMenuManager.IsMenuOpen || IsInventoryModeActive())
        {
            _inputMove = Vector2.zero;
            BobOffset = Vector3.Lerp(BobOffset, Vector3.zero, Time.deltaTime * 12f);
            return;
        }

        // 5. 【地面检测与平台移动更新】
        CheckGrounded();

        if (_isGrounded && Time.time > _groundCheckDisabledUntil)
        {
            _jumpCount = 0;
            _isClimbing = false;
            _canClimbThisJump = true;
            _struggleHeightGained = 0f;
            HandlePlatformMovement();
        }
        else
        {
            _isGrounded = false;
            _activePlatform = null;
            
            // 坠落状态处理
            if (_isClimbing && Time.time - _climbStartTime < 1.5f)
            {
                if (transform.position.y >= _activeClimbTargetY)
                {
                    _rb.velocity = new Vector3(_rb.velocity.x, Mathf.Min(_rb.velocity.y, 0f), _rb.velocity.z);
                    _isClimbing = false;
                }
            }
        }

        // 6. 【跳跃逻辑】
        bool jumpPressed = Input.GetButtonDown("Jump") || Input.GetKeyDown(KeyCode.Space);
        if (jumpPressed)
        {
            if (_jumpCount == 0 && _isGrounded)
            {
                ExecuteJump();
            }
        }

        // 7. 【状态更新】：采集输入、处理视觉晃动、计算跳跃高度、刷新动画
        GatherInput();
        HandleHeadBob();
        TrackJumpPeak();
        HandleAnimation();
    }

    private void HandleAnimation()
    {
        if (playerAnimator == null) return;

        // Skip animation logic if dead or menu is open
        if (hp <= 0 || GameMenuManager.IsMenuOpen) return;

        bool isMovingForward = _inputMove.y > 0.1f && _isGrounded;
        
        if (isMovingForward && !_isMovingForwardAnim)
        {
            _isMovingForwardAnim = true;
            playerAnimator.CrossFadeInFixedTime(forwardMoveAnimState, 0.2f);
        }
        else if (!isMovingForward && _isMovingForwardAnim)
        {
            _isMovingForwardAnim = false;
            playerAnimator.CrossFadeInFixedTime(idleAnimState, 0.2f);
        }
    }

    private float _currentJumpPeakY = -Mathf.Infinity;
    private void TrackJumpPeak()
    {
        if (!_isGrounded)
        {
            if (transform.position.y > _currentJumpPeakY)
                _currentJumpPeakY = transform.position.y;
        }
        else if (_currentJumpPeakY > -Mathf.Infinity)
        {
            if (logJumpPeak)
            {
                Debug.Log($"[Jump Peak] Maximum Height Reached: {_currentJumpPeakY:F2}m (Delta: {(_currentJumpPeakY - transform.position.y):F2}m)");
            }
            _currentJumpPeakY = -Mathf.Infinity;
        }
    }

    void FixedUpdate()
    {
        // 🌟 强力拦截 2：如果正在靠着/躺在家具上，物理帧绝对禁止重写速度或应用物理力
        if (IsUsingFurniture)
        {
            if (_rb != null)
            {
                _rb.velocity = Vector3.zero;
                _rb.angularVelocity = Vector3.zero;
            }
            return;
        }

        // 同样的逻辑应用到物理更新
        bool isNetworkActive = NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening;
        if (isNetworkActive && (!IsSpawned || !IsOwner)) return;

        // 如果玩家死亡，停止物理处理和常规移动
        if (hp <= 0 || PlayerDeathFlowController.IsPlayerDead) return;

        // 如果暂停菜单打开，停止物理处理
        if (GameMenuManager.IsMenuOpen) return;

        if (IsInventoryModeActive()) return;

        HandleMovement();
        
        // Clear collision state for the upcoming physics step
        _isTouchingWall = false;
        _wallNormal = Vector3.zero;
    }

    bool IsInventoryModeActive()
    {
        int frame = Time.frameCount;
        if (_inventoryModeCacheFrame == frame)
            return _cachedInventoryModeActive;

        if (inventoryCameraController == null)
            inventoryCameraController = InventoryCameraController.GetPrimaryController();

        _cachedInventoryModeActive = inventoryCameraController != null && inventoryCameraController.IsInventoryActive;
        _inventoryModeCacheFrame = frame;
        return _cachedInventoryModeActive;
    }

    // Movements
    public void ResetVelocity()
    {
        if (_rb != null)
        {
            _rb.velocity = Vector3.zero;
            _rb.angularVelocity = Vector3.zero;
            _rb.freezeRotation = true; // 恢复竖直锁定
            
            // 恢复玩家直立姿态
            transform.rotation = Quaternion.Euler(0f, transform.eulerAngles.y, 0f);
        }
        _jumpCount = 0;

        if (playerAnimator != null)
        {
            _isMovingForwardAnim = false;
            playerAnimator.Play(idleAnimState, 0, 0f);
        }
    }

    public void TakeDamage(int amount, Vector3 sourcePos = default)
    {
        if (hp <= 0) return;
        hp -= amount;
        if (hp <= 0)
        {
            hp = 0;
            Die(sourcePos);
        }

        // 每次 hp 发生改变，就通知 UI 刷新
SimpleCircleBar.Instance.UpdateHealthBar(hp, maxHp);
        Debug.Log($"[PlayerController] Took {amount} damage. Current HP: {hp}");
    }

    private void Die(Vector3 sourcePos)
    {
        // 让真实的玩家变成受击倒下状态
        if (_rb != null)
        {
            _rb.freezeRotation = false; // 解除旋转锁定
            Vector3 pushDir = (transform.position - sourcePos).normalized;
            if (pushDir.sqrMagnitude < 0.01f) pushDir = -transform.forward;
            pushDir.y = 0.5f; 
            _rb.AddForce(pushDir * 12f, ForceMode.Impulse);
            _rb.AddRelativeTorque(Random.insideUnitSphere * 8f, ForceMode.Impulse);
        }

        PlayerDeathFlowController pdf = GetComponent<PlayerDeathFlowController>();
        if (pdf == null) pdf = FindObjectOfType<PlayerDeathFlowController>();
        
        if (pdf != null)
        {
            pdf.TriggerTrapDeathPhase1();
        }
    }

    public Transform SpawnCorpseAndHide()
    {
        // 彻底倒下后生成尸体
        GameObject corpse = Instantiate(gameObject, transform.position, transform.rotation);
        corpse.name = "PlayerCorpse";
        
        // 移除所有活体逻辑组件
        Destroy(corpse.GetComponent<PlayerController>());
        Destroy(corpse.GetComponent<PlayerDeathFlowController>());
        Destroy(corpse.GetComponent<PlayerStamina>());
        
        CharacterController cc = corpse.GetComponent<CharacterController>();
        if (cc != null) Destroy(cc);

        Animator anim = corpse.GetComponent<Animator>();
        if (anim != null) Destroy(anim);

        var netObj = corpse.GetComponent<NetworkObject>();
        if (netObj != null) Destroy(netObj);

        // 确保网格碰撞体为凸面
        foreach (MeshCollider mc in corpse.GetComponentsInChildren<MeshCollider>())
        {
            mc.convex = true;
        }

        // 清理摄像机及音频监听
        foreach (Camera cam in corpse.GetComponentsInChildren<Camera>())
            Destroy(cam.gameObject);
        foreach (AudioListener al in corpse.GetComponentsInChildren<AudioListener>())
            Destroy(al);
        foreach (MouseLook ml in corpse.GetComponentsInChildren<MouseLook>())
            Destroy(ml);

        // 核心修改：强制固定尸体位置，避免物理滚动
        Rigidbody corpseRb = corpse.GetComponent<Rigidbody>();
        if (corpseRb != null)
        {
            corpseRb.isKinematic = true;          // 设置为 Kinematic，使其不受物理力影响
            corpseRb.velocity = Vector3.zero;     // 清除任何遗留速度
            corpseRb.angularVelocity = Vector3.zero; // 清除任何旋转动量
        }

        // 显化尸体外观及阴影
        foreach (Renderer r in corpse.GetComponentsInChildren<Renderer>())
        {
            r.enabled = true;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
        }

        // 隐藏原始玩家角色
        SetPlayerVisible(false);
        return corpse.transform;
    }

    public void SetPlayerVisible(bool visible)
    {
        foreach (Renderer r in GetComponentsInChildren<Renderer>())
        {
            if (visible)
            {
                r.enabled = true;
                // 🌟 核心创意：存活时，玩家肉身对摄像机完全透明（ShadowsOnly），但是依然能透视/投射真实的阴影！
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.ShadowsOnly;
            }
            else
            {
                // 死亡或在车内驾驶隐藏时，Renderer 完全关闭
                r.enabled = false;
            }
        }
        
        if (_col != null) _col.enabled = visible;
        if (_rb != null)
        {
            _rb.isKinematic = !visible;
            if (!visible)
            {
                _rb.velocity = Vector3.zero;
                _rb.angularVelocity = Vector3.zero;
            }
        }
    }

    void GatherInput()
    {
        float h = useRawMovementInput ? Input.GetAxisRaw("Horizontal") : Input.GetAxis("Horizontal");
        float v = useRawMovementInput ? Input.GetAxisRaw("Vertical") : Input.GetAxis("Vertical");

        if (Mathf.Abs(h) < movementInputDeadZone) h = 0f;
        if (Mathf.Abs(v) < movementInputDeadZone) v = 0f;

        _inputMove = new Vector2(h, v);
    }

    void HandleMovement()
    {
        bool wantsSprint = Input.GetKey(KeyCode.LeftShift);
        bool canSprint   = _stamina == null || _stamina.HasStamina;
        bool isSprinting = wantsSprint && canSprint && _inputMove.y > 0.1f;

        if (isSprinting)
            _stamina?.Drain();
        else
            _stamina?.Recover();

        float speed = (isSprinting ? sprintSpeed : walkSpeed) * SpeedMultiplier;

        Vector3 moveDir = transform.right * _inputMove.x + transform.forward * _inputMove.y;
        if (moveDir.sqrMagnitude > 1f)
            moveDir.Normalize();

        Vector3 targetVelocity = moveDir * speed;
        
        // --- Wall Slide Projection ---
        if (_isTouchingWall)
        {
            Vector3 flatWallNormal = new Vector3(_wallNormal.x, 0, _wallNormal.z).normalized;
            if (flatWallNormal.sqrMagnitude > 0.001f && Vector3.Dot(targetVelocity, flatWallNormal) < 0)
            {
                targetVelocity = Vector3.ProjectOnPlane(targetVelocity, flatWallNormal);
            }
        }
        
        float verticalVelocity = _rb.velocity.y;

        // --- isStairs Aggressive Grip ---
        if (_isOnStairs && verticalVelocity < 0.5f)
        {
            _rb.AddForce(-Physics.gravity * 0.9f, ForceMode.Acceleration);
            
            Vector3 currentVel = _rb.velocity;
            Vector3 desiredVel = Vector3.zero;
            
            if (targetVelocity.sqrMagnitude > 0.01f)
            {
                desiredVel = Vector3.ProjectOnPlane(targetVelocity, _stairsContactNormal);
            }
            
            Vector3 velChange = desiredVel - currentVel;
            float maxStairsAccel = 120f;
            velChange = Vector3.ClampMagnitude(velChange, maxStairsAccel * Time.fixedDeltaTime);
            
            _rb.AddForce(velChange, ForceMode.VelocityChange);
            return;
        }

        // --- Slow Climb Physics ---
        if (_isClimbing)
        {
            _rb.AddForce(Physics.gravity * 0.7f, ForceMode.Acceleration);
            if (verticalVelocity < -0.1f && Time.time - _climbStartTime > 0.2f) _isClimbing = false;
        }

        // --- Climb Struggle Mechanic ---
        bool recentlyTouchedWall = _climbCandidateCol != null && (Time.time - _climbCandidateTime < 0.3f);
        bool struggleInput = Input.GetMouseButton(0) && _inputMove.y > 0.1f;

        if (!_isGrounded && recentlyTouchedWall && struggleInput && _struggleHeightGained < 1.0f)
        {
            float struggleSpeed = 1.8f;
            verticalVelocity = struggleSpeed;
            _struggleHeightGained += struggleSpeed * Time.deltaTime;
            _rb.AddForce(transform.forward * 5f, ForceMode.Acceleration);
        }

        // --- Final Velocity Assignment ---
        Vector3 currentHorizontal = new Vector3(_rb.velocity.x, 0, _rb.velocity.z);
        Vector3 velocityChange = targetVelocity - currentHorizontal;
        
        float maxAccel = 150f; 
        velocityChange = Vector3.ClampMagnitude(velocityChange, maxAccel * Time.fixedDeltaTime);
        
        _rb.AddForce(velocityChange, ForceMode.VelocityChange);
        
        if (Mathf.Abs(verticalVelocity - _rb.velocity.y) > 0.001f)
        {
            _rb.velocity = new Vector3(_rb.velocity.x, verticalVelocity, _rb.velocity.z);
        }
    }

    void CheckGrounded()
    {
        if (_col == null) return;
        
        float radius = _col.radius * 0.9f;
        Vector3 localBottom = _col.center + Vector3.down * (_col.height / 2f);
        Vector3 worldBottom = transform.TransformPoint(localBottom);
        
        Vector3 origin = worldBottom + Vector3.up * (radius + 0.05f);
        float castDist = 0.28f;
        
        _isGrounded = false;
        
        int hitCount = Physics.SphereCastNonAlloc(origin, radius, Vector3.down, _groundHitBuffer, castDist, groundMask, QueryTriggerInteraction.Ignore);
        RaycastHit bestHit = default;
        bool foundValidGround = false;
        
        for (int i = 0; i < hitCount; i++)
        {
            RaycastHit hit = _groundHitBuffer[i];
            if (hit.transform != null && hit.transform.root != transform.root)
            {
                bestHit = hit;
                foundValidGround = true;
                break;
            }
        }
        
        if (!foundValidGround)
        {
            int fallbackHitCount = Physics.SphereCastNonAlloc(origin, radius, Vector3.down, _groundFallbackHitBuffer, castDist, ~0, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < fallbackHitCount; i++)
            {
                RaycastHit hit = _groundFallbackHitBuffer[i];
                if (hit.transform != null && hit.transform.root != transform.root)
                {
                    bestHit = hit;
                    foundValidGround = true;
                    break;
                }
            }
        }
        
        if (foundValidGround)
        {
            _isGrounded = true;
            _activePlatform = bestHit.rigidbody;
        }
        else
        {
            _activePlatform = null;
        }
    }

    void HandlePlatformMovement()
    {
        if (_activePlatform == null) return;
        
        if (_activePlatform.isKinematic)
        {
            Vector3 platformDelta = _activePlatform.position - _lastPlatformPos;
            if (platformDelta.sqrMagnitude > 0.0001f && platformDelta.sqrMagnitude < 100f)
            {
                _rb.MovePosition(_rb.position + platformDelta);
            }
        }
        
        _lastPlatformPos = _activePlatform.position;
    }

    private void ExecuteJump()
    {
        _rb.velocity = new Vector3(_rb.velocity.x, jumpForce, _rb.velocity.z);
        _isGrounded = false;
        _jumpCount++;
        _jumpBufferedUntil = 0f;
        _groundCheckDisabledUntil = Time.time + 0.15f;
    }

    void HandleHeadBob()
    {
        bool isMoving = new Vector3(_rb.velocity.x, 0f, _rb.velocity.z).sqrMagnitude > 0.04f && _isGrounded;

        if (isMoving)
        {
            _bobTimer += Time.deltaTime * bobFrequency * 2f * Mathf.PI;
            float bobY = Mathf.Sin(_bobTimer)        * bobAmplitude;
            float bobX = Mathf.Sin(_bobTimer * 0.5f) * bobAmplitude * 0.5f;
            BobOffset  = new Vector3(bobX, bobY, 0f);
        }
        else
        {
            _bobTimer = 0f;
            BobOffset = Vector3.Lerp(BobOffset, Vector3.zero, Time.deltaTime * 8f);
        }
    }

    // ── Climbing ─────────────────────────────────────────────────────────────
    bool TryStartClimb()
    {
        if (!_canClimbThisJump) return false;

        Vector3 headPos = new Vector3(transform.position.x, _col.bounds.max.y - 0.2f, transform.position.z);
        Vector3 castDir = transform.forward;
        float castDist = _col.radius + 0.8f;

        bool isFacingWall = Physics.Raycast(headPos, castDir, out RaycastHit wallHit, castDist, climbObstacleMask, QueryTriggerInteraction.Ignore);
        
        Collider targetCol = isFacingWall ? wallHit.collider : _climbCandidateCol;
        Vector3 hitPoint = isFacingWall ? wallHit.point : transform.position + castDir * _col.radius;

        if (targetCol != null && (isFacingWall || (Time.time - _climbCandidateTime < 0.25f)))
        {
            Vector3 scanPos = hitPoint + transform.forward * 0.1f;
            float scanLimitY = transform.position.y + climbMaxHeight + 1.0f;
            Vector3 rayOrigin = new Vector3(scanPos.x, scanLimitY, scanPos.z);
            
            float targetHeightY = transform.position.y;
            int hitCount = Physics.RaycastNonAlloc(rayOrigin, Vector3.down, _climbHitBuffer, climbMaxHeight + 2.0f, climbObstacleMask, QueryTriggerInteraction.UseGlobal);
            
            bool foundLedge = false;
            for (int i = 0; i < hitCount; i++)
            {
                RaycastHit hit = _climbHitBuffer[i];
                if (hit.transform.root == transform.root) continue;

                if (hit.point.y > targetHeightY)
                {
                    targetHeightY = hit.point.y;
                    foundLedge = true;
                }
            }

            if (!foundLedge)
            {
                targetHeightY = targetCol.bounds.max.y;
            }

            float heightDiff = targetHeightY - transform.position.y;
            
            if (heightDiff < 0.4f || heightDiff > climbMaxHeight)
            {
                return false;
            }

            float h = heightDiff + 0.2f;
            float gravity = Mathf.Abs(Physics.gravity.y);
            float vY = Mathf.Sqrt(2f * gravity * h);
            
            Debug.Log($"[Climb Debug] CLIMB SUCCESS! Target: {targetHeightY:F2}m | Force: {vY:F2}");

            _rb.velocity = new Vector3(_rb.velocity.x, vY, _rb.velocity.z);
            
            _isClimbing = true;
            _activeClimbTargetY = targetHeightY - 0.05f;
            _climbStartTime = Time.time;

            _isGrounded = false;
            _isOnStairs = false;
            _jumpBufferedUntil = 0f;
            _canClimbThisJump = false;
                
            return true;
        }
        
        return false;
    }

    // ── 碰撞墙壁检测 ──────────────────────────────────────────────────────────
    private bool _isTouchingWall = false;
    private Vector3 _wallNormal = Vector3.zero;

    void OnCollisionEnter(Collision collision)
    {
        // Intentional empty
    }

    void OnCollisionStay(Collision collision)
    {
        // 🌟 强力拦截 3：如果正在使用家具，绝不处理任何外部碰撞逻辑，避免被家具碰撞体误判为墙壁
        if (IsUsingFurniture) return;

        foreach (ContactPoint cp in collision.contacts)
        {
            float relativeY = cp.point.y - transform.position.y;
            
            if (Mathf.Abs(cp.normal.y) < 0.5f)
            {
                _isTouchingWall = true;
                _wallNormal += cp.normal; 
            }

            if (relativeY > 0.25f && Mathf.Abs(cp.normal.y) < 0.4f)
            {
                _climbCandidateCol = cp.otherCollider;
                _climbCandidateTime = Time.time;
            }
        }

        if (_isTouchingWall)
        {
            _wallNormal.Normalize();
        }

        Rigidbody otherRb = collision.rigidbody;
        if (otherRb != null && !otherRb.isKinematic)
        {
            WorldObject wo = collision.gameObject.GetComponentInParent<WorldObject>();
            if (wo != null && wo.canBePushed)
            {
                Vector3 pushDir = Vector3.zero;
                foreach (ContactPoint contact in collision.contacts)
                {
                    pushDir -= contact.normal;
                }
                pushDir.y = 0f;
                
                if (pushDir.sqrMagnitude > 0.01f && _inputMove.sqrMagnitude > 0.01f)
                {
                    pushDir.Normalize();
                    otherRb.AddForce(pushDir * (pushForce * 50f), ForceMode.Force);
                }
            }
        }
    }
}
