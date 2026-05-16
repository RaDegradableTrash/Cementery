using UnityEngine;

[RequireComponent(typeof(WheelCollider))] // 自动添加 Wheel Collider
public class Tire : MonoBehaviour
{
    [Header("轮胎参数")]
    [Tooltip("轮胎半径（米）")]
    public float radius = 0.5f;
    
    [Tooltip("悬挂行程（米）- 轮胎能向上压缩的距离")]
    public float suspensionTravel = 0.2f;
    
    [Tooltip("弹簧硬度 - 越大越硬")]
    public float springStrength = 35000f;
    
    [Tooltip("弹簧阻尼 - 抑制弹跳")]
    public float springDamper = 4500f;
    
    [Tooltip("是否转向轮")]
    public bool isSteeringWheel = false;
    
    [Tooltip("最大转向角度（度）")]
    public float maxSteeringAngle = 25f;
    
    [Tooltip("是否驱动轮")]
    public bool isDrivingWheel = true;
    
    [Header("视觉微调")]
    [Tooltip("Y轴偏移修正（如果视觉模型和物理中心不对齐）")]
    public float visualHeightOffset = 0f;
    
    [Tooltip("旋转轴向（根据你的模型调整）")]
    public Vector3 rotationAxis = Vector3.right;
    
    // 组件引用
    private WheelCollider wheelCollider;
    private Transform visualTransform;
    private float currentRotationAngle = 0f;
    
    // 公开属性供外部访问
    public WheelCollider Collider => wheelCollider;
    public float CurrentRPM => wheelCollider != null ? wheelCollider.rpm : 0f;
    public float CurrentSteerAngle => wheelCollider != null ? wheelCollider.steerAngle : 0f;
    
    void Awake()
    {
        // 获取或创建组件
        wheelCollider = GetComponent<WheelCollider>();
        if (wheelCollider == null)
        {
            wheelCollider = gameObject.AddComponent<WheelCollider>();
        }
        
        visualTransform = transform;
    }
    
    void Start()
    {
        // 配置 Wheel Collider
        ConfigureWheelCollider();
        
        // 验证设置
        ValidateSetup();
    }
    
    void ConfigureWheelCollider()
    {
        // 基本参数
        wheelCollider.radius = radius;
        wheelCollider.mass = 50f;
        
        // 悬挂设置（关键：悬挂向上延伸）
        wheelCollider.suspensionDistance = suspensionTravel;
        JointSpring suspensionSpring = wheelCollider.suspensionSpring;
        suspensionSpring.spring = springStrength;
        suspensionSpring.damper = springDamper;
        wheelCollider.suspensionSpring = suspensionSpring;
        
        // 摩擦力设置（标准值，可根据需要调整）
        WheelFrictionCurve forwardFriction = wheelCollider.forwardFriction;
        forwardFriction.extremumSlip = 0.4f;
        forwardFriction.extremumValue = 1f;
        forwardFriction.asymptoteSlip = 0.8f;
        forwardFriction.asymptoteValue = 0.5f;
        wheelCollider.forwardFriction = forwardFriction;
        
        WheelFrictionCurve sidewaysFriction = wheelCollider.sidewaysFriction;
        sidewaysFriction.extremumSlip = 0.3f;
        sidewaysFriction.extremumValue = 1f;
        sidewaysFriction.asymptoteSlip = 0.5f;
        sidewaysFriction.asymptoteValue = 0.75f;
        wheelCollider.sidewaysFriction = sidewaysFriction;
    }
    
    void ValidateSetup()
    {
        // 检查是否是根物体的子物体且有 Rigidbody
        Rigidbody parentRb = GetComponentInParent<Rigidbody>();
        if (parentRb == null)
        {
            Debug.LogError($"轮胎 {gameObject.name} 的父级没有 Rigidbody！车辆必须有一个根 Rigidbody。");
        }
        
        // 警告：如果这个物体自身有 Rigidbody
        Rigidbody myRb = GetComponent<Rigidbody>();
        if (myRb != null)
        {
            Debug.LogError($"轮胎 {gameObject.name} 自身有 Rigidbody！这会导物理错误，请删除它。");
            DestroyImmediate(myRb);
        }
        
        // 警告：如果有非 Trigger 的 Collider
        Collider myCollider = GetComponent<Collider>();
        if (myCollider != null && myCollider != wheelCollider && !myCollider.isTrigger)
        {
            Debug.LogWarning($"轮胎 {gameObject.name} 有额外的 Collider，建议设为 Trigger 或删除。");
        }
    }
    
    void Update()
    {
        // 同步视觉位置和旋转
        SyncVisualWithPhysics();
    }
    
    void SyncVisualWithPhysics()
    {
        if (wheelCollider == null) return;
        
        // 获取物理轮子的真实世界位置和旋转
        Vector3 physicsPosition;
        Quaternion physicsRotation;
        wheelCollider.GetWorldPose(out physicsPosition, out physicsRotation);
        
        // 同步位置（可加高度偏移修正）
        Vector3 targetPosition = physicsPosition;
        if (visualHeightOffset != 0f)
        {
            targetPosition.y += visualHeightOffset;
        }
        visualTransform.position = targetPosition;
        
        // 计算旋转（基于 RPM）
        float rpm = wheelCollider.rpm;
        float rotationDelta = (rpm / 60f) * 360f * Time.deltaTime;
        currentRotationAngle += rotationDelta;
        currentRotationAngle %= 360f;
        
        // 应用旋转（加上转向角度）
        float finalAngle = currentRotationAngle;
        Vector3 finalRotation = rotationAxis * finalAngle;
        
        if (isSteeringWheel)
        {
            // 转向轮的 Y 轴旋转来自 steerAngle
            visualTransform.localRotation = Quaternion.Euler(finalRotation.x, wheelCollider.steerAngle, finalRotation.z);
        }
        else
        {
            visualTransform.localRotation = Quaternion.Euler(finalRotation);
        }
    }
    
    // 公开方法：手动设置驱动力（由 CarController 调用）
    public void SetMotorTorque(float torque)
    {
        if (wheelCollider != null && isDrivingWheel)
        {
            wheelCollider.motorTorque = torque;
        }
    }
    
    // 公开方法：设置刹车力
    public void SetBrakeTorque(float torque)
    {
        if (wheelCollider != null)
        {
            wheelCollider.brakeTorque = torque;
        }
    }
    
    // 公开方法：设置转向角度
    public void SetSteerAngle(float angle)
    {
        if (wheelCollider != null && isSteeringWheel)
        {
            wheelCollider.steerAngle = angle * maxSteeringAngle;
        }
    }
    
    // 调试：在 Scene 视图中显示轮胎信息
    void OnDrawGizmosSelected()
    {
        if (wheelCollider != null)
        {
            // 显示悬挂范围
            Gizmos.color = Color.green;
            Vector3 center = transform.position;
            Vector3 top = center + Vector3.up * (suspensionTravel * 0.5f);
            Vector3 bottom = center - Vector3.up * (suspensionTravel * 0.5f);
            Gizmos.DrawLine(top, bottom);
            Gizmos.DrawWireSphere(center, radius);
            
            // 显示转向范围（如果是转向轮）
            if (isSteeringWheel && Application.isPlaying)
            {
                Gizmos.color = Color.yellow;
                Quaternion leftRot = Quaternion.Euler(0, -maxSteeringAngle, 0);
                Quaternion rightRot = Quaternion.Euler(0, maxSteeringAngle, 0);
                Vector3 forward = transform.forward * radius * 2;
                Gizmos.DrawRay(center, leftRot * forward);
                Gizmos.DrawRay(center, rightRot * forward);
            }
        }
    }
}