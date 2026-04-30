using UnityEngine;

/// <summary>
/// First-person player controller. Requires a Rigidbody and CapsuleCollider component.
/// Supports: WASD movement, sprinting (Left Shift), jump force, physics-gravity falling, head bobbing.
/// </summary>
[RequireComponent(typeof(Rigidbody))]
[RequireComponent(typeof(CapsuleCollider))]
public class PlayerController : MonoBehaviour
{
    [Header("Movement")]
    public float walkSpeed = 4f;
    public float sprintSpeed = 7f;

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

    [Header("Climbing")]
    [SerializeField] private float climbMaxHeight = 2.5f;
    [SerializeField] private LayerMask climbObstacleMask = ~0;
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
    private float _groundedUntil;
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

    void Update()
    {
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
            _jumpBufferedUntil = Time.time + JumpBufferTime;
        }

        GatherInput();
        HandleHeadBob();
        // UpdateDebugCollisionLog(); // Commented out per user request
        TrackJumpPeak();
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
        if (IsInventoryModeActive()) return;

        HandleMovement();
        HandleJump();
    }

    bool IsInventoryModeActive()
    {
        if (inventoryCameraController == null)
            inventoryCameraController = InventoryCameraController.GetPrimaryController();

        return inventoryCameraController != null && inventoryCameraController.IsInventoryActive;
    }

    // ── Movement ─────────────────────────────────────────────────────────────
    public void ResetVelocity()
    {
        if (_rb != null)
            _rb.velocity = new Vector3(0f, _rb.velocity.y, 0f);
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
        
        float verticalVelocity = _rb.velocity.y;

        // --- isStairs Aggressive Grip ---
        if (_isOnStairs)
        {
            // If not jumping (upward velocity is low), we "glue" the player to the surface
            if (verticalVelocity < 0.5f)
            {
                // Kill all velocity to prevent sliding down steep slopes
                _rb.velocity = Vector3.zero;
                _rb.angularVelocity = Vector3.zero;
                
                // Allow movement relative to the slope
                if (targetVelocity.sqrMagnitude > 0.01f)
                {
                    // Project movement onto the slope to allow climbing up/down
                    Vector3 slopeDir = Vector3.ProjectOnPlane(targetVelocity, _stairsContactNormal);
                    _rb.velocity = slopeDir;
                }
                
                // Counteract gravity entirely for this frame
                _rb.AddForce(-Physics.gravity, ForceMode.Acceleration);
                return; // Skip standard assignment
            }
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
        _rb.velocity = new Vector3(targetVelocity.x, verticalVelocity, targetVelocity.z);
    }



    void CheckGrounded()
    {
        // GroundCheck: Narrow downward spherecast (0.1m radius) to avoid walls
        float radius = 0.1f;
        float castDist = 0.15f;
        Vector3 origin = transform.position + Vector3.up * 0.1f;
        
        _isGrounded = false;
        if (Physics.SphereCast(origin, radius, Vector3.down, out RaycastHit hit, castDist, groundMask, QueryTriggerInteraction.Ignore))
        {
            // Only count if it's NOT part of the player
            if (hit.transform.root != transform.root)
            {
                _isGrounded = true;
                _activePlatform = hit.rigidbody;
            }
        }
        else
        {
            _activePlatform = null;
        }
    }

    void HandlePlatformMovement()
    {
        if (_activePlatform == null) return;
        
        // If the platform moved, nudge the player by the same amount to prevent slipping/clipping
        Vector3 platformDelta = _activePlatform.position - _lastPlatformPos;
        if (platformDelta.sqrMagnitude > 0.0001f && platformDelta.sqrMagnitude < 100f)
        {
            _rb.position += platformDelta;
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

    // ── Head Bob ─────────────────────────────────────────────────────────────
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
            _groundedUntil = 0f;
            _canClimbThisJump = false;

            if (mouseLook != null)
            {
                // Ready for future camera effects
            }
                
            return true;
        }
        
        return false;
    }

    // ── Pushing ───────────────────────────────────────────────────────────────
    void OnCollisionStay(Collision collision)
    {
        HandleCrushEscape(collision);

        // WallCheck: Only consider contacts as climb candidates if they hit our torso/waist area (>0.5m)
        // This ensures the ground is NEVER treated as a wall for second jumps.
        foreach (ContactPoint cp in collision.contacts)
        {
            float relativeY = cp.point.y - transform.position.y;
            
            // If contact is vertical-ish AND above knee height (0.25m)
            // This ensures the ground is NEVER treated as a wall, but low obstacles are.
            if (relativeY > 0.25f && Mathf.Abs(cp.normal.y) < 0.4f)
            {
                _climbCandidateCol = cp.otherCollider;
                _climbCandidateTime = Time.time;
            }
        }

        Rigidbody otherRb = collision.rigidbody;
        if (otherRb == null || otherRb.isKinematic) return;

        WorldObject wo = collision.gameObject.GetComponentInParent<WorldObject>();
        if (wo == null || !wo.canBePushed) return;

        Vector3 pushDir = Vector3.zero;
        foreach (ContactPoint contact in collision.contacts)
        {
            pushDir -= contact.normal;
        }
        pushDir.y = 0f;
        if (pushDir.sqrMagnitude < 0.01f) return;
        pushDir.Normalize();

        if (otherRb.mass <= 0f) return;

        float pushSpeed = Mathf.Min(pushForce / otherRb.mass, 3f); // Cap push speed
        Vector3 currentHorizontal = new Vector3(otherRb.velocity.x, 0f, otherRb.velocity.z);
        Vector3 desiredHorizontal = pushDir * pushSpeed;
        Vector3 nextHorizontal = Vector3.MoveTowards(currentHorizontal, desiredHorizontal, pushSpeed * 0.35f);

        // Clamp downward velocity to prevent collision resolution pushing objects through floor
        float yVel = Mathf.Max(otherRb.velocity.y, -5f);
        otherRb.velocity = new Vector3(nextHorizontal.x, yVel, nextHorizontal.z);
        otherRb.angularVelocity *= 0.9f;
    }

    // ── Anti-Crush ────────────────────────────────────────────────────────────
    // Physics-driven gradual escape: when something presses from above while grounded,
    // apply a continuous lateral acceleration proportional to the crushing pressure.
    // This creates a natural "squeezed sideways" feel without instant teleportation.
    void OnCollisionEnter(Collision collision) { HandleCrushEscape(collision); }

    void HandleCrushEscape(Collision collision)
    {
        if (!_isGrounded) return;

        Vector3 escapeDir = Vector3.zero;
        float crushPressure = 0f;

        foreach (ContactPoint contact in collision.contacts)
        {
            // Contact normal pointing downward = object pressing from above
            if (contact.normal.y < -0.3f)
            {
                // Pressure intensity from how vertical the contact is (straight down = 1.0)
                float intensity = Mathf.Abs(contact.normal.y);
                crushPressure = Mathf.Max(crushPressure, intensity);
                
                Vector3 lateral = transform.position - contact.point;
                lateral.y = 0f;
                if (lateral.sqrMagnitude > 0.001f)
                    escapeDir += lateral.normalized * intensity;
            }
        }

        if (crushPressure > 0f && escapeDir.sqrMagnitude > 0.001f)
        {
            escapeDir.Normalize();
            // Continuous acceleration, not instant velocity — gradual squeeze-out
            // 30 m/s² × pressure creates a building lateral movement
            float escapeAccel = 30f * crushPressure;
            _rb.AddForce(escapeDir * escapeAccel, ForceMode.Acceleration);
            
            // Suppress downward velocity while being crushed to prevent floor penetration
            if (_rb.velocity.y < 0f)
                _rb.velocity = new Vector3(_rb.velocity.x, 0f, _rb.velocity.z);
        }
    }
}
