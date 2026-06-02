using System.Collections;
using System.Collections.Generic;
using UnityEngine;

[DisallowMultipleComponent]
public class DroneControl : MonoBehaviour
{
    [System.Serializable]
    public struct ArmPoseLink
    {
        public Transform arm;
        public Transform targetPose;
    }

    [Header("Transforms")]
    [SerializeField] private Transform movementRoot;
    [SerializeField] private Transform bodyVisual;
    [SerializeField] private Transform altitudeRayOrigin;

    [Header("Movement")]
    [SerializeField] private float horizontalSpeed = 5f;
    [SerializeField] private float verticalSpeed = 3f;
    [SerializeField] private float yawSpeedDeg = 120f;

    [Header("Tilt (Visual)")]
    [SerializeField] private float maxPitchDeg = 15f;
    [SerializeField] private float maxRollDeg = 15f;
    [SerializeField] private float tiltResponsiveness = 10f;

    [Header("Propellers")]
    [SerializeField] private Transform[] propellers;
    [SerializeField] private float propellerSpinDegPerSec = 1800f;

    [Header("Altitude Ray")]
    [SerializeField] private float altitudeRayMaxDistance = 50f;
    [SerializeField] private LayerMask altitudeRayLayers = ~0;

    [Header("Arms (Auto by Altitude)")]
    [SerializeField] private ArmPoseLink[] arms;
    [SerializeField] private float armDeployDistance = 2.0f;
    [SerializeField] private float armRetractDistance = 2.5f;
    [SerializeField] private float armPoseLerpSpeed = 10f;

    public float CurrentAltitude { get; private set; } = float.PositiveInfinity;
    public bool HasAltitudeHit { get; private set; }

    private Vector3[] _armBaseLocalPositions;
    private Quaternion[] _armBaseLocalRotations;
    private float _yawDeg;
    private bool _armsDeployed;
    private Quaternion _bodyBaseLocalRotation = Quaternion.identity;
    private readonly RaycastHit[] _altitudeHits = new RaycastHit[16];

    private void Awake()
    {
        if (movementRoot == null) movementRoot = transform;
        if (altitudeRayOrigin == null) altitudeRayOrigin = movementRoot;

        if (bodyVisual == movementRoot) bodyVisual = null;
        if (bodyVisual != null) _bodyBaseLocalRotation = bodyVisual.localRotation;

        _yawDeg = movementRoot.eulerAngles.y;

        CacheArmBasePoses();
    }

    private void CacheArmBasePoses()
    {
        if (arms == null || arms.Length == 0)
        {
            _armBaseLocalPositions = null;
            _armBaseLocalRotations = null;
            return;
        }

        _armBaseLocalPositions = new Vector3[arms.Length];
        _armBaseLocalRotations = new Quaternion[arms.Length];

        for (int i = 0; i < arms.Length; i++)
        {
            Transform arm = arms[i].arm;
            if (arm == null)
            {
                _armBaseLocalPositions[i] = Vector3.zero;
                _armBaseLocalRotations[i] = Quaternion.identity;
                continue;
            }

            _armBaseLocalPositions[i] = arm.localPosition;
            _armBaseLocalRotations[i] = arm.localRotation;
        }
    }

    private void Update()
    {
        float dt = Time.deltaTime;

        ReadAltitude();
        UpdateMovement(dt);
        UpdateTilt(dt);
        UpdatePropellers(dt);
        UpdateArms(dt);
    }

    private void ReadAltitude()
    {
        HasAltitudeHit = false;
        CurrentAltitude = float.PositiveInfinity;

        if (altitudeRayOrigin == null) return;

        Vector3 origin = altitudeRayOrigin.position;

        int hitCount = Physics.RaycastNonAlloc(
            origin,
            Vector3.down,
            _altitudeHits,
            altitudeRayMaxDistance,
            altitudeRayLayers,
            QueryTriggerInteraction.Ignore);

        float best = float.PositiveInfinity;
        for (int i = 0; i < hitCount; i++)
        {
            Collider c = _altitudeHits[i].collider;
            if (c == null) continue;

            Transform hitTransform = c.transform;
            if (movementRoot != null && (hitTransform == movementRoot || hitTransform.IsChildOf(movementRoot)))
            {
                continue;
            }

            float d = _altitudeHits[i].distance;
            if (d < best) best = d;
        }

        if (!float.IsPositiveInfinity(best))
        {
            HasAltitudeHit = true;
            CurrentAltitude = best;
        }
    }

    private void UpdateMovement(float dt)
    {
        float inputX = 0f;
        float inputZ = 0f;
        float inputY = 0f;

        if (Input.GetKey(KeyCode.A)) inputX -= 1f;
        if (Input.GetKey(KeyCode.D)) inputX += 1f;
        if (Input.GetKey(KeyCode.W)) inputZ += 1f;
        if (Input.GetKey(KeyCode.S)) inputZ -= 1f;

        if (Input.GetKey(KeyCode.UpArrow)) inputY += 1f;
        if (Input.GetKey(KeyCode.DownArrow)) inputY -= 1f;

        float yawInput = 0f;
        if (Input.GetKey(KeyCode.Q)) yawInput -= 1f;
        if (Input.GetKey(KeyCode.E)) yawInput += 1f;

        _yawDeg = Mathf.Repeat(_yawDeg + yawInput * yawSpeedDeg * dt, 360f);
        Quaternion yawRotation = Quaternion.Euler(0f, _yawDeg, 0f);

        Vector3 planarMove = new Vector3(inputX, 0f, inputZ);
        planarMove = Vector3.ClampMagnitude(planarMove, 1f);
        Vector3 worldPlanarMove = yawRotation * planarMove;
        Vector3 worldVerticalMove = Vector3.up * Mathf.Clamp(inputY, -1f, 1f);

        Vector3 velocity = worldPlanarMove * horizontalSpeed + worldVerticalMove * verticalSpeed;
        movementRoot.position += velocity * dt;

        if (bodyVisual != null)
        {
            movementRoot.rotation = yawRotation;
        }
        else
        {
            float targetPitch = -planarMove.z * maxPitchDeg;
            float targetRoll = -planarMove.x * maxRollDeg;
            Quaternion tiltRotation = Quaternion.Euler(targetPitch, 0f, targetRoll);
            movementRoot.rotation = yawRotation * tiltRotation;
        }
    }

    private void UpdateTilt(float dt)
    {
        if (bodyVisual == null) return;

        float inputX = 0f;
        float inputZ = 0f;

        if (Input.GetKey(KeyCode.A)) inputX -= 1f;
        if (Input.GetKey(KeyCode.D)) inputX += 1f;
        if (Input.GetKey(KeyCode.W)) inputZ += 1f;
        if (Input.GetKey(KeyCode.S)) inputZ -= 1f;

        Vector3 planarMove = Vector3.ClampMagnitude(new Vector3(inputX, 0f, inputZ), 1f);
        float targetPitch = -planarMove.z * maxPitchDeg;
        float targetRoll = -planarMove.x * maxRollDeg;
        Quaternion targetLocal = _bodyBaseLocalRotation * Quaternion.Euler(targetPitch, 0f, targetRoll);

        float t = 1f - Mathf.Exp(-Mathf.Max(0.01f, tiltResponsiveness) * dt);
        bodyVisual.localRotation = Quaternion.Slerp(bodyVisual.localRotation, targetLocal, t);
    }

    private void UpdatePropellers(float dt)
    {
        if (propellers == null || propellers.Length == 0) return;

        float angle = propellerSpinDegPerSec * dt;
        for (int i = 0; i < propellers.Length; i++)
        {
            Transform p = propellers[i];
            if (p == null) continue;
            p.Rotate(0f, angle, 0f, Space.Self);
        }
    }

    private void UpdateArms(float dt)
    {
        if (arms == null || arms.Length == 0) return;
        if (_armBaseLocalPositions == null || _armBaseLocalRotations == null) CacheArmBasePoses();
        if (_armBaseLocalPositions == null || _armBaseLocalRotations == null) return;

        bool hasAlt = HasAltitudeHit;
        float alt = CurrentAltitude;

        if (!hasAlt)
        {
            _armsDeployed = false;
        }
        else
        {
            float deploy = Mathf.Max(0f, armDeployDistance);
            float retract = Mathf.Max(deploy, armRetractDistance);

            if (_armsDeployed)
            {
                if (alt > retract) _armsDeployed = false;
            }
            else
            {
                if (alt < deploy) _armsDeployed = true;
            }
        }

        float t = 1f - Mathf.Exp(-Mathf.Max(0.01f, armPoseLerpSpeed) * dt);
        for (int i = 0; i < arms.Length; i++)
        {
            Transform arm = arms[i].arm;
            if (arm == null) continue;

            Vector3 targetPos;
            Quaternion targetRot;

            if (_armsDeployed)
            {
                Transform targetPose = arms[i].targetPose;
                if (targetPose != null)
                {
                    targetPos = targetPose.localPosition;
                    targetRot = targetPose.localRotation;
                }
                else
                {
                    targetPos = _armBaseLocalPositions[i];
                    targetRot = _armBaseLocalRotations[i];
                }
            }
            else
            {
                targetPos = _armBaseLocalPositions[i];
                targetRot = _armBaseLocalRotations[i];
            }

            arm.localPosition = Vector3.Lerp(arm.localPosition, targetPos, t);
            arm.localRotation = Quaternion.Slerp(arm.localRotation, targetRot, t);
        }
    }
}
