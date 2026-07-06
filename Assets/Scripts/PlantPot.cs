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
    [SerializeField, Tooltip("Seconds between carried-item checks for empty pots.")]
    private float emptyPotCheckInterval = 0.15f;
    [SerializeField, Tooltip("Seconds between planted kelp growth visual updates.")]
    private float growthVisualUpdateInterval = 0.1f;

    private WorldObject _worldObject;
    private Kelp _currentPlant;
    private float _progress = 0f;
    private bool _isGrowing = false;
    private bool _isMature = false;
    private float _nextEmptyPotCheckTime;
    private float _nextGrowthVisualTime;
    private bool _lastInteractable;
    private string _lastInteractMessage;
    private bool _hasAppliedWorldObjectState;
    private static WorldObject s_cachedCarriedObject;
    private static bool s_cachedCarriedObjectIsPlantable;

    void Awake()
    {
        _worldObject = GetComponent<WorldObject>();
        _worldObject.onInteract.AddListener(OnInteract);
        
        // Initial setup
        SetWorldObjectState(false, "Plant Kelp"); // Only interactable when holding Kelp plant initially
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
            if (Time.time >= _nextEmptyPotCheckTime)
            {
                _nextEmptyPotCheckTime = Time.time + Mathf.Max(0.01f, emptyPotCheckInterval);

                bool holdingKelp = false;
                if (InteractionSystem.Instance != null)
                {
                    WorldObject carried = InteractionSystem.Instance.CarriedWorldObject;
                    holdingKelp = IsPlantableCarriedObject(carried);
                }
                SetWorldObjectState(holdingKelp, holdingKelp ? "Plant Kelp" : "");
            }
        }
        else
        {
            // Pot with plant is always interactable (either to check progress or harvest)
            if (_isMature)
            {
                SetWorldObjectState(true, "Harvest Leaves");
            }
            else
            {
                SetWorldObjectState(true, "Check Growth Progress");
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

            if (Time.time >= _nextGrowthVisualTime || _isMature)
            {
                _nextGrowthVisualTime = Time.time + Mathf.Max(0.01f, growthVisualUpdateInterval);
                _currentPlant.SetGrowthScale(_progress);
            }
        }
    }

    private void SetWorldObjectState(bool interactable, string message)
    {
        if (_worldObject == null)
        {
            return;
        }

        if (!_hasAppliedWorldObjectState || _lastInteractable != interactable)
        {
            _worldObject.interactable = interactable;
            _lastInteractable = interactable;
        }

        if (!_hasAppliedWorldObjectState || _lastInteractMessage != message)
        {
            _worldObject.interactMessage = message;
            _lastInteractMessage = message;
        }

        _hasAppliedWorldObjectState = true;
    }

    private static bool IsPlantableCarriedObject(WorldObject carried)
    {
        if (carried == null)
        {
            s_cachedCarriedObject = null;
            s_cachedCarriedObjectIsPlantable = false;
            return false;
        }

        if (carried == s_cachedCarriedObject)
        {
            return s_cachedCarriedObjectIsPlantable;
        }

        s_cachedCarriedObject = carried;
        s_cachedCarriedObjectIsPlantable = carried.GetComponent<Kelp>() != null || carried.GetComponent<KelpLeaf>() != null;
        return s_cachedCarriedObjectIsPlantable;
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
        _nextGrowthVisualTime = 0f;

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
                if (carried != null)
                {
                    if (carried.GetComponent<Kelp>() != null)
                    {
                        PlantKelp(carried);
                        // Consume the carried object from the player's hands since it is now parented to the pot
                        InteractionSystem.Instance.ConsumeCarriedObjectSilently();
                        InteractionSystem.Instance.ClearPrompts();
                    }
                    else if (carried.GetComponent<KelpLeaf>() != null)
                    {
                        // Plant from leaf
                        InteractionSystem.Instance.ConsumeCarriedObjectSilently();
                        if (Application.isPlaying) Destroy(carried.gameObject);
                        else UnityEngine.Object.DestroyImmediate(carried.gameObject);

                        GameObject prefabToSpawn = kelpPrefab;
                        if (prefabToSpawn == null || prefabToSpawn.GetComponent<Kelp>() == null)
                        {
#if UNITY_EDITOR
                            prefabToSpawn = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Kelp.prefab");
#endif
                        }

                        if (prefabToSpawn != null && prefabToSpawn.GetComponent<Kelp>() != null)
                        {
                            GameObject newKelpObj = Instantiate(prefabToSpawn);
                            WorldObject newKelpWo = newKelpObj.GetComponent<WorldObject>();
                            PlantKelp(newKelpWo);
                        }
                        else
                        {
                            Debug.LogError("PlantPot: Valid Kelp prefab (with Kelp script) is missing! Cannot plant from leaf.");
                        }
                        InteractionSystem.Instance.ClearPrompts();
                    }
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

        // Stem stays intact, growth is retained

        if (_worldObject != null)
        {
            SetWorldObjectState(false, "");
        }
    }
}
