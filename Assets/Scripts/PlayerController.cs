using UnityEngine;

/// <summary>
/// First-person player controller. Requires a CharacterController component.
/// Supports: WASD movement, sprinting (Left Shift), jumping (Space), gravity, head bobbing.
/// </summary>
[RequireComponent(typeof(CharacterController))]
public class PlayerController : MonoBehaviour
{
    [Header("Movement")]
    public float walkSpeed = 4f;
    public float sprintSpeed = 7f;
    public float jumpHeight = 1.2f;
    public float gravity = -20f;

    [Header("Ground Check")]
    [Tooltip("Extra distance below the capsule bottom to check for ground.")]
    public float groundCheckDistance = 0.08f;
    public LayerMask groundMask = ~0;

    [Header("Head Bob")]
    [SerializeField] private float bobFrequency = 1.8f;
    [SerializeField] private float bobAmplitude = 0.06f;

    /// <summary>
    /// Additive eye-space offset produced by head bobbing.
    /// MouseLook reads this each LateUpdate to nudge the camera.
    /// </summary>
    public Vector3 BobOffset { get; private set; }

    // ── Internal State ──────────────────────────────────────────────────────
    private CharacterController _cc;
    private PlayerStamina _stamina;

    private Vector3 _yVelocity;
    private float _bobTimer;
    private bool _isGrounded;

    // ── Lifecycle ────────────────────────────────────────────────────────────
    void Awake()
    {
        _cc      = GetComponent<CharacterController>();
        _stamina = GetComponent<PlayerStamina>();
    }

    void Update()
    {
        CheckGround();
        HandleMovement();
        ApplyGravity();
        HandleHeadBob();
    }

    // ── Ground Detection ─────────────────────────────────────────────────────
    void CheckGround()
    {
        // Sphere at the bottom of the capsule
        Vector3 sphereCenter = transform.position + Vector3.down * (_cc.height * 0.5f - _cc.radius);
        _isGrounded = Physics.CheckSphere(
            sphereCenter,
            _cc.radius + groundCheckDistance,
            groundMask,
            QueryTriggerInteraction.Ignore);

        if (_isGrounded && _yVelocity.y < 0f)
            _yVelocity.y = -2f; // keeps grounded without fighting the floor
    }

    // ── Movement ─────────────────────────────────────────────────────────────
    void HandleMovement()
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
        if (move.sqrMagnitude > 1f) move.Normalize();

        _cc.Move(move * speed * Time.deltaTime);

        // Jump
        if (Input.GetButtonDown("Jump") && _isGrounded)
            _yVelocity.y = Mathf.Sqrt(jumpHeight * -2f * gravity);
    }

    // ── Gravity ───────────────────────────────────────────────────────────────
    void ApplyGravity()
    {
        _yVelocity.y += gravity * Time.deltaTime;
        _cc.Move(_yVelocity * Time.deltaTime);
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
}
