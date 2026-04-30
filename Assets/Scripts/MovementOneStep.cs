using System.Collections;
using UnityEngine;

[RequireComponent(typeof(WorldObject))]
public class Furniture_SlideDoor : MonoBehaviour
{
    [Header("状态配置")]
    [SerializeField] private bool startsAtRight = true; // 初始状态是否在右侧/终点

    [Header("移动配置 (最直观的方法)")]
    [Tooltip("直接在柜子下建一个空物体，移动到开门位置，然后拖拽到这里！不需要猜坐标轴！")]
    [SerializeField] private Transform openPositionMarker;

    [Header("移动配置 (数值方法 - 基于父级坐标系)")]
    [Tooltip("如果你不想用上面的Marker，可以在这里填相对于父级的位移")]
    [SerializeField] private Vector3 relativeOffset = new Vector3(0.8f, 0f, 0f); // 切换时的相对位移
    [Tooltip("如果你不想用上面的Marker，可以在这里填相对于父级的旋转角度变化")]
    [SerializeField] private Vector3 relativeRotationOffset = Vector3.zero; // 切换时的相对旋转
    [SerializeField] private float moveDuration = 0.5f; // 平滑过渡的时间
    [SerializeField] private AnimationCurve moveCurve = AnimationCurve.EaseInOut(0, 0, 1, 1); // 移动曲线

    private Vector3 positionLeft;  // 左侧（起点）位置
    private Vector3 positionRight; // 右侧（终点）位置
    private Quaternion rotationLeft;  // 左侧（起点）旋转
    private Quaternion rotationRight; // 右侧（终点）旋转
    private bool isAtRight;        // 当前是否在右侧
    private bool isMoving = false; // 是否正在移动中

    void Awake()
    {
        if (openPositionMarker != null)
        {
            // 如果提供了参照物，起点就是自己现在的位置，终点就是参照物的位置
            positionLeft = transform.localPosition;
            rotationLeft = transform.localRotation;
            positionRight = openPositionMarker.localPosition;
            rotationRight = openPositionMarker.localRotation;
            isAtRight = false; // 初始认为在起点
        }
        else
        {
            // 回退到数值计算，直接基于父级 localPosition 和 localRotation
            if (startsAtRight)
            {
                positionRight = transform.localPosition;
                rotationRight = transform.localRotation;
                positionLeft = transform.localPosition - relativeOffset;
                rotationLeft = Quaternion.Euler(transform.localEulerAngles - relativeRotationOffset);
                isAtRight = true;
            }
            else
            {
                positionLeft = transform.localPosition;
                rotationLeft = transform.localRotation;
                positionRight = transform.localPosition + relativeOffset;
                rotationRight = Quaternion.Euler(transform.localEulerAngles + relativeRotationOffset);
                isAtRight = false;
            }
        }
    }

    void Start()
    {
        WorldObject wo = GetComponent<WorldObject>();
        if (wo != null)
        {
            wo.interactable = true;
            wo.onInteract.AddListener((GameObject interactor) => Interact());
        }
    }

    // 供玩家交互脚本调用的公共方法
    public void Interact()
    {
        if (isMoving) return; // 移动中禁止再次交互

        Vector3 targetPos = isAtRight ? positionLeft : positionRight;
        Quaternion targetRot = isAtRight ? rotationLeft : rotationRight;
        StartCoroutine(MoveDoor(targetPos, targetRot));
        isAtRight = !isAtRight; // 切换状态标识
    }

    private IEnumerator MoveDoor(Vector3 targetPos, Quaternion targetRot)
    {
        isMoving = true;
        Vector3 startPos = transform.localPosition;
        Quaternion startRot = transform.localRotation;
        float elapsed = 0f;

        WorldObject rootWo = GetComponentInParent<Rigidbody>()?.GetComponent<WorldObject>();
        if (rootWo != null) rootWo.AddAnimationLock();

        while (elapsed < moveDuration)
        {
            elapsed += Time.deltaTime;
            float percent = Mathf.Clamp01(elapsed / moveDuration);
            float curvePercent = moveCurve.Evaluate(percent);
            transform.localPosition = Vector3.Lerp(startPos, targetPos, curvePercent);
            transform.localRotation = Quaternion.Slerp(startRot, targetRot, curvePercent);
            yield return null;
        }

        transform.localPosition = targetPos;
        transform.localRotation = targetRot;
        isMoving = false;

        if (rootWo != null) rootWo.RemoveAnimationLock();
    }
}
