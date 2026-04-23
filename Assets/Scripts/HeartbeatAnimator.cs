using UnityEngine;

public class HeartbeatAnimator : MonoBehaviour
{
    private void OnEnable()
    {
        // Kept only for scene compatibility. Heartbeat is now driven by HeartbeatSystem.
        enabled = false;
    }
}
