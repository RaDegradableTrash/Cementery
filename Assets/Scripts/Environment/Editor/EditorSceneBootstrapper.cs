#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace EnvironmentSystem
{
    /// <summary>
    /// Quality-of-Life Tool: Automatically ensures Main_Persistent is loaded first 
    /// whenever you hit "Play" in the Unity Editor, regardless of what scene you are currently editing.
    /// </summary>
    [InitializeOnLoad]
    public static class EditorSceneBootstrapper
    {
        private const string PersistentScenePath = "Assets/Scenes/Main_Persistent.unity";
        private const string LastActiveSceneKey = "Bootstrapper_LastActiveScene";

        static EditorSceneBootstrapper()
        {
            EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
        }

        private static void OnPlayModeStateChanged(PlayModeStateChange state)
        {
            if (!EditorPrefs.GetBool("Bootstrapper_Enabled", true)) return;

            if (state == PlayModeStateChange.ExitingEditMode)
            {
                // 1. Keep track of what scene the designer was editing
                Scene activeScene = SceneManager.GetActiveScene();
                string path = activeScene.path;

                // Only redirect to Main_Persistent if the active scene is located inside Chunks folder
                bool isChunkScene = path.Contains("/Chunks/") || System.IO.Path.GetFileNameWithoutExtension(path).Contains("Chunk");

                if (path != PersistentScenePath && isChunkScene)
                {
                    EditorPrefs.SetString(LastActiveSceneKey, path);
                    
                    // 2. Force save current dirty scenes
                    EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo();

                    // 3. Set Main_Persistent as the startup scene target in playmode
                    SceneAsset persistentSceneAsset = AssetDatabase.LoadAssetAtPath<SceneAsset>(PersistentScenePath);
                    if (persistentSceneAsset != null)
                    {
                        EditorSceneManager.playModeStartScene = persistentSceneAsset;
                        Debug.Log($"<color=#4ade80><b>[Bootstrapper]</b></color> Automatically set startup scene to persistent core: {PersistentScenePath}");
                    }
                    else
                    {
                        Debug.LogWarning($"[Bootstrapper] Main_Persistent scene not found at: {PersistentScenePath}. Please check your path!");
                    }
                }
                else
                {
                    // Not a chunk scene or already in persistent scene, clear redirect to let it run natively
                    EditorSceneManager.playModeStartScene = null;
                    EditorPrefs.SetString(LastActiveSceneKey, "");
                }
            }
            else if (state == PlayModeStateChange.EnteredEditMode)
            {
                // Clean up start scene setting when returning to edit mode
                EditorSceneManager.playModeStartScene = null;

                // Optional: Re-open the scene the designer was editing
                if (EditorPrefs.GetBool("Bootstrapper_RestoreScene", true))
                {
                    string lastScene = EditorPrefs.GetString(LastActiveSceneKey, "");
                    if (!string.IsNullOrEmpty(lastScene) && SceneManager.GetActiveScene().path != lastScene)
                    {
                        EditorSceneManager.OpenScene(lastScene, OpenSceneMode.Single);
                    }
                }
            }
        }

        [MenuItem("Tools/World Streamer/Toggle Bootstrapper", false, 10)]
        public static void ToggleBootstrapper()
        {
            bool enabled = !EditorPrefs.GetBool("Bootstrapper_Enabled", true);
            EditorPrefs.SetBool("Bootstrapper_Enabled", enabled);
            Debug.Log($"[Bootstrapper] Editor Scene Bootstrapper is now {(enabled ? "<color=#4ade80>ENABLED</color>" : "<color=#ef4444>DISABLED</color>")}.");
        }
    }
}
#endif
