using UnityEngine;
using UnityEngine.UI;
using TMPro; // 引入 TMP 命名空间

public class GlobalVolumeController : MonoBehaviour
{
    [Header("UI 组件绑定")]
    [Tooltip("把你要控制的 Slider 拖到这里（若挂在 Slider 上可不拖，会自动抓取）")]
    [SerializeField] private Slider volumeSlider;

    [Tooltip("（可选）把你用来显示 100% 百分比的 TextMeshPro 拖到这里")]
    [SerializeField] private TextMeshProUGUI percentageText;

    private void Start()
    {
        // 1. 防呆：如果你忘了拖 Slider 槽位，自动拿挂载物体上的组件
        if (volumeSlider == null)
        {
            volumeSlider = GetComponent<Slider>();
        }

        if (volumeSlider != null)
        {
            // 2. 强行规范 Slider 的最大最小值（0 是静音，1 是满音量）
            volumeSlider.minValue = 0f;
            volumeSlider.maxValue = 1f;

            // 3. 核心死命令：开局游戏，全局音量直接拉满 100%
            AudioListener.volume = 1f;
            volumeSlider.value = 1f;

            // 4. 监听鼠标在 3D 空间中的实时拖拽滑动
            volumeSlider.onValueChanged.AddListener(OnVolumeChanged);

            // 5. 初始刷新一次文本
            UpdateTextDisplay(1f);
        }
        else
        {
            Debug.LogError($"[{gameObject.name}] 错误：未找到 Slider 组件！请拖拽赋值或检查挂载。");
        }
    }

    // 当滑动条被拖动时触发
    private void OnVolumeChanged(float value)
    {
        // 实时修改整个 Unity 引擎的总音量
        AudioListener.volume = value;

        // 实时更新百分比文字
        UpdateTextDisplay(value);
    }

    // 格式化文本显示（把 0f ~ 1f 的小数转成 0% ~ 100%）
    private void UpdateTextDisplay(float value)
    {
        if (percentageText != null)
        {
            int percent = Mathf.RoundToInt(value * 100f);
            percentageText.text = percent + "";
        }
    }

    private void OnDestroy()
    {
        // 移除监听，防止内存泄漏或报错
        if (volumeSlider != null)
        {
            volumeSlider.onValueChanged.RemoveListener(OnVolumeChanged);
        }
    }
}