using UnityEngine;

namespace RVSystem
{
    public class AntiRollBar : MonoBehaviour
    {
        public WheelCollider wheelL;
        public WheelCollider wheelR;
        public float antiRollForce = 5000f;

        private Rigidbody _rb;

        void Start()
        {
            _rb = GetComponent<Rigidbody>();
        }

        void FixedUpdate()
        {
            WheelHit hit;
            float travelL = 1.0f;
            float travelR = 1.0f;

            bool groundedL = wheelL.GetGroundHit(out hit);
            if (groundedL)
                travelL = (-wheelL.transform.InverseTransformPoint(hit.point).y - wheelL.radius) / wheelL.suspensionDistance;

            bool groundedR = wheelR.GetGroundHit(out hit);
            if (groundedR)
                travelR = (-wheelR.transform.InverseTransformPoint(hit.point).y - wheelR.radius) / wheelR.suspensionDistance;

            float antiRollAmount = (travelL - travelR) * antiRollForce;

            if (groundedL)
                _rb.AddForceAtPosition(wheelL.transform.up * -antiRollAmount, wheelL.transform.position);
            if (groundedR)
                _rb.AddForceAtPosition(wheelR.transform.up * antiRollAmount, wheelR.transform.position);
        }
    }
}
