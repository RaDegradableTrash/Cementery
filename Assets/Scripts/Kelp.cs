using System.Collections;
using System.Collections.Generic;
using UnityEngine;

[RequireComponent(typeof(WorldObject))]
[RequireComponent(typeof(Rigidbody))]
public class Kelp : MonoBehaviour
{
    [Header("References")]
    public Transform stemTransform;
    public Transform[] leafTransforms;
    [Tooltip("The Leaf prefab spawned during harvest.")]
    public GameObject leafPrefab;

    [Header("Transition Settings")]
    [Tooltip("Visual transform representing the initial inserted leaf during transition.")]
    public Transform plantedLeafVisual;

    [Header("Swaying Settings")]
    public float swaySpeed = 3f;
    public float swayAmount = 8f; // Degrees

    private Rigidbody _rb;
    private WorldObject _worldObject;
    private bool _isPlanted = false;
    private bool _isWild = true;
    private Vector3 _initialWorldScale;
    private Quaternion _initialLocalRotation;
    private Vector3[] _leafOriginalRotations;
    private Quaternion _stemOriginalRotation;

    // Growth variables
    private float _stemTargetScaleY = 1.0f;
    private Vector3[] _leafTargetLocalScales;
    private Material[] _leafMaterials;

    private float _stemLocalMinY = -1.0f;
    private Vector3 _stemInitialBottomPos;
    private Vector3[] _leafRelativeOffsets;
    private Quaternion[] _leafInitialLocalRotations;
    private Vector3[] _leafInitialLocalPositions;
    private Vector3 _plantedLeafRelativeOffset;
    private Quaternion _plantedLeafInitialLocalRot;

    private float[] _leafProgress;
    private float[] _lastAttemptProgress;
    private int[] _failedAttempts;

    public Vector3 InitialWorldScale => _initialWorldScale;
    public bool IsWild => _isWild;

    void Awake()
    {
        _rb = GetComponent<Rigidbody>();
        _worldObject = GetComponent<WorldObject>();
        _initialWorldScale = transform.lossyScale;
        _initialLocalRotation = transform.localRotation;

        FindComponents();

        if (stemTransform != null)
        {
            _stemOriginalRotation = stemTransform.localRotation;
            _stemTargetScaleY = stemTransform.localScale.y;

            float localMinY = -1.0f;
            MeshFilter mf = stemTransform.GetComponent<MeshFilter>();
            if (mf == null) mf = stemTransform.GetComponentInChildren<MeshFilter>();
            if (mf != null && mf.sharedMesh != null)
            {
                localMinY = mf.sharedMesh.bounds.min.y;
            }
            _stemLocalMinY = localMinY;

            Vector3 localOffsetToBottom = new Vector3(0f, _stemLocalMinY, 0f);
            _stemInitialBottomPos = stemTransform.localPosition + _stemOriginalRotation * Vector3.Scale(stemTransform.localScale, localOffsetToBottom);
        }

        // Initially in wild state: interactable to harvest leaves directly
        _worldObject.interactable = _isWild;
        _worldObject.carryable = false;

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

        InitializeLeafMaterials();
    }

    private void FindComponents()
    {
        if (stemTransform == null)
        {
            stemTransform = transform.Find("EmptyRelocate/Cylinder");
            if (stemTransform == null) stemTransform = transform.Find("Cylinder");
        }

        Transform relocate = transform.Find("EmptyRelocate");
        Transform searchRoot = relocate != null ? relocate : transform;
        
        // Find planted leaf visual if not assigned (look for first child plane)
        if (plantedLeafVisual == null)
        {
            plantedLeafVisual = searchRoot.Find("PlantedLeafVisual");
            if (plantedLeafVisual == null)
            {
                Transform firstPlane = searchRoot.Find("Plane");
                if (firstPlane != null)
                {
                    GameObject tempObj = Instantiate(firstPlane.gameObject, searchRoot);
                    tempObj.name = "PlantedLeafVisual_Temp";
                    plantedLeafVisual = tempObj.transform;

                    Collider col = tempObj.GetComponent<Collider>();
                    if (col != null) col.enabled = false;
                    Rigidbody rb = tempObj.GetComponent<Rigidbody>();
                    if (rb != null) Destroy(rb);
                }
            }
        }

        var planes = new List<Transform>();
        for (int i = 0; i < searchRoot.childCount; i++)
        {
            Transform child = searchRoot.GetChild(i);
            // Do NOT include the temporary/designated transition leaf in the growth list
            if (child.name.StartsWith("Plane") && child != plantedLeafVisual)
            {
                planes.Add(child);
            }
        }
        leafTransforms = planes.ToArray();
    }

    private void InitializeLeafMaterials()
    {
        if (leafTransforms != null && leafTransforms.Length > 0)
        {
            _leafTargetLocalScales = new Vector3[leafTransforms.Length];
            _leafMaterials = new Material[leafTransforms.Length];
            _leafRelativeOffsets = new Vector3[leafTransforms.Length];
            _leafInitialLocalRotations = new Quaternion[leafTransforms.Length];
            _leafInitialLocalPositions = new Vector3[leafTransforms.Length];

            _leafProgress = new float[leafTransforms.Length];
            _lastAttemptProgress = new float[leafTransforms.Length];
            _failedAttempts = new int[leafTransforms.Length];

            Vector3 stemInitialScale = stemTransform != null ? stemTransform.localScale : Vector3.one;
            Quaternion stemInitialRotInverse = stemTransform != null ? Quaternion.Inverse(_stemOriginalRotation) : Quaternion.identity;
            Vector3 stemInitialPos = stemTransform != null ? stemTransform.localPosition : Vector3.zero;

            // Use target scale cached in Awake if available to avoid shrunken calculations
            if (stemTransform != null)
            {
                stemInitialScale.y = _stemTargetScaleY;
            }

            for (int i = 0; i < leafTransforms.Length; i++)
            {
                if (leafTransforms[i] != null)
                {
                    _leafTargetLocalScales[i] = leafTransforms[i].localScale;
                    _leafInitialLocalPositions[i] = leafTransforms[i].localPosition;
                    _leafInitialLocalRotations[i] = leafTransforms[i].localRotation;

                    _leafProgress[i] = _isWild ? 1f : 0f;
                    _lastAttemptProgress[i] = _isWild ? 1f : 0f;
                    _failedAttempts[i] = 0;

                    if (stemTransform != null)
                    {
                        Vector3 toLeaf = _leafInitialLocalPositions[i] - stemInitialPos;
                        Vector3 localOffset = stemInitialRotInverse * toLeaf;
                        _leafRelativeOffsets[i] = new Vector3(
                            localOffset.x / stemInitialScale.x,
                            localOffset.y / stemInitialScale.y,
                            localOffset.z / stemInitialScale.z
                        );
                    }
                    
                    Renderer r = leafTransforms[i].GetComponent<Renderer>();
                    if (r != null)
                    {
                        // Instance material so we don't overwrite source assets
                        _leafMaterials[i] = r.material;
                    }
                }
            }

            _leafOriginalRotations = new Vector3[leafTransforms.Length];
            for (int i = 0; i < leafTransforms.Length; i++)
            {
                if (leafTransforms[i] != null)
                    _leafOriginalRotations[i] = leafTransforms[i].localEulerAngles;
            }
        }

        if (plantedLeafVisual != null)
        {
            _plantedLeafInitialLocalRot = plantedLeafVisual.localRotation;
            if (stemTransform != null)
            {
                Vector3 toLeaf = plantedLeafVisual.localPosition - stemTransform.localPosition;
                Vector3 localOffset = Quaternion.Inverse(_stemOriginalRotation) * toLeaf;
                Vector3 stemInitialScale = stemTransform.localScale;
                stemInitialScale.y = _stemTargetScaleY;
                _plantedLeafRelativeOffset = new Vector3(
                    localOffset.x / stemInitialScale.x,
                    localOffset.y / stemInitialScale.y,
                    localOffset.z / stemInitialScale.z
                );
            }
        }
    }

    void Update()
    {
        ApplySwaying();
    }

    private void ApplySwaying()
    {
        float timeFactor = Time.time * swaySpeed;

        if (stemTransform != null)
        {
            float angleX = Mathf.Sin(timeFactor) * swayAmount;
            float angleZ = Mathf.Cos(timeFactor * 0.8f) * swayAmount;
            
            Quaternion currentStemRot = _stemOriginalRotation * Quaternion.Euler(angleX, 0f, angleZ);
            stemTransform.localRotation = currentStemRot;

            Vector3 currentStemScale = stemTransform.localScale;
            Vector3 localOffsetToBottom = new Vector3(0f, _stemLocalMinY, 0f);
            Vector3 currentBottomOffset = currentStemRot * Vector3.Scale(currentStemScale, localOffsetToBottom);
            stemTransform.localPosition = _stemInitialBottomPos - currentBottomOffset;

            if (leafTransforms != null)
            {
                for (int i = 0; i < leafTransforms.Length; i++)
                {
                    if (leafTransforms[i] != null)
                    {
                        Vector3 currentLocalOffset = Vector3.Scale(currentStemScale, _leafRelativeOffsets[i]);
                        Vector3 currentOffsetInParent = currentStemRot * currentLocalOffset;
                        leafTransforms[i].localPosition = stemTransform.localPosition + currentOffsetInParent;

                        Quaternion stemRotationDiff = currentStemRot * Quaternion.Inverse(_stemOriginalRotation);

                        float phase = i * 0.5f;
                        float leafAngleX = Mathf.Sin(timeFactor * 1.2f + phase) * (swayAmount * 0.8f);
                        float leafAngleZ = Mathf.Cos(timeFactor * 0.9f + phase) * (swayAmount * 0.8f);
                        Quaternion leafSway = Quaternion.Euler(leafAngleX, 0f, leafAngleZ);

                        leafTransforms[i].localRotation = stemRotationDiff * _leafInitialLocalRotations[i] * leafSway;
                    }
                }
            }

            if (plantedLeafVisual != null && plantedLeafVisual.gameObject.activeSelf)
            {
                Vector3 currentLocalOffset = Vector3.Scale(currentStemScale, _plantedLeafRelativeOffset);
                Vector3 currentOffsetInParent = currentStemRot * currentLocalOffset;
                plantedLeafVisual.localPosition = stemTransform.localPosition + currentOffsetInParent;

                Quaternion stemRotationDiff = currentStemRot * Quaternion.Inverse(_stemOriginalRotation);
                plantedLeafVisual.localRotation = stemRotationDiff * _plantedLeafInitialLocalRot;
            }
        }
    }

    // Wild Kelp interaction (RMB click) -> harvests leaves
    private void OnWildInteract(GameObject actor)
    {
        if (!_isWild) return;
        HarvestLeaves();
    }

    public void OnPlanted(Transform spot)
    {
        _isPlanted = true;
        _isWild = false;

        // Parent to spot
        transform.parent = spot;
        transform.localPosition = Vector3.zero;

        // Align rotation to match the pot's coordinate system (ignoring spot's local rotation offset) and flip it 180 degrees to point upwards
        PlantPot pot = spot.GetComponentInParent<PlantPot>();
        if (pot != null)
        {
            // Calculate local rotation relative to spot and rotate 180 degrees around X to flip it right side up
            transform.localRotation = Quaternion.Inverse(spot.localRotation) * _initialLocalRotation * Quaternion.Euler(180f, 0f, 0f);
        }
        else
        {
            transform.localRotation = _initialLocalRotation;
        }

        // Disable physics/colliders
        _rb.isKinematic = true;
        _rb.useGravity = false;
        
        Collider[] colliders = GetComponentsInChildren<Collider>();
        foreach (Collider c in colliders)
        {
            c.enabled = false;
        }

        _worldObject.interactable = false;
        _worldObject.carryable = false;

        // Start Transition
        StartCoroutine(PlantTransitionCo());
    }

    // Leaf to Stem transition animation
    private IEnumerator PlantTransitionCo()
    {
        // Setup initial transition state:
        // Leaf is fully visible, stem is scaled down to Y = 0.05
        if (plantedLeafVisual != null)
        {
            plantedLeafVisual.gameObject.SetActive(true);
            plantedLeafVisual.localScale = Vector3.one; // Standard scale
        }

        if (stemTransform != null)
        {
            stemTransform.gameObject.SetActive(true);
            Vector3 sScale = stemTransform.localScale;
            sScale.y = 0.05f;
            stemTransform.localScale = sScale;
        }

        // Deactivate other leaves during transition
        if (leafTransforms != null)
        {
            foreach (var leaf in leafTransforms)
            {
                if (leaf != null && leaf != plantedLeafVisual) leaf.gameObject.SetActive(false);
            }
        }

        float elapsed = 0f;
        float duration = 1.5f;

        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / duration);

            // Planted leaf visual shrinks down to zero
            if (plantedLeafVisual != null)
            {
                plantedLeafVisual.localScale = Vector3.Lerp(Vector3.one, Vector3.zero, t);
            }

            // Stem Y-scale grows to transition height (e.g. 0.2 of standard Y height)
            if (stemTransform != null)
            {
                Vector3 sScale = stemTransform.localScale;
                sScale.y = Mathf.Lerp(0.05f, _stemTargetScaleY * 0.2f, t);
                stemTransform.localScale = sScale;
            }

            yield return null;
        }

        // Deactivate and destroy transition leaf visual if it is a temporary clone
        if (plantedLeafVisual != null)
        {
            if (plantedLeafVisual.name == "PlantedLeafVisual_Temp")
            {
                Destroy(plantedLeafVisual.gameObject);
                plantedLeafVisual = null;
            }
            else
            {
                plantedLeafVisual.gameObject.SetActive(false);
            }
        }

        // Complete transition
        SetGrowthProgress(0f);
    }

    // Updates the visual state based on growth progress (0.0 to 1.0)
    public void SetGrowthProgress(float progress)
    {
        // 1. Stem vertical growth (from 0.2 to 1.0 of standard height)
        if (stemTransform != null)
        {
            Vector3 sScale = stemTransform.localScale;
            sScale.y = Mathf.Lerp(_stemTargetScaleY * 0.2f, _stemTargetScaleY, progress);
            stemTransform.localScale = sScale;
        }

        // 2. Leaf growth stages
        if (leafTransforms == null || leafTransforms.Length == 0) return;

        for (int i = 0; i < leafTransforms.Length; i++)
        {
            Transform leaf = leafTransforms[i];
            if (leaf == null) continue;

            // If global progress reaches 1.0, force catch-up
            if (progress >= 1.0f)
            {
                _leafProgress[i] = 1.0f;
            }
            else
            {
                // Check if we need to attempt to grow (every 0.02 progress step)
                float step = 0.02f;
                if (progress - _lastAttemptProgress[i] >= step)
                {
                    _lastAttemptProgress[i] = progress;
                    
                    // Attempt growth check: 15% success rate
                    if (Random.value < 0.15f)
                    {
                        _leafProgress[i] = progress;
                        _failedAttempts[i] = 0;
                    }
                    else
                    {
                        _failedAttempts[i]++;
                        // Pity counter: if failed 20 times, 21st attempt succeeds
                        if (_failedAttempts[i] >= 20)
                        {
                            _leafProgress[i] = progress;
                            _failedAttempts[i] = 0;
                        }
                    }
                }
            }

            // Apply visual based on individual leaf progress
            float lp = _leafProgress[i];
            if (lp < 0.3f)
            {
                leaf.gameObject.SetActive(false);
            }
            else if (lp < 0.7f)
            {
                float stageProgress = (lp - 0.3f) / 0.4f;
                float sproutScaleFactor = Mathf.Lerp(0.1f, 0.4f, stageProgress);
                Color sproutColor = new Color(0.6f, 1f, 0.6f, 1f);

                leaf.gameObject.SetActive(true);
                leaf.localScale = _leafTargetLocalScales[i] * sproutScaleFactor;

                if (_leafMaterials != null && i < _leafMaterials.Length && _leafMaterials[i] != null)
                {
                    _leafMaterials[i].color = sproutColor;
                }
            }
            else
            {
                float stageProgress = (lp - 0.7f) / 0.3f;
                float matureScaleFactor = Mathf.Lerp(0.4f, 1.0f, stageProgress);
                Color matureColor = new Color(0.12f, 0.75f, 0.12f, 1f);

                leaf.gameObject.SetActive(true);
                leaf.localScale = _leafTargetLocalScales[i] * matureScaleFactor;

                if (_leafMaterials != null && i < _leafMaterials.Length && _leafMaterials[i] != null)
                {
                    _leafMaterials[i].color = matureColor;
                }
            }
        }
    }

    // Harvests all currently active leaves, popping them out as carryable items
    public void HarvestLeaves()
    {
        // Find count of active/visible leaves on the plant and record their lossy scales
        var activeLeafScales = new List<Vector3>();
        if (leafTransforms != null)
        {
            for (int i = 0; i < leafTransforms.Length; i++)
            {
                Transform leaf = leafTransforms[i];
                if (leaf != null)
                {
                    if (leaf.gameObject.activeSelf)
                    {
                        activeLeafScales.Add(leaf.lossyScale);
                        leaf.gameObject.SetActive(false); // Hide the leaf on the plant
                    }
                    if (_leafProgress != null && i < _leafProgress.Length) _leafProgress[i] = 0f;
                    if (_lastAttemptProgress != null && i < _lastAttemptProgress.Length) _lastAttemptProgress[i] = 0f;
                    if (_failedAttempts != null && i < _failedAttempts.Length) _failedAttempts[i] = 0;
                }
            }
        }

        // Also check the transition/planted leaf visual (which is Plane)
        if (plantedLeafVisual != null && plantedLeafVisual.gameObject.activeSelf)
        {
            activeLeafScales.Add(plantedLeafVisual.lossyScale);
            plantedLeafVisual.gameObject.SetActive(false);
        }

        // Shorten the stem to indicate it was harvested
        if (stemTransform != null)
        {
            Vector3 sScale = stemTransform.localScale;
            sScale.y = _stemTargetScaleY * 0.2f;
            stemTransform.localScale = sScale;
        }

        // Spawn harvested leaf items popping out
        if (activeLeafScales.Count > 0)
        {
#if UNITY_EDITOR
            if (leafPrefab == null)
            {
                leafPrefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/KelpLeaf.prefab");
            }
#endif
            ItemData leafItem = null;
            if (leafPrefab != null)
            {
                WorldObject prefabWo = leafPrefab.GetComponent<WorldObject>();
                if (prefabWo != null)
                {
                    leafItem = prefabWo.collectItemData;
                }
            }

            // Find a source leaf transform to duplicate (so we copy the exact mesh and renderer settings)
            Transform sourceLeaf = null;
            if (leafTransforms != null)
            {
                foreach (var l in leafTransforms)
                {
                    if (l != null) { sourceLeaf = l; break; }
                }
            }
            if (sourceLeaf == null) sourceLeaf = plantedLeafVisual;

            for (int i = 0; i < activeLeafScales.Count; i++)
            {
                Vector3 spawnPos = transform.position + Vector3.up * 0.3f;
                GameObject spawned = null;
                
                if (sourceLeaf != null)
                {
                    // Duplicate the source leaf directly!
                    spawned = Instantiate(sourceLeaf.gameObject, spawnPos, Quaternion.identity);
                    spawned.name = "KelpLeaf";
                    spawned.gameObject.SetActive(true);

                    // Add required components
                    Rigidbody rbComp = spawned.GetComponent<Rigidbody>();
                    if (rbComp == null) rbComp = spawned.AddComponent<Rigidbody>();
                    rbComp.isKinematic = false;
                    rbComp.useGravity = true;

                    WorldObject woComp = spawned.GetComponent<WorldObject>();
                    if (woComp == null) woComp = spawned.AddComponent<WorldObject>();
                    woComp.carryable = true;
                    woComp.interactable = false;
                    woComp.collectable = true;
                    woComp.canBePlacedOnFloor = true;
                    woComp.collectItemData = leafItem;

                    if (spawned.GetComponent<KelpLeaf>() == null)
                    {
                        spawned.AddComponent<KelpLeaf>();
                    }

                    // Setup collider
                    Collider col = spawned.GetComponent<Collider>();
                    if (col == null)
                    {
                        Renderer rend = spawned.GetComponent<Renderer>();
                        BoxCollider box = spawned.AddComponent<BoxCollider>();
                        if (rend != null)
                        {
                            box.size = rend.bounds.size;
                        }
                        else
                        {
                            box.size = new Vector3(0.2f, 0.05f, 0.2f);
                        }
                    }
                    else
                    {
                        col.enabled = true;
                        col.isTrigger = false;
                        if (col is MeshCollider mc)
                        {
                            mc.convex = true;
                        }
                    }
                }
                else
                {
                    // Fallback to prefab if sourceLeaf is somehow null
#if UNITY_EDITOR
                    if (leafPrefab == null)
                    {
                        leafPrefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/KelpLeaf.prefab");
                    }
#endif
                    if (leafPrefab != null)
                    {
                        spawned = Instantiate(leafPrefab, spawnPos, Quaternion.identity);
                    }
                }

                if (spawned != null)
                {
                    spawned.transform.localScale = activeLeafScales[i];
                    WorldObject wo = spawned.GetComponent<WorldObject>();
                    if (wo != null)
                    {
                        wo.BaseScale = activeLeafScales[i];
                    }

                    Rigidbody rb = spawned.GetComponent<Rigidbody>();
                    if (rb != null)
                    {
                        rb.isKinematic = false;
                        rb.useGravity = true;

                        // Apply outward explosion forces distributed in a circle to scatter them beautifully
                        float angle = i * (Mathf.PI * 2f / activeLeafScales.Count) + Random.Range(-0.2f, 0.2f);
                        Vector3 forceDir = new Vector3(
                            Mathf.Cos(angle) * Random.Range(0.8f, 1.2f),
                            Random.Range(0.8f, 1.4f),
                            Mathf.Sin(angle) * Random.Range(0.8f, 1.2f)
                        ).normalized;

                        float forceMagnitude = Random.Range(4f, 6f);
                        rb.AddForce(forceDir * forceMagnitude, ForceMode.Impulse);
                        rb.AddTorque(new Vector3(Random.Range(-5f, 5f), Random.Range(-5f, 5f), Random.Range(-5f, 5f)), ForceMode.Impulse);
                    }
                }
            }
        }

        if (_isWild)
        {
            _isWild = false;
            // Deactivate wild interaction since it is harvested
            _worldObject.interactable = false;
        }
    }
}
