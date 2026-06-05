using UnityEngine;

namespace RVSystem
{
    public class RVCameraController : MonoBehaviour
    {
        [Header("Cameras")]
        public GameObject interiorCamera;
        public GameObject exteriorCamera;
        
        [Header("Inputs")]
        public KeyCode switchKey = KeyCode.C;

        void Start()
        {
            var stateMachine = GetComponent<RVStateMachine>();
            if (stateMachine == null) stateMachine = GetComponentInParent<RVStateMachine>();

            if (stateMachine != null && stateMachine.currentState == RVState.Parked)
            {
                if (interiorCamera != null) interiorCamera.SetActive(false);
                if (exteriorCamera != null) exteriorCamera.SetActive(false);
            }
            else
            {
                if (interiorCamera != null) interiorCamera.SetActive(true);
                if (exteriorCamera != null) exteriorCamera.SetActive(false);
            }
        }

        void Update()
        {
            if (Input.GetKeyDown(switchKey))
            {
                SwitchPerspective();
            }
        }

        public void SwitchPerspective()
        {
            bool isInterior = interiorCamera.activeSelf;
            interiorCamera.SetActive(!isInterior);
            exteriorCamera.SetActive(isInterior);
        }
    }
}
