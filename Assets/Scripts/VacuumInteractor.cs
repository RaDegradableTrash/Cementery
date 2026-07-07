using UnityEngine;

public class VacuumInteractor : MonoBehaviour
{
    [Header("Vacuum Settings")]
    public float interactionRadius = 2.0f;
    public float vacuumSpeed = 0.5f;
    public float maxDistance = 10f;
    public LayerMask terrainLayer = ~0; // Default hit everything
    [SerializeField, Min(0.01f)] private float vacuumTickInterval = 0.05f;

    private Transform _cachedTransform;
    private float _nextVacuumTime;

    private void Awake()
    {
        _cachedTransform = transform;
    }

    private void Update()
    {
        if (!Input.GetMouseButton(0) || Time.time < _nextVacuumTime)
            return;

        _nextVacuumTime = Time.time + Mathf.Max(0.01f, vacuumTickInterval);

        Ray ray = new Ray(_cachedTransform.position, _cachedTransform.forward);
        if (Physics.Raycast(ray, out RaycastHit hit, maxDistance, terrainLayer) &&
            SnowAccumulationManager.Instance != null)
        {
            SnowAccumulationManager.Instance.VacuumSnow(hit.point, interactionRadius, vacuumSpeed);
        }
    }
}
