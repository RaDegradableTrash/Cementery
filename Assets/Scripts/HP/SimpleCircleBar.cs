using UnityEngine;
using UnityEngine.UI;

public class SimpleCircleBar : MonoBehaviour
{
    // 单例模式：让 PlayerController 可以随时一句话访问它
    public static SimpleCircleBar Instance;

    [Header("把你的 HP_Fill 图片拖到这里")]
    public Image hpFillImage;

    [Header("血条平滑过渡速度")]
    public float smoothSpeed = 5f;

    private float targetFillAmount = 1f;

    void Awake()
    {
        Instance = this; // 初始化单例
        enabled = false;
    }

    void Update()
    {
        if (hpFillImage == null)
        {
            enabled = false;
            return;
        }

        if (Mathf.Abs(hpFillImage.fillAmount - targetFillAmount) < 0.001f)
        {
            hpFillImage.fillAmount = targetFillAmount;
            enabled = false;
            return;
        }

        // 让血条圆环平滑地转动到目标血量
        hpFillImage.fillAmount = Mathf.Lerp(hpFillImage.fillAmount, targetFillAmount, Time.deltaTime * smoothSpeed);
    }

    /// <summary>
    /// 外部调用的更新血条方法
    /// </summary>
    /// <param name="currentHP">当前血量</param>
    /// <param name="maxHP">最大血量</param>
    public void UpdateHealthBar(float currentHP, float maxHP)
    {
        if (maxHP <= 0) return;
        // 计算出 0 ~ 1 之间的比例
        float nextFillAmount = Mathf.Clamp01(currentHP / maxHP);
        bool targetChanged = Mathf.Abs(nextFillAmount - targetFillAmount) >= 0.001f;
        bool fillNeedsSync = hpFillImage != null && Mathf.Abs(hpFillImage.fillAmount - nextFillAmount) >= 0.001f;
        if (!targetChanged && !fillNeedsSync) return;

        targetFillAmount = nextFillAmount;
        enabled = true;
    }
}
