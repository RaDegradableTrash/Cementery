using UnityEngine;

public class VacuumInteractor : MonoBehaviour
{
    [Header("Vacuum Settings")]
    public float interactionRadius = 2.0f;
    public float vacuumSpeed = 0.5f;
    public float maxDistance = 10f;
    public LayerMask terrainLayer = ~0; // Default hit everything

    private void Update()
    {
        if (Input.GetMouseButton(0)) // Left click to vacuum
        {
            Ray ray = new Ray(transform.position, transform.forward);
            if (Physics.Raycast(ray, out RaycastHit hit, maxDistance, terrainLayer))
            {
                if (SnowAccumulationManager.Instance != null)
                {
                    SnowAccumulationManager.Instance.VacuumSnow(hit.point, interactionRadius, vacuumSpeed);
                }
            }
        }
    }
}
