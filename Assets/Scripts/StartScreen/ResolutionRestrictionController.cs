using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using TMPro;

public class ResolutionRestrictionController : MonoBehaviour, IPointerUpHandler
{
    [Header("UI 组件绑定")]
    [SerializeField] private Slider resSlider;
    [SerializeField] private TextMeshProUGUI resText;

    // 档位定义：0% 到 100% 对应的目标“纵向物理像素高度 (P)”
    private readonly float[] percentages = { 0.0f, 0.2f, 0.4f, 0.6f, 0.8f, 1.0f };
    private readonly int[] targetHeights = { 480, 720, 1080, 1440, 2160, 4320 }; // 对应 480P, 720P, 1080P, 2K, 4K, 8K
    private readonly string[] resLabels = { "480P", "720P", "1080P", "2K", "4K", "8K" };

    private void Start()
    {
        if (resSlider == null) resSlider = GetComponent<Slider>();

        if (resSlider != null)
        {
            resSlider.minValue = 0f;
            resSlider.maxValue = 1f;

            // 默认开局给 1080P（40% 档位）或者 4K，这里我们默认拉到 1080P
            resSlider.value = 0.4f;
            ApplyResolution(0.4f);

            resSlider.onValueChanged.AddListener(OnSliderDragging);
        }
    }

    private void OnSliderDragging(float rawValue)
    {
        UpdateTextDisplay(rawValue);
    }

    // 💡 松手一瞬间，计算最近档位并吸附
    public void OnPointerUp(PointerEventData eventData)
    {
        if (resSlider == null) return;

        float currentVal = resSlider.value;
        float bestMatchPercent = percentages[0];
        float minDistance = Mathf.Abs(currentVal - percentages[0]);

        for (int i = 1; i < percentages.Length; i++)
        {
            float dist = Mathf.Abs(currentVal - percentages[i]);
            if (dist < minDistance)
            {
                minDistance = dist;
                bestMatchPercent = percentages[i];
            }
        }

        resSlider.value = bestMatchPercent;
        ApplyResolution(bestMatchPercent);
    }

    // 核心：动态计算非固定比例的分辨率并应用
    private void ApplyResolution(float percent)
    {
        for (int i = 0; i < percentages.Length; i++)
        {
            if (Mathf.Approximately(percent, percentages[i]))
            {
                int targetHeight = targetHeights[i];

                // 1. 获取玩家显示器当前的绝对原生宽高比（比如 1920/1080 = 1.7778）
                // 使用 Screen.currentResolution 确保拿到的是显示器物理比例，而不是当前窗口缩放比
                float currentAspectRatio = (float)Screen.currentResolution.width / Screen.currentResolution.height;

                // 2. 动态算出当前比例下，对应的宽度应该是多少
                int targetWidth = Mathf.RoundToInt(targetHeight * currentAspectRatio);

                // 3. 调用 Unity 原生 API 调整分辨率（保持玩家当前的窗口/全屏模式）
                Screen.SetResolution(targetWidth, targetHeight, Screen.fullScreenMode);

                Debug.Log($"[系统] 分辨率已自适应调整为: {targetWidth} x {targetHeight} ({resLabels[i]})");
                
                UpdateTextDisplay(percent);
                break;
            }
        }
    }

    private void UpdateTextDisplay(float percent)
    {
        if (resText == null) return;

        int closestIndex = 0;
        float minDistance = Mathf.Abs(percent - percentages[0]);
        for (int i = 1; i < percentages.Length; i++)
        {
            float dist = Mathf.Abs(percent - percentages[i]);
            if (dist < minDistance)
            {
                minDistance = dist;
                closestIndex = i;
            }
        }

        // 动态预览当前档位下的计算分辨率
        float currentAspectRatio = (float)Screen.currentResolution.width / Screen.currentResolution.height;
        int previewWidth = Mathf.RoundToInt(targetHeights[closestIndex] * currentAspectRatio);
        
        // TMP 文本显示格式： "RESOLUTION: 1920x1080 (1080P)"
        resText.text = $" {previewWidth}x{targetHeights[closestIndex]} ({resLabels[closestIndex]})";
    }

    private void OnDestroy()
    {
        if (resSlider != null) resSlider.onValueChanged.RemoveListener(OnSliderDragging);
    }
}
