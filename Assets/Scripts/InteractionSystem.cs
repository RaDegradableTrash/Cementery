using TMPro;
using UnityEngine;

/// <summary>
/// Attach to the Player. Handles all three interaction modes defined on WorldObject:
///
///   [E]  Interact  — fires WorldObject.onInteract (door, vending machine…)
///   [E]  Collect   — fires WorldObject.onCollect then destroys the object
///                    (Interact takes priority when both flags are true)
///   [F]  Carry     — picks up / drops a carryable object; object smoothly follows
///                    a hold point in front of the camera
///
/// Requires:
///   • Player has a Camera assigned to the "playerCamera" field (drag your Main Camera in).
///   • WorldObject components on scene objects.
/// </summary>
public class InteractionSystem : MonoBehaviour
{
    // ── Settings ─────────────────────────────────────────────────────────────
    [Header("References")]
    [SerializeField] private Camera playerCamera;

    [Header("Detection")]
    [SerializeField] private float interactRange = 3f;
    [SerializeField] private LayerMask interactMask = ~0;

    [Header("Keys")]
    [SerializeField] private KeyCode interactKey = KeyCode.E;
    [SerializeField] private KeyCode carryKey    = KeyCode.F;

    [Header("Carry Settings")]
    [Tooltip("Distance in front of the camera at which carried objects are held.")]
    [SerializeField] private float holdDistance = 2f;
    [Tooltip("How quickly carried objects lerp to the hold position.")]
    [SerializeField] private float holdLerpSpeed = 15f;
    [Tooltip("Max distance the held object may stray before it is force-dropped.")]
    [SerializeField] private float maxHoldDistance = 4f;

    [Header("UI")]
    [Tooltip("Optional TextMeshProUGUI element for displaying action prompts.")]
    [SerializeField] private TextMeshProUGUI promptLabel;

    // ── Runtime State ─────────────────────────────────────────────────────────
    private WorldObject _lookedAt;
    private WorldObject _carried;
    private Rigidbody   _carriedRb;
    private bool        _rbWasKinematic;
    private bool        _rbHadGravity;

    // ── Lifecycle ─────────────────────────────────────────────────────────────
    void Awake()
    {
        if (playerCamera == null)
            playerCamera = Camera.main;
    }

    void Update()
    {
        ScanForWorldObject();
        HandleInput();
        UpdatePrompt();
    }

    void FixedUpdate()
    {
        MoveCarriedObject();
    }

    // ── Scan ──────────────────────────────────────────────────────────────────
    void ScanForWorldObject()
    {
        if (_carried != null) { _lookedAt = null; return; }

        Ray ray = new Ray(playerCamera.transform.position, playerCamera.transform.forward);
        _lookedAt = Physics.Raycast(ray, out RaycastHit hit, interactRange, interactMask, QueryTriggerInteraction.Ignore)
            ? hit.collider.GetComponentInParent<WorldObject>()
            : null;
    }

    // ── Input ─────────────────────────────────────────────────────────────────
    void HandleInput()
    {
        if (Input.GetKeyDown(interactKey) && _lookedAt != null)
        {
            if (_lookedAt.interactable)
                _lookedAt.TriggerInteract(gameObject);
            else if (_lookedAt.collectable)
                Collect(_lookedAt);
        }

        if (Input.GetKeyDown(carryKey))
        {
            if (_carried != null)
                Drop();
            else if (_lookedAt != null && _lookedAt.carryable)
                PickUp(_lookedAt);
        }
    }

    // ── Collect ───────────────────────────────────────────────────────────────
    void Collect(WorldObject obj)
    {
        obj.TriggerCollect(gameObject);
        _lookedAt = null;
        Destroy(obj.gameObject);
    }

    // ── Carry ─────────────────────────────────────────────────────────────────
    void PickUp(WorldObject obj)
    {
        _carried   = obj;
        _carriedRb = obj.GetComponent<Rigidbody>();

        if (_carriedRb != null)
        {
            _rbWasKinematic        = _carriedRb.isKinematic;
            _rbHadGravity          = _carriedRb.useGravity;
            _carriedRb.isKinematic = true;
            _carriedRb.useGravity  = false;
        }

        _carried.TriggerPickUp(gameObject);
    }

    void Drop()
    {
        if (_carried == null) return;

        if (_carriedRb != null)
        {
            _carriedRb.isKinematic = _rbWasKinematic;
            _carriedRb.useGravity  = _rbHadGravity;
            _carriedRb = null;
        }

        _carried.TriggerDrop(gameObject);
        _carried = null;
    }

    void MoveCarriedObject()
    {
        if (_carried == null) return;

        Vector3 holdPos = playerCamera.transform.position
                        + playerCamera.transform.forward * holdDistance;

        if (Vector3.Distance(_carried.transform.position, holdPos) > maxHoldDistance)
        {
            Drop();
            return;
        }

        if (_carriedRb != null)
            _carriedRb.MovePosition(Vector3.Lerp(
                _carried.transform.position, holdPos, holdLerpSpeed * Time.fixedDeltaTime));
        else
            _carried.transform.position = Vector3.Lerp(
                _carried.transform.position, holdPos, holdLerpSpeed * Time.fixedDeltaTime);
    }

    // ── Prompt ────────────────────────────────────────────────────────────────
    void UpdatePrompt()
    {
        if (promptLabel == null) return;
        string text    = BuildPromptText();
        bool   hasText = !string.IsNullOrEmpty(text);
        promptLabel.gameObject.SetActive(hasText);
        if (hasText) promptLabel.text = text;
    }

    string BuildPromptText()
    {
        if (_carried != null)
            return $"[ {carryKey} ]  {_carried.dropPrompt}";

        if (_lookedAt == null) return null;

        string eLine = _lookedAt.interactable ? $"[ {interactKey} ]  {_lookedAt.interactPrompt}"
                     : _lookedAt.collectable  ? $"[ {interactKey} ]  {_lookedAt.collectPrompt}"
                     : null;

        string fLine = _lookedAt.carryable ? $"[ {carryKey} ]  {_lookedAt.carryPrompt}" : null;

        if (eLine != null && fLine != null) return $"{eLine}\n{fLine}";
        return eLine ?? fLine;
    }
}
