using System.Collections.Generic;
using UnityEngine;
using TMPro;

[RequireComponent(typeof(AudioSource))]
public class CarControl : MonoBehaviour
{
    public enum GearMode
    {
        Park,
        Reverse,
        Neutral,
        Drive,
        Sport,
        H6,
        L6
    }

    [Header("Gear")]
    [SerializeField] private GearMode startGear = GearMode.Park;
    [SerializeField] private GearMode currentGear = GearMode.Park;
    public GearMode CurrentGear => currentGear;
    public event System.Action<GearMode> OnGearChanged;
    [SerializeField] private bool engineOn = false;
    public bool EngineOn => engineOn;
    public event System.Action<bool> OnEngineStateChanged;
    [SerializeField] private bool electricalPowerOn = true;
    public bool ElectricalPowerOn => electricalPowerOn;
    public event System.Action<bool> OnElectricalPowerChanged;

    [Header("Start Procedure")]
[SerializeField] private StartProcedure startProcedure;
[SerializeField] private bool startWithEngineRunning = false;  // 新增：是否以启动状态开始

    [Header("Modes")]
    [SerializeField] private float sportTorque = 50000f;
    [SerializeField] private float sixLockTorque = 50000f;
    [SerializeField] private float h6MaxSpeedKmh = 75f;
    [SerializeField] private float l6MaxSpeedKmh = 35f;
    [SerializeField] private float driveMaxSpeedKmh = 160f;
    [SerializeField] private float reverseMaxSpeedKmh = 30f;
    [SerializeField] private float sixLockSwitchMaxWheelRpm = 0.01f;
    [SerializeField] private float speedLimiterBrake = 0.2f;
    [SerializeField] private WheelControl[] sixLockWheels = new WheelControl[6];

    public float motorTorque = 35000;
    public float brakeTorque = 400000;
    public float eBrakeTorque = 10000000f;
    public float maxSpeed = 20;
    public float steeringRange = 30;
    public float steeringRangeAtMaxSpeed = 10;
    public float centreOfGravityOffset = -1f;

    [Header("Transmission (Auto)")]
    [SerializeField] private float finalDriveRatio = 3.42f;
    [SerializeField] private float[] forwardGearRatios = new float[] { 3.5f, 2.0f, 1.4f, 1.0f, 0.75f, 0.6f };
    [SerializeField] private float reverseGearRatio = 3.0f;
    [SerializeField] private float engineMinRpm = 500f;
    [SerializeField] private float upshiftRpm = 2200f;
    [SerializeField] private float downshiftRpm = 1200f;
    [SerializeField] private float shiftDuration = 0.5f;

    private int currentTransmissionGear = 0;
    private float shiftTimer = 0f;
    private float smoothEngineRpm = 500f;

    // 暴露给外部脚本（如相机晃动脚本）访问的平滑发动机转速接口
public float SmoothEngineRpm => smoothEngineRpm;

    [Header("Control Override")]
    [SerializeField] private bool activeControl = false;
    public bool ActiveControl { get => activeControl; set => activeControl = value; }
    
    [SerializeField] private TextMeshProUGUI speedDisplay;
    [SerializeField] private TextMeshProUGUI rpmDisplay;
    [SerializeField] private TextMeshProUGUI gearDisplay;
    [SerializeField] private float speedMultiplier = 1f;
    [SerializeField] private Transform steeringWheel;
    [SerializeField] private Vector3 steeringWheelLocalAxis = new Vector3(0, 0, 1);
    [SerializeField] private float steeringWheelMaxTurn = 540f;
    [SerializeField] private bool invertSteeringWheel = false;
    [SerializeField] private float steeringResponseSpeed = 45f;
    [SerializeField] private float steeringReturnSpeed = 120f;
    [SerializeField] private float steeringReturnMinSpeedKmh = 1f;
    [SerializeField] private float innerSteerAngle = 37f;
    [SerializeField] private float outerSteerAngle = 25f;
    [SerializeField] private float l6ThrottleRise = 0.4f;
    [SerializeField] private float l6ThrottleFall = 0.8f;
    [SerializeField] private AnimationCurve l6TorqueBySpeed = new AnimationCurve(
        new Keyframe(0f, 1f),
        new Keyframe(10f, 1f),
        new Keyframe(25f, 0.85f),
        new Keyframe(35f, 0.7f)
    );
    [SerializeField] private AnimationCurve steeringReturnBySpeed = new AnimationCurve(
        new Keyframe(0f, 0f),
        new Keyframe(10f, 0.3f),
        new Keyframe(30f, 0.7f),
        new Keyframe(80f, 1f)
    );
    private Quaternion steeringWheelInitialLocalRotation;
    [SerializeField] private AnimationCurve steeringLimitBySpeed = new AnimationCurve(
        new Keyframe(0f, 1f),
        new Keyframe(30f, 0.27f),
        new Keyframe(60f, 0.11f),
        new Keyframe(80f, 0.065f),
        new Keyframe(100f, 0.045f),
        new Keyframe(120f, 0.0335f),
        new Keyframe(140f, 0.0205f),
        new Keyframe(160f, 0.0205f)
    );

    // ========== 引擎声音参数 ==========
    [Header("Engine Audio")]
    [Range(0f, 0.8f)] public float engineMasterVolume = 0.5f;
    [Range(0.05f, 0.25f)] public float pulseWidth = 0.12f;
    [Range(0f, 1f)] public float pulseSharpness = 0.6f;
    [Range(0f, 1f)] public float exhaustResonance = 0.7f;
    [Range(0f, 1f)] public float exhaustDrone = 0.4f;
    [Range(0f, 1f)] public float intakeSound = 0.5f;
    [Range(0f, 1f)] public float turboWhine = 0.6f;
    [Range(0f, 0.15f)] public float mechanicalNoise = 0.07f;
    [Range(0f, 0.3f)] public float cylinderImbalance = 0.15f;

    WheelControl[] wheels;
    Rigidbody rigidBody;
    private float currentSteerAngle;
    private float currentSpeedKmh;
    private float l6ThrottleCurrent;
    private readonly HashSet<WheelControl> sixLockWheelSet = new HashSet<WheelControl>();
    
    // 引擎声音相关变量
    private float engineLoad = 0f;
    private double phase;
    private double exhaustPhase;
    private double intakePhase;
    private double turboPhase;
    private double samplingRate;
    private uint noiseSeed = 123456789u;

    public void SetGear(GearMode gear)
    {
        SetGearInternal(gear, false);
    }

    public void SetEngineOn(bool value)
    {
        if (engineOn == value)
        {
            return;
        }

        engineOn = value;
        OnEngineStateChanged?.Invoke(engineOn);
    }

    public void SetElectricalPower(bool value)
    {
        if (electricalPowerOn == value)
        {
            return;
        }

        electricalPowerOn = value;
        OnElectricalPowerChanged?.Invoke(electricalPowerOn);
    }

    private void SetGearInternal(GearMode gear, bool force)
    {
        if (!force && currentGear == gear)
        {
            return;
        }

        if (!force && !CanSwitchGear(gear))
        {
            return;
        }

        currentGear = gear;
        OnGearChanged?.Invoke(currentGear);
    }

    private bool CanSwitchGear(GearMode targetGear)
    {
        bool currentSix = IsSixLockGear(currentGear);
        bool targetSix = IsSixLockGear(targetGear);
        if (currentSix || targetSix)
        {
            bool isHandBraking = activeControl && Input.GetKey(KeyCode.Space);
            return GetMaxWheelRpm() <= sixLockSwitchMaxWheelRpm || isHandBraking;
        }
        return true;
    }

    private static bool IsSixLockGear(GearMode gear)
    {
        return gear == GearMode.H6 || gear == GearMode.L6;
    }

    private void BuildSixLockWheelSet()
    {
        sixLockWheelSet.Clear();
        if (sixLockWheels == null)
        {
            return;
        }
        foreach (WheelControl wheel in sixLockWheels)
        {
            if (wheel != null)
            {
                sixLockWheelSet.Add(wheel);
            }
        }
    }

    private float GetMaxWheelRpm()
    {
        float maxRpm = 0f;
        if (sixLockWheelSet.Count > 0)
        {
            foreach (WheelControl wheel in sixLockWheelSet)
            {
                if (wheel == null || wheel.WheelCollider == null)
                {
                    continue;
                }
                float rpm = Mathf.Abs(wheel.WheelCollider.rpm);
                if (rpm > maxRpm)
                {
                    maxRpm = rpm;
                }
            }
            return maxRpm;
        }

        if (wheels == null)
        {
            return maxRpm;
        }

        foreach (WheelControl wheel in wheels)
        {
            if (wheel == null || wheel.WheelCollider == null)
            {
                continue;
            }
            float rpm = Mathf.Abs(wheel.WheelCollider.rpm);
            if (rpm > maxRpm)
            {
                maxRpm = rpm;
            }
        }
        return maxRpm;
    }

    private float GetStableWheelRpm(float forwardSpeed)
    {
        float radius = 0.35f;
        if (wheels != null && wheels.Length > 0 && wheels[0] != null && wheels[0].WheelCollider != null)
        {
            radius = wheels[0].WheelCollider.radius;
        }
        return (Mathf.Abs(forwardSpeed) * 60f) / (2f * Mathf.PI * radius);
    }

    void Start()
    {
        rigidBody = GetComponent<Rigidbody>();

        if (startProcedure == null)
        {
            startProcedure = FindObjectOfType<StartProcedure>();
        }

        rigidBody.centerOfMass += Vector3.up * centreOfGravityOffset;

        wheels = GetComponentsInChildren<WheelControl>();
        BuildSixLockWheelSet();
        
        // 初始化 AudioSource
        AudioSource audioSource = GetComponent<AudioSource>();
        if (audioSource != null)
        {
            audioSource.playOnAwake = false;
            audioSource.loop = true;
            audioSource.spatialBlend = 0f;
            audioSource.Play();
        }
        
        samplingRate = AudioSettings.outputSampleRate;
        
        if (steeringWheel != null)
        {
            steeringWheelInitialLocalRotation = steeringWheel.localRotation;
        }

SetGearInternal(startGear, true);

// 新增：根据 startWithEngineRunning 决定初始状态
if (startWithEngineRunning)
{
    // 强制启动状态：引擎开，电源开
    SetEngineOn(true);
    SetElectricalPower(true);
    
    // 如果有 StartProcedure 组件，也同步其状态
    if (startProcedure != null)
    {
        // 使用反射或公共方法强制启动 StartProcedure
        // 由于 StartProcedure 可能需要启动油泵等，我们直接调用其公共方法（如果有的话）
        // 如果没有，至少确保电源和引擎状态同步
        startProcedure.ForceStartVehicle(); // 需要你在 StartProcedure 中添加这个方法
    }
}
else
{
    // 原来的逻辑
    SetEngineOn(engineOn);
    if (startProcedure != null)
    {
        SetElectricalPower(startProcedure.HasAnyBatteryOn());
    }
    else
    {
        SetElectricalPower(true);
    }
}
    }

    void Update()
    {
        if (startProcedure != null)
        {
            SetElectricalPower(startProcedure.HasAnyBatteryOn());
            if (engineOn != startProcedure.EngineOn)
            {
                SetEngineOn(startProcedure.EngineOn);
            }
        }
        float rawVertical = activeControl ? Input.GetAxis("Vertical") : 0f;
        float hInputRaw = activeControl ? Input.GetAxisRaw("Horizontal") : 0f;

        float forwardSpeed = Vector3.Dot(transform.forward, rigidBody.velocity);

        float displaySpeed = Mathf.Abs(forwardSpeed) * 3.6f * speedMultiplier;
        currentSpeedKmh = displaySpeed;
        if (speedDisplay != null)
        {
            speedDisplay.text = Mathf.Round(displaySpeed).ToString() + "";//km/h
        }

        float speedFactorMotor = Mathf.InverseLerp(0, maxSpeed, forwardSpeed);

        float requestedMotorTorque = motorTorque;
        if (currentGear == GearMode.Sport)
        {
            requestedMotorTorque = sportTorque;
        }
        else if (currentGear == GearMode.H6 || currentGear == GearMode.L6)
        {
            requestedMotorTorque = sixLockTorque;
        }

        if (currentGear == GearMode.L6)
        {
            requestedMotorTorque *= Mathf.Clamp01(l6TorqueBySpeed.Evaluate(displaySpeed));
        }

        float currentMotorTorque = Mathf.Lerp(requestedMotorTorque, 0, speedFactorMotor);

        float steeringLimitMultiplier = Mathf.Clamp01(steeringLimitBySpeed.Evaluate(displaySpeed));
        bool steeringLocked = currentGear == GearMode.Park;
        float outerMaxAngle = outerSteerAngle * steeringLimitMultiplier;
        float innerMaxAngle = innerSteerAngle * steeringLimitMultiplier;
        float currentMaxWheelSteerAngle = outerMaxAngle;

        float sumSteerAngles = 0f;
        int steerCount = 0;

        bool wantsForward = rawVertical > 0.01f;
        bool wantsBackward = rawVertical < -0.01f;

        float throttleInput = 0f;
        float brakeInput = 0f;

        switch (currentGear)
        {
            case GearMode.Park:
                throttleInput = 0f;
                brakeInput = 1f;
                steeringLocked = true;
                break;
            case GearMode.Neutral:
                throttleInput = 0f;
                brakeInput = wantsBackward ? Mathf.Abs(rawVertical) : 0f;
                break;
            case GearMode.Drive:
            case GearMode.Sport:
            case GearMode.H6:
            case GearMode.L6:
                if (wantsForward)
                {
                    throttleInput = -rawVertical;
                }
                if (wantsBackward)
                {
                    brakeInput = Mathf.Abs(rawVertical);
                }
                break;
            case GearMode.Reverse:
                if (wantsForward)
                {
                    throttleInput = rawVertical;
                }
                if (wantsBackward)
                {
                    brakeInput = Mathf.Abs(rawVertical);
                }
                break;
        }

        if (engineOn)
        {
            if (FuelTank.SharedFuel <= 0f)
            {
                SetEngineOn(false);
                if (startProcedure != null)
                {
                    startProcedure.ForceShutdownEngine();
                }
                throttleInput = 0f;
            }
            else
            {
                float baseRate = 0.5f;
                float activeRate = 1.5f;
                float consumption = (baseRate + Mathf.Abs(rawVertical) * activeRate) * Time.deltaTime;
                FuelTank.SharedFuel -= consumption;
            }
        }

        if (!engineOn)
        {
            throttleInput = 0f;
        }
        else if (startProcedure != null && !startProcedure.HasAnyPumpOn())
        {
            throttleInput = 0f;
        }

        float appliedThrottleInput = throttleInput;
        if (currentGear == GearMode.L6)
        {
            float rate = Mathf.Abs(throttleInput) > Mathf.Abs(l6ThrottleCurrent) ? l6ThrottleRise : l6ThrottleFall;
            l6ThrottleCurrent = Mathf.MoveTowards(l6ThrottleCurrent, throttleInput, rate * Time.deltaTime);
            appliedThrottleInput = l6ThrottleCurrent;
        }
        else
        {
            l6ThrottleCurrent = throttleInput;
        }

        float speedLimitKmh = 0f;
        switch (currentGear)
        {
            case GearMode.H6:
                speedLimitKmh = h6MaxSpeedKmh;
                break;
            case GearMode.L6:
                speedLimitKmh = l6MaxSpeedKmh;
                break;
            case GearMode.Drive:
            case GearMode.Sport:
                speedLimitKmh = driveMaxSpeedKmh;
                break;
            case GearMode.Reverse:
                speedLimitKmh = reverseMaxSpeedKmh;
                break;
        }

        if (speedLimitKmh > 0f && displaySpeed > speedLimitKmh)
        {
            appliedThrottleInput = 0f;
            if (currentGear == GearMode.L6)
            {
                l6ThrottleCurrent = Mathf.MoveTowards(l6ThrottleCurrent, 0f, l6ThrottleFall * Time.deltaTime);
            }
        }

        // --- Transmission & Engine RPM Calculation ---
        float currentGearRatio = 0f;
        string gearString = currentGear.ToString();

        if (currentGear == GearMode.Reverse) {
            currentGearRatio = reverseGearRatio;
            gearString = "R";
        } else if (currentGear == GearMode.Drive || currentGear == GearMode.Sport || IsSixLockGear(currentGear)) {
            if (currentTransmissionGear < 0) currentTransmissionGear = 0;
            if (currentTransmissionGear >= forwardGearRatios.Length) currentTransmissionGear = forwardGearRatios.Length - 1;
            currentGearRatio = forwardGearRatios[currentTransmissionGear];
            gearString = "D" + (currentTransmissionGear + 1);
        } else if (currentGear == GearMode.Park) {
            gearString = "P";
        } else if (currentGear == GearMode.Neutral) {
            gearString = "N";
        }
        
        if (gearDisplay != null) {
            gearDisplay.text = gearString;
        }

        // ========== 用转速限制车速 ==========
        float maxEngineRpm = 2800f;

        // 获取当前车轮半径
        float wheelRadius = 0.35f;
        if (wheels != null && wheels.Length > 0 && wheels[0] != null && wheels[0].WheelCollider != null)
        {
            wheelRadius = wheels[0].WheelCollider.radius;
        }

        // 根据当前档位计算理论最高车速
        if (currentGear == GearMode.Drive || currentGear == GearMode.Sport || IsSixLockGear(currentGear))
        {
            float maxWheelRpmForCurrentGear = maxEngineRpm / (currentGearRatio * finalDriveRatio);
            float maxForwardSpeedForCurrentGear = maxWheelRpmForCurrentGear * (2f * Mathf.PI * wheelRadius) / 60f;
            
            // 限制 forwardSpeed 不超过理论最高速度
            float absForwardSpeed = Mathf.Abs(forwardSpeed);
            if (absForwardSpeed > maxForwardSpeedForCurrentGear)
            {
                float limitedSpeed = Mathf.Sign(forwardSpeed) * maxForwardSpeedForCurrentGear;
                
                // 修正刚体速度
                Vector3 currentVel = rigidBody.velocity;
                float currentSideways = Vector3.Dot(transform.right, currentVel);
                float currentUp = Vector3.Dot(transform.up, currentVel);
                rigidBody.velocity = transform.forward * limitedSpeed + transform.right * currentSideways + transform.up * currentUp;
                
                forwardSpeed = limitedSpeed;
            }
        }
        // ============================================

        float absWheelRpm = GetStableWheelRpm(forwardSpeed);
        float calculatedEngineRpm = absWheelRpm * currentGearRatio * finalDriveRatio;
        float targetEngineRpm = Mathf.Max(engineMinRpm, Mathf.Min(calculatedEngineRpm, maxEngineRpm));

        // Auto-Shift Logic
        if (shiftTimer <= 0f && (currentGear == GearMode.Drive || currentGear == GearMode.Sport || IsSixLockGear(currentGear)))
        {
            if (targetEngineRpm > upshiftRpm && currentTransmissionGear < forwardGearRatios.Length - 1)
            {
                currentTransmissionGear++;
                shiftTimer = shiftDuration;
            }
            else if (targetEngineRpm < downshiftRpm && currentTransmissionGear > 0)
            {
                currentTransmissionGear--;
                shiftTimer = shiftDuration;
            }
        }

        if (shiftTimer > 0f)
        {
            shiftTimer -= Time.deltaTime;
            appliedThrottleInput = 0f;
            targetEngineRpm = engineMinRpm;
        }
        else if (currentGear == GearMode.Park || currentGear == GearMode.Neutral)
        {
            targetEngineRpm = engineMinRpm + Mathf.Abs(throttleInput) * (upshiftRpm - engineMinRpm);
        }

        if (!engineOn)
        {
            targetEngineRpm = 0f;
            appliedThrottleInput = 0f;
        }

        float rpmLerpSpeed = (targetEngineRpm > smoothEngineRpm) ? 5f : 3f;
        
        if (engineOn)
        {
            smoothEngineRpm = Mathf.Lerp(smoothEngineRpm, targetEngineRpm, Time.deltaTime * rpmLerpSpeed);
        }
        else
        {
            smoothEngineRpm = Mathf.Lerp(smoothEngineRpm, 0f, Time.deltaTime * 3f);
        }

        // 更新引擎负载（用于声音）
        float targetLoad = engineOn ? Mathf.Abs(appliedThrottleInput) : 0f;
        engineLoad = Mathf.Lerp(engineLoad, targetLoad, Time.deltaTime * 8f);

        if (rpmDisplay != null)
        {
            rpmDisplay.text = Mathf.Round(smoothEngineRpm).ToString() + "";//rpm
        }

        bool steerInputActive = Mathf.Abs(hInputRaw) > 0.01f;
        float targetSteerAngle = hInputRaw * currentMaxWheelSteerAngle;
        if (!steeringLocked && steerInputActive)
        {
            currentSteerAngle = Mathf.MoveTowards(currentSteerAngle, targetSteerAngle, steeringResponseSpeed * Time.deltaTime);
        }
        else if (!steeringLocked && displaySpeed > steeringReturnMinSpeedKmh)
        {
            float returnSpeed = steeringReturnSpeed * Mathf.Clamp01(steeringReturnBySpeed.Evaluate(displaySpeed));
            currentSteerAngle = Mathf.MoveTowards(currentSteerAngle, 0f, returnSpeed * Time.deltaTime);
        }

        bool isHandBraking = activeControl && Input.GetKey(KeyCode.Space);

        foreach (var wheel in wheels)
        {
            if (wheel.steerable)
            {
                float steerAngleForWheel = currentSteerAngle;
                if (wheel.isFrontLeft || wheel.isFrontRight)
                {
                    float absOuter = Mathf.Abs(currentSteerAngle);
                    if (absOuter > 0.0001f)
                    {
                        float ratio = outerMaxAngle > 0.001f ? (innerMaxAngle / outerMaxAngle) : 1f;
                        float absInner = absOuter * ratio;
                        bool turningRight = currentSteerAngle > 0f;
                        bool isInner = (turningRight && wheel.isFrontRight) || (!turningRight && wheel.isFrontLeft);
                        steerAngleForWheel = Mathf.Sign(currentSteerAngle) * (isInner ? absInner : absOuter);
                    }
                }

                wheel.WheelCollider.steerAngle = steerAngleForWheel;
                sumSteerAngles += wheel.WheelCollider.steerAngle;
                steerCount++;
            }
            
            if (currentGear == GearMode.Park)
            {
                wheel.WheelCollider.brakeTorque = brakeTorque;
                wheel.WheelCollider.motorTorque = 0f;
                continue;
            }

            if (isHandBraking)
            {
                wheel.WheelCollider.brakeTorque = eBrakeTorque;
                wheel.WheelCollider.motorTorque = 0f;
            }
            else
            {
                wheel.WheelCollider.brakeTorque = brakeInput * brakeTorque;
                bool isSixLock = IsSixLockGear(currentGear);
                bool allowSixLockDrive = isSixLock && (sixLockWheelSet.Count == 0 || sixLockWheelSet.Contains(wheel));
                bool isMotorized = isSixLock ? allowSixLockDrive : wheel.motorized;
                if (isMotorized)
                {
                    wheel.WheelCollider.motorTorque = appliedThrottleInput * currentMotorTorque;
                }
                else
                {
                    wheel.WheelCollider.motorTorque = 0f;
                }
            }
        }
        
        if (steeringWheel != null)
        {
            float avgWheelSteerAngle = steerCount > 0 ? (sumSteerAngles / steerCount) : 0f;
            float denom = outerSteerAngle != 0f ? outerSteerAngle : 1f;
            float steeringNormalized = Mathf.Clamp(avgWheelSteerAngle / denom, -1f, 1f);
            float dir = invertSteeringWheel ? -1f : 1f;
            float targetAngle = steeringNormalized * steeringWheelMaxTurn * dir;
            steeringWheel.localRotation = steeringWheelInitialLocalRotation * Quaternion.AngleAxis(targetAngle, steeringWheelLocalAxis);
        }
    }

    // ========== 引擎声音生成 ==========
    private float GetDeterministicNoise()
    {
        noiseSeed ^= noiseSeed << 13;
        noiseSeed ^= noiseSeed >> 17;
        noiseSeed ^= noiseSeed << 5;
        return (noiseSeed / (float)uint.MaxValue) * 2f - 1f;
    }

    void OnAudioFilterRead(float[] data, int channels)
    {
        float rpm = Mathf.Max(0, smoothEngineRpm);
        float rpmRatio = Mathf.Clamp01(rpm / 2800f);
        
        double freqIncrement = (rpm / 60.0) * 4.0 / samplingRate;
        double exhaustIncrement = (rpm / 60.0) * 2.0 / samplingRate;
        double intakeIncrement = (rpm / 60.0) * 8.0 / samplingRate;
        double turboIncrement = (rpm / 60.0) * 24.0 / samplingRate;
        
        float vol = engineMasterVolume;
        float pWidth = pulseWidth;
        float sharpness = pulseSharpness;
        float exhaustRes = exhaustResonance;
        float exhaustDroneVal = exhaustDrone;
        float intake = intakeSound;
        float turbo = turboWhine;
        float mechanical = mechanicalNoise;
        float imbalance = cylinderImbalance;
        
        float idleBias = Mathf.Clamp01(1f - rpmRatio * 2f);
        float highRpmBias = rpmRatio * rpmRatio;
        
        double localPhase = phase;
        double localExhaustPhase = exhaustPhase;
        double localIntakePhase = intakePhase;
        double localTurboPhase = turboPhase;
        
        for (int i = 0; i < data.Length; i += channels)
        {
            localPhase += freqIncrement;
            if (localPhase > 1.0) localPhase -= 1.0;
            localExhaustPhase += exhaustIncrement;
            if (localExhaustPhase > 1.0) localExhaustPhase -= 1.0;
            localIntakePhase += intakeIncrement;
            if (localIntakePhase > 1.0) localIntakePhase -= 1.0;
            localTurboPhase += turboIncrement;
            if (localTurboPhase > 1.0) localTurboPhase -= 1.0;
            
            float signal = 0f;
            float phaseAngle = (float)(localPhase * Mathf.PI * 2f);
            
            // 点火脉冲
            float pulse = 0f;
            if (localPhase < pWidth)
            {
                float t = (float)(localPhase / pWidth);
                float curve = Mathf.Lerp(1f - t, Mathf.Exp(-sharpness * 5f * t), sharpness);
                pulse = curve * (1f + Mathf.Sin(phaseAngle * 0.5f) * 0.3f);
            }
            
            float cylinderVar = 1f + Mathf.Sin((float)(localPhase * Mathf.PI * 32f)) * imbalance * 0.5f;
            pulse *= cylinderVar;
            signal += pulse * 0.7f;
            
            // 十字曲轴律动
            float rumble = Mathf.Sin(phaseAngle) * 0.25f;
            rumble += Mathf.Sin(phaseAngle * 2f) * 0.12f * rpmRatio;
            rumble += Mathf.Sin(phaseAngle * 3f) * 0.06f * highRpmBias;
            signal += rumble;
            
            // 排气系统
            float exhaustAngle = (float)(localExhaustPhase * Mathf.PI * 2f);
            float exhaust = 0f;
            exhaust += Mathf.Sin(exhaustAngle * 2f) * 0.4f * exhaustRes;
            exhaust += Mathf.Sin((float)(localPhase * Mathf.PI * 0.8f)) * 0.3f * exhaustDroneVal * idleBias;
            exhaust += Mathf.Exp(-Mathf.Abs(Mathf.Sin(exhaustAngle))) * 0.2f;
            signal += exhaust * 0.3f;
            
            // 进气系统
            float intakeAngle = (float)(localIntakePhase * Mathf.PI * 2f);
            float intakeSig = 0f;
            float intakeBias = Mathf.Sin(rpmRatio * Mathf.PI) * 0.8f;
            intakeSig += Mathf.Sin(intakeAngle) * 0.4f * intakeBias;
            intakeSig += Mathf.Sin(intakeAngle * 3f) * 0.15f * highRpmBias;
            signal += intakeSig * intake;
            
            // 涡轮
            float turboAngle = (float)(localTurboPhase * Mathf.PI * 2f);
            float turbosound = 0f;
            if (rpmRatio > 0.4f)
            {
                float turboStrength = Mathf.Clamp01((rpmRatio - 0.4f) / 0.6f);
                turbosound += Mathf.Sin(turboAngle) * 0.25f * turboStrength;
                turbosound += Mathf.Sin(turboAngle * 2.3f) * 0.12f * turboStrength;
                if (highRpmBias > 0.6f)
                {
                    turbosound += Mathf.Sin(turboAngle * 4.7f) * 0.08f;
                }
            }
            signal += turbosound * turbo;
            
            // 机械噪音
            float mechNoise = GetDeterministicNoise() * mechanical;
            mechNoise *= (0.5f + rpmRatio * 0.5f);
            signal += mechNoise;
            
            // 发动机负载感
            float loadPulse = Mathf.Sin(phaseAngle * 4f) * engineLoad * 0.2f;
            signal += loadPulse;
            
            // 音量包络
            float volumeEnvelope = Mathf.Lerp(0.65f, 1.0f, rpmRatio);
            volumeEnvelope += engineLoad * 0.2f;
            volumeEnvelope = Mathf.Clamp01(volumeEnvelope);
            
            // 低通滤波
            float filteredSignal = signal;
            if (rpmRatio < 0.3f)
            {
                filteredSignal = signal * 0.7f + mechNoise * 0.3f;
            }
            
            float finalSample = filteredSignal * vol * volumeEnvelope;
            finalSample = Mathf.Clamp(finalSample, -0.95f, 0.95f);
            
            for (int ch = 0; ch < channels; ch++)
            {
                data[i + ch] = finalSample;
            }
        }
        
        phase = localPhase;
        exhaustPhase = localExhaustPhase;
        intakePhase = localIntakePhase;
        turboPhase = localTurboPhase;
    }
}