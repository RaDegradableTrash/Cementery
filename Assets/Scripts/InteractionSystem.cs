using System.Collections;
using TMPro;
using UnityEngine;

/// <summary>
/// Player interaction system:
/// - [F] interact WorldObject
/// - [Hold LMB] carry WorldObject (must be carryable + Rigidbody)
/// - [RMB] collect WorldObject
///
/// Simplified carry:
/// - keeps collisions
/// - keeps gravity enabled while carrying
/// - drives object by velocity toward a camera target
/// - yaw-only rotation follow to avoid pitching into ground
/// </summary>
public class InteractionSystem : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private Camera playerCamera;

    [Header("Detection")]
    [SerializeField] private float interactRange = 3f;
    [SerializeField] private float carryPickUpRange = 2f;
    [SerializeField] private LayerMask interactMask = ~0;

    [Header("Carry")]
    [SerializeField] private float carryFollowStrength = 18f;
    [SerializeField] private float carryMaxSpeed = 10f;
    [SerializeField] private float carryTurnStrength = 14f;
    [SerializeField] private float carryDrag = 8f;
    [SerializeField] private float carryAngularDrag = 10f;
    [SerializeField] private float carryOffsetAdaptRate = 10f;
    [SerializeField] private float carryOffsetFullAdaptError = 0.7f;

    [Header("UI Prompts")]
    [SerializeField] private TextMeshProUGUI interactLabel;
    [SerializeField] private TextMeshProUGUI carryLabel;
    [SerializeField] private TextMeshProUGUI collectLabel;

    [Header("UI Info Label")]
    [SerializeField] private TextMeshProUGUI infoLabel;
    [SerializeField] private float infoDisplayDuration = 2.5f;

    private WorldObject _lookedAt;
    private Rigidbody _carryCandidateRb;
    private WorldObject _carryCandidateWo;

    private Transform _carriedTransform;
    private WorldObject _carriedWo;
    private Rigidbody _carriedRb;

    private Collider[] _carriedCols;
    private Collider[] _playerCols;
    private PhysicMaterial[] _carriedOriginalMaterials;
    private PhysicMaterial _carryNoFrictionMaterial;
    private CharacterController _playerCc;

    private bool _rbWasKinematic;
    private bool _rbHadGravity;
    private RigidbodyInterpolation _rbInterpolation;
    private CollisionDetectionMode _rbCollisionDetectionMode;
    private float _rbOriginalDrag;
    private float _rbOriginalAngularDrag;
    private bool _carryPlayerCollisionIgnored;

    private float _carryYawOffset;
    private Quaternion _carryPitchRollOffset = Quaternion.identity;
    private Vector3 _carryRayLocalDir = Vector3.forward;
    private float _carryRayDistance;
    private float _carriedRadius;

    private Coroutine _hideInfoCo;

    void Awake()
    {
        if (playerCamera == null)
            playerCamera = Camera.main;

        _carryNoFrictionMaterial = new PhysicMaterial("Carry_NoFriction_Runtime")
        {
            dynamicFriction = 0f,
            staticFriction = 0f,
            frictionCombine = PhysicMaterialCombine.Minimum,
            bounciness = 0f,
            bounceCombine = PhysicMaterialCombine.Minimum
        };
        _carryNoFrictionMaterial.hideFlags = HideFlags.HideAndDontSave;

        _playerCc = GetComponent<CharacterController>();
    }

    void ApplyCarryNoFriction()
    {
        if (_carriedCols == null || _carryNoFrictionMaterial == null)
            return;

        _carriedOriginalMaterials = new PhysicMaterial[_carriedCols.Length];
        for (int i = 0; i < _carriedCols.Length; i++)
        {
            Collider c = _carriedCols[i];
            if (c == null || c.isTrigger)
                continue;

            _carriedOriginalMaterials[i] = c.sharedMaterial;
            c.sharedMaterial = _carryNoFrictionMaterial;
        }
    }

    void RestoreCarryFriction()
    {
        if (_carriedCols == null || _carriedOriginalMaterials == null)
            return;

        int count = Mathf.Min(_carriedCols.Length, _carriedOriginalMaterials.Length);
        for (int i = 0; i < count; i++)
        {
            Collider c = _carriedCols[i];
            if (c == null || c.isTrigger)
                continue;

            c.sharedMaterial = _carriedOriginalMaterials[i];
        }

        _carriedOriginalMaterials = null;
    }

    void Update()
    {
        Scan();
        HandleInput();
        UpdatePrompt();
    }

    void FixedUpdate()
    {
        if (_carriedRb == null)
            return;

        UpdateAdaptiveCarryOffset(Time.fixedDeltaTime);
        DriveCarry();
    }

    void UpdateAdaptiveCarryOffset(float dt)
    {
        Transform cam = playerCamera != null ? playerCamera.transform : transform;
        Vector3 rayDir = cam.TransformDirection(_carryRayLocalDir);
        if (rayDir.sqrMagnitude < 0.0001f)
            rayDir = cam.forward;
        rayDir.Normalize();

        Vector3 targetWorld = cam.position + rayDir * _carryRayDistance;
        float targetError = Vector3.Distance(_carriedRb.position, targetWorld);

        // If physics/obstacles make target hard to maintain, relax offset toward actual pose.
        float pressure = Mathf.Clamp01(targetError / Mathf.Max(0.01f, carryOffsetFullAdaptError));
        float adaptRate = carryOffsetAdaptRate * (0.25f + pressure * 0.75f);
        float t = 1f - Mathf.Exp(-adaptRate * Mathf.Max(0.0001f, dt));

        float actualDistanceOnRay = Vector3.Dot(_carriedRb.position - cam.position, rayDir);
        actualDistanceOnRay = Mathf.Max(0.05f, actualDistanceOnRay);
        _carryRayDistance = Mathf.Lerp(_carryRayDistance, actualDistanceOnRay, t);
    }

    void DriveCarry()
    {
        Vector3 targetPos = GetCarryTargetPosition();
        Vector3 toTarget = targetPos - _carriedRb.position;

        Vector3 desiredVel = toTarget * carryFollowStrength + GetPlayerVelocity();
        if (desiredVel.magnitude > carryMaxSpeed)
            desiredVel = desiredVel.normalized * carryMaxSpeed;
        _carriedRb.velocity = desiredVel;

        Quaternion targetRot = GetCarryTargetRotationYawOnly();
        Quaternion delta = targetRot * Quaternion.Inverse(_carriedRb.rotation);
        delta.ToAngleAxis(out float angleDeg, out Vector3 axis);
        if (angleDeg > 180f)
            angleDeg -= 360f;

        if (axis.sqrMagnitude > 0.0001f)
            _carriedRb.angularVelocity = axis.normalized * (angleDeg * Mathf.Deg2Rad) * carryTurnStrength;
        else
            _carriedRb.angularVelocity = Vector3.zero;
    }

    Vector3 GetCarryTargetPosition()
    {
        Transform cam = playerCamera != null ? playerCamera.transform : transform;
        Vector3 origin = cam.position;
        Vector3 rayDir = cam.TransformDirection(_carryRayLocalDir);
        if (rayDir.sqrMagnitude < 0.0001f)
            rayDir = cam.forward;
        rayDir.Normalize();

        Vector3 desired = origin + rayDir * _carryRayDistance;

        Vector3 cast = desired - origin;
        float castDist = cast.magnitude;
        if (castDist <= 0.001f)
            return desired;

        Vector3 dir = cast / castDist;
        float castRadius = Mathf.Max(0.05f, _carriedRadius * 0.85f);

        if (Physics.SphereCast(origin, castRadius, dir, out RaycastHit hit, castDist, interactMask, QueryTriggerInteraction.Ignore))
        {
            if (!IsIgnoredCarryHitCollider(hit.collider))
            {
                float safeDist = Mathf.Max(0.15f, hit.distance - castRadius - 0.03f);
                return origin + dir * safeDist;
            }
        }

        return desired;
    }

    Quaternion GetCarryTargetRotationYawOnly()
    {
        Transform cam = playerCamera != null ? playerCamera.transform : transform;
        Vector3 fwd = Vector3.ProjectOnPlane(cam.forward, Vector3.up);
        if (fwd.sqrMagnitude < 0.0001f)
            fwd = Vector3.ProjectOnPlane(transform.forward, Vector3.up);
        if (fwd.sqrMagnitude < 0.0001f)
            fwd = Vector3.forward;

        float yaw = Mathf.Atan2(fwd.x, fwd.z) * Mathf.Rad2Deg;
        Quaternion yawTarget = Quaternion.Euler(0f, yaw + _carryYawOffset, 0f);
        return yawTarget * _carryPitchRollOffset;
    }

    bool IsIgnoredCarryHitCollider(Collider c)
    {
        if (c == null)
            return false;

        if (IsPlayerCollider(c))
            return true;

        if (_carriedCols == null)
            return false;

        for (int i = 0; i < _carriedCols.Length; i++)
            if (_carriedCols[i] == c)
                return true;

        return false;
    }

    float ComputeCarriedRadius()
    {
        if (_carriedCols == null || _carriedCols.Length == 0)
            return 0.2f;

        float radius = 0.2f;
        for (int i = 0; i < _carriedCols.Length; i++)
        {
            Collider c = _carriedCols[i];
            if (c == null || !c.enabled) continue;
            Bounds b = c.bounds;
            radius = Mathf.Max(radius, Mathf.Max(b.extents.x, b.extents.z));
        }

        return radius;
    }

    bool IsPlayerCollider(Collider c)
    {
        if (_playerCc != null && c == _playerCc)
            return true;

        if (_playerCols == null)
            return false;

        for (int i = 0; i < _playerCols.Length; i++)
            if (_playerCols[i] == c)
                return true;

        return false;
    }

    void SetCarryPlayerCollisionIgnored(bool ignored)
    {
        if (_carriedCols == null)
            return;

        if (_playerCc != null)
        {
            for (int i = 0; i < _carriedCols.Length; i++)
            {
                Collider cc = _carriedCols[i];
                if (cc == null) continue;
                Physics.IgnoreCollision(_playerCc, cc, ignored);
            }
        }

        if (_playerCols != null)
        {
            for (int p = 0; p < _playerCols.Length; p++)
            {
                Collider pc = _playerCols[p];
                if (pc == null || pc == _playerCc) continue;
                for (int c = 0; c < _carriedCols.Length; c++)
                {
                    Collider cc = _carriedCols[c];
                    if (cc == null) continue;
                    Physics.IgnoreCollision(pc, cc, ignored);
                }
            }
        }

        _carryPlayerCollisionIgnored = ignored;
    }

    void Scan()
    {
        if (_carriedRb != null)
        {
            _lookedAt = null;
            _carryCandidateRb = null;
            _carryCandidateWo = null;
            return;
        }

        float rayRange = Mathf.Max(interactRange, carryPickUpRange);
        Ray ray = new Ray(playerCamera.transform.position, playerCamera.transform.forward);

        if (!Physics.Raycast(ray, out RaycastHit hit, rayRange, interactMask, QueryTriggerInteraction.Ignore))
        {
            _lookedAt = null;
            _carryCandidateRb = null;
            _carryCandidateWo = null;
            return;
        }

        _lookedAt = hit.collider.GetComponentInParent<WorldObject>();
        _carryCandidateRb = ResolveCarryCandidate(hit, out _carryCandidateWo);
    }

    Rigidbody ResolveCarryCandidate(RaycastHit hit, out WorldObject worldObject)
    {
        worldObject = null;

        Collider c = hit.collider;
        if (c == null)
            return null;

        Rigidbody rb = c.attachedRigidbody;
        if (rb == null)
            rb = c.GetComponentInParent<Rigidbody>();
        if (rb == null)
            return null;

        if (hit.distance > carryPickUpRange)
            return null;

        worldObject = c.GetComponentInParent<WorldObject>();
        if (worldObject == null)
            worldObject = rb.GetComponentInParent<WorldObject>();

        if (worldObject == null || !worldObject.carryable)
            return null;

        return rb;
    }

    bool HasCarryCandidate()
    {
        return _carryCandidateRb != null;
    }

    void HandleInput()
    {
        if (Input.GetKeyDown(KeyCode.F) && _lookedAt != null && _lookedAt.interactable)
        {
            _lookedAt.TriggerInteract(gameObject);
            _lookedAt.PlayInteractAnim();
            ShowInfo(_lookedAt.interactMessage);
        }

        if (Input.GetMouseButtonDown(0) && _carriedRb == null && HasCarryCandidate())
            PickUp(_carryCandidateRb, _carryCandidateWo);

        if (Input.GetMouseButtonUp(0) && _carriedRb != null)
            Drop();

        if (Input.GetMouseButtonDown(1) && _lookedAt != null && _lookedAt.collectable)
            Collect(_lookedAt);
    }

    void Collect(WorldObject obj)
    {
        ShowInfo(obj.collectMessage);
        obj.TriggerCollect(gameObject);
        _lookedAt = null;
        obj.PlayCollectAnim(() => Destroy(obj.gameObject));
    }

    void PickUp(Rigidbody rb, WorldObject wo)
    {
        if (rb == null)
            return;

        if (Vector3.Distance(playerCamera.transform.position, rb.worldCenterOfMass) > carryPickUpRange)
            return;

        wo?.CancelAnims();

        _carriedRb = rb;
        _carriedTransform = rb.transform;
        _carriedWo = wo;

        _rbWasKinematic = _carriedRb.isKinematic;
        _rbHadGravity = _carriedRb.useGravity;
        _rbInterpolation = _carriedRb.interpolation;
        _rbCollisionDetectionMode = _carriedRb.collisionDetectionMode;
        _rbOriginalDrag = _carriedRb.drag;
        _rbOriginalAngularDrag = _carriedRb.angularDrag;

        _playerCols = GetComponentsInChildren<Collider>();
        _carriedCols = rb.GetComponentsInChildren<Collider>();
        _carriedRadius = ComputeCarriedRadius();
        ApplyCarryNoFriction();

        Transform cam = playerCamera != null ? playerCamera.transform : transform;
        Vector3 ray = _carriedTransform.position - cam.position;
        float rayLen = ray.magnitude;
        if (rayLen < 0.05f)
        {
            ray = cam.forward * 0.05f;
            rayLen = 0.05f;
        }

        _carryRayLocalDir = cam.InverseTransformDirection(ray / rayLen).normalized;
        _carryRayDistance = rayLen;

        Vector3 flatCamFwd = Vector3.ProjectOnPlane(cam.forward, Vector3.up);
        if (flatCamFwd.sqrMagnitude < 0.0001f)
            flatCamFwd = Vector3.forward;
        float camYaw = Mathf.Atan2(flatCamFwd.x, flatCamFwd.z) * Mathf.Rad2Deg;
        float objYaw = _carriedRb.rotation.eulerAngles.y;
        _carryYawOffset = Mathf.DeltaAngle(camYaw, objYaw);
        Quaternion objYawOnly = Quaternion.Euler(0f, objYaw, 0f);
        _carryPitchRollOffset = Quaternion.Inverse(objYawOnly) * _carriedRb.rotation;

        SetCarryPlayerCollisionIgnored(true);

        _carriedRb.isKinematic = false;
        _carriedRb.useGravity = true;
        _carriedRb.drag = carryDrag;
        _carriedRb.angularDrag = carryAngularDrag;
        _carriedRb.interpolation = RigidbodyInterpolation.Interpolate;
        _carriedRb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
        _carriedRb.velocity = Vector3.zero;
        _carriedRb.angularVelocity = Vector3.zero;

        _carriedWo?.SetCarriedState(true);
        _carriedWo?.TriggerPickUp(gameObject);
    }

    void Drop()
    {
        if (_carriedRb == null)
            return;

        if (_carryPlayerCollisionIgnored)
            SetCarryPlayerCollisionIgnored(false);

        RestoreCarryFriction();

        _carriedRb.isKinematic = _rbWasKinematic;
        _carriedRb.useGravity = _rbHadGravity;
        _carriedRb.interpolation = _rbInterpolation;
        _carriedRb.collisionDetectionMode = _rbCollisionDetectionMode;
        _carriedRb.drag = _rbOriginalDrag;
        _carriedRb.angularDrag = _rbOriginalAngularDrag;

        _carriedWo?.SetCarriedState(false);
        _carriedWo?.TriggerDrop(gameObject);

        _carriedTransform = null;
        _carriedWo = null;
        _carriedRb = null;
        _carriedCols = null;
        _playerCols = null;
        _carriedOriginalMaterials = null;
        _carryRayLocalDir = Vector3.forward;
        _carryRayDistance = 0f;
        _carryPitchRollOffset = Quaternion.identity;
        _carriedRadius = 0f;
    }

    Vector3 GetPlayerVelocity()
    {
        if (_playerCc != null)
            return _playerCc.velocity;

        Rigidbody rb = GetComponent<Rigidbody>();
        return rb != null ? rb.velocity : Vector3.zero;
    }

    void UpdatePrompt()
    {
        if (_carriedRb != null)
        {
            SetLabel(interactLabel, false);
            SetLabel(carryLabel, true);
            SetLabel(collectLabel, false);
            return;
        }

        SetLabel(interactLabel, _lookedAt != null && _lookedAt.interactable);
        SetLabel(carryLabel, HasCarryCandidate());
        SetLabel(collectLabel, _lookedAt != null && _lookedAt.collectable);
    }

    void SetLabel(TextMeshProUGUI label, bool active)
    {
        if (label != null)
            label.gameObject.SetActive(active);
    }

    void ShowInfo(string message)
    {
        if (infoLabel == null || string.IsNullOrEmpty(message))
            return;

        if (_hideInfoCo != null)
            StopCoroutine(_hideInfoCo);

        infoLabel.text = message;
        infoLabel.gameObject.SetActive(true);
        _hideInfoCo = StartCoroutine(HideInfoAfter(infoDisplayDuration));
    }

    IEnumerator HideInfoAfter(float delay)
    {
        yield return new WaitForSeconds(delay);
        if (infoLabel != null)
            infoLabel.gameObject.SetActive(false);
        _hideInfoCo = null;
    }
}
