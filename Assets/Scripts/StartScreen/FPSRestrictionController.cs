using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using TMPro;

public class FPSRestrictionController : MonoBehaviour, IPointerUpHandler
{
    [Header("UI 组件绑定")]
    [SerializeField] private Slider fpsSlider;
    [SerializeField] private TextMeshProUGUI fpsText;

    // ==========================================
    // 💡 实时监控：运行游戏时，直接在 Inspector 面板看这里！
    [Header("📊 实时帧率监控 (Inspector Live View)")]
    [ReadOnlyInspector] [SerializeField] private string currentActualFPS = "0 FPS";
    // ==========================================

    private readonly float[] percentages = { 0.0f, 0.2f, 0.4f, 0.6f, 0.8f, 1.0f };
    private readonly int[] fpsValues = { 24, 30, 60, 120, 240, -1 };

    private float deltaTime = 0.0f;
    private float fpsUpdateTimer = 0.0f;

    private void Start()
    {
        if (fpsSlider == null) fpsSlider = GetComponent<Slider>();

        if (fpsSlider != null)
        {
            fpsSlider.minValue = 0f;
            fpsSlider.maxValue = 1f;
            fpsSlider.value = 1f;
            ApplyFPS(1.0f);

            fpsSlider.onValueChanged.AddListener(OnSliderDragging);
        }
    }

    // 每一帧计算实际 FPS
    private void Update()
    {
        // 累加每帧消耗时间（计算平均 delta，防止数值抖动太剧烈看不清）
        deltaTime += (Time.unscaledDeltaTime - deltaTime) * 0.1f;
        fpsUpdateTimer += Time.unscaledDeltaTime;

        // 每 0.2 秒在 Inspector 上刷新一次数字，既保持实时，又不会晃眼
        if (fpsUpdateTimer >= 0.2f)
        {
            float calculatedFps = 1.0f / deltaTime;
            currentActualFPS = $"{Mathf.RoundToInt(calculatedFps)} FPS";
            fpsUpdateTimer = 0.0f;
        }
    }

    private void OnSliderDragging(float rawValue)
    {
        UpdateTextDisplay(rawValue);
    }

    public void OnPointerUp(PointerEventData eventData)
    {
        if (fpsSlider == null) return;

        float currentVal = fpsSlider.value;
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

        fpsSlider.value = bestMatchPercent;
        ApplyFPS(bestMatchPercent);
    }

    private void ApplyFPS(float percent)
    {
        for (int i = 0; i < percentages.Length; i++)
        {
            if (Mathf.Approximately(percent, percentages[i])) 
            {
                int targetFPS = fpsValues[i];
                Application.targetFrameRate = targetFPS;
                UpdateTextDisplay(percent);
                break;
            }
        }
    }

    private void UpdateTextDisplay(float percent)
    {
        if (fpsText == null) return;

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

        int fps = fpsValues[closestIndex];
        fpsText.text = (fps == -1) ? "UNLIMITED" : $" {fps}";
    }

    private void OnDestroy()
    {
        if (fpsSlider != null) fpsSlider.onValueChanged.RemoveListener(OnSliderDragging);
    }
}

// 这是一个简易的自定义属性：让变量在 Inspector 里变成只读，防止你不小心手动去改它
public class ReadOnlyInspectorAttribute : PropertyAttribute { }

#if UNITY_EDITOR
[UnityEditor.CustomPropertyDrawer(typeof(ReadOnlyInspectorAttribute))]
public class ReadOnlyInspectorDrawer : UnityEditor.PropertyDrawer
{
    public override void OnGUI(Rect position, UnityEditor.SerializedProperty property, GUIContent label)
    {
        GUI.enabled = false; // 禁用输入框
        UnityEditor.EditorGUI.PropertyField(position, property, label);
        GUI.enabled = true;
    }
}
#endif