using UnityEngine;

namespace RVSystem
{
    [RequireComponent(typeof(Rigidbody))]
    public class RVController : MonoBehaviour
    {
        [Header("Wheel Colliders")]
        public WheelCollider[] driveWheels;
        public WheelCollider[] steerWheels;
        public WheelCollider[] allWheels;

        [Header("Physical Settings")]
        public Transform centerOfMass;
        public float motorTorque = 2500f;
        public float brakeTorque = 5000f;
        public float maxSteerAngle = 35f;

        private Rigidbody _rb;

        void Start()
        {
            _rb = GetComponent<Rigidbody>();
            
            // Apply Center of Mass in FixedUpdate to ensure Rigidbody is fully initialized
        }

        void FixedUpdate()
        {
            ConfigureCenterOfMass();
        }

        private void ConfigureCenterOfMass()
        {
            if (centerOfMass != null)
            {
                _rb.centerOfMass = centerOfMass.localPosition;
            }
            else
            {
                // Fallback: set CoM lower if not assigned
                _rb.centerOfMass = new Vector3(0, -0.5f, 0);
            }
        }

        public void ApplyInputs(float throttle, float steer, bool braking)
        {
            float torque = throttle * motorTorque;
            float angle = steer * maxSteerAngle;

            foreach (var wheel in driveWheels)
            {
                wheel.motorTorque = torque;
                wheel.brakeTorque = braking ? brakeTorque : 0f;
            }

            foreach (var wheel in steerWheels)
            {
                wheel.steerAngle = angle;
            }

            if (braking)
            {
                foreach (var wheel in allWheels)
                {
                    wheel.brakeTorque = brakeTorque;
                }
            }
        }

        public void StopVehicle()
        {
            foreach (var wheel in allWheels)
            {
                wheel.motorTorque = 0;
                wheel.brakeTorque = brakeTorque;
            }
        }
    }
}
