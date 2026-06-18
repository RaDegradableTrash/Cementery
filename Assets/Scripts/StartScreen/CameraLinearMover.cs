using UnityEngine;
using UnityEngine.UI;

[RequireComponent(typeof(Button))]
public class ButtonCameraMover : MonoBehaviour
{
    [Header("相机与目标配置")]
    [SerializeField] private Transform mainCameraTransform; 
    [SerializeField] private Transform targetTransform; 

    [Header("动画参数")]
    [SerializeField] private float duration = 2.0f; 

    private Button selfButton;
    private Vector3 startPosition;
    private Quaternion startRotation;
    private float elapsedTime = 0f;
    private bool isMoving = false;

    private void Start()
    {
        selfButton = GetComponent<Button>();
        if (selfButton != null)
        {
            selfButton.onClick.AddListener(StartCameraMove);
        }

        if (mainCameraTransform == null && Camera.main != null)
        {
            mainCameraTransform = Camera.main.transform;
        }
    }

    private void StartCameraMove()
    {
        // 如果相机已经在移动中，直接拦截，防止狂点导致画面抖动
        if (isMoving) return; 

        if (mainCameraTransform == null || targetTransform == null) return;

        startPosition = mainCameraTransform.position;
        startRotation = mainCameraTransform.rotation;
        
        elapsedTime = 0f;
        isMoving = true;

        // 【删除了之前的 selfButton.interactable = false】
    }

    private void Update()
    {
        if (!isMoving) return;

        elapsedTime += Time.deltaTime;
        float t = Mathf.Clamp01(elapsedTime / duration);
        float smoothT = Mathf.SmoothStep(0f, 1f, t);

        mainCameraTransform.position = Vector3.Lerp(startPosition, targetTransform.position, smoothT);
        mainCameraTransform.rotation = Quaternion.Lerp(startRotation, targetTransform.rotation, smoothT);

        if (t >= 1.0f)
        {
            isMoving = false;
        }
    }

    private void OnDestroy()
    {
        if (selfButton != null)
        {
            selfButton.onClick.RemoveListener(StartCameraMove);
        }
    }
}