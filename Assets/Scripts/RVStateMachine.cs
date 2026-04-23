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
            if (newState == RVState.Parked)
            {
                controller.StopVehicle();
                // Logic to enable player movement inside would be here
            }
            else
            {
                // Logic to lock player to cabin but allow control
            }
        }
    }
}
