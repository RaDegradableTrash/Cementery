using System.Collections.Generic;
using UnityEngine;
using TMPro;

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

    [Header("Control Override")]
    [SerializeField] private bool activeControl = false;
    public bool ActiveControl { get => activeControl; set => activeControl = value; }
    
    [SerializeField] private TextMeshProUGUI speedDisplay;
    [SerializeField] private float speedMultiplier = 1f;
    [SerializeField] private Transform steeringWheel;
    [SerializeField] private Vector3 steeringWheelLocalAxis = new Vector3(0, 0, 1);
    [SerializeField] private float steeringWheelMaxTurn = 540f; // degrees (1.5 turns)
    [SerializeField] private bool invertSteeringWheel = false;
    [SerializeField] private float steeringResponseSpeed = 45f; // degrees per second
    [SerializeField] private float steeringReturnSpeed = 120f; // degrees per second
    [SerializeField] private float steeringReturnMinSpeedKmh = 1f;
    [SerializeField] private float innerSteerAngle = 37f; // degrees
    [SerializeField] private float outerSteerAngle = 25f; // degrees
    [SerializeField] private float l6ThrottleRise = 0.4f; // per second
    [SerializeField] private float l6ThrottleFall = 0.8f; // per second
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

    WheelControl[] wheels;
    Rigidbody rigidBody;
    private float currentSteerAngle;
    private float currentSpeedKmh;
    private float l6ThrottleCurrent;
    private readonly HashSet<WheelControl> sixLockWheelSet = new HashSet<WheelControl>();

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
            return GetMaxWheelRpm() <= sixLockSwitchMaxWheelRpm;
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

    // Start is called before the first frame update
    void Start()
    {
        rigidBody = GetComponent<Rigidbody>();

        if (startProcedure == null)
        {
            startProcedure = FindObjectOfType<StartProcedure>();
        }

        // Adjust center of mass vertically, to help prevent the car from rolling
        rigidBody.centerOfMass += Vector3.up * centreOfGravityOffset;

        // Find all child GameObjects that have the WheelControl script attached
        wheels = GetComponentsInChildren<WheelControl>();
        BuildSixLockWheelSet();
        // Record initial local rotation of steering wheel (if assigned)
        if (steeringWheel != null)
        {
            steeringWheelInitialLocalRotation = steeringWheel.localRotation;
        }

        SetGearInternal(startGear, true);
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

    // Update is called once per frame
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

        // Calculate current speed in relation to the forward direction of the car
        // (this returns a negative number when traveling backwards)
        float forwardSpeed = Vector3.Dot(transform.forward, rigidBody.velocity);

        // Update speed display in km/h
        float displaySpeed = Mathf.Abs(forwardSpeed) * 3.6f * speedMultiplier;
        currentSpeedKmh = displaySpeed;
        if (speedDisplay != null)
        {
            speedDisplay.text = Mathf.Round(displaySpeed).ToString() + " km/h";
        }

        // Calculate motor torque factor using Unity's forwardSpeed (m/s)
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

        // Use that to calculate how much torque is available (zero torque at top speed)
        float currentMotorTorque = Mathf.Lerp(requestedMotorTorque, 0, speedFactorMotor);

        // Calculate steering limit multiplier from speed (km/h)
        // Higher speed means a smaller allowed steering angle.
        float steeringLimitMultiplier = Mathf.Clamp01(steeringLimitBySpeed.Evaluate(displaySpeed));
        bool steeringLocked = currentGear == GearMode.Park;
        float outerMaxAngle = outerSteerAngle * steeringLimitMultiplier;
        float innerMaxAngle = innerSteerAngle * steeringLimitMultiplier;
        float currentMaxWheelSteerAngle = outerMaxAngle;

        // Prepare accumulators to compute actual wheel steer angle average
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
            // Apply steering to Wheel colliders that have "Steerable" enabled
            if (wheel.steerable)
            {
                // Set steer angle based on inner/outer wheel settings
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

            // Apply handbrake if spacebar is pressed
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
        // After applying wheel steer angles, map actual average wheel steer to steering wheel rotation
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
}