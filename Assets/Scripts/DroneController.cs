using UnityEngine;
using UnityEngine.UI;
using RVSystem;

public class DroneController : MonoBehaviour
{
    [Header("Movement Settings")]
    public float speed = 5f;
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
        
        // Attach camera to drone
        _droneCamera.transform.SetParent(transform);
        _droneCamera.transform.localPosition = Vector3.zero;
        _droneCamera.transform.localRotation = Quaternion.identity;

        _pitch = transform.eulerAngles.x;
        _yaw = transform.eulerAngles.y;

        // Ensure Drone has physical components for RV detection and collisions
        Rigidbody rb = GetComponent<Rigidbody>();
        if (rb == null) rb = gameObject.AddComponent<Rigidbody>();
        rb.isKinematic = false; // Needs to collide with walls
        rb.useGravity = false;  // Drone hovers
        rb.freezeRotation = true; // Prevents spinning out of control on collision
        rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;

        Collider col = GetComponent<Collider>();
        if (col == null)
        {
            SphereCollider sCol = gameObject.AddComponent<SphereCollider>();
            sCol.radius = 0.2f;
            col = sCol;
        }
        col.isTrigger = false; // Needs to bounce off walls instead of passing through

        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;
    }

    private float _debugTimer = 0f;
    void Update()
    {
        if (_droneCamera == null)
        {
            _debugTimer += Time.deltaTime;
            if (_debugTimer >= 1f)
            {
                _debugTimer = 0f;
                Debug.LogWarning("[DroneController] Camera is null! The drone is active but has no bound camera.");
            }
            return;
        }

        _debugTimer += Time.deltaTime;
        if (_debugTimer >= 1f)
        {
            _debugTimer = 0f;
            Transform parent = _droneCamera.transform.parent;
            Debug.Log($"[Drone Diagnostics] Drone WorldPos: {transform.position} | Cam Parent: {(parent != null ? parent.name : "null")} | Cam LocalPos: {_droneCamera.transform.localPosition} | Cam WorldPos: {_droneCamera.transform.position}");
            
            // 🌟 列出场景中所有处于激活状态的摄像机，揪出到底是哪台相机在占屏渲染
            Camera[] cams = FindObjectsOfType<Camera>();
            string camLog = $"[Camera Diagnostics] Found {cams.Length} active cameras in scene:\n";
            foreach (Camera c in cams)
            {
                camLog += $"- '{c.name}' | Tag: '{c.tag}' | Enabled: {c.enabled} | Depth: {c.depth} | TargetTexture: {(c.targetTexture != null ? c.targetTexture.name : "null")} | WorldPos: {c.transform.position}\n";
            }
            Debug.Log(camLog);
        }

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

        Vector3 move = transform.right * h + transform.forward * v + Vector3.up * up;
        
        Rigidbody rb = GetComponent<Rigidbody>();
        if (rb != null)
        {
            rb.velocity = move * speed;
        }
        else
        {
            transform.position += move * (speed * Time.deltaTime);
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
            if (_holdTimer >= requiredHoldTime)
            {
                _hasSoul = true;
                _holdTimer = 0f;
                // Destroy or hide the corpse once retrieved
                if (_targetCorpse != null)
                {
                    Destroy(_targetCorpse.gameObject);
                }
            }
        }
        else
        {
            _holdTimer = 0f;
        }
    }

    void OnCollisionEnter(Collision collision)
    {
        CheckHitRV(collision.collider);
    }

    void OnTriggerEnter(Collider other)
    {
        CheckHitRV(other);
    }

    void CheckHitRV(Collider other)
    {
        if (!_hasSoul) return;

        // Check if we hit the RV
        RVController rv = other.GetComponentInParent<RVController>();
        if (rv != null)
        {
            if (_flowController != null)
            {
                _flowController.CompleteRevive();
            }
        }
    }

    void OnGUI()
    {
        if (_droneCamera == null) return;

        if (_hasSoul)
        {
            GUI.Label(new Rect(Screen.width / 2 - 100, Screen.height / 2 + 50, 200, 30), "<color=cyan>Soul Retrieved! Return to RV.</color>");
            return;
        }

        Ray ray = new Ray(_droneCamera.transform.position, _droneCamera.transform.forward);
        if (Physics.Raycast(ray, out RaycastHit hit, interactDistance))
        {
            if (hit.collider.transform.root.name.Contains("PlayerCorpse"))
            {
                if (_holdTimer > 0)
                {
                    float progress = _holdTimer / requiredHoldTime * 100f;
                    GUI.Label(new Rect(Screen.width / 2 - 100, Screen.height / 2 + 50, 200, 30), $"<color=yellow>Retrieving... {progress:F0}%</color>");
                }
                else
                {
                    GUI.Label(new Rect(Screen.width / 2 - 100, Screen.height / 2 + 50, 200, 30), "<color=white>[Hold F] Retrieve Corpse</color>");
                }
            }
        }
    }
}
