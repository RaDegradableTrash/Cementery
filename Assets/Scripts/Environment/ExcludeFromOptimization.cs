using UnityEngine;

namespace EnvironmentSystem
{
    /// <summary>
    /// Add this component to any GameObject that should NEVER be managed by BetterGameplayManager.
    ///
    /// Examples:
    ///   - Player character
    ///   - RV vehicle
    ///   - Items the player is currently holding
    ///   - UI elements or lights that must always be visible
    ///
    /// BetterGameplayManager checks for this component before adding OptimizableObject.
    /// If already managed, this component will also force-unregister the object.
    /// </summary>
    [DisallowMultipleComponent]
    public class ExcludeFromOptimization : MonoBehaviour
    {
        private void Awake()
        {
            // If an OptimizableObject was already added (e.g. via scan before this Awake ran),
            // unregister it and destroy the component.
            OptimizableObject opt = GetComponent<OptimizableObject>();
            if (opt != null)
            {
                if (BetterGameplayManager.Instance != null)
                    BetterGameplayManager.Instance.Unregister(opt);

                Destroy(opt);
            }
        }
    }
}
