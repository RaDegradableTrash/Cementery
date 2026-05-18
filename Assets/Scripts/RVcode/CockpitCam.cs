using UnityEngine;

public class CockpitCam : MonoBehaviour
{
	private enum RaycastMode
	{
		RayOriginForward,
		CameraForward,
		ScreenCenter
	}

	[Header("Camera")]
	[SerializeField] private Camera cockpitCamera;
	[SerializeField] private bool onlyWhenCameraEnabled = true;

	[Header("Look")]
	[SerializeField] private bool toggleLookWithClick = true;
	[SerializeField] private KeyCode exitLookKey = KeyCode.Escape;
	[SerializeField] private float lookSensitivity = 2f;
	[SerializeField] private bool invertY = false;
	[SerializeField] private float minPitch = -60f;
	[SerializeField] private float maxPitch = 60f;
	[SerializeField] private Transform rotationRoot;
	[SerializeField] private bool manageCursor = true;

	[Header("Interaction")]
	[SerializeField] private Transform rayOrigin;
	[SerializeField] private float interactDistance = 3f;
	[SerializeField] private LayerMask interactMask = ~0;
	[SerializeField] private KeyCode interactKey = KeyCode.E;
	[SerializeField] private RaycastMode rayMode = RaycastMode.ScreenCenter;

	[Header("Ray Debug")]
	[SerializeField] private bool debugDrawRay = false;
	[SerializeField] private bool debugDrawHitPoint = false;
	[SerializeField] private Color debugRayColor = new Color(0.2f, 0.9f, 1f, 1f);
	[SerializeField] private Color debugHitColor = new Color(0.2f, 1f, 0.4f, 1f);
	[SerializeField] private float debugHitSize = 0.05f;
	[SerializeField] private bool useLineRenderer = false;
	[SerializeField] private bool lineOnlyWhenLooking = false;
	[SerializeField] private bool autoCreateLineRenderer = true;
	[SerializeField] private LineRenderer rayLine;
	[SerializeField] private Color lineRendererColor = new Color(0.2f, 0.9f, 1f, 1f);
	[SerializeField] private float lineRendererWidth = 0.01f;

	private bool isLooking;
	private float yaw;
	private float pitch;
	private bool lastRayHit;
	private Vector3 lastRayOrigin;
	private Vector3 lastRayDirection;
	private float lastRayDistance;
	private Vector3 lastRayHitPoint;

	private void Awake()
	{
		if (cockpitCamera == null)
		{
			cockpitCamera = GetComponentInChildren<Camera>();
		}

		if (rotationRoot == null)
		{
			rotationRoot = transform;
		}

		if (rayOrigin == null && cockpitCamera != null)
		{
			rayOrigin = cockpitCamera.transform;
		}

		CacheAngles();
		SetupLineRenderer();
		SetLook(false);
	}

	private void OnEnable()
	{
		CacheAngles();
	}

	private void Update()
	{
		if (!IsCameraActive())
		{
			if (isLooking)
			{
				SetLook(false);
			}
			SetRayLineActive(false);
			return;
		}

		if (Input.GetMouseButtonDown(0))
		{
			if (toggleLookWithClick)
			{
				SetLook(!isLooking);
			}
			else
			{
				SetLook(true);
			}
		}

		if (isLooking && Input.GetKeyDown(exitLookKey))
		{
			SetLook(false);
		}

		if (isLooking)
		{
			UpdateLook();
		}

		ICockpitInteractable target = GetLookTarget();
		UpdateRayDebug();
		if (target != null && Input.GetKeyDown(interactKey))
		{
			target.Interact();
		}
	}

	private bool IsCameraActive()
	{
		if (cockpitCamera == null)
		{
			return false;
		}

		if (!cockpitCamera.gameObject.activeInHierarchy)
		{
			return false;
		}

		if (onlyWhenCameraEnabled && !cockpitCamera.enabled)
		{
			return false;
		}

		return true;
	}

	private void UpdateLook()
	{
		float mouseX = Input.GetAxis("Mouse X");
		float mouseY = Input.GetAxis("Mouse Y");

		yaw += mouseX * lookSensitivity;
		float yDelta = mouseY * lookSensitivity * (invertY ? 1f : -1f);
		pitch = Mathf.Clamp(pitch + yDelta, minPitch, maxPitch);

		rotationRoot.localRotation = Quaternion.Euler(pitch, yaw, 0f);
	}

	private void CacheAngles()
	{
		Vector3 euler = rotationRoot.localEulerAngles;
		yaw = euler.y;
		pitch = NormalizeAngle(euler.x);
	}

	private void SetLook(bool value)
	{
		isLooking = value;
		if (!manageCursor)
		{
			return;
		}
		Cursor.visible = !value;
		Cursor.lockState = value ? CursorLockMode.Locked : CursorLockMode.None;
	}

	private ICockpitInteractable GetLookTarget()
	{
		if (!TryBuildRay(out Ray ray))
		{
			return null;
		}

		lastRayOrigin = ray.origin;
		lastRayDirection = ray.direction;
		lastRayDistance = interactDistance;
		lastRayHit = false;
		lastRayHitPoint = ray.origin + ray.direction * interactDistance;

		RaycastHit[] hits = Physics.RaycastAll(ray, interactDistance, interactMask, QueryTriggerInteraction.Collide);
		if (hits == null || hits.Length == 0)
		{
			return null;
		}

		float nearestAnyHit = float.MaxValue;
		float bestDistance = float.MaxValue;
		ICockpitInteractable bestTarget = null;

		foreach (RaycastHit hit in hits)
		{
			if (hit.collider != null && hit.distance < nearestAnyHit)
			{
				nearestAnyHit = hit.distance;
				lastRayHitPoint = hit.point;
				lastRayHit = true;
			}

			if (hit.collider == null || !hit.collider.isTrigger)
			{
				continue;
			}

			if (hit.distance >= bestDistance)
			{
				continue;
			}

			ICockpitInteractable candidate = hit.collider.GetComponentInParent<ICockpitInteractable>();
			if (candidate != null)
			{
				bestDistance = hit.distance;
				bestTarget = candidate;
			}
		}

		return bestTarget;
	}

	private bool TryBuildRay(out Ray ray)
	{
		switch (rayMode)
		{
			case RaycastMode.RayOriginForward:
				if (rayOrigin == null)
				{
					ray = default;
					return false;
				}
				ray = new Ray(rayOrigin.position, rayOrigin.forward);
				return true;
			case RaycastMode.CameraForward:
				if (cockpitCamera == null)
				{
					ray = default;
					return false;
				}
				ray = new Ray(cockpitCamera.transform.position, cockpitCamera.transform.forward);
				return true;
			case RaycastMode.ScreenCenter:
				if (cockpitCamera == null)
				{
					ray = default;
					return false;
				}
				ray = cockpitCamera.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0f));
				return true;
			default:
				ray = default;
				return false;
		}
	}

	private void UpdateRayDebug()
	{
		if (debugDrawRay)
		{
			Debug.DrawRay(lastRayOrigin, lastRayDirection * lastRayDistance, debugRayColor, 0f, false);
		}

		if (debugDrawHitPoint && lastRayHit)
		{
			DrawDebugCross(lastRayHitPoint, debugHitSize, debugHitColor);
		}

		bool allowLine = useLineRenderer && (!lineOnlyWhenLooking || isLooking);
		if (!allowLine)
		{
			SetRayLineActive(false);
			return;
		}

		SetupLineRenderer();
		if (rayLine == null)
		{
			return;
		}

		SetRayLineActive(true);
		rayLine.startWidth = lineRendererWidth;
		rayLine.endWidth = lineRendererWidth;
		rayLine.startColor = lineRendererColor;
		rayLine.endColor = lineRendererColor;
		rayLine.positionCount = 2;
		rayLine.SetPosition(0, lastRayOrigin);
		rayLine.SetPosition(1, lastRayHit ? lastRayHitPoint : lastRayOrigin + lastRayDirection * lastRayDistance);
	}

	private void SetupLineRenderer()
	{
		if (!useLineRenderer || rayLine != null || !autoCreateLineRenderer)
		{
			return;
		}

		GameObject lineObject = new GameObject("CockpitCam_RayLine");
		lineObject.transform.SetParent(transform, false);
		rayLine = lineObject.AddComponent<LineRenderer>();
		rayLine.useWorldSpace = true;
		rayLine.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
		rayLine.receiveShadows = false;
		rayLine.material = new Material(Shader.Find("Sprites/Default"));
	}

	private void SetRayLineActive(bool active)
	{
		if (rayLine != null)
		{
			rayLine.enabled = active;
		}
	}

	private static void DrawDebugCross(Vector3 point, float size, Color color)
	{
		float half = size * 0.5f;
		Debug.DrawLine(point - Vector3.right * half, point + Vector3.right * half, color, 0f, false);
		Debug.DrawLine(point - Vector3.up * half, point + Vector3.up * half, color, 0f, false);
		Debug.DrawLine(point - Vector3.forward * half, point + Vector3.forward * half, color, 0f, false);
	}

	private static float NormalizeAngle(float angle)
	{
		if (angle > 180f)
		{
			angle -= 360f;
		}
		return angle;
	}
}
