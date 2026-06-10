using System.Collections;
using UnityEngine;

[RequireComponent(typeof(WorldObject))]
public class PlantPot : MonoBehaviour
{
    [Header("References")]
    [Tooltip("The spot where the kelp will be positioned and grow.")]
    public Transform plantSpotEmpty;
    [Tooltip("The full Kelp plant prefab (fallback for planting if needed).")]
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
        _worldObject.interactMessage = "Plant Kelp";
        _worldObject.interactable = false; // Only interactable when holding Kelp plant initially
    }

    void Start()
    {
#if UNITY_EDITOR
        if (kelpPrefab == null)
        {
            kelpPrefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Kelp.prefab");
        }
#endif
    }

    void Update()
    {
        // Enforce the interaction gate: empty pot interactable ONLY when holding a carried Kelp plant
        if (!HasPlant())
        {
            bool holdingKelp = false;
            if (InteractionSystem.Instance != null)
            {
                WorldObject carried = InteractionSystem.Instance.CarriedWorldObject;
                if (carried != null && carried.GetComponent<Kelp>() != null)
                {
                    holdingKelp = true;
                }
            }
            _worldObject.interactable = holdingKelp;
            _worldObject.interactMessage = holdingKelp ? "Plant Kelp" : "";
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

        // Handle growth progress accumulation
        if (_isGrowing && _currentPlant != null)
        {
            _progress += Time.deltaTime / growthDuration;
            if (_progress >= 1f)
            {
                _progress = 1f;
                _isGrowing = false;
                _isMature = true;
            }
            _currentPlant.SetGrowthScale(_progress);
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

    // Handles planting a carried Kelp plant into this pot
    public void PlantKelp(WorldObject kelpWo)
    {
        if (!CanPlant()) return;

        Kelp kelp = kelpWo.GetComponent<Kelp>();
        if (kelp == null) return;

        // Parent the actual carried Kelp object to the spot
        Transform spot = plantSpotEmpty != null ? plantSpotEmpty : transform;
        
        _currentPlant = kelp;
        _progress = 0f;
        _isGrowing = true;
        _isMature = false;

        // Let the plant align and disable its physics/colliders
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

        if (InteractionSystem.Instance != null)
        {
            InteractionSystem.Instance.ShowInfoMessage("Kelp Planted!");
        }
    }

    // Called on RMB interaction
    private void OnInteract(GameObject actor)
    {
        // Case 1: Empty pot & holding Kelp plant -> Plant it
        if (CanPlant())
        {
            if (InteractionSystem.Instance != null)
            {
                WorldObject carried = InteractionSystem.Instance.CarriedWorldObject;
                if (carried != null && carried.GetComponent<Kelp>() != null)
                {
                    PlantKelp(carried);
                    // Consume the carried object from the player's hands since it is now parented to the pot
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

        // Harvest all leaves, spawning 5 KelpLeaf items
        _currentPlant.HarvestLeaves();

        // Destroy the fully grown plant from the pot to free the pot
        Destroy(_currentPlant.gameObject);

        // Reset pot state to empty and ready to be planted again
        _currentPlant = null;
        _progress = 0f;
        _isGrowing = false;
        _isMature = false;

        if (InteractionSystem.Instance != null)
        {
            InteractionSystem.Instance.ShowInfoMessage("Harvested 5 Leaves!");
        }
    }
}
