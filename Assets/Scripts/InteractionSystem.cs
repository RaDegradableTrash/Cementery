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

    [Min(0f)]
    [SerializeField] private float interactionRayRadius = 0.08f;
    [SerializeField] private LayerMask interactMask = ~0;

    [Header("Carry (Legacy/Unused)")]
    // [SerializeField] private float carryFollowStrength = 18f;
    // [SerializeField] private float carryMaxSpeed = 10f;
    [SerializeField] private float carryTurnStrength = 14f;
    // [SerializeField] private float carryDrag = 8f;
    // [SerializeField] private float carryAngularDrag = 10f;

    [SerializeField] private float carryScrollStep = 0.2f;
    [Min(0.05f)]
    [SerializeField] private float carryMinDistance = 0.15f;
    [Min(0.06f)]
    [SerializeField] private float carryMaxDistance = 6f;
    [SerializeField] private float carryScrollDirection = 1f;

    [Header("Carry Hold Anchor")]
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


    [Header("Debug")]
    [SerializeField] private bool visualizeInteractionRay = true;
    [SerializeField] private Color interactionRayColor = new Color(1f, 0.85f, 0.1f, 1f);

    private MouseLook _mouseLook;

    private WorldObject _lookedAt;
    private Rigidbody _carryCandidateRb;
    private WorldObject _carryCandidateWo;
    private Vector3 _dragAnchorLocal;

    private Transform _carriedTransform;
    private WorldObject _carriedWo;
    private Rigidbody _carriedRb;

    private Collider[] _carriedCols;
    private Collider[] _playerCols;
    private PhysicMaterial[] _carriedOriginalMaterials;
    private PhysicMaterial _carryNoFrictionMaterial;
    private CapsuleCollider _playerCol;

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

    // Transition Animation
    private float _carryTransitionTimer = 0f;
    private const float CarryTransitionDuration = 0.35f; // Slightly longer for better feel
    private Vector3 _carryStartLocalPos;
    private Quaternion _carryStartLocalRot;
    private float _carrySmoothDistance;

    private Coroutine _hideInfoCo;
    private Coroutine _hideCarryDistanceLimitCo;

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
    private Vector3 _placementSurfaceNormal = Vector3.up;

    [Header("Throw Mechanic (Q)")]
    [SerializeField] private float minThrowForce = 5f;
    [SerializeField] private float maxThrowForce = 25f;
    [SerializeField] private float maxThrowChargeTime = 1.5f;
    private bool _isThrowCharging;
    private float _throwChargeTimer;

    private float _timeRButtonPressed;
    private float _placementRotationOffset = 0f;

    public static InteractionSystem Instance { get; private set; }

    void Awake()
    {
        Instance = this;

        if (playerCamera == null)
            playerCamera = Camera.main;

        if (inventoryCameraController == null)
            inventoryCameraController = FindObjectOfType<InventoryCameraController>();

        if (_mouseLook == null && playerCamera != null)
            _mouseLook = playerCamera.GetComponent<MouseLook>();

        _carryNoFrictionMaterial = new PhysicMaterial("Carry_NoFriction_Runtime")
        {
            dynamicFriction = 0f,
            staticFriction = 0f,
            frictionCombine = PhysicMaterialCombine.Minimum,
            bounciness = 0f,
            bounceCombine = PhysicMaterialCombine.Minimum
        };
        _carryNoFrictionMaterial.hideFlags = HideFlags.HideAndDontSave;

        _playerCol = GetComponent<CapsuleCollider>();
        ResolveAttractAimPointIfNeeded();
        InitializePlacementMaterials();
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
 
        // Heavy objects: drive via forces in FixedUpdate (physics-based dragging)
        if (_carriedWo != null && _carriedWo.isHeavy)
            DriveCarry();
    }

    void LateUpdate()
    {
        if (_carriedRb == null) return;
        
        // Non-heavy (kinematic) objects: drive position
        if (_carriedWo == null || !_carriedWo.isHeavy)
            DriveCarryKinematic();
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
        if (_carriedWo != null && _carriedWo.isHeavy)
        {
            Transform playerT = _playerCol != null ? _playerCol.transform : transform;
            Vector3 pPos = playerT.position; pPos.y = 0;
            Vector3 aPos = _carriedRb.transform.TransformPoint(_dragAnchorLocal); aPos.y = 0;
            
            float currentDist = Vector3.Distance(pPos, aPos);
            if (currentDist > _carryRayDistance)
            {
                Vector3 pullForceDir = (pPos - aPos).normalized;
                float stretch = currentDist - _carryRayDistance;
                
                // Soft spring: low constant + clamped max force for gentle, PEAK-like interaction
                float springForce = Mathf.Min(stretch * 15f, 20f);
                Vector3 force = pullForceDir * springForce;
                _carriedRb.AddForceAtPosition(force, _carriedRb.transform.TransformPoint(_dragAnchorLocal), ForceMode.Acceleration);
            }
            
            // Strong damping to prevent bouncing and launching
            Vector3 vel = _carriedRb.velocity;
            vel.x *= 0.85f;
            vel.z *= 0.85f;
            // Clamp max horizontal speed to prevent objects/player flying off
            float hSpeed = new Vector2(vel.x, vel.z).magnitude;
            if (hSpeed > 3f)
            {
                float scale = 3f / hSpeed;
                vel.x *= scale;
                vel.z *= scale;
            }
            _carriedRb.velocity = vel;
            // Strong angular damping to prevent wild spinning
            _carriedRb.angularVelocity *= 0.8f;
            if (_carriedRb.angularVelocity.magnitude > 2f)
                _carriedRb.angularVelocity = _carriedRb.angularVelocity.normalized * 2f;

            // Manual soft separation: since Unity collision is ignored between
            // player and carried heavy object, manually prevent overlap.
            // Uses horizontal-only push to avoid floor clipping.
            if (_playerCol != null && _carriedCols != null)
            {
                Bounds pb = _playerCol.bounds;
                for (int ci = 0; ci < _carriedCols.Length; ci++)
                {
                    Collider cc = _carriedCols[ci];
                    if (cc == null || !cc.enabled) continue;
                    if (!pb.Intersects(cc.bounds)) continue;

                    // Horizontal separation direction from object center to player
                    Vector3 sep = _playerCol.transform.position - cc.bounds.center;
                    sep.y = 0f;
                    if (sep.sqrMagnitude < 0.001f)
                        sep = -_playerCol.transform.forward; // fallback
                    sep.Normalize();

                    // Apply gentle continuous push to player (acceleration = mass-independent)
                    Rigidbody playerRb = _playerCol.attachedRigidbody;
                    if (playerRb != null)
                        playerRb.AddForce(sep * 25f, ForceMode.Acceleration);
                }
            }
        }
    }

    void DriveCarryKinematic()
    {
        if (playerCamera == null || _carriedRb == null) return;
        Transform camT = playerCamera.transform;

        // 1. Mode-based direction LERP (Normal vs Placement)
        Vector3 targetDirLocal = _isPlacementMode ? new Vector3(0.55f, -0.6f, 0.8f).normalized : unifiedCarryAnchorLocalOffset.normalized;
        _carryRayLocalDir = Vector3.Lerp(_carryRayLocalDir, targetDirLocal, Time.deltaTime * 10f);
        
        // 2. Calculate Base Target Orientation
        Quaternion targetRot = GetCarryTargetRotationYawOnly();
        Vector3 rayDirWorld = camT.TransformDirection(_carryRayLocalDir).normalized;
        
        // 3. Smooth Distance Calculation (Wall Avoidance + Scroll)
        float desiredDist = ClampCarryDistance(_carryRayDistance);
        float castRadius = Mathf.Max(0.05f, _carriedRadius * 0.8f);
        
        if (Physics.SphereCast(camT.position, castRadius, rayDirWorld, out RaycastHit hit, desiredDist, interactMask, QueryTriggerInteraction.Ignore))
        {
            if (!IsIgnoredCarryHitCollider(hit.collider))
            {
                desiredDist = Mathf.Max(0.15f, hit.distance - 0.02f);
            }
        }

        // Smoothly transition the distance to avoid snapping when hitting walls
        _carrySmoothDistance = Mathf.Lerp(_carrySmoothDistance, desiredDist, Time.deltaTime * 15f);
        Vector3 targetPos = camT.position + rayDirWorld * _carrySmoothDistance;

        // 4. Transition Animation (LERP from original world pickup point)
        if (_carryTransitionTimer < CarryTransitionDuration)
        {
            _carryTransitionTimer += Time.deltaTime;
            float progress = _carryTransitionTimer / CarryTransitionDuration;
            
            // Variable speed: Ease Out Cubic (starts fast, slows down)
            float t = 1f - Mathf.Pow(1f - progress, 3f);
            
            Vector3 startWorldPos = camT.TransformPoint(_carryStartLocalPos);
            Quaternion startWorldRot = camT.rotation * _carryStartLocalRot;
            
            _carriedRb.transform.position = Vector3.Lerp(startWorldPos, targetPos, t);
            _carriedRb.transform.rotation = Quaternion.Slerp(startWorldRot, targetRot, t);
        }
        else
        {
            _carriedRb.transform.position = targetPos;
            _carriedRb.transform.rotation = Quaternion.Slerp(_carriedRb.transform.rotation, targetRot, Time.deltaTime * carryTurnStrength);
        }
    }

    Vector3 GetCarryTargetPosition()
    {
        if (_carriedWo != null && _carriedWo.isHeavy)
        {
            Transform playerT = _playerCol != null ? _playerCol.transform : transform;
            Vector3 pPos = playerT.position;
            Vector3 oPos = _carriedRb.position;
            pPos.y = 0; oPos.y = 0; // 2D projection for pure horizontal trailing
            
            Vector3 dragDir = (oPos - pPos).normalized;
            if (dragDir.sqrMagnitude < 0.001f) dragDir = -playerT.forward;
            dragDir.y = 0;
            
            float targetDist = Mathf.Max(1.5f, _carryRayDistance);
            return playerT.position + dragDir * targetDist;
        }

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
                desired = origin + dir * safeDist;
            }
        }

        // Second pass: Linecast from camera to target position.
        // Only block on wall-like surfaces (not floors, which are naturally between camera and carried object).
        Vector3 camPos = playerCamera != null ? playerCamera.transform.position : origin;
        Vector3 toDesired = desired - camPos;
        float toDesiredDist = toDesired.magnitude;
        if (toDesiredDist > 0.01f)
        {
            if (Physics.Raycast(camPos, toDesired / toDesiredDist, out RaycastHit wallHit, toDesiredDist, interactMask, QueryTriggerInteraction.Ignore))
            {
                if (!IsIgnoredCarryHitCollider(wallHit.collider))
                {
                    // Only block on wall-like surfaces, not floors/ceilings
                    float wallDot = Vector3.Dot(wallHit.normal, Vector3.up);
                    if (Mathf.Abs(wallDot) < 0.5f)
                    {
                        desired = wallHit.point + wallHit.normal * (castRadius + 0.05f);
                    }
                }
            }
        }

        return desired;
    }

    Quaternion GetCarryTargetRotationYawOnly()
    {
        if (_carriedWo != null && _carriedWo.isHeavy)
        {
            // Do not force rotation on heavy objects dragged on the ground
            return _carriedRb.rotation;
        }

        Vector3 fwd = GetCarryReferenceForward();

        float yaw = Mathf.Atan2(fwd.x, fwd.z) * Mathf.Rad2Deg;
        Quaternion yawTarget = Quaternion.Euler(0f, yaw + _carryYawOffset, 0f);
        return yawTarget * _carryPitchRollOffset;
    }

    /// <summary>
    /// Compute placement rotation for the current carried object on the given surface.
    /// Respects isFlippingRestricted_Wall/Ceiling: when true, the object keeps its
    /// upright (ground) orientation instead of aligning to the surface normal.
    /// </summary>
    Quaternion ComputePlacementRotation(Vector3 projectedForward, Vector3 surfaceNormal)
    {
        Quaternion basePosture = _carriedWo != null ? _carriedWo.defaultPitchRoll : Quaternion.identity;
        float upDot = Vector3.Dot(surfaceNormal, Vector3.up);

        // Classify surface: floor (>0.7), ceiling (<-0.7), wall (between)
        bool isWall = Mathf.Abs(upDot) <= 0.7f;
        bool isCeiling = upDot < -0.7f;

        bool keepUpright = false;
        if (_carriedWo != null)
        {
            if (isWall && _carriedWo.isFlippingRestricted_Wall) keepUpright = true;
            if (isCeiling && _carriedWo.isFlippingRestricted_Ceiling) keepUpright = true;
        }

        if (keepUpright)
        {
            // Keep the object's up = world up. Face the object away from the surface.
            Vector3 outward = -surfaceNormal;
            outward.y = 0f;
            if (outward.sqrMagnitude < 0.001f)
                outward = projectedForward; // pure ceiling/floor edge case
            outward.Normalize();

            // Apply user rotation offset around world up
            Quaternion yawOffset = Quaternion.Euler(0f, _placementRotationOffset, 0f);
            Quaternion uprightFrame = Quaternion.LookRotation(outward, Vector3.up) * yawOffset;
            return uprightFrame * basePosture;
        }
        else
        {
            // Default: align object's up with surface normal (poster behavior)
            Quaternion surfaceFrame = Quaternion.LookRotation(projectedForward, surfaceNormal) 
                                    * Quaternion.Euler(0f, _placementRotationOffset, 0f);
            return surfaceFrame * basePosture;
        }
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
        if (_playerCol == null)
            _playerCol = GetComponent<CapsuleCollider>();

        if (_playerCols == null || _playerCols.Length == 0)
            _playerCols = GetComponentsInChildren<Collider>(true);
    }

    float GetCameraCarryRangeCompensation()
    {
        if (playerCamera == null)
            return 0f;

        EnsurePlayerCollidersCached();
        Vector3 bodyPivot = _playerCol != null ? _playerCol.bounds.center : transform.position;

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
        if (attractAimPoint != null)
            return;

        if (attractAimPoint != null)
            return;

        Transform root = transform;
        if (_playerCol != null)
            root = _playerCol.transform.root;

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
        return _playerCol != null ? _playerCol.transform : transform;
    }

    Vector3 GetCarryReferenceOrigin()
    {
        EnsurePlayerCollidersCached();
        if (_playerCol != null)
            return _playerCol.bounds.center;

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

        if (_playerCol != null && c == _playerCol)
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

        if (_playerCol != null)
        {
            for (int i = 0; i < _carriedCols.Length; i++)
            {
                Collider cc = _carriedCols[i];
                if (cc == null) continue;
                Physics.IgnoreCollision(_playerCol, cc, ignored);
            }
        }

        if (_playerCols != null)
        {
            for (int p = 0; p < _playerCols.Length; p++)
            {
                Collider pc = _playerCols[p];
                if (pc == null || pc == _playerCol) continue;
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

        // When multiple objects are hit, prefer the one most aligned with the exact
        // screen-center ray (smallest angular deviation), with distance as tiebreaker.
        float bestScore = float.PositiveInfinity;
        for (int i = 0; i < hits.Length; i++)
        {
            RaycastHit hit = hits[i];
            Collider c = hit.collider;
            if (c == null) continue;
            if (IsIgnoredCarryHitCollider(c)) continue;

            // Unity SphereCast returns (0,0,0) and distance 0 if the sphere overlaps the collider at the start.
            // This causes straight-on views to fail because distance to origin is evaluated instead of distance to ray.
            Vector3 pointToEvaluate = hit.point;
            if (hit.distance == 0f && hit.point == Vector3.zero)
            {
                pointToEvaluate = c.ClosestPoint(ray.origin);
            }

            // Angular deviation: how far the hit point is from the ray center line
            Vector3 toHit = pointToEvaluate - ray.origin;
            float alongRay = Vector3.Dot(toHit, ray.direction);
            
            // Prevent negative projection if the closest point is slightly behind the camera
            if (alongRay < 0.001f) alongRay = 0.001f; 
            
            Vector3 projected = ray.origin + ray.direction * alongRay;
            float lateralDist = Vector3.Distance(pointToEvaluate, projected);
            
            // Score: prioritize center alignment, use distance as secondary factor
            // lateralDist weighted heavily so centered objects always win
            float score = lateralDist * 10f + hit.distance * 0.1f;

            if (score < bestScore)
            {
                bestScore = score;
                bestHit = hit;
            }
        }

        return bestScore < float.PositiveInfinity;
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
            _carryCandidateRb = null;
            _carryCandidateWo = null;
            return;
        }

        _lookedAt = hit.collider.GetComponentInParent<WorldObject>();
        
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

        // Carry state is strictly bound to the Rigidbody being carried
        worldObject = rb.GetComponent<WorldObject>();
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

            if (_isPlacementMode)
            {
                if (Input.GetKeyDown(KeyCode.R))
                {
                    _timeRButtonPressed = Time.time;
                    if (_mouseLook != null) _mouseLook.suspendMouseLook = true;
                }
                if (Input.GetKey(KeyCode.R))
                {
                    if (Time.time - _timeRButtonPressed > 0.15f)
                    {
                        float mouseX = Input.GetAxis("Mouse X");
                        _placementRotationOffset += mouseX * 300f * Time.deltaTime;
                    }
                }
                if (Input.GetKeyUp(KeyCode.R))
                {
                    if (Time.time - _timeRButtonPressed <= 0.15f)
                    {
                        _placementRotationOffset += 90f;
                    }
                    if (_mouseLook != null) _mouseLook.suspendMouseLook = false;
                }
            }
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
            if (_lookedAt != null && _lookedAt.interactable)
            {
                _lookedAt.TriggerInteract(gameObject);
                _lookedAt.PlayInteractAnim();
                ShowInfo(_lookedAt.interactMessage);
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
            camCtrl.EnterInventoryMode(obj.collectItemData);
        }

        _lookedAt = null;
        obj.PlayCollectAnim(() => Destroy(obj.gameObject));
    }

    public bool HasCarriedObject() => _carriedRb != null;

    public void DropCarriedObjectIfAny()
    {
        if (_carriedRb != null)
            Drop();
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

        bool wasKinematic = _carriedRb.isKinematic;
        if (_carriedWo != null && _carriedWo.isPlacedAndAttached)
        {
            _carriedWo.isPlacedAndAttached = false;
            // If the object normally has gravity or is pushable, don't record the wall-attachment's forced kinematic state
            if (_carriedWo.canBePushed || _carriedRb.useGravity)
                wasKinematic = false;
        }

        _placementRotationOffset = 0f;

        _rbWasKinematic = wasKinematic;
        _rbHadGravity = _carriedRb.useGravity;
        _rbInterpolation = _carriedRb.interpolation;
        _rbCollisionDetectionMode = _carriedRb.collisionDetectionMode;
        _rbOriginalDrag = _carriedRb.drag;
        _rbOriginalAngularDrag = _carriedRb.angularDrag;

        _playerCols = GetComponentsInChildren<Collider>();
        _carriedCols = rb.GetComponentsInChildren<Collider>();
        _carriedRadius = ComputeCarriedRadius();
        ApplyCarryNoFriction();

        Transform playerT = _playerCol != null ? _playerCol.transform : transform;
        if (_carriedWo != null && _carriedWo.isHeavy)
        {
            // Set drag anchor to the closest point on bounds and compute initial leash length
            Collider c = rb.GetComponentInChildren<Collider>();
            Vector3 anchorWorld = c != null ? c.ClosestPoint(playerT.position) : rb.position;
            _dragAnchorLocal = rb.transform.InverseTransformPoint(anchorWorld);
            
            Vector3 pPos = playerT.position; pPos.y = 0;
            Vector3 aPos = anchorWorld; aPos.y = 0;
            _carryRayDistance = Mathf.Max(0.5f, Vector3.Distance(pPos, aPos) - 0.5f);
        }
        else
        {
            Transform camT = playerCamera != null ? playerCamera.transform : transform;
            
            // Record start for transition animation (capture exact world state at pickup)
            _carryTransitionTimer = 0f;
            _carryStartLocalPos = camT.InverseTransformPoint(_carriedTransform.position);
            _carryStartLocalRot = Quaternion.Inverse(camT.rotation) * _carriedTransform.rotation;
 
            // Target Anchor: Start with current distance but target unified direction
            _carryRayDistance = GetUnifiedCarryAnchorLocalOffset().magnitude;
            _carryRayLocalDir = _carryStartLocalPos.normalized;
            _carrySmoothDistance = _carryStartLocalPos.magnitude; // Start smooth dist from current
        }

        Vector3 flatCarryFwd = GetCarryReferenceForward();
        float carryYaw = Mathf.Atan2(flatCarryFwd.x, flatCarryFwd.z) * Mathf.Rad2Deg;
        float objYaw = _carriedRb.rotation.eulerAngles.y;
        _carryYawOffset = Mathf.DeltaAngle(carryYaw, objYaw);
        Quaternion objYawOnly = Quaternion.Euler(0f, objYaw, 0f);
        _carryPitchRollOffset = Quaternion.Inverse(objYawOnly) * _carriedRb.rotation;

        // Ignore player-object collisions for all carried objects.
        // Unity's solver can't handle overlapping carried geometry reliably
        // (causes physics explosions when jumping/tilting). Manual soft separation
        // handles heavy-object blocking instead.
        bool isHeavy = _carriedWo != null && _carriedWo.isHeavy;
        SetCarryPlayerCollisionIgnored(true);

        // Clear velocity BEFORE going kinematic to avoid Unity warnings
        _carriedRb.velocity = Vector3.zero;
        _carriedRb.angularVelocity = Vector3.zero;
        // Non-heavy objects: kinematic during carry → zero physics jitter against walls.
        // Heavy objects: stay dynamic for dragging physics.
        _carriedRb.isKinematic = !isHeavy;
        _carriedRb.useGravity = isHeavy;
        _carriedRb.drag = isHeavy ? 0.5f : 0f;
        _carriedRb.angularDrag = isHeavy ? 0.5f : 0.05f;
        
        _carriedRb.interpolation = RigidbodyInterpolation.Interpolate;
        if (!isHeavy)
        {
            _carriedRb.collisionDetectionMode = CollisionDetectionMode.Discrete;
            // Disable colliders during kinematic carry to prevent pushing player/objects through floor
            SetCarriedCollidersEnabled(false);
        }
        else
        {
            _carriedRb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
        }

        _carriedWo?.SetCarriedState(true);
        _carriedWo?.TriggerPickUp(gameObject);

        PlayerController pc = GetComponent<PlayerController>();
        if (pc != null && _carriedWo != null && _carriedWo.isHeavy)
            pc.SpeedMultiplier = 0.4f;
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

        // Re-enable colliders before restoring physics
        SetCarriedCollidersEnabled(true);

        _carriedRb.isKinematic = _rbWasKinematic;
        _carriedRb.useGravity = _rbHadGravity;
        _carriedRb.interpolation = _rbInterpolation;
        _carriedRb.collisionDetectionMode = _rbCollisionDetectionMode;
        _carriedRb.drag = _rbOriginalDrag;
        _carriedRb.angularDrag = _rbOriginalAngularDrag;

        _carriedWo?.SetCarriedState(false);
        _carriedWo?.TriggerDrop(gameObject);

        PlayerController pc = GetComponent<PlayerController>();
        if (pc != null)
            pc.SpeedMultiplier = 1f;

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

    void SetCarriedCollidersEnabled(bool enabled)
    {
        if (_carriedCols == null) return;
        for (int i = 0; i < _carriedCols.Length; i++)
        {
            if (_carriedCols[i] != null)
                _carriedCols[i].enabled = enabled;
        }
    }

    Vector3 GetPlayerVelocity()
    {
        if (_playerCol != null)
            return _playerCol.GetComponent<Rigidbody>().velocity;

        Rigidbody rb = GetComponent<Rigidbody>();
        return rb != null ? rb.velocity : Vector3.zero;
    }

    void UpdatePrompt()
    {
        SetLabel(carryLabel, _carryCandidateRb != null);
        SetLabel(interactLabel, _lookedAt != null && _lookedAt.interactable);
        SetLabel(collectLabel, _carriedWo != null && _carriedWo.collectable);
        SetLabel(collectLabel, _carriedWo != null && _carriedWo.collectable);
    }

    void SetLabel(TextMeshProUGUI label, bool active)
    {
        if (label != null)
            label.gameObject.SetActive(active);
    }

    public GameObject GetLookedAtTarget()
    {
        if (_isPlacementMode) return null;
        if (_lookedAt != null && _lookedAt.interactable)
            return _lookedAt.gameObject;
        return null;
    }

    public GameObject GetCarryTarget()
    {
        if (_isPlacementMode) return null;
        if (_carryCandidateRb != null)
            return _carryCandidateRb.gameObject;
        return null;
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

    }

    void InitializePlacementMaterials()
    {
        Shader customShader = Shader.Find("Custom/URPPlacementGhost");
        if (customShader != null)
        {
            _placementValidMat = new Material(customShader);
            _placementValidMat.SetColor("_BaseColor", new Color(0.1f, 1f, 0.1f, 0.2f));
            _placementValidMat.SetColor("_ContactColor", new Color(0.1f, 1f, 0.1f, 0.8f));
            _placementValidMat.SetFloat("_FadeDistance", 0.25f);

            _placementInvalidMat = new Material(customShader);
            _placementInvalidMat.SetColor("_BaseColor", new Color(1f, 0.1f, 0.1f, 0.2f));
            _placementInvalidMat.SetColor("_ContactColor", new Color(1f, 0.1f, 0.1f, 0.8f));
            _placementInvalidMat.SetFloat("_FadeDistance", 0.25f);
        }
        else
        {
            _placementValidMat = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
            if (_placementValidMat.shader == null) _placementValidMat = new Material(Shader.Find("Standard"));
            SetMaterialTransparentURP(_placementValidMat, new Color(0.2f, 1f, 0.2f, 0.4f));

            _placementInvalidMat = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
            if (_placementInvalidMat.shader == null) _placementInvalidMat = new Material(Shader.Find("Standard"));
            SetMaterialTransparentURP(_placementInvalidMat, new Color(1f, 0.2f, 0.2f, 0.4f));
        }
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
        
        _placementGhost = Instantiate(_carriedRb.gameObject);
        _placementGhost.name = "PlacementGhost";
        
        // 修复由于脱离原父级导致的缩放丢失问题
        _placementGhost.transform.localScale = _carriedRb.transform.lossyScale;
        
        Destroy(_placementGhost.GetComponent<Rigidbody>());
        foreach(MonoBehaviour mb in _placementGhost.GetComponentsInChildren<MonoBehaviour>())
            mb.enabled = false; // Disable instead of Destroy to avoid "RequiredComponent" dependency errors
            
        Collider[] allCols = _placementGhost.GetComponentsInChildren<Collider>();
        foreach(Collider c in allCols)
        {
            if (c.isTrigger)
            {
                // 原本就是Trigger的碰撞体（如交互区域）不能用于物理防穿模计算，直接删除
                Destroy(c);
            }
            else
            {
                c.enabled = true; // Re-enable in case source had disabled colliders during carry
                c.isTrigger = true;
                c.gameObject.layer = 2; // Ignore Raycast layer
            }
        }
        
        _placementGhost.SetActive(false);
    }

    void ExitPlacementMode()
    {
        if (!_isPlacementMode) return;
        _isPlacementMode = false;
        if (_mouseLook != null) _mouseLook.suspendMouseLook = false;
        
        if (_placementGhost != null) Destroy(_placementGhost);
    }
    
    void UpdatePlacementGhost()
    {
        if (_placementGhost == null || _carriedRb == null) return;
        
        Ray ray = new Ray(GetInteractionRayOrigin(), GetInteractionRayDirection(GetInteractionRayOrigin()));
        EnsurePlayerCollidersCached();
        
        float maxReach = GetEffectiveCarryRangeFromCamera();
        RaycastHit[] hits = Physics.RaycastAll(ray, placementRayRange, placementMask, QueryTriggerInteraction.Ignore);
        
        // Find the nearest valid hit (any surface)
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
            if (nearest > maxReach)
            {
                _placementGhost.SetActive(false);
                _isPlacementValid = false;
                return;
            }

            float dotUp = Vector3.Dot(bestHit.normal, Vector3.up);
            bool isFloor = dotUp > 0.7f;
            bool isCeiling = dotUp < -0.7f;
            bool isWall = !isFloor && !isCeiling;

            bool canPlaceOnSurface = true;
            if (isFloor && (_carriedWo != null && !_carriedWo.canBePlacedOnFloor))
                canPlaceOnSurface = false;
            if (isWall && (_carriedWo == null || !_carriedWo.canBePlacedOnWall))
                canPlaceOnSurface = false;
            if (isCeiling && (_carriedWo == null || !_carriedWo.canBePlacedOnCeiling))
                canPlaceOnSurface = false;

            // If nearest hit is a non-placeable wall, find the floor at the wall base
            if (!canPlaceOnSurface && isWall)
            {
                Vector3 wallFront = bestHit.point + bestHit.normal * 0.02f;
                if (Physics.Raycast(wallFront, Vector3.down, out RaycastHit floorAtBase, 10f, placementMask, QueryTriggerInteraction.Ignore))
                {
                    float floorDot = Vector3.Dot(floorAtBase.normal, Vector3.up);
                    bool baseIsFloor = floorDot > 0.7f;
                    if (baseIsFloor && (_carriedWo == null || _carriedWo.canBePlacedOnFloor))
                    {
                        bestHit = floorAtBase;
                        canPlaceOnSurface = true;
                    }
                }
            }

            if (canPlaceOnSurface)
            {
                _placementGhost.SetActive(true);
                
                // 1. Initial placement on the surface
                _placementPosition = bestHit.point;
                _placementSurfaceNormal = bestHit.normal;

                _placementValidMat.SetVector("_PlaneNormal", bestHit.normal);
                _placementValidMat.SetVector("_PlanePoint", bestHit.point);
                _placementInvalidMat.SetVector("_PlaneNormal", bestHit.normal);
                _placementInvalidMat.SetVector("_PlanePoint", bestHit.point);
                
                Vector3 fwd = Vector3.ProjectOnPlane(GetCarryReferenceForward(), bestHit.normal);
                if (fwd.sqrMagnitude < 0.001f) fwd = GetCarryReferenceForward();
                
                _placementRotation = ComputePlacementRotation(fwd, bestHit.normal);
                
                _placementGhost.transform.position = _placementPosition;
                _placementGhost.transform.rotation = _placementRotation;
                Physics.SyncTransforms();
                
                // ── Primary anti-penetration: push ghost out of the placement surface ──
                float maxPenetration = 0f;
                bool foundCollider = false;
                Plane plane = new Plane(bestHit.normal, bestHit.point);

                foreach(Collider c in _placementGhost.GetComponentsInChildren<Collider>())
                {
                    MeshCollider mc = c as MeshCollider;
                    if (mc != null && !mc.convex)
                    {
                        Vector3 center = c.bounds.center;
                        Vector3 extents = c.bounds.extents;
                        Vector3 lowestCorner = center;
                        lowestCorner.x += bestHit.normal.x < 0 ? extents.x : -extents.x;
                        lowestCorner.y += bestHit.normal.y < 0 ? extents.y : -extents.y;
                        lowestCorner.z += bestHit.normal.z < 0 ? extents.z : -extents.z;

                        float d = plane.GetDistanceToPoint(lowestCorner);
                        if (d < 0 && -d > maxPenetration)
                        {
                            maxPenetration = -d;
                            foundCollider = true;
                        }
                        continue;
                    }

                    Vector3 farPoint = c.bounds.center - bestHit.normal * 1000f;
                    Vector3 extremePoint = c.ClosestPoint(farPoint);
                    float dist = plane.GetDistanceToPoint(extremePoint);
                    if (dist < 0 && -dist > maxPenetration)
                    {
                        maxPenetration = -dist;
                        foundCollider = true;
                    }
                }

                if (foundCollider && maxPenetration > 0f)
                {
                    _placementPosition += bestHit.normal * (maxPenetration + 0.01f);
                    _placementGhost.transform.position = _placementPosition;
                }
                else if (!foundCollider)
                {
                    Bounds bounds = new Bounds(_placementGhost.transform.position, Vector3.zero);
                    bool hasRenderer = false;
                    foreach(Renderer r in _placementGhost.GetComponentsInChildren<Renderer>())
                    {
                        if (hasRenderer) bounds.Encapsulate(r.bounds);
                        else { bounds = r.bounds; hasRenderer = true; }
                    }
                    if (hasRenderer)
                    {
                        Vector3 center = bounds.center;
                        Vector3 extents = bounds.extents;
                        Vector3 lowestCorner = center;
                        lowestCorner.x += bestHit.normal.x < 0 ? extents.x : -extents.x;
                        lowestCorner.y += bestHit.normal.y < 0 ? extents.y : -extents.y;
                        lowestCorner.z += bestHit.normal.z < 0 ? extents.z : -extents.z;
                        float dist = plane.GetDistanceToPoint(lowestCorner);
                        if (dist < 0)
                        {
                            _placementPosition += bestHit.normal * (-dist + 0.01f);
                            _placementGhost.transform.position = _placementPosition;
                        }
                    }
                }
                
                // ── Fix 1: Occlusion check — clamp ghost to wall base if occluded ──
                Physics.SyncTransforms();
                Vector3 ghostCenter = _placementPosition + bestHit.normal * 0.05f;
                Vector3 toGhost = ghostCenter - ray.origin;
                float toGhostDist = toGhost.magnitude;
                if (toGhostDist > 0.01f)
                {
                    RaycastHit[] occlusionHits = Physics.RaycastAll(ray.origin, toGhost / toGhostDist, toGhostDist, placementMask, QueryTriggerInteraction.Ignore);
                    RaycastHit occWallHit = default;
                    bool occluded = false;
                    for (int i = 0; i < occlusionHits.Length; i++)
                    {
                        if (IsIgnoredCarryHitCollider(occlusionHits[i].collider)) continue;
                        if (occlusionHits[i].collider == bestHit.collider) continue;
                        float occDot = Vector3.Dot(occlusionHits[i].normal, Vector3.up);
                        bool occIsWall = Mathf.Abs(occDot) < 0.7f;
                        if (occIsWall)
                        {
                            occWallHit = occlusionHits[i];
                            occluded = true;
                            break;
                        }
                    }
                    if (occluded)
                    {
                        // Clamp ghost to the floor at the wall base instead of hiding
                        Vector3 wallFront = occWallHit.point + occWallHit.normal * 0.02f;
                        if (Physics.Raycast(wallFront, Vector3.down, out RaycastHit cornerFloor, 10f, placementMask, QueryTriggerInteraction.Ignore))
                        {
                            float fDot = Vector3.Dot(cornerFloor.normal, Vector3.up);
                            if (fDot > 0.7f && (_carriedWo == null || _carriedWo.canBePlacedOnFloor))
                            {
                                _placementPosition = cornerFloor.point;
                                _placementSurfaceNormal = cornerFloor.normal;
                                bestHit = cornerFloor;
                                
                                Vector3 fwd2 = Vector3.ProjectOnPlane(GetCarryReferenceForward(), cornerFloor.normal);
                                if (fwd2.sqrMagnitude < 0.001f) fwd2 = GetCarryReferenceForward();
                                _placementRotation = ComputePlacementRotation(fwd2, cornerFloor.normal);
                                
                                _placementGhost.transform.position = _placementPosition;
                                _placementGhost.transform.rotation = _placementRotation;
                                Physics.SyncTransforms();
                                
                                // Re-run primary anti-penetration for the new floor surface
                                Plane floorPlane = new Plane(cornerFloor.normal, cornerFloor.point);
                                float floorPen = 0f;
                                foreach(Collider c in _placementGhost.GetComponentsInChildren<Collider>())
                                {
                                    Vector3 fp = c.bounds.center - cornerFloor.normal * 1000f;
                                    Vector3 ep = c.ClosestPoint(fp);
                                    float dd = floorPlane.GetDistanceToPoint(ep);
                                    if (dd < 0 && -dd > floorPen) floorPen = -dd;
                                }
                                if (floorPen > 0f)
                                {
                                    _placementPosition += cornerFloor.normal * (floorPen + 0.01f);
                                    _placementGhost.transform.position = _placementPosition;
                                }
                            }
                            else
                            {
                                _placementGhost.SetActive(false);
                                _isPlacementValid = false;
                                return;
                            }
                        }
                        else
                        {
                            _placementGhost.SetActive(false);
                            _isPlacementValid = false;
                            return;
                        }
                    }
                }
                
                // ── Fix 3: Corner anti-clip — raycast from center to all 8 AABB corners ──
                // If a ray from center to corner hits a non-placement surface,
                // the corner extends past that surface. Push ghost out along hit normal.
                // This handles walls at ANY angle, not just cardinal directions.
                Bounds ghostBounds = new Bounds(_placementGhost.transform.position, Vector3.zero);
                bool hasBounds = false;
                foreach(Renderer r in _placementGhost.GetComponentsInChildren<Renderer>())
                {
                    if (hasBounds) ghostBounds.Encapsulate(r.bounds);
                    else { ghostBounds = r.bounds; hasBounds = true; }
                }
                
                if (hasBounds)
                {
                    // Run up to 3 iterations to resolve multi-surface corners
                    for (int iteration = 0; iteration < 3; iteration++)
                    {
                        Vector3 gc = ghostBounds.center;
                        Vector3 ge = ghostBounds.extents;
                        
                        Vector3[] corners = {
                            gc + new Vector3(-ge.x, -ge.y, -ge.z),
                            gc + new Vector3( ge.x, -ge.y, -ge.z),
                            gc + new Vector3(-ge.x,  ge.y, -ge.z),
                            gc + new Vector3( ge.x,  ge.y, -ge.z),
                            gc + new Vector3(-ge.x, -ge.y,  ge.z),
                            gc + new Vector3( ge.x, -ge.y,  ge.z),
                            gc + new Vector3(-ge.x,  ge.y,  ge.z),
                            gc + new Vector3( ge.x,  ge.y,  ge.z)
                        };
                        
                        float worstPen = 0f;
                        Vector3 worstNormal = Vector3.zero;
                        
                        for (int ci = 0; ci < corners.Length; ci++)
                        {
                            Vector3 dir = corners[ci] - gc;
                            float dist = dir.magnitude;
                            if (dist < 0.001f) continue;
                            dir /= dist;
                            
                            if (Physics.Raycast(gc, dir, out RaycastHit cHit, dist, placementMask, QueryTriggerInteraction.Ignore))
                            {
                                if (cHit.collider == bestHit.collider) continue;
                                if (cHit.collider.transform.IsChildOf(_placementGhost.transform)) continue;
                                if (IsIgnoredCarryHitCollider(cHit.collider)) continue;
                                
                                float pen = dist - cHit.distance;
                                if (pen > worstPen)
                                {
                                    worstPen = pen;
                                    worstNormal = cHit.normal;
                                }
                            }
                        }
                        
                        if (worstPen < 0.001f) break; // No penetration found, done
                        
                        _placementPosition += worstNormal * (worstPen + 0.01f);
                        _placementGhost.transform.position = _placementPosition;
                        Physics.SyncTransforms();
                        
                        // Recompute bounds for next iteration
                        ghostBounds = new Bounds(_placementGhost.transform.position, Vector3.zero);
                        foreach(Renderer r in _placementGhost.GetComponentsInChildren<Renderer>())
                            ghostBounds.Encapsulate(r.bounds);
                    }
                }
                
                CheckPlacementValidity();
            }
            else
            {
                _placementGhost.SetActive(false);
                _isPlacementValid = false;
            }
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
        
        if (_isPlacementValid && hasBounds)
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
        WorldObject wo = _carriedWo;
        Vector3 finalPos = _placementPosition;
        Quaternion finalRot = _placementRotation;
        
        bool attachToSurface = false;
        if (wo != null)
        {
            float dotUp = Vector3.Dot(_placementSurfaceNormal, Vector3.up);
            bool isFloor = dotUp > 0.7f;
            if (!isFloor)
                attachToSurface = true;
        }
        
        ExitPlacementMode();
        Drop();
        
        if (rb != null)
        {
            rb.transform.position = finalPos;
            rb.transform.rotation = finalRot;
            Physics.SyncTransforms();
            
            if (!rb.isKinematic)
            {
                rb.velocity = Vector3.zero;
                rb.angularVelocity = Vector3.zero;
            }

            if (attachToSurface && wo != null)
            {
                wo.isPlacedAndAttached = true;
                rb.isKinematic = true;
            }
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
