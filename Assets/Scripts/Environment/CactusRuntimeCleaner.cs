using UnityEngine;
using UnityEngine.SceneManagement;

namespace EnvironmentSystem
{
    /// <summary>
    /// Removes cactus props from loaded gameplay scenes without touching authored scene files.
    /// This covers existing chunk scenes and any later streamed chunks.
    /// </summary>
    public sealed class CactusRuntimeCleaner : MonoBehaviour
    {
        private static CactusRuntimeCleaner instance;
        private float nextSweepTime;
        private const float SweepInterval = 1f;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Bootstrap()
        {
            if (instance != null)
                return;

            GameObject obj = new GameObject(nameof(CactusRuntimeCleaner));
            DontDestroyOnLoad(obj);
            instance = obj.AddComponent<CactusRuntimeCleaner>();
            SceneManager.sceneLoaded += OnSceneLoaded;
            CleanLoadedScenes();
        }

        private static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            CleanLoadedScenes();
        }

        private void Update()
        {
            if (Time.unscaledTime < nextSweepTime)
                return;

            nextSweepTime = Time.unscaledTime + SweepInterval;
            CleanLoadedScenes();
        }

        public static void CleanLoadedScenes()
        {
            Transform[] transforms = FindObjectsByType<Transform>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            for (int i = 0; i < transforms.Length; i++)
            {
                Transform target = transforms[i];
                if (target == null || !IsCactusName(target.name))
                    continue;

                Transform destroyRoot = FindTopmostCactusTransform(target);
                if (destroyRoot == null || destroyRoot.GetComponent<CactusRuntimeCleaner>() != null)
                    continue;

                if (Application.isPlaying)
                    Destroy(destroyRoot.gameObject);
                else
                    DestroyImmediate(destroyRoot.gameObject);
            }
        }

        public static bool IsCactusName(string objectName)
        {
            return !string.IsNullOrEmpty(objectName)
                && objectName.IndexOf("cactus", System.StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static Transform FindTopmostCactusTransform(Transform target)
        {
            Transform result = target;
            Transform current = target.parent;
            while (current != null && IsCactusName(current.name))
            {
                result = current;
                current = current.parent;
            }

            return result;
        }
    }
}
