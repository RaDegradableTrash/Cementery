using UnityEngine;

namespace RVSystem
{
    public class RVInteriorInteraction : MonoBehaviour
    {
        [Header("Settings")]
        [Tooltip("When player enters this trigger, they become a child of the RV to prevent sliding.")]
        public Transform playerParent; 
        public string playerTag = "Player";

        private Transform _originalParent;

        void OnTriggerEnter(Collider other)
        {
            if (other.CompareTag(playerTag))
            {
                _originalParent = other.transform.parent;
                other.transform.SetParent(playerParent);
                Debug.Log("Player entered RV interior");
            }
        }

        void OnTriggerExit(Collider other)
        {
            if (other.CompareTag(playerTag))
            {
                other.transform.SetParent(_originalParent);
                Debug.Log("Player exited RV interior");
            }
        }
    }
}
