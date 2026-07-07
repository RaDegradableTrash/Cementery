using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Tracks sprint stamina. Attach to the Player GameObject alongside PlayerController.
/// </summary>
[RequireComponent(typeof(Rigidbody))] // 确保安全获取刚体
public class PlayerStamina : MonoBehaviour
{
    [Header("Stamina Values")]
    public float maxStamina = 100f;
    [Tooltip("Stamina drained per second while sprinting.")]
    public float drainRate = 20f;
    [Tooltip("Stamina recovered per second while not sprinting.")]
    public float recoverRate = 10f;
    [Tooltip("Seconds of idle time before recovery begins.")]
    public float recoverDelay = 1.5f;

    [Header("UI")]
    [Tooltip("Assign a UI Image (Image Type = Filled, Fill Method = Horizontal) to show the stamina bar.")]
    public Image staminaBarFill;

    [Header("绑定玩家控制器")]
    [Tooltip("把挂载了 PlayerController 的玩家物体拖到这里，脚本会自动获取组件")]
    public PlayerController playerController; 

    public bool HasStamina => _stamina > 1f;

    private float _stamina;
    private float _recoverCooldown;
    private Rigidbody _rb;
    
    // 用于控制淡入淡出的 CanvasGroup
    private CanvasGroup _uiCanvasGroup; 
    private float _lastFillAmount = -1f;
    private float _lastTargetAlpha = -1f;

    void Awake()
    {
        _stamina = maxStamina;

        if (playerController == null)
        {
            playerController = GetComponent<PlayerController>();
        }

        _rb = GetComponent<Rigidbody>();

        // 自动初始化或获取 UI 容器上的 CanvasGroup 组件
        if (staminaBarFill != null && staminaBarFill.transform.parent != null)
        {
            GameObject container = staminaBarFill.transform.parent.gameObject;
            _uiCanvasGroup = container.GetComponent<CanvasGroup>();
            if (_uiCanvasGroup == null)
            {
                _uiCanvasGroup = container.AddComponent<CanvasGroup>();
            }
        }
    }

    void Update()
    {
        if (playerController != null)
        {
            if (!IsUiSettled())
                UpdateUI();
            return;
        }

        bool isPressingSprint = Input.GetKey(KeyCode.LeftShift);

        // 使用刚体速度判断，增加了安全兼容：如果新版本没有 linearVelocity 就自动用旧版的 velocity
        bool isMoving = false;
        if (_rb != null)
        {
#if UNITY_6000_0_OR_NEWER
            Vector3 velocity = _rb.linearVelocity;
#else
            Vector3 velocity = _rb.velocity;
#endif
            float horizontalSpeedSqr = velocity.x * velocity.x + velocity.z * velocity.z;
            isMoving = horizontalSpeedSqr > 0.09f; // 略微调高阈值，更加稳健
        }

        // 状态判定逻辑
        if (isPressingSprint && isMoving && HasStamina)
        {
            Drain(); 
        }
        else
        {
            Recover(); 
        }

        UpdateUI();
    }

    public void Drain()
    {
        _stamina = Mathf.Max(0f, _stamina - drainRate * Time.deltaTime);
        _recoverCooldown = recoverDelay;
    }

    public void Recover()
    {
        if (_recoverCooldown > 0f)
        {
            _recoverCooldown -= Time.deltaTime;
            return;
        }
        _stamina = Mathf.Min(maxStamina, _stamina + recoverRate * Time.deltaTime);
    }

    float NormalizedStamina => _stamina / maxStamina;

    bool IsUiSettled()
    {
        float normalized = NormalizedStamina;
        bool staminaFull = Mathf.Abs(_stamina - maxStamina) <= 0.001f;
        bool recoverySettled = _recoverCooldown <= 0f;
        bool fillSynced = _lastFillAmount >= 0f && Mathf.Abs(_lastFillAmount - normalized) <= 0.001f;
        bool alphaHidden = _uiCanvasGroup == null || _uiCanvasGroup.alpha <= 0.001f;
        bool alphaTargetHidden = Mathf.Approximately(_lastTargetAlpha, 0f);
        return staminaFull && recoverySettled && fillSynced && alphaHidden && alphaTargetHidden;
    }

    void UpdateUI()
    {
        if (staminaBarFill == null) return;

        float normalized = NormalizedStamina;
        if (Mathf.Abs(_lastFillAmount - normalized) > 0.001f)
        {
            _lastFillAmount = normalized;
            staminaBarFill.fillAmount = normalized;
        }

        // 2. 使用 CanvasGroup 的 alpha 进行平滑淡入淡出，彻底告别 SetActive 带来的物理抽搐
        if (_uiCanvasGroup != null)
        {
            // 如果体力不满 98%，说明在消耗中，目标透明度为 1（显示）；否则满状态目标透明度为 0（隐藏）
            float targetAlpha = (normalized < 0.98f) ? 1f : 0f;
            
            // 每帧平滑过渡透明度
            float nextAlpha = Mathf.MoveTowards(_uiCanvasGroup.alpha, targetAlpha, Time.deltaTime * 5f);
            if (Mathf.Abs(_uiCanvasGroup.alpha - nextAlpha) > 0.001f || _lastTargetAlpha != targetAlpha)
            {
                _uiCanvasGroup.alpha = nextAlpha;
                _lastTargetAlpha = targetAlpha;
            }
        }
    }
}
