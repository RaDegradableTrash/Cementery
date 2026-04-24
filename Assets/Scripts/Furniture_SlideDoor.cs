using System.Collections;
using UnityEngine;

public class Furniture_SlideDoor : MonoBehaviour
{
    [Header("状态配置")]
    [SerializeField] private bool startsAtRight = true; // 初始状态是否在右侧/终点

    [Header("移动配置")]
    [SerializeField] private Vector3 relativeOffset = new Vector3(0.8f, 0f, 0f); // 切换时的相对位移
    [SerializeField] private float moveDuration = 0.5f; // 平滑过渡的时间
    [SerializeField] private AnimationCurve moveCurve = AnimationCurve.EaseInOut(0, 0, 1, 1); // 移动曲线

    private Vector3 positionLeft;  // 左侧（起点）位置
    private Vector3 positionRight; // 右侧（终点）位置
    private bool isAtRight;        // 当前是否在右侧
    private bool isMoving = false; // 是否正在移动中

    void Awake()
    {
        // 根据初始状态计算并记录两个关键点的位置
        if (startsAtRight)
        {
            positionRight = transform.localPosition;
            positionLeft = transform.localPosition - relativeOffset;
            isAtRight = true;
        }
        else
        {
            positionLeft = transform.localPosition;
            positionRight = transform.localPosition + relativeOffset;
            isAtRight = false;
        }
    }

    // 供玩家交互脚本调用的公共方法
    public void Interact()
    {
        if (isMoving) return; // 移动中禁止再次交互

        Vector3 targetPos = isAtRight ? positionLeft : positionRight;
        StartCoroutine(MoveDoor(targetPos));
        isAtRight = !isAtRight; // 切换状态标识
    }

    private IEnumerator MoveDoor(Vector3 target)
    {
        isMoving = true;
        Vector3 startPos = transform.localPosition;
        float elapsed = 0f;

        while (elapsed < moveDuration)
        {
            elapsed += Time.deltaTime;
            float percent = Mathf.Clamp01(elapsed / moveDuration);
            float curvePercent = moveCurve.Evaluate(percent);
            transform.localPosition = Vector3.Lerp(startPos, target, curvePercent);
            yield return null;
        }

        transform.localPosition = target;
        isMoving = false;
    }
}
