using UnityEngine;

/// <summary>
/// 极简轮胎视觉同步脚本
/// 只做一件事：让视觉轮胎跟随 WheelCollider 的位置和旋转
/// 不做任何物理配置
/// </summary>
[RequireComponent(typeof(WheelCollider))]
public class SimpleWheelVisual : MonoBehaviour
{
    [Header("绑定")]
    [Tooltip("视觉轮胎模型（如果没有就使用本物体）")]
    public Transform visualWheel;
    
    [Header("旋转设置")]
    [Tooltip("旋转轴向（根据你的模型调整）")]
    public Vector3 rotationAxis = Vector3.right;
    
    [Tooltip("是否是转向轮")]
    public bool isSteeringWheel = false;
    
    // 组件引用
    private WheelCollider wheelCollider;
    private Transform targetVisual;
    private float currentRotation = 0f;
    
    void Awake()
    {
        // 获取 WheelCollider（已经存在，不做任何修改）
        wheelCollider = GetComponent<WheelCollider>();
        
        // 确定视觉目标
        if (visualWheel == null)
        {
            targetVisual = transform;
        }
        else
        {
            targetVisual = visualWheel;
        }
    }
    
    void Update()
    {
        if (wheelCollider == null || targetVisual == null) return;
        
        // 获取物理轮子的真实位置和旋转
        Vector3 pos;
        Quaternion physRot;
        wheelCollider.GetWorldPose(out pos, out physRot);
        
        // 1. 同步位置
        targetVisual.position = pos;
        
        // 2. 计算并同步旋转（基于 RPM）
        float rpm = wheelCollider.rpm;
        float rotationDelta = (rpm / 60f) * 360f * Time.deltaTime;
        currentRotation += rotationDelta;
        currentRotation %= 360f;
        
        // 3. 应用旋转（如果是转向轮，还要加上 steerAngle）
        if (isSteeringWheel)
        {
            // 注意：这里的轴向可能需要根据你的模型调整
            targetVisual.localRotation = Quaternion.Euler(
                rotationAxis.x * currentRotation,
                wheelCollider.steerAngle,
                rotationAxis.z * currentRotation
            );
        }
        else
        {
            targetVisual.localRotation = Quaternion.Euler(rotationAxis * currentRotation);
        }
    }
    
    // 可选：在编辑器中显示调试信息
    void OnDrawGizmosSelected()
    {
        if (wheelCollider != null)
        {
            Gizmos.color = Color.cyan;
            Gizmos.DrawWireSphere(transform.position, wheelCollider.radius);
        }
    }
}
