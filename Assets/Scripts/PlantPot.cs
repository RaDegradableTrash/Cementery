using System.Collections;
using UnityEngine;

[RequireComponent(typeof(WorldObject))]
public class PlantPot : MonoBehaviour
{
    [Header("References")]
    [Tooltip("The spot where the kelp will be positioned and grow.")]
    public Transform plantSpotEmpty;
    [Tooltip("The Kelp prefab instantiated during harvest.")]
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
        _worldObject.interactable = false; // Only interactable when holding kelp initially
    }

    void Update()
    {
        // Enforce the requirement: empty pot interactable ONLY when holding Kelp
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
                _worldObject.interactMessage = "Harvest Kelp";
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

    // Handles planting a carried Kelp into this pot
    public void PlantKelp(WorldObject kelpWo)
    {
        if (!CanPlant()) return;

        Kelp kelp = kelpWo.GetComponent<Kelp>();
        if (kelp == null) return;

        _currentPlant = kelp;
        _progress = 0f;
        _isGrowing = true;
        _isMature = false;

        // Position Kelp at the plant spot empty anchor
        Transform spot = plantSpotEmpty != null ? plantSpotEmpty : transform;
        kelp.OnPlanted(spot);
        kelp.SetGrowthScale(_progress);

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
        // Case 1: Empty pot & holding kelp -> Plant it
        if (CanPlant())
        {
            if (InteractionSystem.Instance != null)
            {
                WorldObject carried = InteractionSystem.Instance.CarriedWorldObject;
                if (carried != null && carried.GetComponent<Kelp>() != null)
                {
                    PlantKelp(carried);
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

        // Capture original scale of the kelp before destroying it
        Vector3 originalScale = _currentPlant.InitialWorldScale;

        // Destroy the growing plant object
        Destroy(_currentPlant.gameObject);
        _currentPlant = null;
        _isMature = false;
        _isGrowing = false;
        _progress = 0f;

        // Spawn 5 kelps popping out
        if (kelpPrefab != null)
        {
            Transform spawnRoot = plantSpotEmpty != null ? plantSpotEmpty : transform;
            for (int i = 0; i < 5; i++)
            {
                // Instantiate slightly above the pot
                Vector3 spawnPos = spawnRoot.position + Vector3.up * 0.3f;
                GameObject spawned = Instantiate(kelpPrefab, spawnPos, Quaternion.identity);
                spawned.transform.localScale = originalScale;
                
                // Ensure physics and components are enabled
                Rigidbody spawnedRb = spawned.GetComponent<Rigidbody>();
                Kelp spawnedKelp = spawned.GetComponent<Kelp>();
                WorldObject spawnedWo = spawned.GetComponent<WorldObject>();

                // Ignore collisions between Pot and newly spawned Kelp
                Collider[] potColliders = GetComponentsInChildren<Collider>();
                Collider[] spawnedColliders = spawned.GetComponentsInChildren<Collider>();
                foreach (var potCol in potColliders)
                {
                    foreach (var kelpCol in spawnedColliders)
                    {
                        if (potCol != null && kelpCol != null)
                        {
                            Physics.IgnoreCollision(potCol, kelpCol, true);
                        }
                    }
                }

                if (spawnedWo != null)
                {
                    spawnedWo.interactable = false;
                    spawnedWo.carryable = true;
                }

                // Force extraction state so they behave as wild-harvested items lying on the ground
                if (spawnedKelp != null)
                {
                    // Mark as no longer wild so we can carry them directly, and setup colliders
                    System.Reflection.FieldInfo isWildField = typeof(Kelp).GetField("_isWild", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                    if (isWildField != null) isWildField.SetValue(spawnedKelp, false);
                }

                if (spawnedRb != null)
                {
                    spawnedRb.isKinematic = false;
                    spawnedRb.useGravity = true;
                    
                    // Apply outward explosion-like forces
                    Vector3 forceDir = new Vector3(
                        Random.Range(-0.8f, 0.8f),
                        Random.Range(1.2f, 1.8f),
                        Random.Range(-0.8f, 0.8f)
                    ).normalized;
                    
                    float forceMagnitude = Random.Range(4f, 6f);
                    spawnedRb.AddForce(forceDir * forceMagnitude, ForceMode.Impulse);
                    spawnedRb.AddTorque(new Vector3(Random.Range(-3f, 3f), Random.Range(-3f, 3f), Random.Range(-3f, 3f)), ForceMode.Impulse);
                }
            }

            if (InteractionSystem.Instance != null)
            {
                InteractionSystem.Instance.ShowInfoMessage("Harvested 5 Kelp!");
            }
        }
    }
}
