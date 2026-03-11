using System.Linq;
using TMPro;
using UnityEngine;

/// <summary>
/// Attach to the Player. Handles all three interaction modes defined on WorldObject.
///
///   [F]          Interact  — fires WorldObject.onInteract  (interactable == true)
///   [Hold LMB]   Carry     — hold left mouse to carry;  release to drop  (carryable == true)
///   [RMB]        Collect   — right-click to collect and destroy  (collectable == true)
///                            Works whether or not LMB is held.
///
/// Detection: a single raycast fires from the camera centre each frame.
/// Only objects with at least one flag set AND hit by the ray trigger their behaviour.
///
/// Requires:
///   • Camera assigned to "playerCamera" (drag your Main Camera in the Inspector).
///   • WorldObject component on scene objects.
/// </summary>
public class InteractionSystem : MonoBehaviour
{
    // ── Settings ─────────────────────────────────────────────────────────────
    [Header("References")]
    [SerializeField] private Camera playerCamera;

    [Header("Detection")]
    [SerializeField] private float     interactRange = 3f;
    [SerializeField] private LayerMask interactMask  = ~0;

    [Header("Carry Settings")]
    [Tooltip("Distance in front of the camera where the carried object is held.")]
    [SerializeField] private float holdDistance    = 2f;
    [Tooltip("How quickly the carried object lerps toward the hold point.")]
    [SerializeField] private float holdLerpSpeed   = 15f;
    [Tooltip("If the held object strays further than this, it is automatically dropped.")]
    [SerializeField] private float maxHoldDistance = 4f;

    [Header("UI")]
    [Tooltip("Optional TextMeshProUGUI that shows context-sensitive action prompts.")]
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

    // ── Scan (camera-forward raycast) ─────────────────────────────────────────
    void ScanForWorldObject()
    {
        // While carrying, the crosshair is occupied — no new target
        if (_carried != null) { _lookedAt = null; return; }

        Ray ray = new Ray(playerCamera.transform.position, playerCamera.transform.forward);
        _lookedAt = Physics.Raycast(ray, out RaycastHit hit, interactRange, interactMask,
                        QueryTriggerInteraction.Ignore)
            ? hit.collider.GetComponentInParent<WorldObject>()
            : null;
    }

    // ── Input ─────────────────────────────────────────────────────────────────
    void HandleInput()
    {
        // [F] — Interact
        if (Input.GetKeyDown(KeyCode.F) && _lookedAt != null && _lookedAt.interactable)
            _lookedAt.TriggerInteract(gameObject);

        // [Hold LMB] — Start carrying on press
        if (Input.GetMouseButtonDown(0) && _carried == null
            && _lookedAt != null && _lookedAt.carryable)
            PickUp(_lookedAt);

        // [Release LMB] — Drop carried object
        if (Input.GetMouseButtonUp(0) && _carried != null)
            Drop();

        // [RMB] — Collect (works regardless of LMB state)
        if (Input.GetMouseButtonDown(1) && _lookedAt != null && _lookedAt.collectable)
            Collect(_lookedAt);
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
        // Carrying: only show the drop hint
        if (_carried != null)
            return $"[ Release LMB ]  {_carried.dropPrompt}";

        if (_lookedAt == null) return null;

        string fLine   = _lookedAt.interactable ? $"[ F ]  {_lookedAt.interactPrompt}"   : null;
        string lmbLine = _lookedAt.carryable    ? $"[ Hold LMB ]  {_lookedAt.carryPrompt}" : null;
        string rmbLine = _lookedAt.collectable  ? $"[ RMB ]  {_lookedAt.collectPrompt}"   : null;

        // Stack all applicable hints, one per line
        return string.Join("\n", new[] { fLine, lmbLine, rmbLine }
            .Where(s => s != null));
    }
}
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
