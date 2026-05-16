using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class WheelMovement : MonoBehaviour
{
    [System.Serializable]
    public class Wheel
    {
        [Tooltip("Wheel Collider组件")]
        public WheelCollider wheelCollider;
        
        [Tooltip("视觉轮子的Transform（用于显示）")]
        public Transform wheelTransform;
        
        [Tooltip("是否这是转向轮（前轮）")]
        public bool isSteeringWheel = false;
        
        // 用于累积旋转角度（避免直接用Rotate每帧叠加）
        [System.NonSerialized]
        public float currentRotationAngle = 0f;
    }

    [SerializeField]
    private List<Wheel> wheels = new List<Wheel>();
    
    [Header("视觉设置")]
    [Tooltip("是否让视觉轮子完全跟随物理轮子（推荐true）")]
    public bool followPhysicsPosition = true;  // 改为 true！
    
    void Start()
    {
        if (wheels.Count == 0)
        {
            Debug.LogWarning("WheelMovement: 没有配置轮子！请在Inspector中添加轮子信息。");
        }
    }

    void Update()
    {
        UpdateWheelVisuals();
    }

    private void UpdateWheelVisuals()
    {
        foreach (Wheel wheel in wheels)
        {
            if (wheel.wheelCollider == null || wheel.wheelTransform == null)
                continue;

            // ★ 关键修复：使用 GetWorldPose() 获取物理轮子的真实位置和旋转
            Vector3 physicsPosition;
            Quaternion physicsRotation;
            wheel.wheelCollider.GetWorldPose(out physicsPosition, out physicsRotation);
            
            // 1. 位置同步 - 完全跟随物理轮子
            if (followPhysicsPosition)
            {
                wheel.wheelTransform.position = physicsPosition;
            }
            else
            {
                // 备用方案：使用原来的计算方式（不推荐）
                WheelHit hit;
                if (wheel.wheelCollider.GetGroundHit(out hit) && wheel.wheelCollider.isGrounded)
                {
                    Vector3 pos = wheel.wheelCollider.transform.position;
                    pos.y = hit.point.y + wheel.wheelCollider.radius;
                    wheel.wheelTransform.position = pos;
                }
                else
                {
                    wheel.wheelTransform.position = wheel.wheelCollider.transform.position;
                }
            }
            
            // 2. 旋转同步 - 使用 RPM 累加（保持原来的逻辑）
            float wheelRPM = wheel.wheelCollider.rpm;
            float rotationDelta = wheelRPM / 60f * 360f * Time.deltaTime;
            wheel.currentRotationAngle += rotationDelta;
            
            // 应用旋转（保持360度范围内）
            wheel.currentRotationAngle %= 360f;
            
            // 根据你的轮子轴向调整（通常是绕X轴或Z轴）
            Vector3 rotationEuler = new Vector3(wheel.currentRotationAngle, 0, 0);
            
            // 3. 转向角度同步（前轮左右转）
            if (wheel.isSteeringWheel)
            {
                rotationEuler.y = wheel.wheelCollider.steerAngle;
            }
            
            wheel.wheelTransform.localRotation = Quaternion.Euler(rotationEuler);
        }
    }

    public void SetupWheels(WheelCollider[] wheelColliders, Transform[] wheelTransforms, bool[] isSteering = null)
    {
        wheels.Clear();
        
        if (wheelColliders.Length != wheelTransforms.Length)
        {
            Debug.LogError("WheelMovement: Wheel Colliders数量和视觉轮子数量不匹配！");
            return;
        }

        for (int i = 0; i < wheelColliders.Length; i++)
        {
            Wheel wheel = new Wheel
            {
                wheelCollider = wheelColliders[i],
                wheelTransform = wheelTransforms[i],
                isSteeringWheel = isSteering != null && isSteering.Length > i && isSteering[i],
                currentRotationAngle = 0f
            };
            wheels.Add(wheel);
        }
    }
}