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
    [SerializeField, Tooltip("Seconds between plant sway updates. Higher values reduce CPU cost across dense kelp patches.")]
    private float swayUpdateInterval = 0.033f;
    [SerializeField, Tooltip("Seconds between growth/state refreshes. Growth is slow, so it does not need to run every frame.")]
    private float growthUpdateInterval = 0.25f;

    [Header("Extraction Settings")]
    public float jumpForce = 6f;
    public float scatterForce = 1.5f;

    [Header("Color Settings")]
    public Color sproutColor = new Color(0.8f, 0.95f, 0.3f);
    public Color matureColor = new Color(0.3f, 0.7f, 0.3f);

    private Renderer _stemRenderer;
    private Renderer[] _leafRenderers;
    private Material _stemMaterial;
    private bool _stemMaterialHasBaseColor;
    private Material[] _leafMaterials;
    private bool[] _leafMaterialHasBaseColor;
    private static readonly Collider[] s_overlapBuffer = new Collider[32];

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
    private float[] _lastLeafVisualProgress;
    private bool[] _lastLeafActive;
    
    private float _swayOffset;
    private float _nextSwayUpdateTime;
    private float _nextGrowthUpdateTime;
    private float _lastGrowthUpdateTime;
    private string _positionKey;
    private bool _lastWorldObjectInteractable;
    private string _lastWorldObjectMessage;
    private float[] _wildLeafStateBuffer;

    public Vector3 InitialWorldScale => _initialWorldScale;
    public bool IsWild => _isWild;

    void Awake()
    {
        _swayOffset = Random.Range(0f, 100f);
        _nextSwayUpdateTime = Time.time + Random.Range(0f, Mathf.Max(0.001f, swayUpdateInterval));
        _nextGrowthUpdateTime = Time.time + Random.Range(0f, Mathf.Max(0.001f, growthUpdateInterval));
        _lastGrowthUpdateTime = Time.time;
        
        // Prevent overlap double-harvesting immediately on awake
        Vector3 currentPosition = transform.position;
        int hitCount = Physics.OverlapSphereNonAlloc(currentPosition, 0.5f, s_overlapBuffer);
        float overlapRadiusSq = 0.5f * 0.5f;
        for (int i = 0; i < hitCount; i++)
        {
            Collider hit = s_overlapBuffer[i];
            if (hit.gameObject != gameObject)
            {
                Kelp other = hit.GetComponentInParent<Kelp>();
                if (other != null && (currentPosition - other.transform.position).sqrMagnitude < overlapRadiusSq)
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
        _positionKey = GetPositionKey();

        FindComponents();

        if (stemTransform != null)
        {
            _stemRenderer = stemTransform.GetComponent<Renderer>();
            if (_stemRenderer == null) _stemRenderer = stemTransform.GetComponentInChildren<Renderer>();
            if (_stemRenderer != null)
            {
                _stemMaterial = _stemRenderer.material;
                _stemMaterialHasBaseColor = _stemMaterial != null && _stemMaterial.HasProperty("_BaseColor");
            }
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
            _lastLeafVisualProgress = new float[leafTransforms.Length];
            _lastLeafActive = new bool[leafTransforms.Length];
            _leafRenderers = new Renderer[leafTransforms.Length];
            _leafMaterials = new Material[leafTransforms.Length];
            _leafMaterialHasBaseColor = new bool[leafTransforms.Length];
            for (int i = 0; i < leafTransforms.Length; i++)
            {
                if (leafTransforms[i] != null)
                {
                    _leafRenderers[i] = leafTransforms[i].GetComponentInChildren<Renderer>();
                    if (_leafRenderers[i] != null)
                    {
                        _leafMaterials[i] = _leafRenderers[i].material;
                        _leafMaterialHasBaseColor[i] = _leafMaterials[i] != null && _leafMaterials[i].HasProperty("_BaseColor");
                    }
                    _leafOriginalRotations[i] = leafTransforms[i].localEulerAngles;
                    _leafOriginalScales[i] = leafTransforms[i].localScale;
                    _leafGrowthProgress[i] = 1f; // Default to fully grown
                    leafTransforms[i].gameObject.SetActive(true);
                    _lastLeafVisualProgress[i] = -1f;
                    _lastLeafActive[i] = true;
                }
            }
        }

        SetWorldObjectState(_isWild, _worldObject.interactMessage);
        _worldObject.carryable = !_isWild;

        if (_isWild)
        {
            _rb.isKinematic = true;
            _rb.useGravity = false;

            if (_stemRenderer != null)
            {
                ApplyStemColor(matureColor);
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
            if (_wildKelpLeafStates.TryGetValue(_positionKey, out float[] savedStates))
            {
                _wildLeafStateBuffer = savedStates;
                for (int i = 0; i < leafTransforms.Length && i < savedStates.Length; i++)
                {
                    _leafGrowthProgress[i] = savedStates[i];
                }
            }
            else
            {
                _wildLeafStateBuffer = (float[])_leafGrowthProgress.Clone();
                _wildKelpLeafStates[_positionKey] = _wildLeafStateBuffer;
            }
        }
    }

    private string GetPositionKey()
    {
        return $"{Mathf.RoundToInt(transform.position.x * 10f)}_{Mathf.RoundToInt(transform.position.y * 10f)}_{Mathf.RoundToInt(transform.position.z * 10f)}";
    }

    void Update()
    {
        float now = Time.time;
        if (now >= _nextSwayUpdateTime)
        {
            _nextSwayUpdateTime = now + Mathf.Max(0.001f, swayUpdateInterval);
            ApplySwaying();
        }

        if (now < _nextGrowthUpdateTime)
        {
            return;
        }

        float growthDelta = Mathf.Max(0f, now - _lastGrowthUpdateTime);
        _lastGrowthUpdateTime = now;
        _nextGrowthUpdateTime = now + Mathf.Max(0.001f, growthUpdateInterval);
        bool hasAnyHarvestable = false;
        float[] wildLeafStates = _isWild ? EnsureWildLeafStateBuffer() : null;
        
        if (leafTransforms != null && _leafGrowthProgress != null)
        {
            for (int i = 0; i < leafTransforms.Length; i++)
            {
                if (leafTransforms[i] == null) continue;

                if (_leafGrowthProgress[i] < 1f)
                {
                    _leafGrowthProgress[i] += growthDelta / leafRegrowTime;
                    if (_leafGrowthProgress[i] > 1f) _leafGrowthProgress[i] = 1f;

                    if (_isWild)
                    {
                        wildLeafStates[i] = _leafGrowthProgress[i];
                    }
                }

                if (_leafGrowthProgress[i] >= 0.99f) hasAnyHarvestable = true;

                UpdateLeafVisual(i, _leafGrowthProgress[i]);
            }
        }

        if (_worldObject != null)
        {
            SetWorldObjectState(hasAnyHarvestable && (_isWild || _isPlanted), hasAnyHarvestable ? "Harvest Kelp" : "");
        }
    }

    private void UpdateLeafVisual(int index, float progress)
    {
        if (leafTransforms == null || index < 0 || index >= leafTransforms.Length || leafTransforms[index] == null)
        {
            return;
        }

        bool isActive = progress > 0.05f;
        if (_lastLeafActive != null && index < _lastLeafActive.Length && _lastLeafActive[index] != isActive)
        {
            leafTransforms[index].gameObject.SetActive(isActive);
            _lastLeafActive[index] = isActive;
        }

        if (_lastLeafVisualProgress != null && index < _lastLeafVisualProgress.Length)
        {
            if (Mathf.Abs(_lastLeafVisualProgress[index] - progress) < 0.001f)
            {
                return;
            }
            _lastLeafVisualProgress[index] = progress;
        }

        if (_leafOriginalScales != null && index < _leafOriginalScales.Length)
        {
            leafTransforms[index].localScale = _leafOriginalScales[index] * progress;
        }

        if (!isActive || _leafMaterials == null || index >= _leafMaterials.Length || _leafMaterials[index] == null)
        {
            return;
        }

        Color lerped = Color.Lerp(sproutColor, matureColor, progress);
        _leafMaterials[index].color = lerped;
        if (_leafMaterialHasBaseColor != null && index < _leafMaterialHasBaseColor.Length && _leafMaterialHasBaseColor[index])
        {
            _leafMaterials[index].SetColor("_BaseColor", lerped);
        }
    }

    private void ApplyStemColor(Color color)
    {
        if (_stemMaterial == null)
        {
            return;
        }

        _stemMaterial.color = color;
        if (_stemMaterialHasBaseColor)
        {
            _stemMaterial.SetColor("_BaseColor", color);
        }
    }

    private void SetWorldObjectState(bool interactable, string message)
    {
        if (_worldObject == null)
        {
            return;
        }

        if (_lastWorldObjectInteractable != interactable)
        {
            _worldObject.interactable = interactable;
            _lastWorldObjectInteractable = interactable;
        }

        if (_lastWorldObjectMessage != message)
        {
            _worldObject.interactMessage = message;
            _lastWorldObjectMessage = message;
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

        SetWorldObjectState(false, _worldObject.interactMessage);
        _worldObject.carryable = false;

        SetGrowthScale(0f);
        _wildLeafStateBuffer = null;
        
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
            ApplyStemColor(lerped);
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
        SetWorldObjectState(false, _worldObject.interactMessage);
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
                    if (_lastLeafVisualProgress != null && i < _lastLeafVisualProgress.Length)
                    {
                        _lastLeafVisualProgress[i] = 0f;
                    }
                    if (_lastLeafActive != null && i < _lastLeafActive.Length)
                    {
                        _lastLeafActive[i] = false;
                    }
                    
                    if (_isWild)
                    {
                        float[] wildLeafStates = EnsureWildLeafStateBuffer();
                        wildLeafStates[i] = 0f;
                    }
                }
            }
        }
        
        if (spawnCount == 0) return;
        Debug.Log($"[Kelp] Spawning {spawnCount} physical KelpLeaf items based on plant's leaf count.");
    }

    private float[] EnsureWildLeafStateBuffer()
    {
        if (_leafGrowthProgress == null)
        {
            return null;
        }

        if (_wildLeafStateBuffer != null && _wildLeafStateBuffer.Length == _leafGrowthProgress.Length)
        {
            return _wildLeafStateBuffer;
        }

        if (!_wildKelpLeafStates.TryGetValue(_positionKey, out _wildLeafStateBuffer) ||
            _wildLeafStateBuffer == null ||
            _wildLeafStateBuffer.Length != _leafGrowthProgress.Length)
        {
            _wildLeafStateBuffer = (float[])_leafGrowthProgress.Clone();
            _wildKelpLeafStates[_positionKey] = _wildLeafStateBuffer;
        }

        return _wildLeafStateBuffer;
    }
}
