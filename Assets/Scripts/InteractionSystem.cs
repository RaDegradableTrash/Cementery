using System.Collections;
using TMPro;
using UnityEngine;

/// <summary>
/// Attach to the Player. Three interaction modes:
///
///   [F]        Interact  — fires WorldObject.onInteract
///   [Hold LMB] Carry     — parents object to camera; LateUpdate lerps localPosition to holdLocalPos
///   [RMB]      Collect   — fires onCollect, plays pop anim, destroys
///
/// Carry technique (Amnesia / Myst style):
///   Object is made kinematic and parented to the camera. LateUpdate lerps localPosition every
///   frame — no physics fight, no gravity, perfect position sync with player view.
/// </summary>
public class InteractionSystem : MonoBehaviour
{
    // ── References ────────────────────────────────────────────────────────────
    [Header("References")]
    [SerializeField] private Camera playerCamera;

    // ── Detection ─────────────────────────────────────────────────────────────
    [Header("Detection")]
    [SerializeField] private float     interactRange = 3f;
    [SerializeField] private LayerMask interactMask  = ~0;

    // ── Carry ─────────────────────────────────────────────────────────────────
    [Header("Carry")]
    [Tooltip("How far in front of the player (horizontal) the object is held.")]
    [SerializeField] private float holdDistance   = 1.6f;
    [Tooltip("Height offset relative to the camera position (negative = lower).")]
    [SerializeField] private float holdHeightOffset = -0.25f;
    [Tooltip("How quickly the object glides to the hold position (higher = snappier).")]
    [SerializeField] private float holdLerpSpeed  = 14f;
    [Tooltip("How quickly the carried object's rotation is dampened to neutral.")]
    [SerializeField] private float holdRotDamp       = 12f;
    [Tooltip("SphereCast radius for wall detection when holding an object without a Rigidbody.")]
    [SerializeField] private float holdCollisionRadius = 0.2f;

    // ── UI ────────────────────────────────────────────────────────────────────
    [Header("UI Prompts")]
    [SerializeField] private TextMeshProUGUI interactLabel;
    [SerializeField] private TextMeshProUGUI carryLabel;
    [SerializeField] private TextMeshProUGUI collectLabel;

    [Header("UI Info Label")]
    [SerializeField] private TextMeshProUGUI infoLabel;
    [SerializeField] private float           infoDisplayDuration = 2.5f;

    // ── State ─────────────────────────────────────────────────────────────────
    private WorldObject _lookedAt;

    private WorldObject _carried;
    private Rigidbody   _carriedRb;
    private Collider[]  _carriedCols;   // carried object's colliders (for IgnoreCollision restore)
    private Collider[]  _playerCols;    // player's own colliders  (for IgnoreCollision restore)
    private bool        _rbWasKinematic;
    private bool        _rbHadGravity;
    private RigidbodyInterpolation _rbInterpolation;
    private Transform   _carriedOrigParent;

    private Coroutine   _hideInfoCo;

    // ── Lifecycle ─────────────────────────────────────────────────────────────
    void Awake()
    {
        if (playerCamera == null)
            playerCamera = Camera.main;
    }

    void Update()
    {
        Scan();
        HandleInput();
        UpdatePrompt();
        if (_carried != null)
            DriveCarried();
    }

    // Carry driver — runs every Update for both Rigidbody and non-Rigidbody objects.
    //
    // Approach: direct transform.position assignment (so the object is always free to
    // glide toward the ideal hold point on EVERY axis), followed by a depenetration loop
    // using Physics.ComputePenetration.  This is the same technique Unity's CharacterController
    // uses internally: move first, then push out of any overlaps.
    //
    // Why not MovePosition? MovePosition's sweep stops the object when the *sweep direction*
    // hits a surface — meaning lateral / vertical recovery attempts are also blocked the
    // moment the object touches anything. ComputePenetration solves a different problem:
    // it resolves overlaps that have already occurred, so we get full freedom of movement
    // with zero penetration.
    void DriveCarried()
    {
        Transform cam    = playerCamera.transform;
        Vector3   target = cam.position
                         + cam.forward * holdDistance
                         + cam.up      * holdHeightOffset;

        float pt = 1f - Mathf.Exp(-holdLerpSpeed * Time.deltaTime);
        float rt = 1f - Mathf.Exp(-holdRotDamp   * Time.deltaTime);

        // ── 1. Lerp toward ideal target (unrestricted on all axes) ─────────────
        _carried.transform.position = Vector3.Lerp(_carried.transform.position, target, pt);
        _carried.transform.rotation = Quaternion.Slerp(_carried.transform.rotation,
                                                        Quaternion.identity, rt);

        // ── 2. Depenetration: push the object out of any overlapping colliders ──
        // Collect all colliders on the carried object (cached; rebuilt if null).
        if (_carriedCols == null || _carriedCols.Length == 0)
            _carriedCols = _carried.GetComponentsInChildren<Collider>();

        const int maxIter = 5;
        for (int iter = 0; iter < maxIter; iter++)
        {
            bool anyOverlap = false;
            foreach (Collider cc in _carriedCols)
            {
                if (cc == null || !cc.enabled) continue;

                // Find all colliders in the scene overlapping this one.
                // Use a generous bounds check: OverlapBox around the collider's bounds.
                Bounds   b           = cc.bounds;
                Vector3  halfExtents = b.extents * 1.1f;          // slight margin
                Collider[] neighbours = Physics.OverlapBox(
                    b.center, halfExtents, Quaternion.identity,
                    interactMask, QueryTriggerInteraction.Ignore);

                foreach (Collider nb in neighbours)
                {
                    if (nb == null || nb == cc) continue;
                    // Skip the player's own colliders.
                    bool isPlayerCol = false;
                    if (_playerCols != null)
                        foreach (var pc in _playerCols)
                            if (pc == nb) { isPlayerCol = true; break; }
                    if (isPlayerCol) continue;
                    // Skip other colliders belonging to the carried object itself.
                    bool isSelf = false;
                    foreach (var sc in _carriedCols)
                        if (sc == nb) { isSelf = true; break; }
                    if (isSelf) continue;

                    if (Physics.ComputePenetration(
                            cc,  cc.transform.position,  cc.transform.rotation,
                            nb,  nb.transform.position,  nb.transform.rotation,
                            out Vector3 dir, out float dist))
                    {
                        // Push the whole carried object out of the overlap.
                        _carried.transform.position += dir * (dist + 0.001f);
                        anyOverlap = true;
                    }
                }
            }
            if (!anyOverlap) break;
        }

        // Keep the Rigidbody in sync so it doesn't fight us next frame.
        if (_carriedRb != null)
        {
            _carriedRb.position        = _carried.transform.position;
            _carriedRb.rotation        = _carried.transform.rotation;
            _carriedRb.velocity        = Vector3.zero;
            _carriedRb.angularVelocity = Vector3.zero;
        }
    }

    // ── Scan ──────────────────────────────────────────────────────────────────
    void Scan()
    {
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
        // [F] Interact
        if (Input.GetKeyDown(KeyCode.F) && _lookedAt != null && _lookedAt.interactable)
        {
            _lookedAt.TriggerInteract(gameObject);
            _lookedAt.PlayInteractAnim();
            ShowInfo(_lookedAt.interactMessage);
        }

        // [LMB down] Start carry
        if (Input.GetMouseButtonDown(0) && _carried == null
            && _lookedAt != null && _lookedAt.carryable)
            PickUp(_lookedAt);

        // [LMB up] Drop
        if (Input.GetMouseButtonUp(0) && _carried != null)
            Drop();

        // [RMB] Collect
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

    // ── Carry: Pick Up ────────────────────────────────────────────────────────
    void PickUp(WorldObject obj)
    {
        obj.CancelAnims();

        _carried           = obj;
        _carriedOrigParent = obj.transform.parent;
        _carriedRb         = obj.GetComponent<Rigidbody>();

        if (_carriedRb != null)
        {
            _rbWasKinematic  = _carriedRb.isKinematic;
            _rbHadGravity    = _carriedRb.useGravity;
            _rbInterpolation = _carriedRb.interpolation;

            // isKinematic = true enables MovePosition sweep-testing against world geometry.
            // detectCollisions is deliberately left at its default (true) so the sweep
            // actually stops at walls and floors — never touch detectCollisions.
            _carriedRb.isKinematic   = true;
            _carriedRb.useGravity    = false;
            _carriedRb.interpolation = RigidbodyInterpolation.Interpolate;
            _carriedRb.velocity        = Vector3.zero;
            _carriedRb.angularVelocity = Vector3.zero;
        }

        // Ignore collisions between the player and the carried object so the player's
        // body does not push it away. World geometry stays active — it will stop the object.
        _playerCols  = GetComponentsInChildren<Collider>();
        _carriedCols = obj.GetComponentsInChildren<Collider>();
        foreach (var pc in _playerCols)
            foreach (var cc in _carriedCols)
                Physics.IgnoreCollision(pc, cc, true);

        _carried.TriggerPickUp(gameObject);
    }

    // ── Carry: Drop ───────────────────────────────────────────────────────────
    void Drop()
    {
        if (_carried == null) return;

        // Restore collision between player and dropped object.
        if (_playerCols != null && _carriedCols != null)
            foreach (var pc in _playerCols)
                foreach (var cc in _carriedCols)
                    if (pc != null && cc != null)
                        Physics.IgnoreCollision(pc, cc, false);
        _playerCols  = null;
        _carriedCols = null;

        if (_carriedRb != null)
        {
            _carriedRb.isKinematic   = _rbWasKinematic;
            _carriedRb.useGravity    = _rbHadGravity;
            _carriedRb.interpolation = _rbInterpolation;
            _carriedRb.velocity        = Vector3.zero;
            _carriedRb.angularVelocity = Vector3.zero;
            _carriedRb = null;
        }

        _carried.TriggerDrop(gameObject);
        _carried = null;
    }

    // ── Prompt ────────────────────────────────────────────────────────────────
    void UpdatePrompt()
    {
        if (_carried != null)
        {
            SetLabel(interactLabel, false);
            SetLabel(carryLabel,    true);
            SetLabel(collectLabel,  false);
            return;
        }

        SetLabel(interactLabel, _lookedAt != null && _lookedAt.interactable);
        SetLabel(carryLabel,    _lookedAt != null && _lookedAt.carryable);
        SetLabel(collectLabel,  _lookedAt != null && _lookedAt.collectable);
    }

    void SetLabel(TextMeshProUGUI label, bool active)
    {
        if (label != null) label.gameObject.SetActive(active);
    }

    // ── Info ──────────────────────────────────────────────────────────────────
    void ShowInfo(string message)
    {
        if (infoLabel == null || string.IsNullOrEmpty(message)) return;
        if (_hideInfoCo != null) StopCoroutine(_hideInfoCo);
        infoLabel.text = message;
        infoLabel.gameObject.SetActive(true);
        _hideInfoCo = StartCoroutine(HideInfoAfter(infoDisplayDuration));
    }

    IEnumerator HideInfoAfter(float delay)
    {
        yield return new WaitForSeconds(delay);
        if (infoLabel != null) infoLabel.gameObject.SetActive(false);
        _hideInfoCo = null;
    }
}
