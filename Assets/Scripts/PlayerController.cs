using UnityEngine;

/// <summary>
/// First-person player controller. Requires a CharacterController component.
/// Supports: WASD movement, sprinting (Left Shift), jump force, physics-gravity falling, head bobbing.
/// </summary>
[RequireComponent(typeof(CharacterController))]
public class PlayerController : MonoBehaviour
{
    [Header("Movement")]
    public float walkSpeed = 4f;
    public float sprintSpeed = 7f;

    [Header("Inventory")]
    [SerializeField] private InventoryCameraController inventoryCameraController;

    [Header("Jump & Fall")]
    [Tooltip("Initial upward velocity applied when jumping.")]
    public float jumpForce = 7f;

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

    // ── Internal State ──────────────────────────────────────────────────────
    private CharacterController _cc;
    private PlayerStamina _stamina;
    private Collider[] _selfColliders;

    private const float JumpBufferTime = 0.12f;
    private const float CoyoteTime = 0.08f;

    private float _verticalVelocity;
    private float _bobTimer;
    private bool _isGrounded;
    private float _jumpBufferedUntil;
    private float _groundedUntil;

    // ── Lifecycle ────────────────────────────────────────────────────────────
    void Awake()
    {
        _cc      = GetComponent<CharacterController>();
        _stamina = GetComponent<PlayerStamina>();
        _selfColliders = GetComponentsInChildren<Collider>(true);

        // Avoid losing grounded flags on tiny frame displacements when standing still.
        _cc.minMoveDistance = 0f;

        // CharacterController should not collide with colliders in its own hierarchy.
        for (int i = 0; i < _selfColliders.Length; i++)
        {
            Collider c = _selfColliders[i];
            if (c == null || c == _cc)
                continue;

            Physics.IgnoreCollision(_cc, c, true);
        }

        if (inventoryCameraController == null)
            inventoryCameraController = InventoryCameraController.GetPrimaryController();
        if (inventoryCameraController == null)
            inventoryCameraController = FindObjectOfType<InventoryCameraController>();
    }

    void Update()
    {
        if (IsInventoryModeActive())
        {
            _verticalVelocity = 0f;
            _jumpBufferedUntil = 0f;
            _groundedUntil = 0f;
            _stamina?.Recover();
            BobOffset = Vector3.Lerp(BobOffset, Vector3.zero, Time.deltaTime * 12f);
            return;
        }

        _isGrounded = _cc.isGrounded;
        if (_isGrounded)
            _groundedUntil = Time.time + CoyoteTime;

        bool jumpPressed = Input.GetButtonDown("Jump") || Input.GetKeyDown(KeyCode.Space);
        if (jumpPressed)
            _jumpBufferedUntil = Time.time + JumpBufferTime;

        Vector3 planarVelocity = HandleMovement();
        ApplyGravity(planarVelocity);
        HandleHeadBob();
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

    // ── Movement ─────────────────────────────────────────────────────────────
    Vector3 HandleMovement()
    {
        float h = Input.GetAxis("Horizontal");
        float v = Input.GetAxis("Vertical");

        bool wantsSprint = Input.GetKey(KeyCode.LeftShift);
        bool canSprint   = _stamina == null || _stamina.HasStamina;
        bool isSprinting = wantsSprint && canSprint && v > 0.1f;

        if (isSprinting)
            _stamina?.Drain();
        else
            _stamina?.Recover();

        float speed = isSprinting ? sprintSpeed : walkSpeed;

        Vector3 move = transform.right * h + transform.forward * v;
        if (move.sqrMagnitude > 1f)
            move.Normalize();

        return move * speed;
    }

    // ── Gravity ───────────────────────────────────────────────────────────────
    void ApplyGravity(Vector3 planarVelocity)
    {
        if (_isGrounded && _verticalVelocity < 0f)
            _verticalVelocity = 0f;

        bool hasBufferedJump = Time.time <= _jumpBufferedUntil;
        bool canUseGroundedJump = Time.time <= _groundedUntil;
        if (hasBufferedJump && canUseGroundedJump)
        {
            _verticalVelocity = jumpForce;
            _isGrounded = false;
            _jumpBufferedUntil = 0f;
            _groundedUntil = 0f;
        }

        _verticalVelocity += Physics.gravity.y * Time.deltaTime;
        Vector3 frameVelocity = planarVelocity + Vector3.up * _verticalVelocity;
        CollisionFlags flags = _cc.Move(frameVelocity * Time.deltaTime);
        _isGrounded = ((flags & CollisionFlags.Below) != 0) || _cc.isGrounded;
        if (_isGrounded)
            _groundedUntil = Time.time + CoyoteTime;

        if (_isGrounded && _verticalVelocity < 0f)
            _verticalVelocity = 0f;
    }

    // ── Head Bob ─────────────────────────────────────────────────────────────
    void HandleHeadBob()
    {
        bool isMoving = new Vector3(_cc.velocity.x, 0f, _cc.velocity.z).sqrMagnitude > 0.04f && _isGrounded;

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

        // ── Pushing ───────────────────────────────────────────────────────────────
    void OnControllerColliderHit(ControllerColliderHit hit)
    {
        Rigidbody rb = hit.collider.attachedRigidbody;
        if (rb == null || rb.isKinematic) return;

        WorldObject wo = hit.collider.GetComponentInParent<WorldObject>();
        if (wo == null || !wo.canBePushed) return;

        // Ignore downward hits — standing on top of an object should not push it down.
        if (hit.moveDirection.y < -0.3f) return;

        // Push horizontally in the direction we are moving into the object.
        Vector3 pushDir = new Vector3(hit.moveDirection.x, 0f, hit.moveDirection.z);
        if (pushDir.sqrMagnitude < 0.01f) return;
        pushDir.Normalize();

        if (rb.mass <= 0f) return;

        float pushSpeed = pushForce / rb.mass;
        Vector3 currentHorizontal = new Vector3(rb.velocity.x, 0f, rb.velocity.z);
        Vector3 desiredHorizontal = pushDir * pushSpeed;
        Vector3 nextHorizontal = Vector3.MoveTowards(currentHorizontal, desiredHorizontal, pushSpeed * 0.35f);

        rb.velocity = new Vector3(nextHorizontal.x, rb.velocity.y, nextHorizontal.z);
        rb.angularVelocity = Vector3.zero;
    }
}
