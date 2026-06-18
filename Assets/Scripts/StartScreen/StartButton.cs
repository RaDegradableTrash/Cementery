using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using System.Collections;

public class StartButton : MonoBehaviour
{
    [SerializeField] private Button myButton;
    [SerializeField] private int targetIndex = 0;

    [Header("转场动画设计")]
    [Tooltip("按下按钮后，屏幕用多少秒变黑？（变黑后瞬间切场景）")]
    [SerializeField] private float fadeOutDuration = 0.8f;

    private bool isTransitioning = false;
    private CanvasGroup dynamicMask;

    private void Start()
    {
        if (myButton == null) myButton = GetComponent<Button>();
        
        if (myButton != null)
        {
            myButton.onClick.AddListener(OnButtonClick);
        }

        // 动态创建纯黑遮罩
        CreateDynamicMask();
    }

    private void OnButtonClick()
    {
        if (isTransitioning) return;
        StartCoroutine(FadeOutAndSwitchRoutine());
    }

    // 【核心逻辑：平滑变黑，全黑瞬间直接切走】
    private IEnumerator FadeOutAndSwitchRoutine()
    {
        isTransitioning = true;
        if (myButton != null) myButton.interactable = false;

        // 激活拦截，防止转场时玩家乱点屏幕
        if (dynamicMask != null)
        {
            dynamicMask.blocksRaycasts = true;
            dynamicMask.interactable = true;
        }

        float counter = 0f;
        while (counter < fadeOutDuration)
        {
            counter += Time.deltaTime;
            float t = counter / fadeOutDuration; 
            float alpha = Mathf.SmoothStep(0f, 1f, t); // 平滑渐黑
            
            if (dynamicMask != null) dynamicMask.alpha = alpha;
            yield return null;
        }

        // 确保最后一帧绝对全黑
        if (dynamicMask != null) dynamicMask.alpha = 1f;

        // 【就在这一秒】屏幕刚好全黑，立刻启动切换！
        // 遮罩由于没加 DontDestroyOnLoad，会随着这个老场景一起灰飞烟灭
        // 下一个场景开局就是原本亮堂堂的样子
        SceneManager.LoadScene(targetIndex);
    }

    // 纯代码动态构建 UI 遮罩（砍掉了 DontDestroyOnLoad）
    private void CreateDynamicMask()
    {
        GameObject canvasObj = new GameObject("DynamicFadeCanvas");

        Canvas canvas = canvasObj.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 999; // 确保盖在所有 UI 最顶层
        
        canvasObj.AddComponent<CanvasScaler>();
        canvasObj.AddComponent<GraphicRaycaster>();
        
        // 【注意】这里绝不加 DontDestroyOnLoad！让它生于此场景，死于此场景

        GameObject imageObj = new GameObject("FadeImage");
        imageObj.transform.SetParent(canvasObj.transform, false);
        
        Image img = imageObj.AddComponent<Image>();
        img.color = Color.black;

        RectTransform rect = img.GetComponent<RectTransform>();
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.sizeDelta = Vector2.zero;

        dynamicMask = imageObj.AddComponent<CanvasGroup>();
        dynamicMask.alpha = 0f; // 开局完全透明
        dynamicMask.blocksRaycasts = false;
        dynamicMask.interactable = false;
    }
}