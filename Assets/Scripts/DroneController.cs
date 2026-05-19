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
            float progress = Mathf.Clamp01(_holdTimer / requiredHoldTime);
            UpdateSlider(progress, true); // 🌟 实时把进度同步给玩家新做的 Slider 并显示它！

            if (_holdTimer >= requiredHoldTime)
            {
                _hasSoul = true;
                _holdTimer = 0f;
                UpdateSlider(0f, false); // 🌟 完成吸取，自动隐去 Slider！
                
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
            UpdateSlider(0f, false); // 🌟 没有在长按吸取或没有瞄准，自动隐藏 Slider！
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
        if (other == null) return;

        // 必须碰到的非 isTrigger 的实体碰撞箱！
        if (other.isTrigger) return;

        // 向上搜寻挂载了 UV/RV 的较高级母物体
        Transform current = other.transform;
        RVController foundRv = null;
        Transform foundParent = null;

        while (current != null)
        {
            RVController rv = current.GetComponent<RVController>();
            if (rv != null)
            {
                foundRv = rv;
                foundParent = current;
            }

            string nameUpper = current.name.ToUpperInvariant();
            if (nameUpper.Contains("RV") || nameUpper.Contains("UV"))
            {
                foundParent = current;
            }

            current = current.parent;
        }

        if (foundRv != null || foundParent != null)
        {
            if (_flowController != null)
            {
                _flowController.CompleteRevive();
            }
        }
    }

    private Slider _cachedSlider;
    private void UpdateSlider(float progress, bool active)
    {
        if (_cachedSlider == null)
        {
            // 自动检索场景 Canvases 中名称带有 Revive/Soul/Progress 关键字的 Slider，或者兜底获取任意 Slider
            Slider[] sliders = Resources.FindObjectsOfTypeAll<Slider>();
            foreach (Slider s in sliders)
            {
                if (s.gameObject.scene.name == null) continue; // 排除预制体，只搜寻场景实例
                
                string nameUpper = s.name.ToUpperInvariant();
                if (nameUpper.Contains("REVIVE") || nameUpper.Contains("SOUL") || nameUpper.Contains("PROGRESS") || nameUpper.Contains("DEATH"))
                {
                    _cachedSlider = s;
                    break;
                }
            }
            if (_cachedSlider == null && sliders.Length > 0)
            {
                // 实在没有关键字匹配，就默认绑定场景中的第一个 Slider 物体
                _cachedSlider = sliders[0];
            }
        }

        if (_cachedSlider != null)
        {
            // 在起效时显示，其他时间自动休眠，保证 UI 界面高度整洁
            _cachedSlider.gameObject.SetActive(active);
            if (active)
            {
                _cachedSlider.minValue = 0f;
                _cachedSlider.maxValue = 1f;
                _cachedSlider.value = progress;
            }
        }
    }
}
