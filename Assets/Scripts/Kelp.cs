using UnityEngine;

[RequireComponent(typeof(WorldObject))]
[RequireComponent(typeof(Rigidbody))]
public class Kelp : MonoBehaviour
{
    [Header("Swaying Settings")]
    public float swaySpeed = 3f;
    public float swayAmount = 10f; // Degrees
    public Transform stemTransform;
    public Transform[] leafTransforms;

    [Header("Extraction Settings")]
    public float jumpForce = 6f;
    public float scatterForce = 1.5f;

    private Rigidbody _rb;
    private WorldObject _worldObject;
    private bool _isPlanted = false;
    private bool _isWild = true;
    private Vector3[] _leafOriginalRotations;
    private Quaternion _stemOriginalRotation;
    private Vector3 _initialWorldScale;

    public Vector3 InitialWorldScale => _initialWorldScale;

    void Awake()
    {
        _rb = GetComponent<Rigidbody>();
        _worldObject = GetComponent<WorldObject>();
        _initialWorldScale = transform.lossyScale; // Fallback

        // Cache initial rotations
        if (stemTransform != null)
            _stemOriginalRotation = stemTransform.localRotation;

        if (leafTransforms != null && leafTransforms.Length > 0)
        {
            _leafOriginalRotations = new Vector3[leafTransforms.Length];
            for (int i = 0; i < leafTransforms.Length; i++)
            {
                if (leafTransforms[i] != null)
                    _leafOriginalRotations[i] = leafTransforms[i].localEulerAngles;
            }
        }

        // Initially in wild state: interactable to extract, not carryable yet
        _worldObject.interactable = _isWild;
        _worldObject.carryable = !_isWild;

        if (_isWild)
        {
            _rb.isKinematic = true;
            _rb.useGravity = false;
        }

        _worldObject.onInteract.AddListener(OnWildInteract);
    }

    void Start()
    {
        _initialWorldScale = transform.lossyScale;
        if (_worldObject != null)
        {
            _worldObject.BaseScale = transform.localScale;
        }
    }

    void Update()
    {
        // Swaying logic (runs in wild or planted/carried states, unaffected by scale)
        ApplySwaying();
    }

    private void ApplySwaying()
    {
        float timeFactor = Time.time * swaySpeed;

        if (stemTransform != null)
        {
            float angleX = Mathf.Sin(timeFactor) * swayAmount;
            float angleZ = Mathf.Cos(timeFactor * 0.8f) * swayAmount;
            stemTransform.localRotation = _stemOriginalRotation * Quaternion.Euler(angleX, 0f, angleZ);
        }

        if (leafTransforms != null)
        {
            for (int i = 0; i < leafTransforms.Length; i++)
            {
                if (leafTransforms[i] != null)
                {
                    float phase = i * 0.5f;
                    float angleX = Mathf.Sin(timeFactor * 1.2f + phase) * (swayAmount * 0.8f);
                    float angleZ = Mathf.Cos(timeFactor * 0.9f + phase) * (swayAmount * 0.8f);
                    Vector3 orig = _leafOriginalRotations != null && i < _leafOriginalRotations.Length ? _leafOriginalRotations[i] : Vector3.zero;
                    leafTransforms[i].localRotation = Quaternion.Euler(orig) * Quaternion.Euler(angleX, 0f, angleZ);
                }
            }
        }
    }

    // Called when the wild kelp is interacted with (RMB click)
    private void OnWildInteract(GameObject actor)
    {
        if (!_isWild) return;
        Extract();
    }

    [ContextMenu("Extract")]
    public void Extract()
    {
        if (!_isWild) return;

        _isWild = false;
        transform.parent = null;
        transform.localScale = _initialWorldScale;
        _worldObject.BaseScale = _initialWorldScale;

        // Enable physics
        _rb.isKinematic = false;
        _rb.useGravity = true;

        // Enable carry, disable wild interact
        _worldObject.interactable = false;
        _worldObject.carryable = true;

        // Apply jump forces
        Vector3 force = Vector3.up * jumpForce;
        force += new Vector3(Random.Range(-scatterForce, scatterForce), 0f, Random.Range(-scatterForce, scatterForce));
        _rb.AddForce(force, ForceMode.Impulse);
        _rb.AddTorque(new Vector3(Random.Range(-5f, 5f), Random.Range(-5f, 5f), Random.Range(-5f, 5f)), ForceMode.Impulse);
    }

    public void OnPlanted(Transform spot)
    {
        _isPlanted = true;
        _isWild = false;

        // Parent to the planting spot and zero out position, but align rotation to be upright
        transform.parent = spot;
        transform.localPosition = Vector3.zero;
        transform.rotation = Quaternion.LookRotation(spot.forward, Vector3.up);

        // Disable physics/colliders
        _rb.isKinematic = true;
        _rb.useGravity = false;
        
        Collider[] colliders = GetComponentsInChildren<Collider>();
        foreach (Collider c in colliders)
        {
            c.enabled = false;
        }

        // Growing kelp is not interactable directly; player interacts with the Pot instead
        _worldObject.interactable = false;
        _worldObject.carryable = false;
    }

    public void SetGrowthScale(float progress)
    {
        // Smoothly scale from 0.1 to 1.0 of the initial world scale
        float currentScale = Mathf.Lerp(0.1f, 1.0f, progress);
        Vector3 targetWorldScale = _initialWorldScale * currentScale;

        if (transform.parent != null)
        {
            Vector3 parentScale = transform.parent.lossyScale;
            transform.localScale = new Vector3(
                parentScale.x != 0 ? targetWorldScale.x / parentScale.x : targetWorldScale.x,
                parentScale.y != 0 ? targetWorldScale.y / parentScale.y : targetWorldScale.y,
                parentScale.z != 0 ? targetWorldScale.z / parentScale.z : targetWorldScale.z
            );
        }
        else
        {
            transform.localScale = targetWorldScale;
        }

        _worldObject.BaseScale = transform.localScale;
    }
}
