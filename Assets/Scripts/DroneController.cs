using UnityEngine;
using UnityEngine.UI;
using RVSystem;

public class DroneController : MonoBehaviour
{
    [Header("Movement Settings")]
    public float speed = 12.5f; // 已调整：原5f * 250%
    public float lookSensitivity = 2f;
    
    [Header("Interaction Settings")]
    public float interactDistance = 5f;
    public float requiredHoldTime = 3f;
    
    private Camera _droneCamera;
    private float _pitch = 0f;
    private float _yaw = 0f;
    
    private Transform _targetCorpse;
    private float _holdTimer = 0f;
    private bool _hasSoul = false;

    private PlayerDeathFlowController _flowController;

    public void Initialize(Camera cam, PlayerDeathFlowController flowController)
    {
        _droneCamera = cam;
        _flowController = flowController;
        
        _droneCamera.transform.SetParent(transform);
        _droneCamera.transform.localPosition = Vector3.zero;
        _droneCamera.transform.localRotation = Quaternion.identity;

        _pitch = transform.eulerAngles.x;
        _yaw = transform.eulerAngles.y;

        Rigidbody rb = GetComponent<Rigidbody>();
        if (rb == null) rb = gameObject.AddComponent<Rigidbody>();
        rb.isKinematic = false;
        rb.useGravity = false;
        rb.freezeRotation = true;
        rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;

        Collider col = GetComponent<Collider>();
        if (col == null)
        {
            SphereCollider sCol = gameObject.AddComponent<SphereCollider>();
            sCol.radius = 0.2f;
            col = sCol;
        }
        col.isTrigger = false;

        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;
    }

    void Update()
    {
        if (_droneCamera == null) return;

        HandleLook();
        HandleMovement();
        HandleInteraction();
    }

    void HandleLook()
    {
        float mouseX = Input.GetAxis("Mouse X") * lookSensitivity;
        float mouseY = Input.GetAxis("Mouse Y") * lookSensitivity;

        _yaw += mouseX;
        _pitch -= mouseY;
        _pitch = Mathf.Clamp(_pitch, -89f, 89f);

        // 直接旋转无人机本体，使视角与物理碰撞体绑定
        transform.rotation = Quaternion.Euler(0f, _yaw, 0f);
        _droneCamera.transform.localRotation = Quaternion.Euler(_pitch, 0f, 0f);
    }

    void HandleMovement()
    {
        float h = Input.GetAxis("Horizontal");
        float v = Input.GetAxis("Vertical");
        float up = 0f;

        if (Input.GetKey(KeyCode.Space)) up = 1f;
        if (Input.GetKey(KeyCode.LeftShift)) up = -1f;

        // Use camera's transform for fully relative movement
        Vector3 forward = _droneCamera.transform.forward;
        Vector3 right = _droneCamera.transform.right;

        // Flatten the vectors so movement remains perfectly horizontal on WASD
        forward.y = 0f;
        forward.Normalize();
        right.y = 0f;
        right.Normalize();

        Vector3 move = right * h + forward * v + Vector3.up * up;
        
        Rigidbody rb = GetComponent<Rigidbody>();
        if (rb != null)
        {
            rb.velocity = move * speed;
        }
    }

    void HandleInteraction()
    {
        if (_hasSoul) return;

        Ray ray = new Ray(_droneCamera.transform.position, _droneCamera.transform.forward);
        bool aimingAtCorpse = false;

        if (Physics.Raycast(ray, out RaycastHit hit, interactDistance))
        {
            if (hit.collider.transform.root.name.Contains("PlayerCorpse"))
            {
                aimingAtCorpse = true;
                _targetCorpse = hit.collider.transform.root;
            }
        }

        if (aimingAtCorpse && Input.GetKey(KeyCode.F))
        {
            _holdTimer += Time.deltaTime;
            float progress = Mathf.Clamp01(_holdTimer / requiredHoldTime);
            UpdateSlider(progress, true);

            if (_holdTimer >= requiredHoldTime)
            {
                _hasSoul = true;
                _holdTimer = 0f;
                UpdateSlider(0f, false);
                
                if (_targetCorpse != null)
                {
                    if (Application.isPlaying) Destroy(_targetCorpse.gameObject);
                    else UnityEngine.Object.DestroyImmediate(_targetCorpse.gameObject);
                }
            }
        }
        else
        {
            _holdTimer = 0f;
            UpdateSlider(0f, false);
        }
    }

    void OnCollisionEnter(Collision collision) { CheckHitRV(collision.collider); }
    void OnTriggerEnter(Collider other) { CheckHitRV(other); }

    void CheckHitRV(Collider other)
    {
        if (!_hasSoul || other.isTrigger) return;
        Transform current = other.transform;
        while (current != null)
        {
            if (current.GetComponent<RVController>() != null || 
                current.name.ToUpperInvariant().Contains("RV") || 
                current.name.ToUpperInvariant().Contains("UV"))
            {
                if (_flowController != null) _flowController.CompleteRevive();
                return;
            }
            current = current.parent;
        }
    }

    private Slider _cachedSlider;
    private void UpdateSlider(float progress, bool active)
    {
        if (_cachedSlider == null)
        {
            Slider[] sliders = Resources.FindObjectsOfTypeAll<Slider>();
            foreach (Slider s in sliders)
            {
                if (s.gameObject.scene.name == null) continue;
                string nameUpper = s.name.ToUpperInvariant();
                if (nameUpper.Contains("REVIVE") || nameUpper.Contains("SOUL") || nameUpper.Contains("PROGRESS"))
                {
                    _cachedSlider = s;
                    break;
                }
            }
        }
        if (_cachedSlider != null)
        {
            _cachedSlider.gameObject.SetActive(active);
            if (active) _cachedSlider.value = progress;
        }
    }
}