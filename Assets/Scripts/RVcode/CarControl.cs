using UnityEngine;
using TMPro;

public class CarControl : MonoBehaviour
{
    public float motorTorque = 2000;
    public float brakeTorque = 2000;
    public float eBrakeTorque = 10000000f;
    public float maxSpeed = 20;
    public float steeringRange = 30;
    public float steeringRangeAtMaxSpeed = 10;
    public float centreOfGravityOffset = -1f;
    
    [SerializeField] private TextMeshProUGUI speedDisplay;
    [SerializeField] private float speedMultiplier = 1f;
    [SerializeField] private Transform steeringWheel;
    [SerializeField] private Vector3 steeringWheelLocalAxis = new Vector3(0, 0, 1);
    [SerializeField] private float steeringWheelMaxTurn = 540f; // degrees (1.5 turns)
    [SerializeField] private bool invertSteeringWheel = false;
    private Quaternion steeringWheelInitialLocalRotation;
    [SerializeField] private AnimationCurve steeringLimitBySpeed = new AnimationCurve(
        new Keyframe(0f, 1f),
        new Keyframe(10f, 0.7f),
        new Keyframe(20f, 0.4f),
        new Keyframe(30f, 0.2f),
        new Keyframe(40f, 0.1f),
        new Keyframe(50f, 0.07f)
    );

    WheelControl[] wheels;
    Rigidbody rigidBody;

    // Start is called before the first frame update
    void Start()
    {
        rigidBody = GetComponent<Rigidbody>();

        // Adjust center of mass vertically, to help prevent the car from rolling
        rigidBody.centerOfMass += Vector3.up * centreOfGravityOffset;

        // Find all child GameObjects that have the WheelControl script attached
        wheels = GetComponentsInChildren<WheelControl>();
        // Record initial local rotation of steering wheel (if assigned)
        if (steeringWheel != null)
        {
            steeringWheelInitialLocalRotation = steeringWheel.localRotation;
        }
    }

    // Update is called once per frame
    void Update()
    {

        float vInput = -Input.GetAxis("Vertical");
        float hInput = Input.GetAxis("Horizontal");

        // Calculate current speed in relation to the forward direction of the car
        // (this returns a negative number when traveling backwards)
        float forwardSpeed = Vector3.Dot(transform.forward, rigidBody.velocity);

        // Update speed display in km/h
        float displaySpeed = Mathf.Abs(forwardSpeed) * 3.6f * speedMultiplier;
        if (speedDisplay != null)
        {
            speedDisplay.text = Mathf.Round(displaySpeed).ToString() + " km/h";
        }

        // Calculate motor torque factor using Unity's forwardSpeed (m/s)
        float speedFactorMotor = Mathf.InverseLerp(0, maxSpeed, forwardSpeed);

        // Use that to calculate how much torque is available (zero torque at top speed)
        float currentMotorTorque = Mathf.Lerp(motorTorque, 0, speedFactorMotor);

        // Calculate steering limit multiplier from speed (km/h)
        // Higher speed means a smaller allowed steering angle.
        float steeringLimitMultiplier = Mathf.Clamp01(steeringLimitBySpeed.Evaluate(displaySpeed));
        float currentMaxWheelSteerAngle = steeringRange * steeringLimitMultiplier;

        // Prepare accumulators to compute actual wheel steer angle average
        float sumSteerAngles = 0f;
        int steerCount = 0;

        // Check whether the user input is in the same direction 
        // as the car's velocity
        bool isAccelerating = Mathf.Sign(vInput) == Mathf.Sign(forwardSpeed);
        
        // Check if handbrake (spacebar) is pressed
        bool isHandBraking = Input.GetKey(KeyCode.Space);

        foreach (var wheel in wheels)
        {
            // Apply steering to Wheel colliders that have "Steerable" enabled
            if (wheel.steerable)
            {
                // Set steer angle based on current max allowed by speed
                wheel.WheelCollider.steerAngle = hInput * currentMaxWheelSteerAngle;
                sumSteerAngles += wheel.WheelCollider.steerAngle;
                steerCount++;
            }
            
            // Apply handbrake if spacebar is pressed
            if (isHandBraking)
            {
                wheel.WheelCollider.brakeTorque = eBrakeTorque;
                wheel.WheelCollider.motorTorque = 0;
            }
            else if (isAccelerating)
            {
                // Apply torque to Wheel colliders that have "Motorized" enabled
                if (wheel.motorized)
                {
                    wheel.WheelCollider.motorTorque = vInput * currentMotorTorque;
                }
                wheel.WheelCollider.brakeTorque = 0;
            }
            else
            {
                // If the user is trying to go in the opposite direction
                // apply brakes to all wheels
                wheel.WheelCollider.brakeTorque = Mathf.Abs(vInput) * brakeTorque;
                wheel.WheelCollider.motorTorque = 0;
            }
        }
        // After applying wheel steer angles, map actual average wheel steer to steering wheel rotation
        if (steeringWheel != null)
        {
            float avgWheelSteerAngle = steerCount > 0 ? (sumSteerAngles / steerCount) : 0f;
            float denom = currentMaxWheelSteerAngle != 0f ? currentMaxWheelSteerAngle : steeringRange;
            float steeringNormalized = denom != 0f ? Mathf.Clamp(avgWheelSteerAngle / denom, -1f, 1f) : 0f;
            float dir = invertSteeringWheel ? -1f : 1f;
            float targetAngle = steeringNormalized * steeringWheelMaxTurn * dir;
            steeringWheel.localRotation = steeringWheelInitialLocalRotation * Quaternion.AngleAxis(targetAngle, steeringWheelLocalAxis);
        }
    }
}