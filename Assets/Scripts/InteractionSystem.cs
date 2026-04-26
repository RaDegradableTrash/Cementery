using System.Collections;
using System.Collections.Generic;
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
    [SerializeField] private Transform attractAimPoint;

    [Header("Detection")]
    [SerializeField] private float interactRange = 3f;
    [SerializeField] private float carryPickUpRange = 2f;
    [Min(0f)]
    [SerializeField] private float interactionRayRadius = 0.08f;
    [SerializeField] private LayerMask interactMask = ~0;

    [Header("Carry")]
    [SerializeField] private float carryFollowStrength = 18f;
    [SerializeField] private float carryMaxSpeed = 10f;
    [SerializeField] private float carryTurnStrength = 14f;
    [SerializeField] private float carryDrag = 8f;
    [SerializeField] private float carryAngularDrag = 10f;
    [SerializeField] private float carryOffsetAdaptRate = 10f;
    [SerializeField] private float carryOffsetFullAdaptError = 0.7f;
    [SerializeField] private bool autoAdaptCarryDistance = false;
    [SerializeField] private float carryScrollStep = 0.2f;
    [Min(0.05f)]
    [SerializeField] private float carryMinDistance = 0.15f;
    [Min(0.06f)]
    [SerializeField] private float carryMaxDistance = 6f;
    [SerializeField] private float carryScrollDirection = 1f;

    [Header("Carry Hold Anchor")]
    [SerializeField] private bool useUnifiedCarryAnchor = true;
    [SerializeField] private Vector3 unifiedCarryAnchorLocalOffset = new Vector3(0.32f, -0.2f, 1.15f);

    [Header("UI Prompts")]
    [SerializeField] private TextMeshProUGUI interactLabel;
    [SerializeField] private TextMeshProUGUI carryLabel;
    [SerializeField] private TextMeshProUGUI collectLabel;

    [Header("UI Info Label")]
    [SerializeField] private TextMeshProUGUI infoLabel;
    [SerializeField] private float infoDisplayDuration = 2.5f;

    [Header("UI Carry Distance Limit")]
    [SerializeField] private TextMeshProUGUI carryDistanceLimitLabel;
    [SerializeField] private float carryDistanceLimitDisplayDuration = 1.2f;
    [SerializeField] private string carryTooNearMessage = "Can't pull closer";
    [SerializeField] private string carryTooFarMessage = "Can't push farther";

    [Header("Inventory")]
    [SerializeField] private InventoryCameraController inventoryCameraController;
    [SerializeField] private bool openInventoryOnCollect = true;

    [Header("Inventory Restore")]
    [SerializeField] private float inventoryCancelCarrySpawnDistance = 1.15f;

    [Header("Debug")]
    [SerializeField] private bool visualizeInteractionRay = true;
    [SerializeField] private Color interactionRayColor = new Color(1f, 0.85f, 0.1f, 1f);

    private WorldObject _lookedAt;
    private Furniture_SlideDoor _lookedAtFurniture;
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
    private Coroutine _hideCarryDistanceLimitCo;

    private GameObject _pendingCollectedRestoreObject;
    private GameObject _pendingCollectedOriginalObject;
    private ItemData _pendingInventoryCarryItemData;
    private bool _attractAimSearched;

    [Header("Placement Mode (Tab)")]
    [SerializeField] private LayerMask placementMask = ~0;
    [SerializeField] private float placementRayRange = 10f;
    [SerializeField] private float placementOverlapShrink = 0.05f;
    private bool _isPlacementMode;
    private bool _isPlacementValid;
    private GameObject _placementGhost;
    private Material _placementValidMat;
    private Material _placementInvalidMat;
    private Vector3 _placementPosition;
    private Quaternion _placementRotation;

    [Header("Throw Mechanic (Q)")]
    [SerializeField] private float minThrowForce = 5f;
    [SerializeField] private float maxThrowForce = 25f;
    [SerializeField] private float maxThrowChargeTime = 1.5f;
    private bool _isThrowCharging;
    private float _throwChargeTimer;

    void Awake()
    {
        if (playerCamera == null)
            playerCamera = Camera.main;

        if (inventoryCameraController == null)
            inventoryCameraController = FindObjectOfType<InventoryCameraController>();

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
        ResolveAttractAimPointIfNeeded();
        SyncCarryDistanceLabelStyle();
        
        InitializePlacementMaterials();
    }

    void SyncCarryDistanceLabelStyle()
    {
        if (carryDistanceLimitLabel == null)
            return;

        TextMeshProUGUI styleSource = infoLabel != null ? infoLabel : carryLabel;
        if (styleSource == null)
            return;

        carryDistanceLimitLabel.font = styleSource.font;
        carryDistanceLimitLabel.fontSharedMaterial = styleSource.fontSharedMaterial;
        carryDistanceLimitLabel.fontSize = styleSource.fontSize;
        carryDistanceLimitLabel.enableAutoSizing = styleSource.enableAutoSizing;
        carryDistanceLimitLabel.color = styleSource.color;
        carryDistanceLimitLabel.alignment = styleSource.alignment;
        carryDistanceLimitLabel.enableWordWrapping = styleSource.enableWordWrapping;
        carryDistanceLimitLabel.overflowMode = styleSource.overflowMode;
        carryDistanceLimitLabel.margin = styleSource.margin;
        carryDistanceLimitLabel.raycastTarget = styleSource.raycastTarget;
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
        if (IsInventoryModeActive())
        {
            if (_carriedRb != null)
                Drop();

            _lookedAt = null;
            _lookedAtFurniture = null;
            _carryCandidateRb = null;
            _carryCandidateWo = null;
            UpdatePrompt();
            return;
        }

        Scan();
        HandleInput();
        UpdatePrompt();
        
        if (_isPlacementMode && _carriedRb != null)
            UpdatePlacementGhost();
    }

    void FixedUpdate()
    {
        if (_carriedRb == null)
            return;

        if (autoAdaptCarryDistance)
            UpdateAdaptiveCarryOffset(Time.fixedDeltaTime);
        DriveCarry();
    }

    void UpdateAdaptiveCarryOffset(float dt)
    {
        Transform carryRef = GetCarryReferenceTransform();
        Vector3 origin = GetCarryReferenceOrigin();
        Vector3 rayDir = carryRef.TransformDirection(_carryRayLocalDir);
        if (rayDir.sqrMagnitude < 0.0001f)
            rayDir = GetCarryReferenceForward();
        rayDir.Normalize();

        Vector3 targetWorld = origin + rayDir * _carryRayDistance;
        float targetError = Vector3.Distance(_carriedRb.position, targetWorld);

        // If physics/obstacles make target hard to maintain, relax offset toward actual pose.
        float pressure = Mathf.Clamp01(targetError / Mathf.Max(0.01f, carryOffsetFullAdaptError));
        float adaptRate = carryOffsetAdaptRate * (0.25f + pressure * 0.75f);
        float t = 1f - Mathf.Exp(-adaptRate * Mathf.Max(0.0001f, dt));

        float actualDistanceOnRay = Vector3.Dot(_carriedRb.position - origin, rayDir);
        actualDistanceOnRay = Mathf.Max(carryMinDistance, actualDistanceOnRay);
        _carryRayDistance = ClampCarryDistance(Mathf.Lerp(_carryRayDistance, actualDistanceOnRay, t));
    }

    float ClampCarryDistance(float value)
    {
        float minDist = Mathf.Max(0.05f, carryMinDistance);
        float maxDist = Mathf.Max(minDist + 0.01f, carryMaxDistance);
        return Mathf.Clamp(value, minDist, maxDist);
    }

    Vector3 GetUnifiedCarryAnchorLocalOffset()
    {
        Vector3 local = unifiedCarryAnchorLocalOffset;
        if (local.sqrMagnitude < 0.0001f)
            local = new Vector3(0f, -0.15f, Mathf.Max(0.7f, carryMinDistance + 0.4f));

        float dist = ClampCarryDistance(local.magnitude);
        return local.normalized * dist;
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
        Transform carryRef = GetCarryReferenceTransform();
        Vector3 origin = GetCarryReferenceOrigin();
        Vector3 rayDir = carryRef.TransformDirection(_carryRayLocalDir);
        if (rayDir.sqrMagnitude < 0.0001f)
            rayDir = GetCarryReferenceForward();
        rayDir.Normalize();

        float dist = ClampCarryDistance(_carryRayDistance);
        _carryRayDistance = dist;
        Vector3 desired = origin + rayDir * dist;

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
        Vector3 fwd = GetCarryReferenceForward();

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

    void EnsurePlayerCollidersCached()
    {
        if (_playerCc == null)
            _playerCc = GetComponent<CharacterController>();

        if (_playerCols == null || _playerCols.Length == 0)
            _playerCols = GetComponentsInChildren<Collider>(true);
    }

    float GetCameraCarryRangeCompensation()
    {
        if (playerCamera == null)
            return 0f;

        EnsurePlayerCollidersCached();
        Vector3 bodyPivot = _playerCc != null ? _playerCc.bounds.center : transform.position;

        // Shoulder/third-person cameras are offset from the player pivot in multiple axes.
        // Use the full pivot offset so pickup reach matches apparent on-screen distance.
        float cameraOffset = Vector3.Distance(playerCamera.transform.position, bodyPivot);
        return Mathf.Max(0f, cameraOffset);
    }

    float GetEffectiveCarryRangeFromCamera()
    {
        // Keep for compatibility but use the same range as general interaction.
        return Mathf.Max(0.01f, interactRange + GetCameraCarryRangeCompensation());
    }

    void ResolveAttractAimPointIfNeeded()
    {
        if (_attractAimSearched)
            return;

        _attractAimSearched = true;

        if (attractAimPoint != null)
            return;

        Transform root = transform;
        if (_playerCc != null)
            root = _playerCc.transform.root;

        attractAimPoint = FindChildByName(root, "Attract");
        if (attractAimPoint == null && playerCamera != null)
            attractAimPoint = FindChildByName(playerCamera.transform.root, "Attract");
    }

    static Transform FindChildByName(Transform root, string childName)
    {
        if (root == null || string.IsNullOrEmpty(childName))
            return null;

        Transform[] all = root.GetComponentsInChildren<Transform>(true);
        for (int i = 0; i < all.Length; i++)
        {
            Transform t = all[i];
            if (t != null && t.name == childName)
                return t;
        }

        return null;
    }

    Vector3 GetInteractionRayOrigin()
    {
        if (playerCamera != null)
            return playerCamera.transform.position;

        return transform.position;
    }

    Vector3 GetInteractionRayDirection(Vector3 origin)
    {
        if (playerCamera != null)
            return playerCamera.transform.forward;

        return transform.forward;
    }

    Transform GetCarryReferenceTransform()
    {
        EnsurePlayerCollidersCached();
        return _playerCc != null ? _playerCc.transform : transform;
    }

    Vector3 GetCarryReferenceOrigin()
    {
        EnsurePlayerCollidersCached();
        if (_playerCc != null)
            return _playerCc.bounds.center;

        return transform.position;
    }

    Vector3 GetCarryReferenceForward()
    {
        Vector3 fwd = Vector3.ProjectOnPlane(GetCarryReferenceTransform().forward, Vector3.up);
        if (fwd.sqrMagnitude < 0.0001f)
            fwd = Vector3.ProjectOnPlane(transform.forward, Vector3.up);
        if (fwd.sqrMagnitude < 0.0001f)
            fwd = Vector3.forward;

        return fwd.normalized;
    }

    bool IsPlayerCollider(Collider c)
    {
        EnsurePlayerCollidersCached();

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

    bool TryRaycastIgnoringPlayer(Ray ray, float range, out RaycastHit bestHit)
    {
        bestHit = default;
        float sphereRadius = interactionRayRadius > 0.0001f ? interactionRayRadius * 5f : 0f; // 500% thicker cast

        RaycastHit[] hits = sphereRadius > 0.0001f
            ? Physics.SphereCastAll(ray, sphereRadius, range, interactMask, QueryTriggerInteraction.Ignore)
            : Physics.RaycastAll(ray, range, interactMask, QueryTriggerInteraction.Ignore);
        if (hits == null || hits.Length == 0)
            return false;

        float nearest = float.PositiveInfinity;
        for (int i = 0; i < hits.Length; i++)
        {
            RaycastHit hit = hits[i];
            Collider c = hit.collider;
            if (c == null)
                continue;

            if (IsIgnoredCarryHitCollider(c))
                continue;

            if (hit.distance >= nearest)
                continue;

            nearest = hit.distance;
            bestHit = hit;
        }

        return nearest < float.PositiveInfinity;
    }

    void DebugDrawInteractionRay(Ray ray, float range)
    {
        Color color = interactionRayColor;
        Debug.DrawRay(ray.origin, ray.direction * range, color);

        float radius = Mathf.Max(0f, interactionRayRadius) * 5f;
        if (radius <= 0.0001f)
            return;

        Vector3 dir = ray.direction.normalized;
        if (dir.sqrMagnitude < 0.0001f)
            return;

        Vector3 up = Vector3.up;
        if (Mathf.Abs(Vector3.Dot(up, dir)) > 0.99f)
            up = Vector3.right;

        Vector3 ortho1 = Vector3.Cross(dir, up).normalized * radius;
        Vector3 ortho2 = Vector3.Cross(dir, ortho1).normalized * radius;

        const int segments = 16;
        float step = Mathf.PI * 2f / segments;
        Vector3 center = ray.origin + dir * Mathf.Min(range, 1.5f);
        Vector3 prev = center + ortho1;
        for (int i = 1; i <= segments; i++)
        {
            float angle = i * step;
            Vector3 offset = Mathf.Cos(angle) * ortho1 + Mathf.Sin(angle) * ortho2;
            Vector3 next = center + offset;
            Debug.DrawLine(prev, next, color);
            prev = next;
        }
    }

    void Scan()
    {
        float rayRange = GetEffectiveCarryRangeFromCamera();
        Vector3 rayOrigin = GetInteractionRayOrigin();
        Vector3 rayDirection = GetInteractionRayDirection(rayOrigin);
        Ray ray = new Ray(rayOrigin, rayDirection);

        if (visualizeInteractionRay)
            DebugDrawInteractionRay(ray, rayRange);

        if (!TryRaycastIgnoringPlayer(ray, rayRange, out RaycastHit hit))
        {
            _lookedAt = null;
            _lookedAtFurniture = null;
            _carryCandidateRb = null;
            _carryCandidateWo = null;
            return;
        }

        _lookedAt = hit.collider.GetComponentInParent<WorldObject>();
        _lookedAtFurniture = hit.collider.GetComponentInParent<Furniture_SlideDoor>();
        
        if (_carriedRb == null)
            _carryCandidateRb = ResolveCarryCandidate(hit, out _carryCandidateWo);
        else
        {
            _carryCandidateRb = null;
            _carryCandidateWo = null;
        }
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

        // Candidate is already constrained by the SphereCast range; no extra distance gate.

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
        HandleCarryScrollInput();

        if (Input.GetMouseButtonDown(0) && _carriedRb == null && HasCarryCandidate())
            PickUp(_carryCandidateRb, _carryCandidateWo);

        if (_carriedRb != null && !_isPlacementMode)
        {
            if (Input.GetKeyDown(KeyCode.Q))
            {
                _isThrowCharging = true;
                _throwChargeTimer = 0f;
            }
            if (_isThrowCharging)
            {
                _throwChargeTimer += Time.deltaTime;
                if (Input.GetKeyUp(KeyCode.Q))
                {
                    ExecuteThrow();
                }
            }
        }
        else
        {
            _isThrowCharging = false;
            _throwChargeTimer = 0f;
        }

        if (_carriedRb != null)
        {
            if (Input.GetKeyDown(KeyCode.Tab))
                EnterPlacementMode();
            else if (Input.GetKeyUp(KeyCode.Tab))
                ExitPlacementMode();
        }
        else if (_isPlacementMode)
        {
            ExitPlacementMode();
        }

        if (Input.GetMouseButtonUp(0) && _carriedRb != null)
        {
            if (_isPlacementMode && _isPlacementValid)
            {
                ExecutePlacement();
            }
            else
            {
                Drop();
            }
        }

        if (_carriedRb != null && !_isPlacementMode)
        {
            if (Input.GetKeyDown(KeyCode.E) && _carriedWo != null && _carriedWo.collectable)
            {
                WorldObject wo = _carriedWo;
                Drop();
                Collect(wo);
            }
        }

        if (Input.GetMouseButtonDown(1))
        {
            if (_lookedAt != null)
            {
                if (_lookedAt.interactable)
                {
                    _lookedAt.TriggerInteract(gameObject);
                    _lookedAt.PlayInteractAnim();
                    ShowInfo(_lookedAt.interactMessage);
                }
            }
            else if (_lookedAtFurniture != null)
            {
                _lookedAtFurniture.Interact();
            }
        }
    }

    void HandleCarryScrollInput()
    {
        if (_carriedRb == null)
            return;

        float rawScroll = Input.mouseScrollDelta.y;
        if (Mathf.Abs(rawScroll) < 0.01f)
            return;

        float signedScroll = rawScroll * Mathf.Sign(Mathf.Approximately(carryScrollDirection, 0f) ? 1f : carryScrollDirection);
        bool pushFarther = signedScroll > 0f;
        float wantedDistance = _carryRayDistance + signedScroll * carryScrollStep;
        bool blocked = false;

        float clampedByRange = ClampCarryDistance(wantedDistance);
        if (!Mathf.Approximately(clampedByRange, wantedDistance))
            blocked = true;

        // 检查物理阻挡
        float obstacleMax = GetMaxReachableDistanceOnCarryRay();
        float finalDistance = Mathf.Min(clampedByRange, obstacleMax);
        if (pushFarther && finalDistance < clampedByRange - 0.0001f)
            blocked = true;

        if (!pushFarther && finalDistance <= carryMinDistance + 0.0001f && wantedDistance < _carryRayDistance)
            blocked = true;

        // 新增：如果被阻挡，尝试推动阻挡物体
        bool pushTried = false;
        if (blocked) {
            // 重新做一次SphereCast，找到最近的阻挡物体
            Transform carryRef = GetCarryReferenceTransform();
            Vector3 origin = GetCarryReferenceOrigin();
            Vector3 rayDir = carryRef.TransformDirection(_carryRayLocalDir);
            if (rayDir.sqrMagnitude < 0.0001f)
                rayDir = GetCarryReferenceForward();
            rayDir.Normalize();
            float castRadius = Mathf.Max(0.05f, _carriedRadius * 0.85f);
            float checkDistance = Mathf.Max(0.05f, carryMaxDistance);
            RaycastHit[] hits = Physics.SphereCastAll(origin, castRadius, rayDir, checkDistance, interactMask, QueryTriggerInteraction.Ignore);
            float nearest = float.PositiveInfinity;
            Collider nearestCol = null;
            RaycastHit nearestHit = default;
            for (int i = 0; i < hits.Length; i++) {
                Collider c = hits[i].collider;
                if (c == null) continue;
                if (IsIgnoredCarryHitCollider(c)) continue;
                if (hits[i].distance < nearest) {
                    nearest = hits[i].distance;
                    nearestCol = c;
                    nearestHit = hits[i];
                }
            }
            if (nearestCol != null) {
                WorldObject wo = nearestCol.GetComponentInParent<WorldObject>();
                Rigidbody rb = nearestCol.attachedRigidbody;
                if (rb == null && wo != null) rb = wo.GetComponent<Rigidbody>();
                if (wo != null && wo.canBePushed && rb != null) {
                    // 施加一个沿carry方向的力，尝试推动
                    float pushForce = 8f; // 可调参数
                    rb.AddForce(rayDir * pushForce, ForceMode.Impulse);
                    pushTried = true;
                    blocked = false; // 允许scroll继续
                }
            }
        }

        _carryRayDistance = ClampCarryDistance(finalDistance);

        // 只有完全推不动才反馈TMP
        if (blocked && !pushTried)
            ShowCarryDistanceLimit(pushFarther ? carryTooFarMessage : carryTooNearMessage);
    }

    float GetMaxReachableDistanceOnCarryRay()
    {
        Transform carryRef = GetCarryReferenceTransform();
        Vector3 origin = GetCarryReferenceOrigin();
        Vector3 rayDir = carryRef.TransformDirection(_carryRayLocalDir);
        if (rayDir.sqrMagnitude < 0.0001f)
            rayDir = GetCarryReferenceForward();
        rayDir.Normalize();

        float minDist = Mathf.Max(0.05f, carryMinDistance);
        float maxDist = Mathf.Max(minDist + 0.01f, carryMaxDistance);
        float checkDistance = maxDist;
        float castRadius = Mathf.Max(0.05f, _carriedRadius * 0.85f);

        RaycastHit[] hits = Physics.SphereCastAll(origin, castRadius, rayDir, checkDistance, interactMask, QueryTriggerInteraction.Ignore);
        float nearest = float.PositiveInfinity;
        for (int i = 0; i < hits.Length; i++)
        {
            Collider c = hits[i].collider;
            if (c == null) continue;
            if (IsIgnoredCarryHitCollider(c)) continue;
            if (hits[i].distance < nearest)
                nearest = hits[i].distance;
        }

        if (nearest == float.PositiveInfinity)
            return maxDist;

        float safeDist = Mathf.Max(minDist, nearest - castRadius - 0.03f);
        return Mathf.Min(maxDist, safeDist);
    }

    void Collect(WorldObject obj)
    {
        ShowInfo(obj.collectMessage);
        obj.TriggerCollect(gameObject);

        InventoryCameraController camCtrl = GetInventoryCameraController();
        if (openInventoryOnCollect && camCtrl != null)
        {
            CacheCollectedObjectForRestore(obj);
            camCtrl.EnterInventoryMode(obj.collectItemData);
        }

        _lookedAt = null;
        obj.PlayCollectAnim(() => Destroy(obj.gameObject));
    }

    void CacheCollectedObjectForRestore(WorldObject source)
    {
        ClearPendingCollectedRestoreObject();

        if (source == null)
            return;

        _pendingCollectedOriginalObject = source.gameObject;
        _pendingCollectedRestoreObject = Instantiate(source.gameObject);
        _pendingCollectedRestoreObject.name = source.gameObject.name + "_RestoreCarry";
        _pendingCollectedRestoreObject.SetActive(false);
    }

    public void CommitPendingCollectedObject()
    {
        ClearPendingCollectedRestoreObject();
    }

    public void SetPendingInventoryCarryItem(ItemData itemData)
    {
        _pendingInventoryCarryItemData = itemData;
    }

    public bool HasPendingCollectedObject()
    {
        return _pendingCollectedRestoreObject != null || _pendingInventoryCarryItemData != null;
    }

    public bool HasCarriedObject()
    {
        return _carriedRb != null;
    }

    public void DropCarriedObjectIfAny()
    {
        if (_carriedRb != null)
            Drop();
    }

    public void EjectInventoryTempItems(List<InventoryRaycastPlacer.TempItem> items)
    {
        if (playerCamera == null) return;

        foreach (var ti in items)
        {
            if (ti.itemData != null && ti.itemData.worldPrefab != null)
            {
                Vector3 spawnPos = playerCamera.transform.position + playerCamera.transform.forward * 1.5f;
                GameObject spawned = Instantiate(ti.itemData.worldPrefab, spawnPos, ti.rotation);
                Rigidbody rb = spawned.GetComponent<Rigidbody>();
                if (rb != null)
                {
                    Vector3 randomDir = (playerCamera.transform.forward + Random.insideUnitSphere * 0.5f).normalized;
                    rb.AddForce(randomDir * 5f, ForceMode.Impulse);
                }
            }
            if (ti.transform != null)
            {
                Destroy(ti.transform.gameObject);
            }
        }
    }

    public bool RestorePendingCollectedObjectToCarry()
    {
        if (_pendingCollectedRestoreObject == null && _pendingInventoryCarryItemData == null)
            return false;

        GameObject restoreObject = null;
        if (_pendingCollectedRestoreObject != null)
        {
            if (_pendingCollectedOriginalObject != null)
            {
                Destroy(_pendingCollectedOriginalObject);
                _pendingCollectedOriginalObject = null;
            }

            restoreObject = _pendingCollectedRestoreObject;
            _pendingCollectedRestoreObject = null;
        }
        else
        {
            restoreObject = CreateCarryObjectFromItemData(_pendingInventoryCarryItemData);
            _pendingInventoryCarryItemData = null;
        }

        if (restoreObject == null)
            return false;

        restoreObject.SetActive(true);
        EnableAllColliders(restoreObject, true);

        WorldObject wo = restoreObject.GetComponentInChildren<WorldObject>(true);
        if (wo != null)
        {
            wo.enabled = true;
            wo.CancelAnims();
            wo.SetCarriedState(false);
        }
        else
        {
            wo = restoreObject.AddComponent<WorldObject>();
            wo.carryable = true;
        }

        if (!wo.carryable)
            wo.carryable = true;

        Rigidbody rb = restoreObject.GetComponentInChildren<Rigidbody>(true);
        if (rb == null)
            rb = restoreObject.AddComponent<Rigidbody>();

        Collider[] existingColliders = restoreObject.GetComponentsInChildren<Collider>(true);
        if (existingColliders == null || existingColliders.Length == 0)
            restoreObject.AddComponent<BoxCollider>();

        rb.detectCollisions = true;
        rb.isKinematic = false;
        rb.useGravity = true;
        rb.velocity = Vector3.zero;
        rb.angularVelocity = Vector3.zero;

        Transform carryRef = GetCarryReferenceTransform();
        Vector3 localSpawnOffset;
        if (useUnifiedCarryAnchor)
        {
            localSpawnOffset = GetUnifiedCarryAnchorLocalOffset();
        }
        else
        {
            float spawnDistance = Mathf.Clamp(inventoryCancelCarrySpawnDistance, 0.75f, Mathf.Max(0.75f, carryPickUpRange - 0.05f));
            localSpawnOffset = Vector3.forward * spawnDistance;
        }

        Vector3 spawnPos = GetCarryReferenceOrigin() + carryRef.TransformDirection(localSpawnOffset);
        restoreObject.transform.position = spawnPos;

        Vector3 flatForward = GetCarryReferenceForward();
        if (flatForward.sqrMagnitude > 0.0001f)
            restoreObject.transform.rotation = Quaternion.LookRotation(flatForward.normalized, Vector3.up);

        PickUp(rb, wo);
        bool restored = _carriedRb != null;
        if (!restored && restoreObject != null)
            Destroy(restoreObject);

        return restored;
    }

    void ClearPendingCollectedRestoreObject()
    {
        if (_pendingCollectedRestoreObject != null)
            Destroy(_pendingCollectedRestoreObject);

        if (_pendingCollectedOriginalObject != null)
            Destroy(_pendingCollectedOriginalObject);

        _pendingCollectedRestoreObject = null;
        _pendingCollectedOriginalObject = null;
        _pendingInventoryCarryItemData = null;
    }

    GameObject CreateCarryObjectFromItemData(ItemData itemData)
    {
        if (itemData == null)
            return null;

        GameObject go = itemData.previewPrefab != null
            ? Instantiate(itemData.previewPrefab)
            : GameObject.CreatePrimitive(PrimitiveType.Cube);

        if (go == null)
            return null;

        go.name = string.IsNullOrEmpty(itemData.itemName)
            ? itemData.name + "_Carry"
            : itemData.itemName + "_Carry";

        return go;
    }

    void EnableAllColliders(GameObject go, bool enabled)
    {
        if (go == null)
            return;

        Collider[] cols = go.GetComponentsInChildren<Collider>(true);
        for (int i = 0; i < cols.Length; i++)
        {
            if (cols[i] != null)
                cols[i].enabled = enabled;
        }
    }

    InventoryCameraController GetInventoryCameraController()
    {
        InventoryCameraController primary = InventoryCameraController.GetPrimaryController();
        if (primary != null)
            inventoryCameraController = primary;
        else if (inventoryCameraController == null)
            inventoryCameraController = FindObjectOfType<InventoryCameraController>();

        return inventoryCameraController;
    }

    bool IsInventoryModeActive()
    {
        InventoryCameraController camCtrl = GetInventoryCameraController();
        return camCtrl != null && camCtrl.IsInventoryActive;
    }

    void PickUp(Rigidbody rb, WorldObject wo)
    {
        if (rb == null)
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

        Transform carryRef = GetCarryReferenceTransform();
        Vector3 carryOrigin = GetCarryReferenceOrigin();
        if (useUnifiedCarryAnchor)
        {
            Vector3 localAnchor = GetUnifiedCarryAnchorLocalOffset();
            _carryRayLocalDir = localAnchor.normalized;
            _carryRayDistance = localAnchor.magnitude;
        }
        else
        {
            Vector3 ray = _carriedTransform.position - carryOrigin;
            float rayLen = ray.magnitude;
            if (rayLen < 0.05f)
            {
                ray = GetCarryReferenceForward() * 0.05f;
                rayLen = 0.05f;
            }

            _carryRayLocalDir = carryRef.InverseTransformDirection(ray / rayLen).normalized;
            _carryRayDistance = ClampCarryDistance(rayLen);
        }

        Vector3 flatCarryFwd = GetCarryReferenceForward();
        float carryYaw = Mathf.Atan2(flatCarryFwd.x, flatCarryFwd.z) * Mathf.Rad2Deg;
        float objYaw = _carriedRb.rotation.eulerAngles.y;
        _carryYawOffset = Mathf.DeltaAngle(carryYaw, objYaw);
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

        if (_isPlacementMode)
            ExitPlacementMode();

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
        SetLabel(interactLabel, (_lookedAt != null && _lookedAt.interactable) || _lookedAtFurniture != null);
        SetLabel(carryLabel, HasCarryCandidate() || _carriedRb != null);
        SetLabel(collectLabel, _carriedWo != null && _carriedWo.collectable);
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

    void ShowCarryDistanceLimit(string message)
    {
        if (carryDistanceLimitLabel == null || string.IsNullOrEmpty(message))
            return;

        SyncCarryDistanceLabelStyle();

        if (_hideCarryDistanceLimitCo != null)
            StopCoroutine(_hideCarryDistanceLimitCo);

        carryDistanceLimitLabel.text = message;
        carryDistanceLimitLabel.gameObject.SetActive(true);
        _hideCarryDistanceLimitCo = StartCoroutine(HideCarryDistanceLimitAfter(carryDistanceLimitDisplayDuration));
    }

    IEnumerator HideInfoAfter(float delay)
    {
        yield return new WaitForSeconds(delay);
        if (infoLabel != null)
            infoLabel.gameObject.SetActive(false);
        _hideInfoCo = null;
    }

    IEnumerator HideCarryDistanceLimitAfter(float delay)
    {
        yield return new WaitForSeconds(delay);
        if (carryDistanceLimitLabel != null)
            carryDistanceLimitLabel.gameObject.SetActive(false);
        _hideCarryDistanceLimitCo = null;
    }

    void OnDestroy()
    {
        ClearPendingCollectedRestoreObject();
    }

    void InitializePlacementMaterials()
    {
        _placementValidMat = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
        if (_placementValidMat.shader == null) _placementValidMat = new Material(Shader.Find("Standard"));
        SetMaterialTransparentURP(_placementValidMat, new Color(0.2f, 1f, 0.2f, 0.4f));

        _placementInvalidMat = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
        if (_placementInvalidMat.shader == null) _placementInvalidMat = new Material(Shader.Find("Standard"));
        SetMaterialTransparentURP(_placementInvalidMat, new Color(1f, 0.2f, 0.2f, 0.4f));
    }

    void SetMaterialTransparentURP(Material mat, Color color)
    {
        if (mat.shader != null && mat.shader.name.Contains("Universal"))
        {
            mat.SetColor("_BaseColor", color);
            mat.SetFloat("_Surface", 1);
            mat.SetFloat("_Blend", 0);
            mat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
            mat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            mat.SetInt("_ZWrite", 0);
            mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            mat.renderQueue = 3000;
        }
        else
        {
            mat.color = color;
            mat.SetFloat("_Mode", 3);
            mat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
            mat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            mat.SetInt("_ZWrite", 0);
            mat.EnableKeyword("_ALPHABLEND_ON");
            mat.renderQueue = 3000;
        }
    }

    void EnterPlacementMode()
    {
        if (_carriedRb == null || _isPlacementMode) return;
        _isPlacementMode = true;
        _isThrowCharging = false;
        
        Renderer[] renderers = _carriedRb.GetComponentsInChildren<Renderer>();
        foreach (Renderer r in renderers) r.enabled = false;
            
        _placementGhost = Instantiate(_carriedRb.gameObject);
        _placementGhost.name = "PlacementGhost";
        
        Destroy(_placementGhost.GetComponent<Rigidbody>());
        foreach(MonoBehaviour mb in _placementGhost.GetComponentsInChildren<MonoBehaviour>())
            Destroy(mb);
            
        foreach(Collider c in _placementGhost.GetComponentsInChildren<Collider>())
        {
            c.isTrigger = true;
            c.gameObject.layer = 2; // Ignore Raycast layer
        }
    }

    void ExitPlacementMode()
    {
        if (!_isPlacementMode) return;
        _isPlacementMode = false;
        
        if (_carriedRb != null)
        {
            Renderer[] renderers = _carriedRb.GetComponentsInChildren<Renderer>();
            foreach (Renderer r in renderers) r.enabled = true;
        }
        
        if (_placementGhost != null) Destroy(_placementGhost);
    }
    
    void UpdatePlacementGhost()
    {
        if (_placementGhost == null || _carriedRb == null) return;
        
        Ray ray = new Ray(GetInteractionRayOrigin(), GetInteractionRayDirection(GetInteractionRayOrigin()));
        EnsurePlayerCollidersCached();
        
        RaycastHit[] hits = Physics.RaycastAll(ray, placementRayRange, placementMask, QueryTriggerInteraction.Ignore);
        float nearest = float.PositiveInfinity;
        RaycastHit bestHit = default;
        
        for (int i = 0; i < hits.Length; i++)
        {
            if (IsIgnoredCarryHitCollider(hits[i].collider)) continue;
            if (hits[i].distance < nearest)
            {
                nearest = hits[i].distance;
                bestHit = hits[i];
            }
        }
        
        if (nearest < float.PositiveInfinity)
        {
            _placementGhost.SetActive(true);
            _placementPosition = bestHit.point;
            
            Vector3 fwd = Vector3.ProjectOnPlane(GetCarryReferenceForward(), bestHit.normal);
            if (fwd.sqrMagnitude < 0.001f) fwd = GetCarryReferenceForward();
            _placementRotation = Quaternion.LookRotation(fwd, bestHit.normal);
            
            _placementGhost.transform.position = _placementPosition;
            _placementGhost.transform.rotation = _placementRotation;
            
            CheckPlacementValidity();
        }
        else
        {
            _placementGhost.SetActive(false);
            _isPlacementValid = false;
        }
    }
    
    void CheckPlacementValidity()
    {
        _isPlacementValid = true;
        
        Renderer[] ghostRenderers = _placementGhost.GetComponentsInChildren<Renderer>();
        Bounds bounds = new Bounds(_placementGhost.transform.position, Vector3.zero);
        bool hasBounds = false;
        
        foreach(Renderer r in ghostRenderers)
        {
            if (hasBounds) bounds.Encapsulate(r.bounds);
            else { bounds = r.bounds; hasBounds = true; }
        }
        
        if (hasBounds)
        {
            Vector3 extents = bounds.extents - Vector3.one * placementOverlapShrink;
            extents.x = Mathf.Max(0.01f, extents.x);
            extents.y = Mathf.Max(0.01f, extents.y);
            extents.z = Mathf.Max(0.01f, extents.z);
            
            Collider[] hits = Physics.OverlapBox(bounds.center, extents, Quaternion.identity, interactMask, QueryTriggerInteraction.Ignore);
            foreach(Collider c in hits)
            {
                if (IsIgnoredCarryHitCollider(c)) continue;
                if (c.transform.IsChildOf(_placementGhost.transform)) continue;
                
                _isPlacementValid = false;
                break;
            }
        }
        
        Material targetMat = _isPlacementValid ? _placementValidMat : _placementInvalidMat;
        foreach(Renderer r in ghostRenderers)
        {
            Material[] mats = r.sharedMaterials;
            for(int i=0; i<mats.Length; i++) mats[i] = targetMat;
            r.sharedMaterials = mats;
        }
    }
    
    void ExecutePlacement()
    {
        Rigidbody rb = _carriedRb;
        Vector3 finalPos = _placementPosition;
        Quaternion finalRot = _placementRotation;
        
        ExitPlacementMode();
        Drop();
        
        if (rb != null)
        {
            rb.position = finalPos;
            rb.rotation = finalRot;
            rb.velocity = Vector3.zero;
            rb.angularVelocity = Vector3.zero;
            rb.isKinematic = true;
        }
    }

    void ExecuteThrow()
    {
        float t = Mathf.Clamp01(_throwChargeTimer / maxThrowChargeTime);
        float force = Mathf.Lerp(minThrowForce, maxThrowForce, t);
        
        Rigidbody rb = _carriedRb;
        Vector3 dir = GetInteractionRayDirection(GetInteractionRayOrigin());
        
        if (_isPlacementMode) ExitPlacementMode();
        Drop();
        
        if (rb != null)
            rb.AddForce(dir * force, ForceMode.Impulse);
            
        _isThrowCharging = false;
    }
}
