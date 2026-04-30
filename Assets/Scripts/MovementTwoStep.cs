using System.Collections;
using UnityEngine;

[RequireComponent(typeof(WorldObject))]
public class MovementTwoStep : MonoBehaviour
{
    [Header("位置参考点")]
    [Tooltip("第一段移动的目标点")]
    [SerializeField] private Transform step1Marker;
    [Tooltip("第二段移动的目标点")]
    [SerializeField] private Transform step2Marker;

    [Header("移动曲线与时长")]
    [SerializeField] private float step1Duration = 0.5f;
    [SerializeField] private AnimationCurve step1Curve = AnimationCurve.EaseInOut(0, 0, 1, 1);
    
    [SerializeField] private float pauseDuration = 0.3f; // 中间停顿时间

    [SerializeField] private float step2Duration = 0.5f;
    [SerializeField] private AnimationCurve step2Curve = AnimationCurve.EaseInOut(0, 0, 1, 1);

    private Vector3 pos0, pos1, pos2;
    private Quaternion rot0, rot1, rot2;
    
    private bool isAtEnd = false; // 当前是否在 Step2 位置
    private bool isMoving = false;

    void Awake()
    {
        // 记录三个关键点的本地坐标
        pos0 = transform.localPosition;
        rot0 = transform.localRotation;

        if (step1Marker != null)
        {
            pos1 = step1Marker.localPosition;
            rot1 = step1Marker.localRotation;
        }
        else
        {
            pos1 = pos0; rot1 = rot0;
            Debug.LogWarning("MovementTwoStep: Step1 Marker is missing!");
        }

        if (step2Marker != null)
        {
            pos2 = step2Marker.localPosition;
            rot2 = step2Marker.localRotation;
        }
        else
        {
            pos2 = pos1; rot2 = rot1;
            Debug.LogWarning("MovementTwoStep: Step2 Marker is missing!");
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

    public void Interact()
    {
        if (isMoving) return;

        if (!isAtEnd)
            StartCoroutine(SequenceMove(pos1, rot1, pos2, rot2, true));
        else
            StartCoroutine(SequenceMove(pos1, rot1, pos0, rot0, false));
            
        isAtEnd = !isAtEnd;
    }

    private IEnumerator SequenceMove(Vector3 midPos, Quaternion midRot, Vector3 endPos, Quaternion endRot, bool forward)
    {
        isMoving = true;
        WorldObject rootWo = GetComponentInParent<Rigidbody>()?.GetComponent<WorldObject>();
        if (rootWo != null) rootWo.AddAnimationLock();

        // --- 第一步 ---
        yield return StartCoroutine(MovePiece(transform.localPosition, transform.localRotation, midPos, midRot, forward ? step1Duration : step2Duration, forward ? step1Curve : step2Curve));

        // --- 间隔停歇 ---
        if (pauseDuration > 0)
            yield return new WaitForSeconds(pauseDuration);

        // --- 第二步 ---
        yield return StartCoroutine(MovePiece(transform.localPosition, transform.localRotation, endPos, endRot, forward ? step2Duration : step1Duration, forward ? step2Curve : step1Curve));

        isMoving = false;
        if (rootWo != null) rootWo.RemoveAnimationLock();
    }

    private IEnumerator MovePiece(Vector3 startP, Quaternion startR, Vector3 targetP, Quaternion targetR, float duration, AnimationCurve curve)
    {
        float elapsed = 0f;
        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            float percent = Mathf.Clamp01(elapsed / duration);
            float curvePercent = curve.Evaluate(percent);
            
            transform.localPosition = Vector3.Lerp(startP, targetP, curvePercent);
            transform.localRotation = Quaternion.Slerp(startR, targetR, curvePercent);
            yield return null;
        }
        transform.localPosition = targetP;
        transform.localRotation = targetR;
    }
}
