using System.Collections;
using System.Collections.Generic;
using UnityEngine;

[DisallowMultipleComponent]
public class DroneControl : MonoBehaviour
{
    [System.Serializable]
    public struct ArmPoseLink
    {
        public Transform arm;
        public Transform targetPose;
    }

    [Header("Transforms")]
    [SerializeField] private Transform movementRoot;
    [SerializeField] private Transform bodyVisual;
    [SerializeField] private Transform altitudeRayOrigin;

    [Header("Movement (Physics-like)")]
    [SerializeField] private float maxHorizontalSpeed = 12f;
    [SerializeField] private float horizontalAcceleration = 35f;
    [SerializeField] private float horizontalDrag = 3f; // 类似空气阻尼，越大停得越快
    
    [SerializeField] private float maxVerticalSpeed = 6f;
    [SerializeField] private float verticalAcceleration = 20f;
    [SerializeField] private float verticalDrag = 4f;

    [SerializeField] private float maxYawSpeedDeg = 120f;
    [SerializeField] private float yawAccelerationDeg = 400f;
    [SerializeField] private float yawDrag = 5f;

    [Header("Tilt (Visual)")]
    [SerializeField] private float maxPitchDeg = 15f;
    [SerializeField] private float maxRollDeg = 15f;
    [SerializeField] private float tiltResponsiveness = 10f;

    [Header("Wind & Drift (Weather Simulation)")]
    [Tooltip("恶劣天气强度：数值越大，无操作时漂移越厉害")]
    [SerializeField] private float weatherDriftIntensity = 1.5f;
    [Tooltip("漂移频率：数值越大，频率越快（风越乱）")]
    [SerializeField] private float driftSpeed = 1.0f;

    [Header("Propellers")]
    [SerializeField] private Transform[] propellers;
    [SerializeField] private float basePropellerSpeed = 1200f; // 基础怠速转速
    [SerializeField] private float maxPropellerSpeed = 2500f;  // 极限全出力转速
    [SerializeField] private float propellerAcceleration = 5f; // 转速改变的平滑度

    [Header("Altitude Ray")]
    [SerializeField] private float altitudeRayMaxDistance = 50f;
    [SerializeField] private LayerMask altitudeRayLayers = ~0;

    [Header("Arms (Auto by Altitude)")]
    [SerializeField] private ArmPoseLink[] arms;
    [SerializeField] private float armDeployDistance = 2.0f;
    [SerializeField] private float armRetractDistance = 2.5f;
    [SerializeField] private float armPoseLerpSpeed = 10f;

    public float CurrentAltitude { get; private set; } = float.PositiveInfinity;
    public bool HasAltitudeHit { get; private set; }

    // 内部物理变量
    private Vector3 _currentHorizontalVelocity = Vector3.zero;
    private float _currentVerticalVelocity = 0f;
    private float _currentYawVelocity = 0f;
    private float _yawDeg;

    // 内部螺旋桨变量
    private float _currentPropellerRotationSpeed;

    // 噪声时间戳
    private float _noiseTimerX;
    private float _noiseTimerY;
    private float _noiseTimerZ;

    private Vector3[] _armBaseLocalPositions;
    private Quaternion[] _armBaseLocalRotations;
    private bool _armsDeployed;
    private Quaternion _bodyBaseLocalRotation = Quaternion.identity;
    private readonly RaycastHit[] _altitudeHits = new RaycastHit[16];

    private void Awake()
    {
        if (movementRoot == null) movementRoot = transform;
        if (altitudeRayOrigin == null) altitudeRayOrigin = movementRoot;

        if (bodyVisual == movementRoot) bodyVisual = null;
        if (bodyVisual != null) _bodyBaseLocalRotation = bodyVisual.localRotation;

        _yawDeg = movementRoot.eulerAngles.y;

        // 随机化噪声起点，防止多台无人机飘得一模一样
        _noiseTimerX = Random.Range(0f, 100f);
        _noiseTimerY = Random.Range(100f, 200f);
        _noiseTimerZ = Random.Range(200f, 300f);

        _currentPropellerRotationSpeed = basePropellerSpeed;

        CacheArmBasePoses();
    }

    private void CacheArmBasePoses()
    {
        if (arms == null || arms.Length == 0)
        {
            _armBaseLocalPositions = null;
            _armBaseLocalRotations = null;
            return;
        }

        _armBaseLocalPositions = new Vector3[arms.Length];
        _armBaseLocalRotations = new Quaternion[arms.Length];

        for (int i = 0; i < arms.Length; i++)
        {
            Transform arm = arms[i].arm;
            if (arm == null)
            {
                _armBaseLocalPositions[i] = Vector3.zero;
                _armBaseLocalRotations[i] = Quaternion.identity;
                continue;
            }

            _armBaseLocalPositions[i] = arm.localPosition;
            _armBaseLocalRotations[i] = arm.localRotation;
        }
    }

    private void Update()
    {
        float dt = Time.deltaTime;

        ReadAltitude();
        UpdateMovement(dt);
        UpdateTilt(dt);
        UpdatePropellers(dt);
        UpdateArms(dt);
    }

    private void ReadAltitude()
    {
        HasAltitudeHit = false;
        CurrentAltitude = float.PositiveInfinity;

        if (altitudeRayOrigin == null) return;

        Vector3 origin = altitudeRayOrigin.position;

        int hitCount = Physics.RaycastNonAlloc(
            origin,
            Vector3.down,
            _altitudeHits,
            altitudeRayMaxDistance,
            altitudeRayLayers,
            QueryTriggerInteraction.Ignore);

        float best = float.PositiveInfinity;
        for (int i = 0; i < hitCount; i++)
        {
            Collider c = _altitudeHits[i].collider;
            if (c == null) continue;

            Transform hitTransform = c.transform;
            if (movementRoot != null && (hitTransform == movementRoot || hitTransform.IsChildOf(movementRoot)))
            {
                continue;
            }

            float d = _altitudeHits[i].distance;
            if (d < best) best = d;
        }

        if (!float.IsPositiveInfinity(best))
        {
            HasAltitudeHit = true;
            CurrentAltitude = best;
        }
    }

    private void UpdateMovement(float dt)
    {
        // 1. 获取原始输入
        float inputX = 0f;
        float inputZ = 0f; // 修复：W应该增加Z(向前)，S减小Z(向后)
        float inputY = 0f;

        if (Input.GetKey(KeyCode.A)) inputX -= 1f;
        if (Input.GetKey(KeyCode.D)) inputX += 1f;
        if (Input.GetKey(KeyCode.W)) inputZ += 1f; // 修复此处：改为 += 
        if (Input.GetKey(KeyCode.S)) inputZ -= 1f; // 修复此处：改为 -= 

        if (Input.GetKey(KeyCode.UpArrow)) inputY += 1f;
        if (Input.GetKey(KeyCode.DownArrow)) inputY -= 1f;

        float yawInput = 0f;
        if (Input.GetKey(KeyCode.Q)) yawInput -= 1f;
        if (Input.GetKey(KeyCode.E)) yawInput += 1f;

        // 2. 计算航向角（Yaw）的模拟惯性
        if (Mathf.Abs(yawInput) > 0.01f)
        {
            _currentYawVelocity += yawInput * yawAccelerationDeg * dt;
        }
        else
        {
            _currentYawVelocity = Mathf.MoveTowards(_currentYawVelocity, 0f, yawDrag * yawAccelerationDeg * dt);
        }
        _currentYawVelocity = Mathf.Clamp(_currentYawVelocity, -maxYawSpeedDeg, maxYawSpeedDeg);
        _yawDeg = Mathf.Repeat(_yawDeg + _currentYawVelocity * dt, 360f);
        Quaternion yawRotation = Quaternion.Euler(0f, _yawDeg, 0f);

        // 3. 计算本地坐标系下的目标移动向量
        Vector3 localInputMove = new Vector3(inputX, 0f, inputZ);
        localInputMove = Vector3.ClampMagnitude(localInputMove, 1f);
        Vector3 worldInputMove = yawRotation * localInputMove;

        // 4. 水平与垂直速度的物理惯性计算 (加速与阻尼)
        // 水平
        if (worldInputMove.sqrMagnitude > 0.01f)
        {
            _currentHorizontalVelocity += worldInputMove * horizontalAcceleration * dt;
        }
        else
        {
            _currentHorizontalVelocity = Vector3.MoveTowards(_currentHorizontalVelocity, Vector3.zero, horizontalDrag * horizontalAcceleration * dt);
        }
        _currentHorizontalVelocity = Vector3.ClampMagnitude(_currentHorizontalVelocity, maxHorizontalSpeed);

        // 垂直
        if (Mathf.Abs(inputY) > 0.01f)
        {
            _currentVerticalVelocity += inputY * verticalAcceleration * dt;
        }
        else
        {
            _currentVerticalVelocity = Mathf.MoveTowards(_currentVerticalVelocity, 0f, verticalDrag * verticalAcceleration * dt);
        }
        _currentVerticalVelocity = Mathf.Clamp(_currentVerticalVelocity, -maxVerticalSpeed, maxVerticalSpeed);

        // 5. 计算恶劣环境下的随机飘忽定量（柏林噪声模拟风力）
        _noiseTimerX += dt * driftSpeed;
        _noiseTimerY += dt * driftSpeed;
        _noiseTimerZ += dt * driftSpeed;

        // 产生 -1 到 1 的平滑随机噪声
        float driftX = (Mathf.PerlinNoise(_noiseTimerX, 0f) - 0.5f) * 2f;
        float driftY = (Mathf.PerlinNoise(0f, _noiseTimerY) - 0.5f) * 2f;
        float driftZ = (Mathf.PerlinNoise(_noiseTimerZ, _noiseTimerZ) - 0.5f) * 2f;
        Vector3 rawDriftVector = new Vector3(driftX, driftY, driftZ) * weatherDriftIntensity;

        // 动态弱化机制：当玩家操作推杆越剧烈，水平方向的环境影响越小，保证操作手感
        float inputIntensity = localInputMove.magnitude; 
        float horizontalDriftFactor = Mathf.Lerp(1.0f, 0.15f, inputIntensity); // 有操作时水平漂移缩减至15%
        
        Vector3 finalDrift = new Vector3(
            rawDriftVector.x * horizontalDriftFactor,
            rawDriftVector.y, // 依照要求：高度上下不受操作弱化影响
            rawDriftVector.z * horizontalDriftFactor
        );

        // 6. 最终合并应用位移
        Vector3 finalVelocity = _currentHorizontalVelocity + (Vector3.up * _currentVerticalVelocity) + finalDrift;
        movementRoot.position += finalVelocity * dt;

        // 7. 旋转应用
        if (bodyVisual != null)
        {
            movementRoot.rotation = yawRotation;
        }
        else
        {
            // 如果没有分离身体，则将倾斜融入根节点（由于没有输入时会有滑行惯性，倾斜应该基于当前的实际速度而非按键输入）
            Vector3 localVelocity = Quaternion.Inverse(yawRotation) * _currentHorizontalVelocity;
            float targetPitch = (localVelocity.z / maxHorizontalSpeed) * maxPitchDeg;
            float targetRoll = -(localVelocity.x / maxHorizontalSpeed) * maxRollDeg;
            Quaternion tiltRotation = Quaternion.Euler(targetPitch, 0f, targetRoll);
            movementRoot.rotation = yawRotation * tiltRotation;
        }
    }

    private void UpdateTilt(float dt)
    {
        if (bodyVisual == null) return;

        // 视觉倾斜基于无人机【当前的实际移动速度占比】来决定，这样由于惯性滑行时，机身也会有真实的反向回正倾斜感
        Quaternion yawRotation = Quaternion.Euler(0f, _yawDeg, 0f);
        Vector3 localVelocity = Quaternion.Inverse(yawRotation) * _currentHorizontalVelocity;

        // 前进时前倾，后退时后倾；向左时左倾，向右时右倾
        float targetPitch = (localVelocity.z / maxHorizontalSpeed) * maxPitchDeg;
        float targetRoll = -(localVelocity.x / maxHorizontalSpeed) * maxRollDeg;
        
        Quaternion targetLocal = _bodyBaseLocalRotation * Quaternion.Euler(targetPitch, 0f, targetRoll);

        float t = 1f - Mathf.Exp(-Mathf.Max(0.01f, tiltResponsiveness) * dt);
        bodyVisual.localRotation = Quaternion.Slerp(bodyVisual.localRotation, targetLocal, t);
    }

    private void UpdatePropellers(float dt)
    {
        if (propellers == null || propellers.Length == 0) return;

        // 根据玩家的操作强度（速度和油门）动态改变旋转速率
        float horizontalRatio = _currentHorizontalVelocity.magnitude / maxHorizontalSpeed;
        float verticalRatio = Mathf.Abs(_currentVerticalVelocity) / maxVerticalSpeed;
        float powerFactor = Mathf.Max(horizontalRatio, verticalRatio);

        // 计算目标转速：有操作就向maxPropellerSpeed靠拢，无操作回到怠速basePropellerSpeed
        float targetSpeed = Mathf.Lerp(basePropellerSpeed, maxPropellerSpeed, powerFactor);
        
        // 平滑过渡转速（模拟电机加速声浪和物理过渡）
        _currentPropellerRotationSpeed = Mathf.Lerp(_currentPropellerRotationSpeed, targetSpeed, dt * propellerAcceleration);

        float angle = _currentPropellerRotationSpeed * dt;
        for (int i = 0; i < propellers.Length; i++)
        {
            Transform p = propellers[i];
            if (p == null) continue;
            
            // 沿其自身的 Y 轴（Space.Self）进行正确的持续旋转
            p.Rotate(0f, angle, 0f, Space.Self);
        }
    }

    private void UpdateArms(float dt)
    {
        if (arms == null || arms.Length == 0) return;
        if (_armBaseLocalPositions == null || _armBaseLocalRotations == null) CacheArmBasePoses();
        if (_armBaseLocalPositions == null || _armBaseLocalRotations == null) return;

        bool hasAlt = HasAltitudeHit;
        float alt = CurrentAltitude;

        if (!hasAlt)
        {
            _armsDeployed = false;
        }
        else
        {
            float deploy = Mathf.Max(0f, armDeployDistance);
            float retract = Mathf.Max(deploy, armRetractDistance);

            if (_armsDeployed)
            {
                if (alt > retract) _armsDeployed = false;
            }
            else
            {
                if (alt < deploy) _armsDeployed = true;
            }
        }

        float t = 1f - Mathf.Exp(-Mathf.Max(0.01f, armPoseLerpSpeed) * dt);
        for (int i = 0; i < arms.Length; i++)
        {
            Transform arm = arms[i].arm;
            if (arm == null) continue;

            Vector3 targetPos;
            Quaternion targetRot;

            if (_armsDeployed)
            {
                Transform targetPose = arms[i].targetPose;
                if (targetPose != null)
                {
                    targetPos = targetPose.localPosition;
                    targetRot = targetPose.localRotation;
                }
                else
                {
                    targetPos = _armBaseLocalPositions[i];
                    targetRot = _armBaseLocalRotations[i];
                }
            }
            else
            {
                targetPos = _armBaseLocalPositions[i];
                targetRot = _armBaseLocalRotations[i];
            }

            arm.localPosition = Vector3.Lerp(arm.localPosition, targetPos, t);
            arm.localRotation = Quaternion.Slerp(arm.localRotation, targetRot, t);
        }
    }
}