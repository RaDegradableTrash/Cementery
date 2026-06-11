using System.Collections.Generic;
using UnityEngine;

[RequireComponent(typeof(WorldObject))]
[RequireComponent(typeof(Rigidbody))]
public class Kelp : MonoBehaviour
{
    [Header("Hierarchy References")]
    public Transform stemTransform;
    public Transform[] leafTransforms;
    [Tooltip("The leaf prefab spawned during harvest.")]
    public GameObject leafPrefab;

    [Header("Swaying Settings")]
    public float swaySpeed = 1.5f;
    public float swayAmount = 4f;

    [Header("Extraction Settings")]
    public float jumpForce = 6f;
    public float scatterForce = 1.5f;

    [Header("Color Settings")]
    public Color sproutColor = new Color(0.8f, 0.95f, 0.3f);
    public Color matureColor = new Color(0.3f, 0.7f, 0.3f);

    private Renderer _stemRenderer;
    private Renderer[] _leafRenderers;

    private Rigidbody _rb;
    private WorldObject _worldObject;

    private static Dictionary<string, float[]> _wildKelpLeafStates = new Dictionary<string, float[]>();

    public float leafRegrowTime = 60f;

    private bool _isWild = true;
    private bool _isPlanted = false;
    private Vector3 _initialWorldScale;
    private Quaternion _initialLocalRotation;

    private Vector3[] _leafOriginalRotations;
    private Quaternion _stemOriginalRotation;
    private float _stemOriginalScaleY = 1f;

    private Vector3[] _leafOriginalScales;
    private float[] _leafGrowthProgress;
    
    private float _swayOffset;

    public Vector3 InitialWorldScale => _initialWorldScale;
    public bool IsWild => _isWild;

    void Awake()
    {
        _swayOffset = Random.Range(0f, 100f);
        
        // Prevent overlap double-harvesting immediately on awake
        Collider[] hits = Physics.OverlapSphere(transform.position, 0.5f);
        foreach (var hit in hits)
        {
            if (hit.gameObject != gameObject)
            {
                Kelp other = hit.GetComponentInParent<Kelp>();
                if (other != null && Vector3.Distance(transform.position, other.transform.position) < 0.5f)
                {
                    if (other.GetInstanceID() < GetInstanceID())
                    {
                        Destroy(gameObject);
                        return;
                    }
                }
            }
        }

        _rb = GetComponent<Rigidbody>();
        _worldObject = GetComponent<WorldObject>();
        _initialWorldScale = transform.lossyScale;
        _initialLocalRotation = transform.localRotation;

        FindComponents();

        if (stemTransform != null)
        {
            _stemRenderer = stemTransform.GetComponent<Renderer>();
            if (_stemRenderer == null) _stemRenderer = stemTransform.GetComponentInChildren<Renderer>();
        }
        {
            GameObject swayPivot = new GameObject("SwayPivot");
            swayPivot.transform.SetParent(stemTransform.parent, false);
            
            // Set pivot exactly at the plant's root (the ground) so the bottom remains perfectly static
            swayPivot.transform.position = transform.position;
            swayPivot.transform.rotation = stemTransform.rotation;
            swayPivot.transform.localScale = Vector3.one;

            stemTransform.SetParent(swayPivot.transform, true);
            
            if (leafTransforms != null)
            {
                for (int i = 0; i < leafTransforms.Length; i++)
                {
                    if (leafTransforms[i] != null && leafTransforms[i].parent != swayPivot.transform)
                    {
                        leafTransforms[i].SetParent(swayPivot.transform, true);
                    }
                }
            }

            stemTransform = swayPivot.transform;
        }

        if (stemTransform != null)
        {
            _stemOriginalRotation = stemTransform.localRotation;
            _stemOriginalScaleY = stemTransform.localScale.y;
        }

        if (leafTransforms != null && leafTransforms.Length > 0)
        {
            _leafOriginalRotations = new Vector3[leafTransforms.Length];
            _leafOriginalScales = new Vector3[leafTransforms.Length];
            _leafGrowthProgress = new float[leafTransforms.Length];
            _leafRenderers = new Renderer[leafTransforms.Length];
            for (int i = 0; i < leafTransforms.Length; i++)
            {
                if (leafTransforms[i] != null)
                {
                    _leafRenderers[i] = leafTransforms[i].GetComponentInChildren<Renderer>();
                    _leafOriginalRotations[i] = leafTransforms[i].localEulerAngles;
                    _leafOriginalScales[i] = leafTransforms[i].localScale;
                    _leafGrowthProgress[i] = 1f; // Default to fully grown
                    leafTransforms[i].gameObject.SetActive(true);
                }
            }
        }

        _worldObject.interactable = _isWild;
        _worldObject.carryable = !_isWild;

        if (_isWild)
        {
            _rb.isKinematic = true;
            _rb.useGravity = false;

            if (_stemRenderer != null)
            {
                _stemRenderer.material.color = matureColor;
                if (_stemRenderer.material.HasProperty("_BaseColor")) _stemRenderer.material.SetColor("_BaseColor", matureColor);
            }

            Collider[] colliders = GetComponentsInChildren<Collider>();
            foreach (Collider c in colliders)
            {
                if (!c.isTrigger) c.enabled = true;
            }
        }

        _worldObject.onInteract.AddListener(OnWildInteract);
    }

    private void OnWildInteract(GameObject actor)
    {
        if (!_isWild) return;
        HarvestLeaves();
    }

    void Start()
    {
        if (_worldObject != null)
        {
            _worldObject.BaseScale = transform.localScale;
        }

        if (_isWild)
        {
            string posKey = GetPositionKey();
            if (_wildKelpLeafStates.TryGetValue(posKey, out float[] savedStates))
            {
                for (int i = 0; i < leafTransforms.Length && i < savedStates.Length; i++)
                {
                    _leafGrowthProgress[i] = savedStates[i];
                }
            }
            else
            {
                _wildKelpLeafStates[posKey] = (float[])_leafGrowthProgress.Clone();
            }
        }
    }

    private string GetPositionKey()
    {
        return $"{Mathf.RoundToInt(transform.position.x * 10f)}_{Mathf.RoundToInt(transform.position.y * 10f)}_{Mathf.RoundToInt(transform.position.z * 10f)}";
    }

    void Update()
    {
        ApplySwaying();

        bool hasAnyHarvestable = false;
        
        if (leafTransforms != null && _leafGrowthProgress != null)
        {
            for (int i = 0; i < leafTransforms.Length; i++)
            {
                if (leafTransforms[i] == null) continue;

                if (_leafGrowthProgress[i] < 1f)
                {
                    _leafGrowthProgress[i] += Time.deltaTime / leafRegrowTime;
                    if (_leafGrowthProgress[i] > 1f) _leafGrowthProgress[i] = 1f;

                    if (_isWild)
                    {
                        string key = GetPositionKey();
                        if (!_wildKelpLeafStates.ContainsKey(key))
                        {
                            _wildKelpLeafStates[key] = new float[leafTransforms.Length];
                        }
                        _wildKelpLeafStates[key][i] = _leafGrowthProgress[i];
                    }
                }

                if (_leafGrowthProgress[i] >= 0.99f) hasAnyHarvestable = true;

                leafTransforms[i].localScale = _leafOriginalScales[i] * _leafGrowthProgress[i];
                leafTransforms[i].gameObject.SetActive(_leafGrowthProgress[i] > 0.05f);
                
                if (_leafRenderers != null && _leafRenderers[i] != null && _leafGrowthProgress[i] > 0.05f)
                {
                    Color lerped = Color.Lerp(sproutColor, matureColor, _leafGrowthProgress[i]);
                    _leafRenderers[i].material.color = lerped;
                    if (_leafRenderers[i].material.HasProperty("_BaseColor")) _leafRenderers[i].material.SetColor("_BaseColor", lerped);
                }
            }
        }

        if (_worldObject != null)
        {
            _worldObject.interactable = hasAnyHarvestable && (_isWild || _isPlanted);
            if (hasAnyHarvestable) _worldObject.interactMessage = "Harvest Kelp";
            else _worldObject.interactMessage = "";
        }
    }

    private void FindComponents()
    {
        if (stemTransform == null)
        {
            stemTransform = FindDeepChild(transform, "Cylinder");
        }

        if (leafTransforms == null || leafTransforms.Length == 0)
        {
            var planes = new List<Transform>();
            FindPlanesRecursive(transform, planes);
            leafTransforms = planes.ToArray();
        }
    }

    private void FindPlanesRecursive(Transform parent, List<Transform> planes)
    {
        foreach (Transform child in parent)
        {
            if (child.name.Contains("Plane"))
            {
                planes.Add(child);
            }
            FindPlanesRecursive(child, planes);
        }
    }

    private Transform FindDeepChild(Transform parent, string childName)
    {
        foreach (Transform child in parent)
        {
            if (child.name == childName) return child;
            Transform result = FindDeepChild(child, childName);
            if (result != null) return result;
        }
        return null;
    }

    private void ApplySwaying()
    {
        float timeFactor = (Time.time + _swayOffset) * swaySpeed;

        if (stemTransform != null)
        {
            float angleX = Mathf.Sin(timeFactor) * swayAmount;
            float angleZ = Mathf.Cos(timeFactor * 0.8f) * swayAmount;
            float angleY = Mathf.Sin(timeFactor * 1.3f) * (swayAmount * 1.5f); // Twisting motion
            stemTransform.localRotation = _stemOriginalRotation * Quaternion.Euler(angleX, angleY, angleZ);
        }

        if (leafTransforms != null && _leafOriginalRotations != null)
        {
            for (int i = 0; i < leafTransforms.Length; i++)
            {
                if (leafTransforms[i] != null && i < _leafOriginalRotations.Length)
                {
                    float phase = i * 0.5f;
                    float angleX = Mathf.Sin(timeFactor * 1.2f + phase) * (swayAmount * 1.2f);
                    float angleZ = Mathf.Cos(timeFactor * 0.9f + phase) * (swayAmount * 1.2f);
                    Vector3 orig = _leafOriginalRotations[i];
                    leafTransforms[i].localRotation = Quaternion.Euler(orig) * Quaternion.Euler(angleX, 0f, angleZ);
                }
            }
        }
    }

    public void OnPlanted(Transform spot)
    {
        _isPlanted = true;
        _isWild = false;

        transform.parent = spot;
        transform.localPosition = Vector3.zero;

        PlantPot pot = spot.GetComponentInParent<PlantPot>();
        if (pot != null)
        {
            transform.localRotation = Quaternion.Inverse(spot.localRotation) * _initialLocalRotation * Quaternion.Euler(90f, 0f, 0f);
        }
        else
        {
            transform.localRotation = _initialLocalRotation * Quaternion.Euler(90f, 0f, 0f);
        }

        _rb.isKinematic = true;
        _rb.useGravity = false;

        Collider[] colliders = GetComponentsInChildren<Collider>();
        foreach (Collider c in colliders)
        {
            c.enabled = false;
        }

        _worldObject.interactable = false;
        _worldObject.carryable = false;

        SetGrowthScale(0f);
        
        if (leafTransforms != null && _leafGrowthProgress != null)
        {
            for (int i = 0; i < leafTransforms.Length; i++)
            {
                _leafGrowthProgress[i] = 0f;
            }
        }
    }

    public void SetGrowthScale(float progress)
    {
        if (_stemRenderer != null)
        {
            Color lerped = Color.Lerp(sproutColor, matureColor, progress);
            _stemRenderer.material.color = lerped;
            if (_stemRenderer.material.HasProperty("_BaseColor")) _stemRenderer.material.SetColor("_BaseColor", lerped);
        }

        float smoothT = Mathf.SmoothStep(0f, 1f, progress);
        float currentScale = Mathf.Lerp(0.1f, 1.0f, smoothT);
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
    }

    public void Extract()
    {
        if (!_isWild) return;

        _isWild = false;
        _worldObject.interactable = false;
        _worldObject.carryable = true;

        _rb.isKinematic = false;
        _rb.useGravity = true;

        Collider[] colliders = GetComponentsInChildren<Collider>();
        foreach (Collider c in colliders)
        {
            c.enabled = true;
        }

        _rb.AddForce(Vector3.up * jumpForce, ForceMode.Impulse);

        float randX = Random.Range(-scatterForce, scatterForce);
        float randZ = Random.Range(-scatterForce, scatterForce);
        _rb.AddForce(new Vector3(randX, 0f, randZ), ForceMode.Impulse);

        _rb.AddTorque(Random.insideUnitSphere * jumpForce, ForceMode.Impulse);

        Debug.Log("[Kelp] Wild kelp extracted and is now carryable.");
    }

    public void HarvestLeaves()
    {
#if UNITY_EDITOR
        if (leafPrefab == null)
        {
            leafPrefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/KelpLeaf.prefab");
        }
#endif

        if (leafPrefab == null)
        {
            Debug.LogError("[Kelp] Leaf prefab is missing, cannot harvest.");
            return;
        }

        int spawnCount = 0;
        if (leafTransforms != null && _leafGrowthProgress != null)
        {
            for (int i = 0; i < leafTransforms.Length; i++)
            {
                if (_leafGrowthProgress[i] >= 0.99f)
                {
                    spawnCount++;
                    
                    Vector3 spawnPos = leafTransforms[i].position;
                    GameObject leafObj = Instantiate(leafPrefab, spawnPos, leafTransforms[i].rotation);

                    Renderer plantLeafRenderer = leafTransforms[i].GetComponentInChildren<Renderer>();
                    Renderer dropLeafRenderer = leafObj.GetComponentInChildren<Renderer>();
                    if (plantLeafRenderer != null && dropLeafRenderer != null)
                    {
                        float dropSize = dropLeafRenderer.bounds.size.magnitude;
                        float plantSize = plantLeafRenderer.bounds.size.magnitude;
                        if (dropSize > 0.001f)
                        {
                            leafObj.transform.localScale *= (plantSize / dropSize);
                            
                            // Align the visual center of the dropped leaf with the plant leaf, slightly raised
                            Vector3 visualOffset = dropLeafRenderer.bounds.center - leafObj.transform.position;
                            leafObj.transform.position = plantLeafRenderer.bounds.center - visualOffset + Vector3.up * 0.3f;
                            
                            WorldObject leafWo = leafObj.GetComponent<WorldObject>();
                            if (leafWo != null)
                            {
                                Color lerped = Color.Lerp(sproutColor, matureColor, _leafGrowthProgress[i]);
                                leafWo.BaseScale = leafObj.transform.localScale;
                                leafWo.customColor = lerped;
                                leafWo.ApplyCustomColor();
                            }
                        }
                    }

                    Rigidbody lRb = leafObj.GetComponent<Rigidbody>();
                    if (lRb != null)
                    {
                        lRb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;

                        Vector3 randomDir = Random.insideUnitSphere;
                        randomDir.y = Mathf.Abs(randomDir.y) + 0.5f;
                        randomDir.Normalize();

                        lRb.AddForce(randomDir * 2.5f, ForceMode.Impulse);
                        lRb.AddTorque(Random.insideUnitSphere * 1f, ForceMode.Impulse);
                    }

                    _leafGrowthProgress[i] = 0f;
                    leafTransforms[i].localScale = Vector3.zero;
                    leafTransforms[i].gameObject.SetActive(false);
                    
                    if (_isWild)
                    {
                        _wildKelpLeafStates[GetPositionKey()][i] = 0f;
                    }
                }
            }
        }
        
        if (spawnCount == 0) return;
        Debug.Log($"[Kelp] Spawning {spawnCount} physical KelpLeaf items based on plant's leaf count.");
    }
}
