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
    private Collider[] _colliders; // 【修改】改为数组，支持关闭自身及子物体所有的碰撞体
    private bool _isEquipped = false;
    private Transform _originalParent;
    private Quaternion _localRotation;

    void Awake()
    {
        _worldObject = GetComponent<WorldObject>();
        _rb = GetComponent<Rigidbody>();
        // 【修改】获取该物体以及所有子物体上的碰撞体，防止部分碰撞体漏网导致物理冲突
        _colliders = GetComponentsInChildren<Collider>(); 
        _originalParent = transform.parent;
        _localRotation = Quaternion.Euler(localRotationEuler);
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

        // 1. 【彻底关闭】循环关闭所有碰撞体，彻底杜绝和玩家的物理碰撞
        if (_colliders != null)
        {
            foreach (var col in _colliders)
            {
                if (col != null) col.enabled = false;
            }
        }

        // 2. 【彻底关闭】剥离并禁用 Rigidbody 组件
// 2. 【彻底关闭】剥离并禁用 Rigidbody 的物理检测
if (_rb != null)
{
    _rb.isKinematic = true;
    _rb.useGravity = false;
    _rb.velocity = Vector3.zero;
    _rb.angularVelocity = Vector3.zero;
    _rb.detectCollisions = false; // 【改为这个】让刚体彻底停止物理碰撞检测
}

        _worldObject.isPlacedAndAttached = true;

        // 3. 挂载到相机
        transform.SetParent(playerCamera);
        transform.localPosition = localOffset;
        transform.localRotation = _localRotation;

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
        
        // 4. 【恢复】重新启用 Rigidbody 组件并给予物理速度
// 4. 【恢复】重新启用 Rigidbody 并给予物理速度
if (_rb != null)
{
    _rb.detectCollisions = true; // 【改为这个】扔掉时重新开启刚体的碰撞检测
    _rb.isKinematic = false;
    _rb.useGravity = true;
    if (playerCamera != null)
    {
        _rb.velocity = playerCamera.forward * 1.5f + Vector3.up * 0.5f;
    }
}

        // 5. 【恢复】重新开启所有碰撞体，使其能正常落到地面上
        if (_colliders != null)
        {
            foreach (var col in _colliders)
            {
                if (col != null) col.enabled = true;
            }
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
                if ((transform.localPosition - localOffset).sqrMagnitude > 0.000001f)
                {
                    transform.localPosition = localOffset;
                }

                if (Quaternion.Angle(transform.localRotation, _localRotation) > 0.01f)
                {
                    transform.localRotation = _localRotation;
                }
            }

            if (Input.GetKeyDown(dropKey))
            {
                DropFlashlight();
            }
        }
    }
}
