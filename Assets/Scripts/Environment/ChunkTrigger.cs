using System.Collections.Generic;
using UnityEngine;

namespace EnvironmentSystem
{
    [RequireComponent(typeof(BoxCollider))]
    public class ChunkTrigger : MonoBehaviour
    {
        [Header("Chunk Configuration")]
        [Tooltip("List of Scene names that should be loaded when the player is inside this trigger volume.")]
        public List<string> requiredChunks = new List<string>();

        private void Start()
        {
            Collider col = GetComponent<Collider>();
            col.isTrigger = true;
        }

        private void OnTriggerEnter(Collider other)
        {
            // Only respond to the Player (and perhaps the RV)
            if (other.CompareTag("Player") || other.GetComponentInParent<RVSystem.RVController>() != null)
            {
                if (WorldStreamer.Instance != null)
                {
                    WorldStreamer.Instance.RequestChunks(requiredChunks);
                }
                else
                {
                    Debug.LogWarning("[ChunkTrigger] WorldStreamer Instance not found in the scene!");
                }
            }
        }
    }
}
