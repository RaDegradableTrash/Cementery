using UnityEngine;
using System.Collections.Generic;

namespace RVSystem
{
    [RequireComponent(typeof(Rigidbody))]
    public class RVController : MonoBehaviour
    {
        [System.Serializable]
        public class WheelPair
        {
            public WheelCollider collider;
            public Transform mesh;
            public bool isSteer;
            public bool isDrive;
            [HideInInspector] public Quaternion visualRotationOffset = Quaternion.identity;
        }

        [Header("Wheel Configuration (Auto-Bound)")]
        public List<WheelPair> wheels = new List<WheelPair>();

        [Header("Physical Settings")]
        public Transform centerOfMass;
        public float motorTorque = 2500f;
        public float brakeTorque = 5000f;
        public float maxSteerAngle = 35f;

        [Header("Stability")]
        [Tooltip("Fallback center of mass when no centerOfMass transform is assigned. Lower values reduce rollover risk.")]
        public Vector3 fallbackCenterOfMass = new Vector3(0f, -1.1f, 0f);
        [Tooltip("Reduces steering lock as speed rises so the RV cannot snap-roll from small inputs.")]
        [Range(0f, 1f)] public float highSpeedSteerReduction = 0.55f;
        [Tooltip("Speed in m/s where high-speed steering reduction reaches full strength.")]
        [Min(1f)] public float steerReductionSpeed = 18f;
        [Tooltip("Force applied across left/right wheel pairs to resist body roll.")]
        [Min(0f)] public float antiRollForce = 9500f;
        [Tooltip("Angular damping applied around the RV forward axis to calm sudden tilt.")]
        [Min(0f)] public float rollAngularDamping = 2.4f;
        [Tooltip("Maximum Rigidbody angular velocity while driving.")]
        [Min(0.5f)] public float maxDrivingAngularVelocity = 3.5f;

        private Rigidbody _rb;
        private bool _hasSetupKinematic = false;
        private float _startupTime;
        private Vector3 _configuredCenterOfMass;
        private bool _centerOfMassConfigured;

        void Awake()
        {
            _rb = GetComponent<Rigidbody>();
            if (_rb != null)
            {
                _rb.isKinematic = true;
                _rb.maxAngularVelocity = maxDrivingAngularVelocity;
            }
        }

        void Start()
        {
            _startupTime = Time.time;
            if (_rb == null) _rb = GetComponent<Rigidbody>();
            ConfigureCenterOfMass(true);
            AutoBindWheels();

            // FIX: Force all renderers on the RV to properly receive Ambient Light in URP
            // This prevents the "pitch black" issue caused by invalid GI or Lightmap settings
            foreach (var r in GetComponentsInChildren<Renderer>(true))
            {
                r.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
                r.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Simple;
                
                // If the material has emission but is rendering black, ensure it's using the correct URP color
                foreach (var mat in r.materials)
                {
                    if (mat.HasProperty("_BaseColor") && mat.GetColor("_BaseColor") == Color.black)
                    {
                        mat.SetColor("_BaseColor", Color.gray);
                    }
                }
            }
        }

        [ContextMenu("Auto Bind Wheels (Editor)")]
        public void AutoBindWheels()
        {
            wheels.Clear();
            
            Transform bodyMesh = transform.Find("UVBodyMesh");
            if (bodyMesh == null)
            {
                // Fallback deep search
                MeshRenderer[] renderers = GetComponentsInChildren<MeshRenderer>(true);
                foreach (var r in renderers)
                {
                    if (r.name == "UVBodyMesh")
                    {
                        bodyMesh = r.transform;
                        break;
                    }
                }
            }

            if (bodyMesh == null)
            {
                Debug.LogWarning("UVBodyMesh not found! Cannot auto-bind wheels.");
                return;
            }

            string[] suffixes = { "L1", "L2", "L3", "R1", "R2", "R3" };
            WheelCollider[] allColliders = GetComponentsInChildren<WheelCollider>(true);
            Transform[] allMeshes = bodyMesh.GetComponentsInChildren<Transform>(true);

            foreach (string suffix in suffixes)
            {
                WheelCollider wc = null;
                Transform wm = null;

                foreach (var c in allColliders)
                {
                    if (c.name.EndsWith(suffix) || c.name.Contains("WheelCollider" + suffix))
                    {
                        wc = c;
                        break;
                    }
                }

                foreach (var t in allMeshes)
                {
                    if (t.name == "Wheel" + suffix)
                    {
                        wm = t;
                        break;
                    }
                }

                if (wc != null && wm != null)
                {
                    // Snap the invisible WheelCollider to exactly match the Mesh's initial position and rotation
                    // This ensures the physics raycast starts from the correct visual wheel center
                    wc.transform.position = wm.position;
                    // Usually we don't copy rotation because WheelColliders must point their local Y axis downwards.
                    // But if the mesh is perfectly aligned, it's fine. We will just align position to be safe.
                    
                    // 1 = Front (Steer), 2 = Mid (Drive), 3 = Back (Drive)
                    bool isFront = suffix.EndsWith("1");
                    
                    wheels.Add(new WheelPair
                    {
                        collider = wc,
                        mesh = wm,
                        isSteer = isFront,
                        isDrive = !isFront, // 后两排驱动
                        visualRotationOffset = Quaternion.Inverse(wc.transform.rotation) * wm.rotation
                    });
                }
            }
            
            Debug.Log($"Auto-bound {wheels.Count} wheels successfully.");
        }

        void Update()
        {
            if (!_hasSetupKinematic)
            {
                bool shouldEnable = false;
                if (EnvironmentSystem.WorldStreamer.Instance != null)
                {
                    if (EnvironmentSystem.WorldStreamer.Instance.HasLoadedAnyChunks)
                        shouldEnable = true;
                }
                else
                {
                    if (Time.time - _startupTime > 1.0f)
                        shouldEnable = true;
                }

                if (shouldEnable)
                {
                    if (_rb != null) _rb.isKinematic = false;
                    _hasSetupKinematic = true;
                }
            }

            // Sync wheel meshes to colliders every frame for smooth visuals
            foreach (var pair in wheels)
            {
                if (pair.collider != null && pair.mesh != null)
                {
                    pair.collider.GetWorldPose(out Vector3 pos, out Quaternion rot);
                    pair.mesh.position = pos;
                    // Apply the initial visual rotation offset to fix Blender/FBX import 90-degree rotations
                    pair.mesh.rotation = rot * pair.visualRotationOffset;
                }
            }
        }

        void FixedUpdate()
        {
            ConfigureCenterOfMass(false);
            ApplyStabilityAssist();
        }

        private void ConfigureCenterOfMass(bool force)
        {
            if (_rb == null) return;

            Vector3 targetCenterOfMass = centerOfMass != null
                ? centerOfMass.localPosition
                : fallbackCenterOfMass;

            if (!force &&
                _centerOfMassConfigured &&
                (_configuredCenterOfMass - targetCenterOfMass).sqrMagnitude < 0.000001f)
            {
                return;
            }

            _rb.centerOfMass = targetCenterOfMass;
            _configuredCenterOfMass = targetCenterOfMass;
            _centerOfMassConfigured = true;
        }

        private void OnValidate()
        {
            _centerOfMassConfigured = false;
        }

        public void ApplyInputs(float throttle, float steer, bool braking)
        {
            float torque = throttle * motorTorque;
            float speed01 = _rb != null
                ? Mathf.Clamp01(_rb.velocity.magnitude / Mathf.Max(1f, steerReductionSpeed))
                : 0f;
            float steerScale = Mathf.Lerp(1f, Mathf.Clamp01(1f - highSpeedSteerReduction), speed01);
            float angle = steer * maxSteerAngle * steerScale;

            foreach (var pair in wheels)
            {
                if (pair.isDrive)
                    pair.collider.motorTorque = torque;

                if (pair.isSteer)
                    pair.collider.steerAngle = angle;

                pair.collider.brakeTorque = braking ? brakeTorque : 0f;
            }
        }

        private void ApplyStabilityAssist()
        {
            if (_rb == null || _rb.isKinematic)
                return;

            _rb.maxAngularVelocity = Mathf.Max(0.5f, maxDrivingAngularVelocity);

            if (rollAngularDamping > 0f)
            {
                Vector3 rollAxis = transform.forward;
                float rollRate = Vector3.Dot(_rb.angularVelocity, rollAxis);
                _rb.AddTorque(-rollAxis * rollRate * rollAngularDamping, ForceMode.Acceleration);
            }

            if (antiRollForce <= 0f || wheels.Count < 2)
                return;

            for (int i = 0; i < wheels.Count; i++)
            {
                WheelPair left = wheels[i];
                if (left == null || left.collider == null)
                    continue;

                Vector3 leftLocal = transform.InverseTransformPoint(left.collider.transform.position);
                if (leftLocal.x >= 0f)
                    continue;

                int rightIndex = FindOppositeWheelIndex(leftLocal.z);
                if (rightIndex < 0)
                    continue;

                ApplyAntiRollPair(left.collider, wheels[rightIndex].collider);
            }
        }

        private int FindOppositeWheelIndex(float localZ)
        {
            int bestIndex = -1;
            float bestDistance = float.MaxValue;

            for (int i = 0; i < wheels.Count; i++)
            {
                WheelPair candidate = wheels[i];
                if (candidate == null || candidate.collider == null)
                    continue;

                Vector3 candidateLocal = transform.InverseTransformPoint(candidate.collider.transform.position);
                if (candidateLocal.x <= 0f)
                    continue;

                float distance = Mathf.Abs(candidateLocal.z - localZ);
                if (distance < bestDistance)
                {
                    bestDistance = distance;
                    bestIndex = i;
                }
            }

            return bestIndex;
        }

        private void ApplyAntiRollPair(WheelCollider left, WheelCollider right)
        {
            float leftTravel = GetSuspensionTravel(left, out bool leftGrounded);
            float rightTravel = GetSuspensionTravel(right, out bool rightGrounded);
            float force = (leftTravel - rightTravel) * antiRollForce;

            if (leftGrounded)
                _rb.AddForceAtPosition(left.transform.up * -force, left.transform.position, ForceMode.Force);

            if (rightGrounded)
                _rb.AddForceAtPosition(right.transform.up * force, right.transform.position, ForceMode.Force);
        }

        private static float GetSuspensionTravel(WheelCollider wheel, out bool grounded)
        {
            grounded = wheel.GetGroundHit(out WheelHit hit);
            if (!grounded || wheel.suspensionDistance <= 0.001f)
                return 1f;

            float localHitY = wheel.transform.InverseTransformPoint(hit.point).y;
            return Mathf.Clamp01((-localHitY - wheel.radius) / wheel.suspensionDistance);
        }

        public void StopVehicle()
        {
            foreach (var pair in wheels)
            {
                pair.collider.motorTorque = 0;
                pair.collider.brakeTorque = brakeTorque;
            }
        }
    }
}
