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

        private bool _hasCameraPair;
        private bool _isInteriorActive;
        private bool _camerasEnabled;

        private void Awake()
        {
            _hasCameraPair = interiorCamera != null && exteriorCamera != null;
        }

        void Start()
        {
            var stateMachine = GetComponent<RVStateMachine>();
            if (stateMachine == null) stateMachine = GetComponentInParent<RVStateMachine>();

            if (stateMachine != null && stateMachine.currentState == RVState.Parked)
            {
                SetCamerasEnabled(false);
            }
            else
            {
                SetCamerasEnabled(true);
                SetInteriorActive(true);
            }
        }

        void Update()
        {
            if (!_hasCameraPair || !_camerasEnabled)
                return;

            if (Input.GetKeyDown(switchKey))
            {
                SwitchPerspective();
            }
        }

        public void SwitchPerspective()
        {
            if (!_hasCameraPair)
                return;

            SetInteriorActive(!_isInteriorActive);
        }

        public void SetCamerasEnabled(bool enabled)
        {
            if (!_hasCameraPair)
            {
                if (interiorCamera != null && interiorCamera.activeSelf != enabled)
                    interiorCamera.SetActive(enabled);
                if (exteriorCamera != null && exteriorCamera.activeSelf)
                    exteriorCamera.SetActive(false);
                _camerasEnabled = enabled;
                _isInteriorActive = enabled && interiorCamera != null && interiorCamera.activeSelf;
                return;
            }

            if (_camerasEnabled == enabled)
                return;

            _camerasEnabled = enabled;
            if (!enabled)
            {
                SetActiveIfChanged(interiorCamera, false);
                SetActiveIfChanged(exteriorCamera, false);
                return;
            }

            SetInteriorActive(true);
        }

        public void SetInteriorActive(bool active)
        {
            if (!_hasCameraPair)
                return;

            if (_isInteriorActive == active && interiorCamera.activeSelf == active && exteriorCamera.activeSelf == !active)
                return;

            _isInteriorActive = active;
            SetActiveIfChanged(interiorCamera, active);
            SetActiveIfChanged(exteriorCamera, !active);
        }

        private static void SetActiveIfChanged(GameObject target, bool active)
        {
            if (target != null && target.activeSelf != active)
                target.SetActive(active);

            if (target != null && active)
            {
                Camera camera = target.GetComponent<Camera>();
                if (camera != null && !camera.enabled)
                    camera.enabled = true;
            }
        }
    }
}
