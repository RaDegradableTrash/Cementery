using UnityEngine;

/// <summary>
/// First-person camera controller. Can be placed anywhere in the hierarchy —
/// it follows the assigned Player transform by code rather than parenting.
///
/// Setup:
///   1. Attach this script to your Main Camera GameObject.
///   2. Drag the Player root into the "Player" field in the Inspector.
///   3. Adjust "Eye Offset" to position the lens at head height (default: 1.65 m).
/// </summary>
public class MouseLook : MonoBehaviour
{
    [Header("Target")]
    [SerializeField] private Transform player;
    [Tooltip("Local offset from the player pivot to the eye position (metres).")]
    [SerializeField] private Vector3 eyeOffset = new Vector3(0f, 1.65f, 0f);

    [Header("Sensitivity")]
    [SerializeField] private float sensitivityX = 2f;
    [SerializeField] private float sensitivityY = 2f;

    [Header("Vertical Clamp (degrees)")]
    [SerializeField] private float minVertical = -80f;
    [SerializeField] private float maxVertical =  80f;

    // ── State ─────────────────────────────────────────────────────────────────
    private float _pitch;
    private PlayerController _playerController;

    // ── Lifecycle ─────────────────────────────────────────────────────────────
    void Start()
    {
        LockCursor();

        _pitch = transform.eulerAngles.x;
        if (_pitch > 180f) _pitch -= 360f;

        if (player != null)
            _playerController = player.GetComponent<PlayerController>();
    }

    void Update()
    {
        HandleCursorToggle();
        if (Cursor.lockState != CursorLockMode.Locked) return;
        ApplyMouseLook();
    }

    /// LateUpdate runs after CharacterController.Move() — guarantees zero lag between
    /// player body and camera position.
    void LateUpdate()
    {
        if (player == null) return;

        Vector3 bob = _playerController != null
            ? _playerController.BobOffset
            : Vector3.zero;

        transform.position = player.position
            + player.TransformDirection(eyeOffset)
            + player.TransformDirection(bob);
    }

    // ── Look ──────────────────────────────────────────────────────────────────
    void ApplyMouseLook()
    {
        float mouseX = Input.GetAxis("Mouse X") * sensitivityX;
        float mouseY = Input.GetAxis("Mouse Y") * sensitivityY;

        // Vertical pitch — applied only to the camera
        _pitch -= mouseY;
        _pitch  = Mathf.Clamp(_pitch, minVertical, maxVertical);
        transform.localRotation = Quaternion.Euler(_pitch, 0f, 0f);

        // Horizontal yaw — rotates the player body so movement stays aligned with the view
        if (player != null)
            player.Rotate(Vector3.up * mouseX, Space.World);
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
}
