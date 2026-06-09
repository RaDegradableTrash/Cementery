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
    public float swaySpeed = 3f;
    public float swayAmount = 8f; // Degrees

    [Header("Extraction Settings")]
    public float jumpForce = 6f;
    public float scatterForce = 1.5f;

    private Rigidbody _rb;
    private WorldObject _worldObject;

    private bool _isWild = true;
    private bool _isPlanted = false;
    private Vector3 _initialWorldScale;
    private Quaternion _initialLocalRotation;

    private Vector3[] _leafOriginalRotations;
    private Quaternion _stemOriginalRotation;

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
        }

        // Cache initial local rotations of leaf transforms
        if (leafTransforms != null && leafTransforms.Length > 0)
        {
            _leafOriginalRotations = new Vector3[leafTransforms.Length];
            for (int i = 0; i < leafTransforms.Length; i++)
            {
                if (leafTransforms[i] != null)
                {
                    _leafOriginalRotations[i] = leafTransforms[i].localEulerAngles;
                    // Ensure the leaves on the plant are active by default
                    leafTransforms[i].gameObject.SetActive(true);
                }
            }
        }

        // Initially in wild state: interactable to extract, not carryable
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
        ApplySwaying();
    }

    private void FindComponents()
    {
        if (stemTransform == null)
        {
            stemTransform = FindDeepChild(transform, "Cylinder");
            if (stemTransform == null) stemTransform = transform.Find("EmptyRelocate/Cylinder");
            if (stemTransform == null) stemTransform = transform.Find("Cylinder");
        }

        var planes = new List<Transform>();
        FindPlanesRecursive(transform, planes);
        leafTransforms = planes.ToArray();

        Debug.Log($"[Kelp] FindComponents: Found {leafTransforms.Length} leaf planes. Stem: {(stemTransform != null ? stemTransform.name : "null")}");
    }

    private Transform FindDeepChild(Transform parent, string name)
    {
        foreach (Transform child in parent)
        {
            if (child.name == name) return child;
            Transform result = FindDeepChild(child, name);
            if (result != null) return result;
        }
        return null;
    }

    private void FindPlanesRecursive(Transform parent, List<Transform> list)
    {
        foreach (Transform child in parent)
        {
            if (child.name.StartsWith("Plane") || child.name.Contains("Plane"))
            {
                list.Add(child);
            }
            FindPlanesRecursive(child, list);
        }
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
                    Vector3 orig = (_leafOriginalRotations != null && i < _leafOriginalRotations.Length) ? _leafOriginalRotations[i] : Vector3.zero;
                    leafTransforms[i].localRotation = Quaternion.Euler(orig) * Quaternion.Euler(angleX, 0f, angleZ);
                }
            }
        }
    }

    private void OnWildInteract(GameObject actor)
    {
        if (!_isWild) return;
        HarvestLeaves();
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

        // Parent to spot
        transform.parent = spot;
        transform.localPosition = Vector3.zero;

        // Align rotation to match the pot's coordinate system and point upwards
        PlantPot pot = spot.GetComponentInParent<PlantPot>();
        if (pot != null)
        {
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

        // Set initial small growth scale
        SetGrowthScale(0f);
    }

    public void SetGrowthScale(float progress)
    {
        // Smooth ease-in-ease-out curve from 0.1 to 1.0 of the initial base scale
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

        _worldObject.BaseScale = transform.localScale;
    }

    // Harvest leaves popping out from this mature plant
    public void HarvestLeaves()
    {
        Debug.Log("[Kelp] HarvestLeaves triggered.");

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
#if UNITY_EDITOR
        if (leafItem == null)
        {
            leafItem = UnityEditor.AssetDatabase.LoadAssetAtPath<ItemData>("Assets/ItemData/ItemDataKelpLeaf.asset");
            if (leafItem == null)
            {
                leafItem = UnityEditor.AssetDatabase.LoadAssetAtPath<ItemData>("Assets/ItemDataKelpLeaf.asset");
            }
        }
#endif

        // Cache the source leaf before deactivating the visual meshes
        Transform sourceLeaf = null;
        if (leafTransforms != null)
        {
            foreach (var l in leafTransforms)
            {
                if (l != null) { sourceLeaf = l; break; }
            }
        }

        // Disable wild interaction states so it can only be harvested once
        if (_isWild)
        {
            _isWild = false;
            _worldObject.interactable = false;
        }

        int spawnCount = (leafTransforms != null && leafTransforms.Length > 0) ? leafTransforms.Length : 5;
        Debug.Log($"[Kelp] Spawning {spawnCount} physical KelpLeaf items based on plant's leaf count.");

        List<Collider> spawnedColliders = new List<Collider>();

        for (int i = 0; i < spawnCount; i++)
        {
            // Apply outward explosion forces distributed in a circle to scatter them beautifully
            float angle = i * (Mathf.PI * 2f / spawnCount) + Random.Range(-0.2f, 0.2f);
            Vector3 forceDir = new Vector3(
                Mathf.Cos(angle) * Random.Range(0.8f, 1.2f),
                Random.Range(0.3f, 0.6f), // Gentler upward force to avoid flying high
                Mathf.Sin(angle) * Random.Range(0.8f, 1.2f)
            ).normalized;

            Vector3 spawnPos = transform.position + Vector3.up * 0.3f + forceDir * 0.2f;

            // Create root spawned object with scale (1,1,1) for stable physics
            GameObject spawned = new GameObject("KelpLeaf");
            spawned.transform.position = spawnPos;
            spawned.transform.rotation = Quaternion.identity;
            spawned.transform.localScale = Vector3.one;
            spawned.layer = gameObject.layer; // Match the Kelp plant's layer for raycasting/interaction

            // Add required components to the root
            Rigidbody rb = spawned.AddComponent<Rigidbody>();
            rb.isKinematic = false;
            rb.useGravity = true;
            rb.drag = 1.0f;           // High linear drag to prevent sliding infinitely
            rb.angularDrag = 1.0f;    // High angular drag to prevent rolling infinitely

            WorldObject woComp = spawned.AddComponent<WorldObject>();
            woComp.carryable = true;
            woComp.interactable = false;
            woComp.collectable = true;
            woComp.canBePlacedOnFloor = true;
            woComp.collectItemData = leafItem;
            woComp.BaseScale = Vector3.one;

            // Add KelpLeaf component which sets up the BoxCollider
            KelpLeaf leafComp = spawned.AddComponent<KelpLeaf>();
            BoxCollider box = spawned.GetComponent<BoxCollider>();
            if (box != null)
            {
                box.size = new Vector3(0.5f, 0.35f, 0.5f);
                box.center = Vector3.zero;
            }

            // Instantiate visual child
            if (sourceLeaf != null)
            {
                GameObject visualChild = Instantiate(sourceLeaf.gameObject, spawned.transform);
                visualChild.transform.localPosition = Vector3.zero;
                visualChild.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
                visualChild.transform.localScale = sourceLeaf.lossyScale;
                visualChild.SetActive(true);

                // Destroy any colliders on the visual child mesh to prevent duplicate/overlapping physics conflicts
                Collider[] childCols = visualChild.GetComponentsInChildren<Collider>();
                foreach (var c in childCols)
                {
                    if (c != null && c != box) Destroy(c);
                }
            }
            else if (leafPrefab != null)
            {
                GameObject visualChild = Instantiate(leafPrefab, spawned.transform);
                visualChild.transform.localPosition = Vector3.zero;
                visualChild.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
                visualChild.transform.localScale = new Vector3(0.3f, 0.3f, 0.3f);
                visualChild.SetActive(true);

                Collider[] childCols = visualChild.GetComponentsInChildren<Collider>();
                foreach (var c in childCols)
                {
                    if (c != null && c != box) Destroy(c);
                }
            }

            // Set up collision ignores
            if (box != null)
            {
                // Ignore collision with other spawned leaves
                foreach (var otherCol in spawnedColliders)
                {
                    if (otherCol != null) Physics.IgnoreCollision(box, otherCol, true);
                }
                spawnedColliders.Add(box);

                // Ignore collision with player
                Collider[] playerCols = null;
                if (InteractionSystem.Instance != null)
                {
                    playerCols = InteractionSystem.Instance.GetComponentsInChildren<Collider>();
                }
                else
                {
                    var pc = FindObjectOfType<PlayerController>();
                    if (pc != null) playerCols = pc.GetComponentsInChildren<Collider>();
                }
                if (playerCols != null)
                {
                    foreach (var pc in playerCols)
                    {
                        if (pc != null) Physics.IgnoreCollision(box, pc, true);
                    }
                }
            }

            // Apply gentle impulse force and torque to scatter them
            float forceMagnitude = Random.Range(1.5f, 2.5f);
            rb.AddForce(forceDir * forceMagnitude, ForceMode.Impulse);
            rb.AddTorque(new Vector3(Random.Range(-1f, 1f), Random.Range(-1f, 1f), Random.Range(-1f, 1f)), ForceMode.Impulse);
        }

        // Visually deactivate the leaves on the plant AFTER we are done instantiating them
        if (leafTransforms != null)
        {
            foreach (var leaf in leafTransforms)
            {
                if (leaf != null) leaf.gameObject.SetActive(false);
            }
        }

        // Shorten the stem to indicate the plant was cut/harvested
        if (stemTransform != null)
        {
            Vector3 sScale = stemTransform.localScale;
            sScale.y *= 0.2f;
            stemTransform.localScale = sScale;
        }
    }
}
