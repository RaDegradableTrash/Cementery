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
	[SerializeField] private WorldObject interactTarget;
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
	private ICockpitHighlightable currentHighlight;

	private GameObject activePlayer;
	private bool isDriving;
	private CarControl carControl;

	private Transform originalCameraParent;
	private Vector3 originalCameraLocalPos;
	private Quaternion originalCameraLocalRot;

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
		carControl = GetComponentInParent<CarControl>();
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

		if (Input.GetKeyDown(exitLookKey))
		{
			if (isLooking)
			{
				SetLook(false);
			}
		}

		if (isDriving && Input.GetKeyDown(KeyCode.BackQuote))
		{
			ExitDrivingMode();
		}

		if (isLooking)
		{
			UpdateLook();
		}

		ICockpitHighlightable highlight;
		ICockpitInteractable target = GetLookTarget(out highlight);
		UpdateHighlight(highlight);
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

	private ICockpitInteractable GetLookTarget(out ICockpitHighlightable highlight)
	{
		highlight = null;
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
				highlight = hit.collider.GetComponentInParent<ICockpitHighlightable>();
			}
		}

		return bestTarget;
	}

	private void UpdateHighlight(ICockpitHighlightable highlight)
	{
		if (currentHighlight == highlight)
		{
			return;
		}

		if (currentHighlight != null)
		{
			currentHighlight.SetHighlighted(false);
		}

		currentHighlight = highlight;
		if (currentHighlight != null)
		{
			currentHighlight.SetHighlighted(true);
		}
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

	private void Start()
	{
		if (interactTarget != null)
		{
			interactTarget.onInteract.AddListener(OnPlayerInteract);
		}
		else
		{
			WorldObject wo = GetComponent<WorldObject>();
			if (wo == null)
			{
				wo = gameObject.AddComponent<WorldObject>();
				wo.interactable = true;
				wo.interactMessage = "Drive RV";
			}
			interactTarget = wo;
			interactTarget.onInteract.AddListener(OnPlayerInteract);
		}
	}

	private void OnPlayerInteract(GameObject actor)
	{
		if (isDriving || actor == null) return;

		activePlayer = actor;

		// 1. Strictly find the Camera component inside the Player GameObject!
		Camera mainCam = activePlayer.GetComponentInChildren<Camera>(true);

		if (mainCam != null)
		{
			// 2. Save original parent and transform values
			originalCameraParent = mainCam.transform.parent;
			originalCameraLocalPos = mainCam.transform.localPosition;
			originalCameraLocalRot = mainCam.transform.localRotation;

			// 3. Temporarily disable the MouseLook script on the main camera to stop it reading player mouse input
			MouseLook ml = mainCam.GetComponent<MouseLook>();
			if (ml != null)
			{
				ml.enabled = false;
			}

			// 4. Reparent the unique Main Camera to our cockpit rotation root so it moves/rotates with the cockpit
			mainCam.transform.SetParent(rotationRoot);
			mainCam.transform.localPosition = Vector3.zero;
			mainCam.transform.localRotation = Quaternion.identity;

			// Keep reference to main camera as our active cockpit camera
			cockpitCamera = mainCam;
		}

		// 5. Hide player (no need to parent to vehicle to avoid Netcode NotListeningException)
		activePlayer.SetActive(false);

		// Give vehicle control
		if (carControl == null)
		{
			carControl = GetComponentInParent<CarControl>();
		}
		if (carControl != null)
		{
			carControl.ActiveControl = true;
		}

		isDriving = true;
		SetLook(true);
	}

	private void ExitDrivingMode()
	{
		if (!isDriving) return;

		isDriving = false;
		SetLook(false);

		// Disable vehicle control
		if (carControl != null)
		{
			carControl.ActiveControl = false;
		}

		// 1. Restore the Main Camera back to the player
		if (cockpitCamera != null)
		{
			// Re-enable the MouseLook script
			MouseLook ml = cockpitCamera.GetComponent<MouseLook>();
			if (ml != null)
			{
				ml.enabled = true;
			}

			if (originalCameraParent != null)
			{
				cockpitCamera.transform.SetParent(originalCameraParent);
				cockpitCamera.transform.localPosition = originalCameraLocalPos;
				cockpitCamera.transform.localRotation = originalCameraLocalRot;
			}
		}

		// 2. Reactivate player and set their position to the vehicle's current position
		if (activePlayer != null)
		{
			// Move player slightly back to step out of the driver's seat into the RV living cabin space!
			activePlayer.transform.position = transform.position - transform.forward * 1.0f + transform.up * 0.1f;
			activePlayer.SetActive(true);
		}

		activePlayer = null;
		cockpitCamera = null;
		originalCameraParent = null;
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
