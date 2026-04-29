using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace EnvironmentSystem
{
    public class WorldStreamer : MonoBehaviour
    {
        public static WorldStreamer Instance { get; private set; }

        [Header("Settings")]
        public float unloadDelay = 5f; // Delay before unloading to prevent hitching if the player dances on the border

        private HashSet<string> _requestedChunks = new HashSet<string>();
        private HashSet<string> _loadedChunks = new HashSet<string>();
        private Dictionary<string, Coroutine> _unloadRoutines = new Dictionary<string, Coroutine>();

        private void Awake()
        {
            if (Instance == null) Instance = this;
            else Destroy(gameObject);
        }

        public void RequestChunks(List<string> chunkSceneNames)
        {
            _requestedChunks.Clear();
            foreach (var chunk in chunkSceneNames)
            {
                _requestedChunks.Add(chunk);
                LoadChunk(chunk);
            }

            // Unload chunks that are no longer requested
            List<string> chunksToUnload = new List<string>();
            foreach (var loaded in _loadedChunks)
            {
                if (!_requestedChunks.Contains(loaded))
                {
                    chunksToUnload.Add(loaded);
                }
            }

            foreach (var chunk in chunksToUnload)
            {
                UnloadChunk(chunk);
            }
        }

        private void LoadChunk(string chunkName)
        {
            if (string.IsNullOrEmpty(chunkName)) return;

            // If it was queued for unloading, cancel the unload
            if (_unloadRoutines.TryGetValue(chunkName, out Coroutine routine))
            {
                if (routine != null) StopCoroutine(routine);
                _unloadRoutines.Remove(chunkName);
            }

            if (!_loadedChunks.Contains(chunkName))
            {
                _loadedChunks.Add(chunkName);
                StartCoroutine(LoadSceneAsync(chunkName));
            }
        }

        private void UnloadChunk(string chunkName)
        {
            if (!_unloadRoutines.ContainsKey(chunkName) && _loadedChunks.Contains(chunkName))
            {
                _unloadRoutines[chunkName] = StartCoroutine(UnloadSceneAsync(chunkName));
            }
        }

        private IEnumerator LoadSceneAsync(string sceneName)
        {
            AsyncOperation asyncLoad = SceneManager.LoadSceneAsync(sceneName, LoadSceneMode.Additive);
            if (asyncLoad == null)
            {
                Debug.LogWarning($"[WorldStreamer] Failed to load chunk {sceneName}. Check Build Settings!");
                _loadedChunks.Remove(sceneName);
                yield break;
            }

            // You could link this to a loading screen UI or progress bar here
            while (!asyncLoad.isDone)
            {
                yield return null;
            }
            Debug.Log($"[WorldStreamer] Loaded chunk: {sceneName}");
        }

        private IEnumerator UnloadSceneAsync(string sceneName)
        {
            // Wait a few seconds before actual unload to avoid thrashing
            yield return new WaitForSeconds(unloadDelay);

            // Double check if it got re-requested during the delay
            if (_requestedChunks.Contains(sceneName))
            {
                _unloadRoutines.Remove(sceneName);
                yield break;
            }

            AsyncOperation asyncUnload = SceneManager.UnloadSceneAsync(sceneName);
            if (asyncUnload == null) yield break;

            while (!asyncUnload.isDone)
            {
                yield return null;
            }

            _loadedChunks.Remove(sceneName);
            _unloadRoutines.Remove(sceneName);
            Debug.Log($"[WorldStreamer] Unloaded chunk: {sceneName}");
        }
    }
}
