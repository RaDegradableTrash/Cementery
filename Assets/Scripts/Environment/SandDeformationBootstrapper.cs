using UnityEngine;
using RVSystem;

namespace EnvironmentSystem
{
    /// <summary>
    /// Static bootstrapper that automatically hooks up SandDeformer components
    /// to both the RV wheels and the Player characters at runtime.
    /// Eliminates manual component assigning in the Unity editor completely!
    /// </summary>
    public static class SandDeformationBootstrapper
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void AutoBindInteractiveDeformers()
        {
            // 1. Automatically detect and bind to RV Car wheels
            var rvControllers = Object.FindObjectsOfType<RVController>();
            foreach (var rv in rvControllers)
            {
                if (rv != null)
                {
                    // Find all WheelColliders on the RV
                    var wheelColliders = rv.GetComponentsInChildren<WheelCollider>(true);
                    int boundCount = 0;
                    foreach (var wc in wheelColliders)
                    {
                        if (wc.GetComponent<SandDeformer>() == null)
                        {
                            var deformer = wc.gameObject.AddComponent<SandDeformer>();
                            
                            // Customize tire-specific footprints (slightly larger radius & deeper indentation for heavy RV)
                            deformer.radius = 0.52f;
                            deformer.depth = 0.18f;
                            deformer.rimWidth = 0.16f;
                            deformer.rimHeight = 0.05f;
                            deformer.stampSpacing = 0.28f; // Tire tracks stamp frequently for continuity
                            deformer.lifetime = 22f;       // Tire tracks last slightly longer
                            
                            boundCount++;
                        }
                    }
                    if (boundCount > 0)
                    {
                        Debug.Log($"[SandDeformationBootstrapper] Successfully auto-bound {boundCount} SandDeformers to RV Wheels on '{rv.name}'.");
                    }
                }
            }

            // 2. Automatically detect and bind to Player characters
            // Search robustly for the player transform via tags
            var players = GameObject.FindGameObjectsWithTag("Player");
            foreach (var player in players)
            {
                if (player != null && player.GetComponent<SandDeformer>() == null)
                {
                    var deformer = player.AddComponent<SandDeformer>();
                    
                    // Customize player-specific foot prints (smaller radius and shallower indentation)
                    deformer.radius = 0.36f;
                    deformer.depth = 0.11f;
                    deformer.rimWidth = 0.11f;
                    deformer.rimHeight = 0.03f;
                    deformer.stampSpacing = 0.35f; // Footsteps stamp at standard walking pace stride
                    deformer.lifetime = 16f;
                    
                    Debug.Log($"[SandDeformationBootstrapper] Successfully auto-bound SandDeformer to Player Character on '{player.name}'.");
                }
            }
        }
    }
}
