using UnityEngine;

[RequireComponent(typeof(WorldObject))]
public class FlashlightInteractable : MonoBehaviour
{
    [Header("挂载设置")]
    [Tooltip("玩家相机的 Transform")]
    [SerializeField] private Transform playerCamera;
    
    [Tooltip("手电筒相对于挂载点的局部坐标偏移")]
    [SerializeField] private Vector3 localOffset = new Vector3(-0.3f, -0.2f, 0.5f);
    
    [Tooltip("手电筒相对于挂载点的局部旋转角度")]
    [SerializeField] private Vector3 localRotationEuler = Vector3.zero;

    [Header("全局放下的按键")]
    [SerializeField] private KeyCode dropKey = KeyCode.BackQuote;

    [Header("手电筒组件")]
    [SerializeField] private Light flashlightLight;

    private WorldObject _worldObject;
    private Rigidbody _rb;
    private Collider _collider; // 【新增】用于获取并开关碰撞体
    private bool _isEquipped = false;
    private Transform _originalParent;

    void Awake()
    {
        _worldObject = GetComponent<WorldObject>();
        _rb = GetComponent<Rigidbody>();
        _collider = GetComponent<Collider>(); // 自动抓取碰撞体
        _originalParent = transform.parent;
    }

    void OnEnable()
    {
        if (_worldObject != null)
        {
            _worldObject.onInteract.AddListener(OnInteractedWith);
        }
    }

    void OnDisable()
    {
        if (_worldObject != null)
        {
            _worldObject.onInteract.RemoveListener(OnInteractedWith);
        }
    }

    /// <summary>
    /// 【拿起手电筒】
    /// </summary>
    private void OnInteractedWith(GameObject actor)
    {
        if (_isEquipped) return;

        if (playerCamera == null && actor != null)
        {
            Camera mainCam = actor.GetComponentInChildren<Camera>();
            if (mainCam != null) playerCamera = mainCam.transform;
        }

        if (playerCamera == null) return;

        _worldObject.TriggerPickUp(actor);
        _worldObject.CancelAnims();

        // 1. 【核心修复】关闭碰撞体，彻底杜绝和玩家物理碰撞导致的“自推”滑行
        if (_collider != null)
        {
            _collider.enabled = false;
        }

        // 2. 剥离刚体物理
        if (_rb != null)
        {
            _rb.isKinematic = true;
            _rb.useGravity = false;
            _rb.velocity = Vector3.zero;
            _rb.angularVelocity = Vector3.zero;
        }

        _worldObject.isPlacedAndAttached = true;

        // 3. 挂载到相机
        transform.SetParent(playerCamera);
        transform.localPosition = localOffset;
        transform.localRotation = Quaternion.Euler(localRotationEuler);

        if (flashlightLight != null) flashlightLight.enabled = true;

        _worldObject.interactable = false;
        _isEquipped = true;
    }

    /// <summary>
    /// 【丢下手电筒】
    /// </summary>
    public void DropFlashlight()
    {
        if (!_isEquipped) return;

        transform.SetParent(_originalParent);

        _worldObject.isPlacedAndAttached = false;
        if (_rb != null)
        {
            _rb.isKinematic = false;
            _rb.useGravity = true;
            if (playerCamera != null)
            {
                _rb.velocity = playerCamera.forward * 1.5f + Vector3.up * 0.5f;
            }
        }

        // 4. 【核心修复】丢到地上时，重新开启碰撞体，让它能正常落到地面上而不会穿模掉进虚空
        if (_collider != null)
        {
            _collider.enabled = true;
        }

        if (flashlightLight != null) flashlightLight.enabled = false;

        _worldObject.interactable = true;
        _worldObject.TriggerDrop(playerCamera != null ? playerCamera.root.gameObject : null);

        _isEquipped = false;
    }

    void Update()
    {
        if (_isEquipped)
        {
            if (transform.parent == playerCamera)
            {
                transform.localPosition = localOffset;
                transform.localRotation = Quaternion.Euler(localRotationEuler);
            }

            if (Input.GetKeyDown(dropKey))
            {
                DropFlashlight();
            }
        }
    }
}