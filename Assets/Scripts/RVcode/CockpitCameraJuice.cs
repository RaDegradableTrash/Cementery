using UnityEngine;

public class CockpitCameraJuice : MonoBehaviour
{
    [Header("绑定车辆物理")]
    [SerializeField] private Rigidbody carRigidbody;
    [SerializeField] private CarControl carControl; // 用来拿你的引擎转速

    [Header("转弯惯性 (G-Force)")]
    [SerializeField] private float leanAmountX = 0.05f;   // 左右转弯时头向反方向摆动的幅度
    [SerializeField] private float leanAmountZ = 0.1f;    // 加减速时身体前后晃动的幅度
    [SerializeField] private float smoothSpeed = 5f;      // 晃动回弹的平滑度

    [Header("路面颠簸/引擎震动")]
    [SerializeField] private float idleVibration = 0.002f; // 怠速时的微小抖动
    [SerializeField] private float speedBumpFactor = 0.01f;// 速度越快，路面颠簸越剧烈

    private Vector3 lastVelocity;
    private Vector3 localAcceleration;
    private Vector3 originalLocalPos;
    private float vibrationTimer;

    void Start()
    {
        originalLocalPos = transform.localPosition;
        if (carRigidbody == null) carRigidbody = GetComponentInParent<Rigidbody>();
    }

    void FixedUpdate()
    {
        if (carRigidbody == null) return;

        // 1. 计算出车辆在自身局部空间下的【加速度】
        Vector3 currentVelocity = carRigidbody.velocity;
        Vector3 acceleration = (currentVelocity - lastVelocity) / Time.fixedDeltaTime;
        lastVelocity = currentVelocity;

        // 将世界坐标下的加速度转为卡车自身的局部坐标系 (X:左右, Y:上下, Z:前后)
        localAcceleration = carRigidbody.transform.InverseTransformDirection(acceleration);
    }

    void Update()
    {
        if (carRigidbody == null) return;

        float currentSpeed = carControl != null
            ? carControl.CurrentSpeedKmh
            : carRigidbody.velocity.magnitude * 3.6f;

        // ------------------ 核心 1：处理前后左右的【惯性摆动】 ------------------
        // 当车向右急转（正X轴加速度），人的头因为惯性会向左偏（负X轴）
        float targetX = -localAcceleration.x * leanAmountX;
        // 当车急刹车（负Z轴加速度），人的头因为惯性向前冲（正Z轴）
        float targetZ = localAcceleration.z * leanAmountZ;

        // 限制最大位移，防止过载时头飞出驾驶室
        targetX = Mathf.Clamp(targetX, -0.15f, 0.15f);
        targetZ = Mathf.Clamp(targetZ, -0.2f, 0.2f);

        Vector3 targetInertiaPos = new Vector3(targetX, 0, targetZ);

        // ------------------ 核心 2：处理路面和引擎的【微小震动】 ------------------
        vibrationTimer += Time.deltaTime * 30f; // 震动频率
        
        // 怠速震动比例（根据你之前的 smoothEngineRpm，转速越高震动频率和幅度可以微调）
        float engineFactor = carControl != null ? (carControl.SmoothEngineRpm / 2500f) : 0.2f;
        
        float shakeY = Mathf.Sin(vibrationTimer) * (idleVibration * engineFactor);
        // 路面颠簸：速度越快，上下无规则抖动越明显
        shakeY += (Random.value - 0.5f) * (currentSpeed * speedBumpFactor * 0.01f); 
        float shakeX = (Random.value - 0.5f) * (currentSpeed * speedBumpFactor * 0.005f);

        Vector3 targetVibrationPos = new Vector3(shakeX, shakeY, 0);

        // ------------------ 核心 3：融合并应用到 Anchor 物体 ------------------
        Vector3 finalLocalPos = originalLocalPos + targetInertiaPos + targetVibrationPos;
        
        // 平滑插值，让相机有“肉身肌肉缓冲”的沉浸感
        transform.localPosition = Vector3.Lerp(transform.localPosition, finalLocalPos, Time.deltaTime * smoothSpeed);
    }
}
