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
        private Camera _interiorCameraComponent;
        private Camera _exteriorCameraComponent;

        private void Awake()
        {
            _hasCameraPair = interiorCamera != null && exteriorCamera != null;
            _interiorCameraComponent = interiorCamera != null ? interiorCamera.GetComponent<Camera>() : null;
            _exteriorCameraComponent = exteriorCamera != null ? exteriorCamera.GetComponent<Camera>() : null;
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
                this.enabled = false;
                return;
            }

            if (_camerasEnabled == enabled)
            {
                this.enabled = enabled;
                return;
            }

            _camerasEnabled = enabled;
            this.enabled = enabled;
            if (!enabled)
            {
                SetActiveIfChanged(interiorCamera, false, _interiorCameraComponent);
                SetActiveIfChanged(exteriorCamera, false, _exteriorCameraComponent);
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
            SetActiveIfChanged(interiorCamera, active, _interiorCameraComponent);
            SetActiveIfChanged(exteriorCamera, !active, _exteriorCameraComponent);
        }

        private static void SetActiveIfChanged(GameObject target, bool active, Camera camera = null)
        {
            if (target != null && target.activeSelf != active)
                target.SetActive(active);

            if (target != null && active)
            {
                if (camera != null && !camera.enabled)
                    camera.enabled = true;
            }
        }
    }
}
