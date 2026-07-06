using UnityEngine;

namespace RVSystem
{
    public enum RVState { Parked, Active }

    public class RVStateMachine : MonoBehaviour
    {
        public RVState currentState = RVState.Parked;
        public RVController controller;
        public GameObject player;

        [Header("Activation Trigger")]
        public Collider enterTrigger;

        [Header("Inputs")]
        public float throttle;
        public float steer;
        public bool braking;

        [Header("Fuel Consumption")]
        [Tooltip("Fuel consumed per second while driving (idle / engine on).")]
        public float baseFuelConsumption = 0.5f;
        [Tooltip("Additional fuel consumed per second when full throttle is applied.")]
        public float activeFuelConsumption = 1.5f;

        private RVCameraController _cameraController;
        private Camera _playerCamera;

        private void Awake()
        {
            CacheTransitionReferences();
            enabled = currentState == RVState.Active;
        }

        void Update()
        {
            if (currentState == RVState.Active)
            {
                float fuel = FuelTank.SharedFuel;

                if (fuel > 0f)
                {
                    throttle = Input.GetAxis("Vertical");
                    steer = Input.GetAxis("Horizontal");
                    braking = Input.GetKey(KeyCode.Space);

                    // Gradually consume fuel while driving
                    float currentConsumption = baseFuelConsumption + Mathf.Abs(throttle) * activeFuelConsumption;
                    FuelTank.SharedFuel -= currentConsumption * Time.deltaTime;
                }
                else
                {
                    throttle = 0f;
                    steer = Input.GetAxis("Horizontal");
                    braking = true; // Auto brake when out of fuel
                }
                
                controller.ApplyInputs(throttle, steer, braking);

                if (Input.GetKeyDown(KeyCode.E)) // Toggle exit
                {
                    SetState(RVState.Parked);
                }
            }
        }

        public void SetState(RVState newState)
        {
            currentState = newState;
            enabled = newState == RVState.Active;
            CacheTransitionReferences();

            if (newState == RVState.Parked)
            {
                controller.StopVehicle();
                
                if (_cameraController != null)
                {
                    _cameraController.SetCamerasEnabled(false);
                }
                if (player != null)
                {
                    player.SetActive(true);
                    if (_playerCamera != null) _playerCamera.enabled = true;
                }
            }
            else
            {
                if (_cameraController != null)
                {
                    _cameraController.SetCamerasEnabled(true);
                    _cameraController.SetInteriorActive(true);
                }
                if (player != null)
                {
                    if (_playerCamera != null) _playerCamera.enabled = false;
                }
            }
        }

        private void CacheTransitionReferences()
        {
            if (_cameraController == null)
            {
                _cameraController = GetComponent<RVCameraController>();
                if (_cameraController == null)
                {
                    _cameraController = GetComponentInParent<RVCameraController>();
                }
            }

            if (player != null && _playerCamera == null)
            {
                _playerCamera = player.GetComponentInChildren<Camera>(true);
            }
        }
    }
}
