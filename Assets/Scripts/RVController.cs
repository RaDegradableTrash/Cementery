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

        private Rigidbody _rb;

        void Start()
        {
            _rb = GetComponent<Rigidbody>();
            AutoBindWheels();

            // FIX: Force all renderers on the RV to properly receive Ambient Light in URP
            // This prevents the "pitch black" issue caused by invalid GI or Lightmap settings
            foreach (var r in GetComponentsInChildren<Renderer>(true))
            {
                if (r is MeshRenderer mr)
                {
                    mr.receiveGI = ReceiveGI.LightProbes;
                }
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

            foreach (var pair in wheels)
            {
                if (pair.isDrive)
                    pair.collider.motorTorque = torque;

                if (pair.isSteer)
                    pair.collider.steerAngle = angle;

                pair.collider.brakeTorque = braking ? brakeTorque : 0f;
            }
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
