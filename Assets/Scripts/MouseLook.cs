using UnityEngine;

/// <summary>
/// Attach to the Camera (child of Player). Handles mouse-look for vertical rotation.
/// Horizontal rotation is applied to the player body.
/// </summary>
public class MouseLook : MonoBehaviour
{
    [Header("Sensitivity")]
    public float sensitivityX = 2f;
    public float sensitivityY = 2f;

    [Header("Vertical Clamp")]
    public float minVertical = -80f;
    public float maxVertical = 80f;

    [Tooltip("Assign the root Player transform so horizontal rotation is applied to the body.")]
    public Transform playerBody;

    private float _verticalAngle;

    void Start()
    {
        LockCursor();
    }

    void Update()
    {
        HandleCursorToggle();

        if (Cursor.lockState != CursorLockMode.Locked) return;

        float mouseX = Input.GetAxis("Mouse X") * sensitivityX;
        float mouseY = Input.GetAxis("Mouse Y") * sensitivityY;

        // Vertical rotation on camera
        _verticalAngle -= mouseY;
        _verticalAngle = Mathf.Clamp(_verticalAngle, minVertical, maxVertical);
        transform.localRotation = Quaternion.Euler(_verticalAngle, 0f, 0f);

        // Horizontal rotation on player body
        if (playerBody != null)
            playerBody.Rotate(Vector3.up * mouseX);
    }

    void HandleCursorToggle()
    {
        if (Input.GetKeyDown(KeyCode.Escape))
        {
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }
        else if (Input.GetMouseButtonDown(0) && Cursor.lockState != CursorLockMode.Locked)
        {
            LockCursor();
        }
    }

    void LockCursor()
    {
        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;
    }
}
