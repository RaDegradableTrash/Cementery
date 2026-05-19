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

    // ── Lifecycle ────────────────────────────────────────────────────────────
    void Awake()
    {
        _rb = GetComponent<Rigidbody>();
        _col = GetComponent<CapsuleCollider>();
        _stamina = GetComponent<PlayerStamina>();
        _selfColliders = GetComponentsInChildren<Collider>(true);

        _rb.freezeRotation = true;
        _rb.useGravity = true;
        _rb.isKinematic = false; // Force non-kinematic initialization to ensure physics simulation is active!
        _rb.interpolation = RigidbodyInterpolation.Interpolate;
        _rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;

        // Apply zero-friction material to prevent sticking to walls
        PhysicMaterial pm = new PhysicMaterial("PlayerMaterial") { dynamicFriction = 0f, staticFriction = 0f, frictionCombine = PhysicMaterialCombine.Minimum };
        _col.material = pm;

        // Player should not collide with colliders in its own hierarchy.
        for (int i = 0; i < _selfColliders.Length; i++)
        {
            Collider c = _selfColliders[i];
            if (c == null || c == _col)
                continue;

            Physics.IgnoreCollision(_col, c, true);
        }

        if (inventoryCameraController == null)
            inventoryCameraController = InventoryCameraController.GetPrimaryController();

        if (mouseLook == null && Camera.main != null)
            mouseLook = Camera.main.GetComponent<MouseLook>();
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
            // Ensure local owner's rigidbody is 100% active and affected by gravity!
            if (_rb != null) _rb.isKinematic = false;
        }
    }

    void Update()
    {
        // 逻辑修正：如果网络管理器没启动（单机测试），或者网络已启动且你是房主/本地玩家，才允许执行逻辑
        bool isNetworkActive = NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening;
        if (isNetworkActive && (!IsSpawned || !IsOwner)) return;

        // 如果玩家死亡，屏蔽所有输入与动作
        if (hp <= 0 || PlayerDeathFlowController.IsPlayerDead)
        {
            _inputMove = Vector2.zero;
            BobOffset = Vector3.Lerp(BobOffset, Vector3.zero, Time.deltaTime * 12f);
            return;
        }

        // 如果暂停菜单打开，屏蔽所有输入
        if (GameMenuManager.IsMenuOpen)
        {
            _inputMove = Vector2.zero;
            return;
        }

        if (IsInventoryModeActive())
        {
            _inputMove = Vector2.zero;
            _jumpBufferedUntil = 0f;
            _stamina?.Recover();
            BobOffset = Vector3.Lerp(BobOffset, Vector3.zero, Time.deltaTime * 12f);
            return;
        }

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
            
            // Handle momentum erasure for high climbs
            if (_isClimbing && Time.time - _climbStartTime < 1.5f)
            {
                if (transform.position.y >= _activeClimbTargetY)
                {
                    // Erase vertical momentum to land precisely
                    _rb.velocity = new Vector3(_rb.velocity.x, Mathf.Min(_rb.velocity.y, 0f), _rb.velocity.z);
                    _isClimbing = false;
                }
            }
        }

        bool jumpPressed = Input.GetButtonDown("Jump") || Input.GetKeyDown(KeyCode.Space);
        if (jumpPressed)
        {
            Debug.Log($"[Jump Debug] Space pressed! isGrounded: {_isGrounded} | jumpCount: {_jumpCount} | canClimb: {_canClimbThisJump} | groundCheckDisabled: {(Time.time <= _groundCheckDisabledUntil)}");
            
            // 1. FIRST JUMP (Only from ground)
            if (_jumpCount == 0 && _isGrounded)
            {
                Debug.Log("[Jump Debug] Executing FIRST JUMP!");
                ExecuteJump();
            }
            // 2. SECOND JUMP (Strictly for climbing walls/ledges) - Temporarily disabled per user request
            /*
            else if (_jumpCount == 1 && _jumpCount < maxJumps)
            {
                Debug.Log("[Jump Debug] Trying CLIMB/SECOND JUMP!");
                if (TryStartClimb())
                {
                    Debug.Log("[Jump Debug] Climb/Double Jump success!");
                }
                else
                {
                    Debug.Log("[Jump Debug] Climb/Double Jump failed!");
                }
            }
            */
        }

        GatherInput();
        HandleHeadBob();
        // UpdateDebugCollisionLog(); // Commented out per user request
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
            Debug.Log($"[Jump Peak] Maximum Height Reached: {_currentJumpPeakY:F2}m (Delta: {(_currentJumpPeakY - transform.position.y):F2}m)");
            _currentJumpPeakY = -Mathf.Infinity;
        }
    }

    private float _collisionDebugTimer = 0f;
    private void UpdateDebugCollisionLog()
    {
        _collisionDebugTimer += Time.deltaTime;
        if (_collisionDebugTimer >= 1f)
        {
            _collisionDebugTimer = 0f;
            
            CapsuleCollider col = GetComponent<CapsuleCollider>();
            if (col == null) return;

            // Calculate capsule world points
            Vector3 point0, point1;
            float radius = col.radius * Mathf.Max(transform.lossyScale.x, transform.lossyScale.z);
            float height = col.height * transform.lossyScale.y;
            Vector3 dir = Vector3.up; // Standard capsule direction is Y
            
            float centerOffset = (height / 2f) - radius;
            point0 = transform.TransformPoint(col.center - dir * centerOffset);
            point1 = transform.TransformPoint(col.center + dir * centerOffset);

            // Detect all colliders in this area
            Collider[] hits = Physics.OverlapCapsule(point0, point1, radius, ~0, QueryTriggerInteraction.Collide);
            
            if (hits.Length > 1) // 1 because it will always hit itself
            {
                string log = $"[Player Collision Debug] Touching {hits.Length - 1} other colliders:\n";
                foreach (var hit in hits)
                {
                    if (hit.gameObject == gameObject) continue;
                    log += $"- {hit.name} (Layer: {LayerMask.LayerToName(hit.gameObject.layer)}, Trigger: {hit.isTrigger}, Type: {hit.GetType().Name})\n";
                }
                Debug.Log(log);
            }
        }
    }

    void FixedUpdate()
    {
        // 同样的逻辑应用到物理更新
        bool isNetworkActive = NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening;
        if (isNetworkActive && (!IsSpawned || !IsOwner)) return;

        // 如果玩家死亡，停止物理处理和常规移动
        if (hp <= 0 || PlayerDeathFlowController.IsPlayerDead) return;

        // 如果暂停菜单打开，停止物理处理
        if (GameMenuManager.IsMenuOpen) return;

        if (IsInventoryModeActive()) return;

        HandleMovement();
        // HandleJump(); // Now fully handled immediately in Update to prevent input loss
        
        // Clear collision state for the upcoming physics step
        _isTouchingWall = false;
        _wallNormal = Vector3.zero;
    }

    bool IsInventoryModeActive()
    {
        if (inventoryCameraController == null)
            inventoryCameraController = InventoryCameraController.GetPrimaryController();

        return inventoryCameraController != null && inventoryCameraController.IsInventoryActive;
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
        
        Destroy(corpse.GetComponent<PlayerController>());
        Destroy(corpse.GetComponent<PlayerDeathFlowController>());
        Destroy(corpse.GetComponent<PlayerStamina>());
        
        CharacterController cc = corpse.GetComponent<CharacterController>();
        if (cc != null) Destroy(cc);

        Animator anim = corpse.GetComponent<Animator>();
        if (anim != null) Destroy(anim);

        var netObj = corpse.GetComponent<NetworkObject>();
        if (netObj != null) Destroy(netObj);

        foreach (MeshCollider mc in corpse.GetComponentsInChildren<MeshCollider>())
        {
            mc.convex = true;
        }

        foreach (Camera cam in corpse.GetComponentsInChildren<Camera>())
            Destroy(cam.gameObject);
        foreach (AudioListener al in corpse.GetComponentsInChildren<AudioListener>())
            Destroy(al);
        foreach (MouseLook ml in corpse.GetComponentsInChildren<MouseLook>())
            Destroy(ml);

        Rigidbody corpseRb = corpse.GetComponent<Rigidbody>();
        if (corpseRb != null && _rb != null)
        {
            corpseRb.isKinematic = false;
            corpseRb.freezeRotation = false; 
            corpseRb.velocity = _rb.velocity;
            corpseRb.angularVelocity = _rb.angularVelocity;
        }

        SetPlayerVisible(false);
        return corpse.transform;
    }

    public void SetPlayerVisible(bool visible)
    {
        foreach (Renderer r in GetComponentsInChildren<Renderer>())
            r.enabled = visible;
        
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
        // Prevent setting velocity into walls, which causes severe clipping and teleportation
        if (_isTouchingWall)
        {
            // Flatten the wall normal to prevent projection from launching us into the air or ground
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
            // FUNDAMENTAL FIX: Do not override _rb.velocity on stairs. 
            // Forcing velocity on a ramp into a wall causes deep wedge penetration, resulting in explosive pop-ups.
            
            // Counteract most of gravity so we don't slide down easily, but keep 10% to stay grounded and avoid jitter.
            _rb.AddForce(-Physics.gravity * 0.9f, ForceMode.Acceleration);
            
            Vector3 currentVel = _rb.velocity;
            Vector3 desiredVel = Vector3.zero;
            
            if (targetVelocity.sqrMagnitude > 0.01f)
            {
                desiredVel = Vector3.ProjectOnPlane(targetVelocity, _stairsContactNormal);
            }
            
            Vector3 velChange = desiredVel - currentVel;
            
            // Clamp acceleration so the physics solver can still push us back from walls
            float maxStairsAccel = 120f;
            velChange = Vector3.ClampMagnitude(velChange, maxStairsAccel * Time.fixedDeltaTime);
            
            _rb.AddForce(velChange, ForceMode.VelocityChange);
            
            return; // Skip standard assignment
        }

        // --- Slow Climb Physics ---
        if (_isClimbing)
        {
            _rb.AddForce(Physics.gravity * 0.7f, ForceMode.Acceleration);
            if (verticalVelocity < -0.1f && Time.time - _climbStartTime > 0.2f) _isClimbing = false;
        }

        // --- Climb Struggle Mechanic ---
        // If mid-air, touching wall, holding Mouse0 + W, slowly hoist up
        bool recentlyTouchedWall = _climbCandidateCol != null && (Time.time - _climbCandidateTime < 0.3f);
        bool struggleInput = Input.GetMouseButton(0) && _inputMove.y > 0.1f;

        if (!_isGrounded && recentlyTouchedWall && struggleInput && _struggleHeightGained < 1.0f)
        {
            float struggleSpeed = 1.8f;
            verticalVelocity = struggleSpeed;
            _struggleHeightGained += struggleSpeed * Time.deltaTime;
            
            // Apply a small forward nudge into the wall to maintain contact
            _rb.AddForce(transform.forward * 5f, ForceMode.Acceleration);
        }

        // --- Final Velocity Assignment ---
        // FUNDAMENTAL FIX: Instead of hard-overriding _rb.velocity (which ignores physics 
        // bounce-back and causes teleportation), we calculate the required velocity change 
        // and apply it as a clamped force.
        Vector3 currentHorizontal = new Vector3(_rb.velocity.x, 0, _rb.velocity.z);
        Vector3 velocityChange = targetVelocity - currentHorizontal;
        
        // Clamp acceleration to prevent infinite pushing force (allows physics to push back)
        float maxAccel = 150f; 
        velocityChange = Vector3.ClampMagnitude(velocityChange, maxAccel * Time.fixedDeltaTime);
        
        _rb.AddForce(velocityChange, ForceMode.VelocityChange);
        
        // We only forcefully override the Y velocity if our custom mechanics (like struggle or stairs) modified it.
        // Otherwise, we leave the Y velocity exactly as the physics engine calculated it!
        if (Mathf.Abs(verticalVelocity - _rb.velocity.y) > 0.001f)
        {
            _rb.velocity = new Vector3(_rb.velocity.x, verticalVelocity, _rb.velocity.z);
        }
    }



    void CheckGrounded()
    {
        if (_col == null) return;
        
        float radius = _col.radius * 0.9f;
        // Calculate the true bottom of the capsule in world space, independent of pivot point
        Vector3 localBottom = _col.center + Vector3.down * (_col.height / 2f);
        Vector3 worldBottom = transform.TransformPoint(localBottom);
        
        // Start the spherecast slightly above the bottom so it doesn't start already clipped into the ground
        Vector3 origin = worldBottom + Vector3.up * (radius + 0.05f);
        float castDist = 0.28f; // Increased from 0.15f to support uneven terrains and skin-width contact offsets
        
        _isGrounded = false;
        
        // 1. Primary check: Use SphereCastAll to query all colliders in the sweep path using the Inspector-defined mask.
        // This is crucial because a single SphereCast will get blocked if it starts inside the player's own collider!
        RaycastHit[] hits = Physics.SphereCastAll(origin, radius, Vector3.down, castDist, groundMask, QueryTriggerInteraction.Ignore);
        
        RaycastHit bestHit = default;
        bool foundValidGround = false;
        
        for (int i = 0; i < hits.Length; i++)
        {
            RaycastHit hit = hits[i];
            if (hit.transform != null && hit.transform.root != transform.root)
            {
                bestHit = hit;
                foundValidGround = true;
                break; // SphereCastAll automatically orders by distance, so first non-player hit is the closest ground!
            }
        }
        
        // 2. Dual-Pass Fallback: If primary check fails, query all layers (~0) as fallback to prevent layer misconfiguration bugs
        if (!foundValidGround)
        {
            RaycastHit[] fallbackHits = Physics.SphereCastAll(origin, radius, Vector3.down, castDist, ~0, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < fallbackHits.Length; i++)
            {
                RaycastHit hit = fallbackHits[i];
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
        
        // ONLY manually track platform position if it's a Kinematic (script-driven) platform like an elevator.
        // For dynamic physics objects (like boxes or debris), we must NOT manually teleport the player,
        // because standard physics friction handles the movement naturally. Manual teleports cause severe clipping.
        if (_activePlatform.isKinematic)
        {
            Vector3 platformDelta = _activePlatform.position - _lastPlatformPos;
            if (platformDelta.sqrMagnitude > 0.0001f && platformDelta.sqrMagnitude < 100f)
            {
                // MovePosition is safer than direct position assignment for dynamic rigidbodies
                _rb.MovePosition(_rb.position + platformDelta);
            }
        }
        
        _lastPlatformPos = _activePlatform.position;
    }

    void HandleJump()
    {
        if (Time.time > _jumpBufferedUntil) return;

        // 1. FIRST JUMP (Only from ground)
        if (_jumpCount == 0 && _isGrounded)
        {
            ExecuteJump();
        }
        // 2. SECOND JUMP (Strictly for climbing walls/ledges)
        else if (_jumpCount == 1 && _jumpCount < maxJumps)
        {
            if (TryStartClimb())
            {
                // TryStartClimb handles the jumpCount increment
            }
        }
    }

    private void ExecuteJump()
    {
        _rb.velocity = new Vector3(_rb.velocity.x, jumpForce, _rb.velocity.z);
        _isGrounded = false;
        _jumpCount++;
        _jumpBufferedUntil = 0f;
        
        // Disable grounding for 0.15s to prevent immediate reset while leaving the floor
        _groundCheckDisabledUntil = Time.time + 0.15f;
    }

    // Head Bobbing
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
            // --- Precise Vertical Exit Detection ---
            // 1. Use the actual collision/raycast hit point to find the scan position
            Vector3 scanPos = hitPoint + transform.forward * 0.1f;
            
            // 2. Scan DOWN from above to find the exact top surface
            float scanLimitY = transform.position.y + climbMaxHeight + 1.0f;
            Vector3 rayOrigin = new Vector3(scanPos.x, scanLimitY, scanPos.z);
            
            float targetHeightY = transform.position.y;
            RaycastHit[] hits = Physics.RaycastAll(rayOrigin, Vector3.down, climbMaxHeight + 2.0f, climbObstacleMask);
            
            bool foundLedge = false;
            foreach (var hit in hits)
            {
                // Skip our own colliders (including any interaction spheres/triggers)
                if (hit.transform.root == transform.root) continue;

                if (hit.point.y > targetHeightY)
                {
                    targetHeightY = hit.point.y;
                    foundLedge = true;
                }
            }

            if (!foundLedge)
            {
                // Fallback to collider bounds if raycast missed but we know something is there
                targetHeightY = targetCol.bounds.max.y;
            }

            // 3. Calculation and Reachability Check
            float heightDiff = targetHeightY - transform.position.y;
            
            // CRITICAL: If the target height is not significantly above us, it's not a climb!
            // This prevents "Double Jumping" on the floor or in empty air.
            if (heightDiff < 0.4f)
            {
                return false;
            }

            // If too high, fail climb
            if (heightDiff > climbMaxHeight)
            {
                Debug.Log($"[Climb Debug] Too high: {heightDiff:F2}m. Fail.");
                return false;
            }

            // Calculate force for the height
            float h = heightDiff + 0.2f;
            float gravity = Mathf.Abs(Physics.gravity.y);
            float vY = Mathf.Sqrt(2f * gravity * h);
            
            Debug.Log($"[Climb Debug] CLIMB SUCCESS! Target: {targetHeightY:F2}m | Force: {vY:F2}");

            _rb.velocity = new Vector3(_rb.velocity.x, vY, _rb.velocity.z);
            
            // Track climb target for momentum erasure
            _isClimbing = true;
            _activeClimbTargetY = targetHeightY - 0.05f;
            _climbStartTime = Time.time;

            _isGrounded = false;
            _isOnStairs = false;
            _jumpBufferedUntil = 0f;
            _canClimbThisJump = false;

            if (mouseLook != null)
            {
                // Ready for future camera effects
            }
                
            return true;
        }
        
        return false;
    }

    // 碰撞墙壁检测
    
    private bool _isTouchingWall = false;
    private Vector3 _wallNormal = Vector3.zero;

    void OnCollisionEnter(Collision collision)
    {
        // Intentional empty: standard physics handles initial impacts. 
        // Custom anti-crush logic removed to prevent explosive lateral teleportation bugs.
    }

    void OnCollisionStay(Collision collision)
    {
        foreach (ContactPoint cp in collision.contacts)
        {
            float relativeY = cp.point.y - transform.position.y;
            
            // 1. Wall Projection Tracking
            // If contact is vertical-ish
            if (Mathf.Abs(cp.normal.y) < 0.5f)
            {
                // Treat ALL vertical contacts as walls to prevent penetration.
                // If it's a pushable object, our custom push logic will move it safely.
                // If it's stuck, we simply won't penetrate it, preventing explosive clipping.
                _isTouchingWall = true;
                _wallNormal += cp.normal; // Accumulate to average out corners
            }

            // 2. Climb Candidate Detection
            // Ensure the contact is above knee height to prevent treating the ground as a wall
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

        // 安全的碰撞检测
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
                    // Apply physics-safe force based on mass instead of hard-setting velocity
                    otherRb.AddForce(pushDir * (pushForce * 50f), ForceMode.Force);
                }
            }
        }
    }
}
