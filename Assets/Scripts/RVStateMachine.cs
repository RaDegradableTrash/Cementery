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

        void Update()
        {
            if (currentState == RVState.Active)
            {
                throttle = Input.GetAxis("Vertical");
                steer = Input.GetAxis("Horizontal");
                braking = Input.GetKey(KeyCode.Space);
                
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
            
            var cameraController = GetComponent<RVCameraController>();
            if (cameraController == null) cameraController = GetComponentInParent<RVCameraController>();

            if (newState == RVState.Parked)
            {
                controller.StopVehicle();
                
                if (cameraController != null)
                {
                    if (cameraController.interiorCamera != null) cameraController.interiorCamera.SetActive(false);
                    if (cameraController.exteriorCamera != null) cameraController.exteriorCamera.SetActive(false);
                }
                if (player != null)
                {
                    player.SetActive(true);
                    var playerCam = player.GetComponentInChildren<Camera>(true);
                    if (playerCam != null) playerCam.enabled = true;
                }
            }
            else
            {
                if (cameraController != null)
                {
                    if (cameraController.interiorCamera != null)
                    {
                        cameraController.interiorCamera.SetActive(true);
                        var camComp = cameraController.interiorCamera.GetComponent<Camera>();
                        if (camComp != null) camComp.enabled = true;
                    }
                }
                if (player != null)
                {
                    var playerCam = player.GetComponentInChildren<Camera>();
                    if (playerCam != null) playerCam.enabled = false;
                }
            }
        }
    }
}
