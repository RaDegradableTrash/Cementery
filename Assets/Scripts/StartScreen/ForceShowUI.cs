using UnityEngine;
using UnityEngine.UI;

[DefaultExecutionOrder(-100)] // 重点：让这个脚本比你同学的破脚本更早执行！
public class ForceShowUI : MonoBehaviour
{

    private int counter = 0; // 计数器，记录强制显示的次数
    private void Awake()
    {
        ForceShowEverything();
    }

    private void Update()
    {
        if(counter < 1) // 最多尝试强制显示1次，防止死循环
        {
            ForceShowEverything();
            counter++;
        }
        else
        {
            Debug.LogWarning($"[{gameObject.name}] 已经尝试强制显示 {counter} 次了，停止继续尝试了。");
            this.enabled = false;
        }
    }

    /// <summary>
    /// 核心震慑方法：管你卡在什么状态，统统给我显示！
    /// </summary>
    private void ForceShowEverything()
    {
        // 1. 强行激活 Canvas 游戏物体本身
        if (!gameObject.activeSelf)
        {
            gameObject.SetActive(true);
        }

        // 2. 强行激活所有子物体（遍历所有层级的子物体，包括隐藏的）
        Transform[] allChildren = GetComponentsInChildren<Transform>(true);
        foreach (Transform child in allChildren)
        {
            if (child != null && !child.gameObject.activeSelf)
            {
                child.gameObject.SetActive(true);
            }
        }

        // 3. 确保 Canvas 渲染组件和点击射线组件没被勾掉
        Canvas canvas = GetComponent<Canvas>();
        if (canvas != null) canvas.enabled = true;

        GraphicRaycaster raycaster = GetComponent<GraphicRaycaster>();
        if (raycaster != null) raycaster.enabled = true;
    }
}