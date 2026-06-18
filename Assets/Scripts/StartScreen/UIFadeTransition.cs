using UnityEngine;
using UnityEngine.UI;
using System.Collections;
using System.Collections.Generic;

[RequireComponent(typeof(Button))]
public class ButtonListFadeTransition : MonoBehaviour
{
    [Header("需要淡出隐藏的 Button 列表")]
    [SerializeField] private List<Button> buttonsToHide;

    [Header("需要淡入显示的 Button 列表")]
    [SerializeField] private List<Button> buttonsToShow;

    [Header("时间参数")]
    [Tooltip("点击后，延迟多少秒开始让下一组按钮淡入")]
    [SerializeField] private float delayBeforeShow = 1.5f;

    [Tooltip("淡入淡出动画持续时间")]
    [SerializeField] private float fadeDuration = 0.5f;

    [Header("开局是否强制初始化此按钮组的状态？")]
    [Tooltip("如果是返回按钮，请取消勾选此项，否则开局主界面会被隐藏。系统会自动尝试识别带有 back 字样的按钮并跳过初始化。")]
    [SerializeField] private bool initializeOnStart = true;

    private Button triggerButton;
    private bool isTransitioning = false;

    // 用来缓存各个按钮的 CanvasGroup，免得重复获取
    private Dictionary<Button, CanvasGroup> hideGroups = new Dictionary<Button, CanvasGroup>();
    private Dictionary<Button, CanvasGroup> showGroups = new Dictionary<Button, CanvasGroup>();

    private void Start()
    {
        triggerButton = GetComponent<Button>();
        if (triggerButton != null)
        {
            triggerButton.onClick.AddListener(StartTransition);
        }

        // 自动检测是不是返回按钮，如果是，则强制取消开局初始化，避免互相覆盖
        if (gameObject.name.ToLower().Contains("back"))
        {
            initializeOnStart = false;
        }

        if (initializeOnStart)
        {
            // 初始化隐藏组：动态补上 CanvasGroup，并全部隐形、禁用点按
            InitButtonGroups(buttonsToHide, hideGroups, 1f, true);
            // 初始化显示组：开局完全透明、无法点按
            InitButtonGroups(buttonsToShow, showGroups, 0f, false);
        }
        else
        {
            // 对于返回按钮，我们只获取或添加 CanvasGroup 到字典里备用，但不强行修改它们的 alpha 状态
            PopulateCanvasGroups(buttonsToHide, hideGroups);
            PopulateCanvasGroups(buttonsToShow, showGroups);
        }
    }

    private void PopulateCanvasGroups(List<Button> btnList, Dictionary<Button, CanvasGroup> dict)
    {
        foreach (var btn in btnList)
        {
            if (btn == null) continue;
            CanvasGroup cg = btn.GetComponent<CanvasGroup>();
            if (cg == null)
            {
                cg = btn.gameObject.AddComponent<CanvasGroup>();
            }
            if (!dict.ContainsKey(btn))
            {
                dict.Add(btn, cg);
            }
        }
    }

    private void InitButtonGroups(List<Button> btnList, Dictionary<Button, CanvasGroup> dict, float targetAlpha, bool interactable)
    {
        foreach (var btn in btnList)
        {
            if (btn == null) continue;

            // 如果物体上没有 CanvasGroup，代码动态帮它加上，省去手动操作
            CanvasGroup cg = btn.GetComponent<CanvasGroup>();
            if (cg == null)
            {
                cg = btn.gameObject.AddComponent<CanvasGroup>();
            }

            cg.alpha = targetAlpha;
            cg.blocksRaycasts = interactable;
            cg.interactable = interactable;

            if (!dict.ContainsKey(btn))
            {
                dict.Add(btn, cg);
            }
        }
    }

    private void StartTransition()
    {
        if (isTransitioning) return;
        StartCoroutine(TransitionRoutine());
    }

    private IEnumerator TransitionRoutine()
    {
        isTransitioning = true;
        if (triggerButton != null) triggerButton.interactable = false;

        // 1. 【淡出阶段】
        // 瞬间关掉要消失按钮的点击检测，防止淡出时被玩家误点
        foreach (var cg in hideGroups.Values)
        {
            if (cg != null)
            {
                cg.blocksRaycasts = false;
                cg.interactable = false;
            }
        }

        float counter = 0f;
        while (counter < fadeDuration)
        {
            counter += Time.unscaledDeltaTime;
            float alpha = Mathf.Lerp(1f, 0f, counter / fadeDuration);
            
            foreach (var cg in hideGroups.Values)
            {
                if (cg != null) cg.alpha = alpha;
            }
            yield return null;
        }

        // 2. 【延迟等待】
        yield return new WaitForSecondsRealtime(delayBeforeShow);

        // 3. 【淡入阶段】
        counter = 0f;
        while (counter < fadeDuration)
        {
            counter += Time.unscaledDeltaTime;
            float alpha = Mathf.Lerp(0f, 1f, counter / fadeDuration);
            
            foreach (var cg in showGroups.Values)
            {
                if (cg != null) cg.alpha = alpha;
            }
            yield return null;
        }

        // 淡入完毕，恢复新按钮的点击检测
        foreach (var cg in showGroups.Values)
        {
            if (cg != null)
            {
                cg.alpha = 1f;
                cg.blocksRaycasts = true;
                cg.interactable = true;
            }
        }

        isTransitioning = false;
        if (triggerButton != null) triggerButton.interactable = true;
    }

    private void OnDestroy()
    {
        if (triggerButton != null)
        {
            triggerButton.onClick.RemoveListener(StartTransition);
        }
    }
}