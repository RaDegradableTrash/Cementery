using UnityEngine;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

namespace Cementery.Rendering
{
    public static class VisualPipelineBootstrapper
    {
        private const string StorageCameraNameToken = "storage";
        private const string InventoryCameraNameToken = "inventory";
        private const string PreviewCameraNameToken = "preview";

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Initialize()
        {
            ApplyToLoadedCameras();
            SceneManager.sceneLoaded -= OnSceneLoaded;
            SceneManager.sceneLoaded += OnSceneLoaded;
        }

        private static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            ApplyToLoadedCameras();
        }

        private static void ApplyToLoadedCameras()
        {
            Camera[] cameras = Object.FindObjectsByType<Camera>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            for (int i = 0; i < cameras.Length; i++)
            {
                Camera camera = cameras[i];
                if (camera == null)
                    continue;

                UniversalAdditionalCameraData cameraData = camera.GetUniversalAdditionalCameraData();
                if (!ShouldUseGameplayPostProcessing(camera, cameraData))
                    continue;

                cameraData.renderPostProcessing = true;
            }
        }

        private static bool ShouldUseGameplayPostProcessing(Camera camera, UniversalAdditionalCameraData cameraData)
        {
            if (cameraData.renderType != CameraRenderType.Base)
                return false;

            if (camera.targetTexture != null)
                return false;

            if (camera.cameraType == CameraType.Preview || camera.cameraType == CameraType.Reflection)
                return false;

            string cameraName = camera.name.ToLowerInvariant();
            if (cameraName.Contains(StorageCameraNameToken) ||
                cameraName.Contains(InventoryCameraNameToken) ||
                cameraName.Contains(PreviewCameraNameToken))
            {
                return false;
            }

            return camera.CompareTag("MainCamera") || !camera.orthographic;
        }
    }
}
