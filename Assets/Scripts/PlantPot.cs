using System.Collections;
using UnityEngine;

[RequireComponent(typeof(WorldObject))]
public class PlantPot : MonoBehaviour
{
    [Header("References")]
    [Tooltip("The spot where the kelp will be positioned and grow.")]
    public Transform plantSpotEmpty;
    [Tooltip("The full Kelp plant prefab instantiated when planting.")]
    public GameObject kelpPrefab;

    [Header("Growth Settings")]
    [Tooltip("Time in seconds for the kelp to reach maturity.")]
    public float growthDuration = 10f;

    private WorldObject _worldObject;
    private Kelp _currentPlant;
    private float _progress = 0f;
    private bool _isGrowing = false;
    private bool _isMature = false;

    void Awake()
    {
        _worldObject = GetComponent<WorldObject>();
        _worldObject.onInteract.AddListener(OnInteract);
        
        // Initial setup
        _worldObject.interactMessage = "Plant Leaf";
        _worldObject.interactable = false; // Only interactable when holding leaf initially
    }

    void Start()
    {
#if UNITY_EDITOR
        // If kelpPrefab is null or doesn't have the Kelp component (e.g. mistakenly assigned the leaf prefab),
        // dynamically load the full Seaweed2 plant prefab from Assets.
        if (kelpPrefab == null || kelpPrefab.GetComponent<Kelp>() == null)
        {
            kelpPrefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Seaweed2.prefab");
        }
#endif
    }

    void Update()
    {
        // Enforce the requirement: empty pot interactable ONLY when holding KelpLeaf
        if (!HasPlant())
        {
            bool holdingLeaf = false;
            if (InteractionSystem.Instance != null)
            {
                WorldObject carried = InteractionSystem.Instance.CarriedWorldObject;
                if (carried != null && carried.GetComponent<KelpLeaf>() != null)
                {
                    holdingLeaf = true;
                }
            }
            _worldObject.interactable = holdingLeaf;
            _worldObject.interactMessage = holdingLeaf ? "Plant Leaf" : "";
        }
        else
        {
            // Pot with plant is always interactable (either to check progress or harvest)
            _worldObject.interactable = true;
            if (_isMature)
            {
                _worldObject.interactMessage = "Harvest Leaves";
            }
            else
            {
                _worldObject.interactMessage = "Check Growth Progress";
            }
        }

        // Handle growth progress accumulation (growth starts after transition is complete in Kelp)
        if (_isGrowing && _currentPlant != null)
        {
            _progress += Time.deltaTime / growthDuration;
            if (_progress >= 1f)
            {
                _progress = 1f;
                _isGrowing = false;
                _isMature = true;
            }
            _currentPlant.SetGrowthProgress(_progress);
        }
    }

    public bool HasPlant()
    {
        return _currentPlant != null;
    }

    public bool CanPlant()
    {
        return !HasPlant() && !_isGrowing && !_isMature;
    }

    // Handles planting a carried KelpLeaf into this pot
    public void PlantLeaf(WorldObject leafWo)
    {
        if (!CanPlant() || kelpPrefab == null) return;

        // Position Kelp plant at the plant spot empty anchor
        Transform spot = plantSpotEmpty != null ? plantSpotEmpty : transform;
        GameObject kelpObj = Instantiate(kelpPrefab, spot.position, spot.rotation);
        
        Kelp kelp = kelpObj.GetComponent<Kelp>();
        if (kelp == null)
        {
            Destroy(kelpObj);
            return;
        }

        _currentPlant = kelp;
        _progress = 0f;
        _isGrowing = true;
        _isMature = false;

        kelp.OnPlanted(spot);

        // Ignore collisions between Pot and Kelp
        Collider[] potColliders = GetComponentsInChildren<Collider>();
        Collider[] kelpColliders = kelp.GetComponentsInChildren<Collider>();
        foreach (var potCol in potColliders)
        {
            foreach (var kelpCol in kelpColliders)
            {
                if (potCol != null && kelpCol != null)
                {
                    Physics.IgnoreCollision(potCol, kelpCol, true);
                }
            }
        }

        // Destroy the carried leaf object which was planted
        Destroy(leafWo.gameObject);

        if (InteractionSystem.Instance != null)
        {
            InteractionSystem.Instance.ShowInfoMessage("Leaf Planted!");
        }
    }

    // Called on RMB interaction
    private void OnInteract(GameObject actor)
    {
        // Case 1: Empty pot & holding leaf -> Plant it
        if (CanPlant())
        {
            if (InteractionSystem.Instance != null)
            {
                WorldObject carried = InteractionSystem.Instance.CarriedWorldObject;
                if (carried != null && carried.GetComponent<KelpLeaf>() != null)
                {
                    PlantLeaf(carried);
                    InteractionSystem.Instance.ConsumeCarriedObjectSilently();
                    InteractionSystem.Instance.ClearPrompts();
                }
            }
            return;
        }

        // Case 2: Growing -> Show progress info
        if (_isGrowing && _currentPlant != null)
        {
            if (InteractionSystem.Instance != null)
            {
                int percentage = Mathf.FloorToInt(_progress * 100f);
                InteractionSystem.Instance.ShowInfoMessage($"Kelp is growing: {percentage}%");
            }
            return;
        }

        // Case 3: Mature -> Harvest
        if (_isMature)
        {
            Harvest();
        }
    }

    [ContextMenu("Harvest")]
    public void Harvest()
    {
        if (!_isMature || _currentPlant == null) return;

        // Harvest all leaves from the growing plant, popping them out
        _currentPlant.HarvestLeaves();

        // The stem remains cut (scaled down) and stays in the pot. It grows again.
        _progress = 0f;
        _isGrowing = true;
        _isMature = false;

        _currentPlant.SetGrowthProgress(0f);

        if (InteractionSystem.Instance != null)
        {
            InteractionSystem.Instance.ShowInfoMessage("Harvested Leaves!");
        }
    }
}
