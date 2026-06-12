using System.Collections;
using System.Collections.Generic;
using UnityEngine;

[DisallowMultipleComponent]
[RequireComponent(typeof(Rigidbody))]
public class DroneControl : MonoBehaviour
{
    [System.Serializable]
    public struct TransformPair
    {
        public Transform targetPart;        
        public Transform landingAnchor;     
        public Transform flyingAnchor;      
    }

    [Header("Transforms & Inspire 3 Joints")]
    [SerializeField] private Transform movementRoot;
    [SerializeField] private Transform altitudeRayOrigin;
    [SerializeField] private TransformPair[] inspire3Parts;
    [SerializeField] private float morphLerpSpeed = 2f; 

    [Header("Gimbal Camera (云台相机)")]
    [SerializeField] private Transform gimbalCamera;
    [SerializeField] private float gimbalPitchSpeed = 45f;
    [SerializeField] private float gimbalYawSpeed = 45f;
    [SerializeField] private float gimbalResetSpeed = 8f;
    [SerializeField] private float maxGimbalPitch = 85f;
    [SerializeField] private float minGimbalPitch = -85f;
    [SerializeField] private float gimbalInertiaStrength = 0.15f;
    [SerializeField] private float bodyFollowGimbalSpeed = 3f;

    [Header("Movement (Physics-based)")]
    [SerializeField] private float maxHorizontalSpeed = 12f;
    [SerializeField] private float horizontalAcceleration = 35f;
    [SerializeField] private float horizontalDrag = 3f; 
    
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

    [Header("Wind & Drift (高度动态风力)")]
    [Tooltip("高空最大恶劣天气强度（风力上限值）")]
    [SerializeField] private float weatherDriftIntensity = 2.0f;
    [Tooltip("风力达到最大值所需的高度（超过该高度风力不再增强）")]
    [SerializeField] private float maxHeightForMaxWind = 100f;
    [SerializeField] private float driftSpeed = 1.0f;

    [Header("Propellers")]
    [SerializeField] private Transform[] propellers;
    [SerializeField] private float basePropellerSpeed = 1200f; 
    [SerializeField] private float maxPropellerSpeed = 2500f;  
    [SerializeField] private float propellerAcceleration = 5f; 

    [Header("Altitude & Flight Ceiling")]
    [SerializeField] private float altitudeRayMaxDistance = 500f; 
    [SerializeField] private LayerMask altitudeRayLayers = ~0;
    
    [Tooltip("切换到形态1（降落姿态）的触发高度极限值")]
    [SerializeField] private float landingStateDistance = 2.5f;
    
    [Tooltip("近地彻底切断代码控制、让物体自由摔落的临界高度")]
    [SerializeField] private float groundCutoffDistance = 0.2f;
    
    [Tooltip("无人机最高飞行限制高度")]
    [SerializeField] private float maxFlightAltitude = 300f;
    [Tooltip("触发超高时，强制往下压回的速度")]
    [SerializeField] private float ceilingPushDownSpeed = 4f;

    // ── 🌟 新增：无人机引擎音频配置 ──────────────────────────────────────────
    [Header("Drone Audio Settings")]
    [Tooltip("无人机引擎/螺旋桨的循环 MP3 音效")]
    [SerializeField] private AudioClip droneEngineSound;
    [Range(0f, 1f)] [SerializeField] private float maxVolume = 0.8f;
    [Tooltip("声音随转速变化的敏感度（数值越大，高速飞行时声音越尖锐）")]
    [SerializeField] private float pitchChangeRange = 0.3f;

    private AudioSource _audioSource;
    // ────────────────────────────────────────────────────────────────────────

    public float CurrentAltitude { get; private set; } = float.PositiveInfinity;
    public bool HasAltitudeHit { get; private set; }

    private Rigidbody _rb;

    // 内部物理速度
    private Vector3 _currentHorizontalVelocity = Vector3.zero;
    private float _currentVerticalVelocity = 0f;
    private float _currentYawVelocity = 0f;
    private float _yawDeg;

    // 云台内部旋转
    private float _gimbalLocalPitch = 0f;
    private float _gimbalLocalYaw = 0f;

    // 螺旋桨转速
    private float _currentPropellerRotationSpeed;

    // 噪声随机起点
    private float _noiseTimerX;
    private float _noiseTimerY;
    private float _noiseTimerZ;

    private readonly RaycastHit[] _altitudeHits = new RaycastHit[16];

    private bool _isInLandingState = true;
    private bool _isFlightControlCutoff = false; 
    private Quaternion _targetBodyTiltRotation = Quaternion.identity;

    private void Awake()
    {
        if (movementRoot == null) movementRoot = transform;
        _rb = movementRoot.GetComponent<Rigidbody>();
        
        _rb.useGravity = false; 
        _rb.isKinematic = false;
        _rb.collisionDetectionMode = CollisionDetectionMode.Continuous; 

        if (altitudeRayOrigin == null) altitudeRayOrigin = movementRoot;

        _yawDeg = movementRoot.eulerAngles.y;

        _noiseTimerX = Random.Range(0f, 100f);
        _noiseTimerY = Random.Range(100f, 200f);
        _noiseTimerZ = Random.Range(200f, 300f);

        _currentPropellerRotationSpeed = basePropellerSpeed;

        // 开机自检回正镜头
        _gimbalLocalPitch = 0f;
        _gimbalLocalYaw = 0f;
        if (gimbalCamera != null) gimbalCamera.localRotation = Quaternion.identity;

        // ── 🌟 新增：动态初始化并立刻播放音频 ────────────────────────────────────
        InitAndPlayDroneAudio();
        // ────────────────────────────────────────────────────────────────────────
    }

    private void InitAndPlayDroneAudio()
    {
        // 获取或自动添加 AudioSource 组件
        _audioSource = GetComponent<AudioSource>();
        if (_audioSource == null)
        {
            _audioSource = gameObject.AddComponent<AudioSource>();
        }

        if (droneEngineSound != null)
        {
            _audioSource.clip = droneEngineSound;
            _audioSource.loop = true;          // 开启循环播放
            _audioSource.playOnAwake = false;
            _audioSource.spatialBlend = 1.0f;   // 开启 3D 空间音效（声音从无人机位置发出）
            _audioSource.volume = maxVolume * 0.5f; // 初始给予基础音量
            _audioSource.pitch = 1.0f;
            _audioSource.Play();               // Awake 时立刻开始播放
        }
        else
        {
            Debug.LogWarning($"<color=yellow>[DroneControl]</color> 未在 Inspector 面板中分配 'Drone Engine Sound' 音频文件！");
        }
    }

    private void Update()
    {
        float dt = Time.deltaTime;

        ReadAltitude();
        GatherInputAndPhysics(dt);
        UpdateInspire3Morph(dt);
        UpdatePropellers(dt);

        // ── 🌟 新增：每一帧根据螺旋桨的转速动态改变音量和音高 ────────────────────
        UpdateAudioDynamics();
        // ────────────────────────────────────────────────────────────────────────
    }

    private void UpdateAudioDynamics()
    {
        if (_audioSource == null || droneEngineSound == null) return;

        // 计算当前转速占最高转速的比例 (0.0 到 1.0)
        float speedRatio = Mathf.InverseLerp(basePropellerSpeed * 0.3f, maxPropellerSpeed, _currentPropellerRotationSpeed);

        // 1. 动态音量：转速快时声音大，断电摔落时声音变弱
        _audioSource.volume = Mathf.Lerp(maxVolume * 0.3f, maxVolume, speedRatio);

        // 2. 动态音高：转速快时螺旋桨声音更尖锐高频
        _audioSource.pitch = Mathf.Lerp(1.0f - pitchChangeRange, 1.0f + pitchChangeRange, speedRatio);
    }

    private void LateUpdate()
    {
        UpdateGimbalCamera(Time.deltaTime);
    }

    private void FixedUpdate()
    {
        if (_rb == null) return;

        // 如果触发了近地切断，飞控完全松手，开启重力，交由物理引擎纯刚体摔落地面
        if (_isFlightControlCutoff)
        {
            _rb.useGravity = true; 
            // 慢慢让倾斜回正到地平线，避免歪着脖子摔倒
            Quaternion flatRotation = Quaternion.Euler(0f, _yawDeg, 0f);
            _rb.MoveRotation(Quaternion.Slerp(_rb.rotation, flatRotation, Time.fixedDeltaTime * 5f));
            return;
        }

        // 正常飞行状态，关闭物理自带重力（由虚拟速度全权接管）
        _rb.useGravity = false;
        _rb.velocity = _currentHorizontalVelocity + (Vector3.up * _currentVerticalVelocity);
        _rb.MoveRotation(_targetBodyTiltRotation);
    }

    private void ReadAltitude()
    {
        HasAltitudeHit = false;
        CurrentAltitude = float.PositiveInfinity;
        if (altitudeRayOrigin == null) return;

        Vector3 origin = altitudeRayOrigin.position;
        int hitCount = Physics.RaycastNonAlloc(origin, Vector3.down, _altitudeHits, altitudeRayMaxDistance, altitudeRayLayers, QueryTriggerInteraction.Ignore);

        float best = float.PositiveInfinity;
        for (int i = 0; i < hitCount; i++)
        {
            Collider c = _altitudeHits[i].collider;
            if (c == null) continue;

            Transform hitTransform = c.transform;
            if (movementRoot != null && (hitTransform == movementRoot || hitTransform.IsChildOf(movementRoot))) continue;

            float d = _altitudeHits[i].distance;
            if (d < best) best = d;
        }

        if (!float.IsPositiveInfinity(best))
        {
            HasAltitudeHit = true;
            CurrentAltitude = best;
        }
    }

    private void GatherInputAndPhysics(float dt)
    {
        // 1. 落地状态判断与近地切断检查
        bool wantsToMoveOrUp = Input.GetKey(KeyCode.W) || Input.GetKey(KeyCode.S) || Input.GetKey(KeyCode.A) || Input.GetKey(KeyCode.D) || Input.GetKey(KeyCode.Z);
        
        if (HasAltitudeHit && CurrentAltitude <= groundCutoffDistance && _currentVerticalVelocity <= 0.05f && !wantsToMoveOrUp)
        {
            _isFlightControlCutoff = true;
            _currentHorizontalVelocity = Vector3.zero;
            _currentVerticalVelocity = 0f;
            return;
        }

        // 2. 如果之前被切断了，但现在玩家按下了 Z 键（起飞申请）
        if (_isFlightControlCutoff && Input.GetKey(KeyCode.Z))
        {
            _isFlightControlCutoff = false; 
            movementRoot.position += Vector3.up * 0.1f; // 强制向上拔离0.1米
            _currentVerticalVelocity = 1.0f; 
        }

        if (_isFlightControlCutoff) return;

        // 3. 核心机制：检查玩家当前是否有任何针对无人机本体的主动操控输入
        bool hasFlightInput = Input.GetKey(KeyCode.W) || Input.GetKey(KeyCode.S) || 
                              Input.GetKey(KeyCode.A) || Input.GetKey(KeyCode.D) || 
                              Input.GetKey(KeyCode.Z) || Input.GetKey(KeyCode.C) || 
                              Input.GetKey(KeyCode.Q) || Input.GetKey(KeyCode.E);

        // 如果【玩家没有任何输入】，直接启动强力消能刹车，洗清残余速度和空中撞击产生的惯性Bug
        if (!hasFlightInput)
        {
            // 以平时加速数倍的高效率（8f）瞬间斩断变量内囤积的速度
            _currentHorizontalVelocity = Vector3.MoveTowards(_currentHorizontalVelocity, Vector3.zero, dt * horizontalAcceleration * 8f);
            _currentVerticalVelocity = Mathf.MoveTowards(_currentVerticalVelocity, 0f, dt * verticalAcceleration * 8f);
            _currentYawVelocity = Mathf.MoveTowards(_currentYawVelocity, 0f, dt * yawAccelerationDeg * 8f);

            // 强行把物理刚体的实际速度强制归零，防止由于撞墙反弹力矩继续自我飘逸
            if (_rb != null)
            {
                _rb.velocity = Vector3.MoveTowards(_rb.velocity, Vector3.zero, dt * 50f);
            }

            // 机身姿态高速度回正水平，进入死锁定点悬停姿态
            _targetBodyTiltRotation = Quaternion.Slerp(_targetBodyTiltRotation, Quaternion.Euler(0f, _yawDeg, 0f), dt * tiltResponsiveness * 2f);
            return; // 零输入逻辑执行完毕，直接跳出后续加速与风力飘逸计算，确保绝对钉死在原地
        }

        // 4. 获取飞行按键输入（仅在hasFlightInput为true时才会往下执行）
        float inputX = 0f;
        float inputZ = 0f; 
        float inputY = 0f;

        if (Input.GetKey(KeyCode.A)) inputX -= 1f;
        if (Input.GetKey(KeyCode.D)) inputX += 1f;
        if (Input.GetKey(KeyCode.W)) inputZ += 1f; 
        if (Input.GetKey(KeyCode.S)) inputZ -= 1f; 

        // 限高检查
        bool isOverCeiling = HasAltitudeHit && CurrentAltitude >= maxFlightAltitude;
        if (Input.GetKey(KeyCode.Z) && !isOverCeiling) inputY += 1f; 
        if (Input.GetKey(KeyCode.C)) inputY -= 1f;

        // 5. 自动靠拢镜头 (Yaw)
        if (Mathf.Abs(_gimbalLocalYaw) > 0.01f)
        {
            float yawTarget = _yawDeg + _gimbalLocalYaw;
            _yawDeg = Mathf.LerpAngle(_yawDeg, yawTarget, bodyFollowGimbalSpeed * dt);
            _gimbalLocalYaw = Mathf.LerpAngle(_gimbalLocalYaw, 0f, bodyFollowGimbalSpeed * dt);
        }

        float yawInput = 0f;
        if (Input.GetKey(KeyCode.Q)) yawInput -= 1f;
        if (Input.GetKey(KeyCode.E)) yawInput += 1f;

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

        // 6. 基础运动推力计算
        Vector3 localInputMove = Vector3.ClampMagnitude(new Vector3(inputX, 0f, inputZ), 1f);
        float moveYaw = _yawDeg;
        if (gimbalCamera != null)
        {
            Vector3 camForward = gimbalCamera.forward;
            camForward.y = 0f;
            if (camForward.sqrMagnitude > 0.001f)
            {
                moveYaw = Quaternion.LookRotation(camForward).eulerAngles.y;
            }
        }
        Vector3 worldInputMove = Quaternion.Euler(0f, moveYaw, 0f) * localInputMove;

        if (worldInputMove.sqrMagnitude > 0.01f)
        {
            _currentHorizontalVelocity += worldInputMove * horizontalAcceleration * dt;
        }
        else
        {
            _currentHorizontalVelocity = Vector3.MoveTowards(_currentHorizontalVelocity, Vector3.zero, horizontalDrag * horizontalAcceleration * dt);
        }
        _currentHorizontalVelocity = Vector3.ClampMagnitude(_currentHorizontalVelocity, maxHorizontalSpeed);

        if (Mathf.Abs(inputY) > 0.01f)
        {
            _currentVerticalVelocity += inputY * verticalAcceleration * dt;
        }
        else
        {
            _currentVerticalVelocity = Mathf.MoveTowards(_currentVerticalVelocity, 0f, verticalDrag * verticalAcceleration * dt);
        }
        _currentVerticalVelocity = Mathf.Clamp(_currentVerticalVelocity, -maxVerticalSpeed, maxVerticalSpeed);

        // 限高强力下压拦截
        if (isOverCeiling)
        {
            _currentVerticalVelocity = Mathf.MoveTowards(_currentVerticalVelocity, -ceilingPushDownSpeed, verticalAcceleration * dt);
        }

        // 7. 【高度动态增强风力系统】
        _noiseTimerX += dt * driftSpeed;
        _noiseTimerY += dt * dt * driftSpeed;
        _noiseTimerZ += dt * driftSpeed;

        float driftX = (Mathf.PerlinNoise(_noiseTimerX, 0f) - 0.5f) * 2f;
        float driftY = (Mathf.PerlinNoise(0f, _noiseTimerY) - 0.5f) * 2f;
        float driftZ = (Mathf.PerlinNoise(_noiseTimerZ, _noiseTimerZ) - 0.5f) * 2f;

        float currentHeightFactor = 0f;
        if (HasAltitudeHit)
        {
            currentHeightFactor = Mathf.Clamp01(CurrentAltitude / maxHeightForMaxWind);
        }
        
        Vector3 dynamicWindVector = new Vector3(driftX, driftY, driftZ) * (weatherDriftIntensity * currentHeightFactor);
        float inputIntensity = localInputMove.magnitude; 
        float horizontalDriftFactor = Mathf.Lerp(1.0f, 0.15f, inputIntensity); 
        
        Vector3 finalDrift = new Vector3(
            dynamicWindVector.x * horizontalDriftFactor,
            dynamicWindVector.y, 
            dynamicWindVector.z * horizontalDriftFactor
        );

        _currentHorizontalVelocity += new Vector3(finalDrift.x, 0f, finalDrift.z);
        _currentVerticalVelocity += finalDrift.y;

        // 8. 计算机体视觉物理倾斜
        Vector3 localVelocity = Quaternion.Inverse(Quaternion.Euler(0f, _yawDeg, 0f)) * _currentHorizontalVelocity;
        float targetPitch = (localVelocity.z / maxHorizontalSpeed) * maxPitchDeg;
        float targetRoll = -(localVelocity.x / maxHorizontalSpeed) * maxRollDeg;
        Quaternion tiltRotation = Quaternion.Euler(targetPitch, 0f, targetRoll);
        
        float t = 1f - Mathf.Exp(-Mathf.Max(0.01f, tiltResponsiveness) * dt);
        _targetBodyTiltRotation = Quaternion.Slerp(_targetBodyTiltRotation, Quaternion.Euler(0f, _yawDeg, 0f) * tiltRotation, t);
    }

    private void UpdateInspire3Morph(float dt)
    {
        if (inspire3Parts == null || inspire3Parts.Length == 0) return;

        if (_isFlightControlCutoff || (HasAltitudeHit && CurrentAltitude < landingStateDistance))
        {
            _isInLandingState = true;
        }
        else
        {
            _isInLandingState = false;
        }

        float t = 1f - Mathf.Exp(-Mathf.Max(0.01f, morphLerpSpeed) * dt);
        for (int i = 0; i < inspire3Parts.Length; i++)
        {
            Transform part = inspire3Parts[i].targetPart;
            if (part == null) continue;

            Transform anchor = _isInLandingState ? inspire3Parts[i].landingAnchor : inspire3Parts[i].flyingAnchor;
            if (anchor == null) continue;

            part.localPosition = Vector3.Lerp(part.localPosition, anchor.localPosition, t);
            part.localRotation = Quaternion.Slerp(part.localRotation, anchor.localRotation, t);
        }
    }

    private void UpdatePropellers(float dt)
    {
        if (propellers == null || propellers.Length == 0) return;

        float targetSpeed = basePropellerSpeed;
        
        if (!_isFlightControlCutoff)
        {
            float horizontalRatio = _currentHorizontalVelocity.magnitude / maxHorizontalSpeed;
            float verticalRatio = Mathf.Abs(_currentVerticalVelocity) / maxVerticalSpeed;
            float powerFactor = Mathf.Max(horizontalRatio, verticalRatio);
            targetSpeed = Mathf.Lerp(basePropellerSpeed, maxPropellerSpeed, powerFactor);
        }
        else
        {
            targetSpeed = basePropellerSpeed * 0.3f; 
        }

        _currentPropellerRotationSpeed = Mathf.Lerp(_currentPropellerRotationSpeed, targetSpeed, dt * propellerAcceleration);

        float angle = _currentPropellerRotationSpeed * dt;
        for (int i = 0; i < propellers.Length; i++)
        {
            Transform p = propellers[i];
            if (p == null) continue;
            p.Rotate(0f, 0f, angle, Space.Self); 
        }
    }

    private void UpdateGimbalCamera(float dt)
    {
        if (gimbalCamera == null) return;

        if (Input.GetKey(KeyCode.RightShift))
        {
            _gimbalLocalPitch = Mathf.Lerp(_gimbalLocalPitch, 0f, dt * gimbalResetSpeed);
            _gimbalLocalYaw = Mathf.Lerp(_gimbalLocalYaw, 0f, dt * gimbalResetSpeed);
        }
        else
        {
            float camPitchInput = 0f;
            float camYawInput = 0f;

            if (Input.GetKey(KeyCode.UpArrow)) camPitchInput -= 1f;    
            if (Input.GetKey(KeyCode.DownArrow)) camPitchInput += 1f;  
            if (Input.GetKey(KeyCode.LeftArrow)) camYawInput -= 1f;   
            if (Input.GetKey(KeyCode.RightArrow)) camYawInput += 1f;  

            // Add Mouse Input
            float mouseX = Input.GetAxis("Mouse X");
            float mouseY = Input.GetAxis("Mouse Y");
            float mouseSensitivity = 3f;

            _gimbalLocalYaw += mouseX * mouseSensitivity;
            _gimbalLocalPitch -= mouseY * mouseSensitivity;

            _gimbalLocalPitch += camPitchInput * gimbalPitchSpeed * dt;
            _gimbalLocalPitch = Mathf.Clamp(_gimbalLocalPitch, minGimbalPitch, maxGimbalPitch);
            _gimbalLocalYaw += camYawInput * gimbalYawSpeed * dt;
        }

        Quaternion inertiaOffset = Quaternion.identity;
        if (!_isFlightControlCutoff)
        {
            Quaternion justBodyTilt = Quaternion.Inverse(Quaternion.Euler(0f, _yawDeg, 0f)) * _targetBodyTiltRotation;
            inertiaOffset = Quaternion.Slerp(Quaternion.identity, justBodyTilt, gimbalInertiaStrength);
        }

        Quaternion worldGimbalBase = Quaternion.Euler(0f, _yawDeg + _gimbalLocalYaw, 0f);
        Quaternion targetWorldRotation = worldGimbalBase * Quaternion.Euler(_gimbalLocalPitch, 0f, 0f) * inertiaOffset;

        gimbalCamera.rotation = targetWorldRotation;
    }

    private void OnCollisionEnter(Collision collision)
    {
        if (_isFlightControlCutoff) return;

        // 碰撞自愈第一步：立即强制捕获实际碰撞后的残余物理状态，刷新虚拟变量
        if (_rb != null)
        {
            Vector3 actualVelocity = _rb.velocity;
            _currentHorizontalVelocity = new Vector3(actualVelocity.x, 0f, actualVelocity.z);
            _currentVerticalVelocity = actualVelocity.y;
        }

        // 碰撞自愈第二步：瞬间强制回正视觉倾斜角，避免物理扭矩在碰撞表面叠加发生连环旋转卡死
        _targetBodyTiltRotation = Quaternion.Euler(0f, _yawDeg, 0f);
    }

    private void OnCollisionStay(Collision collision)
    {
        if (_isFlightControlCutoff) return;

        // 贴墙滑行、卡障碍物擦拭时的连续压制，防止数据反弹和蓄力冲锋
        if (_rb != null && !Input.anyKey)
        {
            _currentHorizontalVelocity = Vector3.MoveTowards(_currentHorizontalVelocity, new Vector3(_rb.velocity.x, 0f, _rb.velocity.z), Time.deltaTime * 50f);
            _currentVerticalVelocity = Mathf.MoveTowards(_currentVerticalVelocity, _rb.velocity.y, Time.deltaTime * 50f);
        }
    }
}