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
            interiorCamera.SetActive(true);
            exteriorCamera.SetActive(false);
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
