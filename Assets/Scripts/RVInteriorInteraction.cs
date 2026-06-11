using UnityEngine;

namespace RVSystem
{
    public class RVInteriorInteraction : MonoBehaviour
    {
        [Header("Settings")]
        [Tooltip("When player enters this trigger, they become a child of the RV to prevent sliding.")]
        public Transform playerParent; 
        public string playerTag = "Player";

        private System.Collections.Generic.Dictionary<Collider, Transform> _originalParents = new System.Collections.Generic.Dictionary<Collider, Transform>();

        void OnTriggerEnter(Collider other)
        {
            WorldObject wo = other.GetComponent<WorldObject>();
            if (other.CompareTag(playerTag) || wo != null)
            {
                if (!_originalParents.ContainsKey(other))
                {
                    _originalParents[other] = other.transform.parent;
                }
                other.transform.SetParent(playerParent);
                
                if (wo != null)
                {
                    wo.SetInsideVehicle(true);
                }
            }
        }

        void OnTriggerExit(Collider other)
        {
            WorldObject wo = other.GetComponent<WorldObject>();
            if (other.CompareTag(playerTag) || wo != null)
            {
                if (_originalParents.TryGetValue(other, out Transform orig))
                {
                    other.transform.SetParent(orig);
                    _originalParents.Remove(other);
                }
                
                if (wo != null)
                {
                    wo.SetInsideVehicle(false);
                }
            }
        }
    }
}
