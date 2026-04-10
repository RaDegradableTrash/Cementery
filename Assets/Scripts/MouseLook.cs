using UnityEngine;

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

    [Header("Sensitivity")]
    [SerializeField] private float sensitivityX = 2f;
    [SerializeField] private float sensitivityY = 2f;

    [Header("Vertical Clamp (degrees)")]
    [SerializeField] private float minVertical = -80f;
    [SerializeField] private float maxVertical =  80f;

    [Header("Inventory")]
    [SerializeField] private InventoryCameraController inventoryCameraController;

    // ── State ─────────────────────────────────────────────────────────────────
    private float _pitch;
    private PlayerController _playerController;
    private Vector3 _holderDefaultLocalPos;

    // ── Lifecycle ─────────────────────────────────────────────────────────────
    void Start()
    {
        LockCursor();

        if (inventoryCameraController == null)
            inventoryCameraController = InventoryCameraController.GetPrimaryController();
        if (inventoryCameraController == null)
            inventoryCameraController = FindObjectOfType<InventoryCameraController>();

        _pitch = transform.eulerAngles.x;
        if (_pitch > 180f) _pitch -= 360f;

        if (player != null)
            _playerController = player.GetComponent<PlayerController>();

        if (cameraHolder != null)
            _holderDefaultLocalPos = cameraHolder.localPosition;
    }

    void Update()
    {
        if (IsInventoryModeActive())
            return;

        HandleCursorToggle();
        if (Cursor.lockState != CursorLockMode.Locked) return;
        ApplyMouseLook();
    }

    // LateUpdate: runs after CharacterController.Move() — apply bob to CameraHolderEmpty.
    void LateUpdate()
    {
        if (cameraHolder == null) return;

        Vector3 bob = _playerController != null
            ? _playerController.BobOffset
            : Vector3.zero;

        cameraHolder.localPosition = _holderDefaultLocalPos + bob;
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
