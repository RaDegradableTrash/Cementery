using System.Collections;
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
    [Tooltip("Distance in front of the camera where the object snaps on pickup.")]
    [SerializeField] private float holdDistance       = 2f;
    [Tooltip("Downward offset from camera-forward so the object sits below screen centre. Negative = lower.")]
    [SerializeField] private float holdVerticalOffset = -0.5f;

    [Header("UI — assign the three TMP children of FacingNotificationEmpty")]
    [SerializeField] private TextMeshProUGUI interactLabel;
    [SerializeField] private TextMeshProUGUI carryLabel;
    [SerializeField] private TextMeshProUGUI collectLabel;

    [Header("UI — Info Label")]
    [Tooltip("Shared TMP that shows per-object messages on interact and collect.")]
    [SerializeField] private TextMeshProUGUI infoLabel;
    [SerializeField] private float           infoDisplayDuration = 2.5f;

    // ── Runtime State ─────────────────────────────────────────────────────────
    private WorldObject _lookedAt;
    private WorldObject _carried;
    private Rigidbody   _carriedRb;
    private bool        _rbWasKinematic;
    private bool        _rbHadGravity;
    private float       _rbWasDrag;
    private float       _rbWasAngularDrag;
    private Transform   _carriedOriginalParent;
    private Coroutine   _hideInfoCo;

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
        {
            _lookedAt.TriggerInteract(gameObject);
            _lookedAt.PlayInteractAnim();
            ShowInfo(_lookedAt.interactMessage);
        }

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
        ShowInfo(obj.collectMessage);
        obj.TriggerCollect(gameObject);
        _lookedAt = null;
        obj.PlayCollectAnim(() => Destroy(obj.gameObject));
    }

    // ── Carry ─────────────────────────────────────────────────────────────────
    void PickUp(WorldObject obj)
    {
        _carried              = obj;
        _carriedRb            = obj.GetComponent<Rigidbody>();
        _carriedOriginalParent = obj.transform.parent;

        if (_carriedRb != null)
        {
            _rbWasKinematic   = _carriedRb.isKinematic;
            _rbHadGravity     = _carriedRb.useGravity;
            _rbWasDrag        = _carriedRb.drag;
            _rbWasAngularDrag = _carriedRb.angularDrag;

            _carriedRb.isKinematic    = true;   // transform-driven while carried
            _carriedRb.useGravity     = false;
            _carriedRb.velocity       = Vector3.zero;
            _carriedRb.angularVelocity = Vector3.zero;
        }

        // Snap to hold point, then parent so the object follows the player
        obj.transform.position = HoldPosition();
        obj.transform.SetParent(transform, worldPositionStays: true);

        _carried.TriggerPickUp(gameObject);
    }

    void Drop()
    {
        if (_carried == null) return;

        // Detach from player, restoring original parent
        _carried.transform.SetParent(_carriedOriginalParent, worldPositionStays: true);

        if (_carriedRb != null)
        {
            _carriedRb.isKinematic = _rbWasKinematic;
            _carriedRb.useGravity  = _rbHadGravity;
            _carriedRb.drag        = _rbWasDrag;
            _carriedRb.angularDrag = _rbWasAngularDrag;
            _carriedRb = null;
        }

        _carried.TriggerDrop(gameObject);
        _carried = null;
    }

    /// <summary>
    /// Returns the world-space hold point: camera position + forward-down direction * holdDistance.
    /// </summary>
    Vector3 HoldPosition()
    {
        Vector3 camPos  = playerCamera.transform.position;
        Vector3 camFwd  = playerCamera.transform.forward;
        Vector3 holdDir = (camFwd + Vector3.up * holdVerticalOffset).normalized;
        return camPos + holdDir * holdDistance;
    }


    // ── Prompt ────────────────────────────────────────────────────────────────
    void UpdatePrompt()
    {
        if (_carried != null)
        {
            SetActive(interactLabel,  false);
            SetActive(carryLabel,     true);
            SetActive(collectLabel,   false);
            return;
        }

        SetActive(interactLabel, _lookedAt != null && _lookedAt.interactable);
        SetActive(carryLabel,    _lookedAt != null && _lookedAt.carryable);
        SetActive(collectLabel,  _lookedAt != null && _lookedAt.collectable);
    }

    void SetActive(TextMeshProUGUI label, bool active)
    {
        if (label != null) label.gameObject.SetActive(active);
    }

    // ── Info Label ────────────────────────────────────────────────────────────
    void ShowInfo(string message)
    {
        if (infoLabel == null || string.IsNullOrEmpty(message)) return;
        if (_hideInfoCo != null) StopCoroutine(_hideInfoCo);
        infoLabel.text = message;
        infoLabel.gameObject.SetActive(true);
        _hideInfoCo = StartCoroutine(HideInfoCo());
    }

    IEnumerator HideInfoCo()
    {
        yield return new WaitForSeconds(infoDisplayDuration);
        if (infoLabel != null)
            infoLabel.gameObject.SetActive(false);
        _hideInfoCo = null;
    }
}
