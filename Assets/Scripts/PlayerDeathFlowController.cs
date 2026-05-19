using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Handles death flow when player Y falls below a threshold:
/// 1) Freeze camera at death position while forcing it to keep looking at the player body.
/// 2) Show stylized death UI (left/right text slide-in + hint + revive button).
/// 3) Revive by teleporting back to initial spawn without reloading scene.
/// </summary>
[DefaultExecutionOrder(600)]
public class PlayerDeathFlowController : MonoBehaviour
{
    public static bool IsPlayerDead { get; private set; }

    [Header("Death Trigger")]
    [SerializeField] private float deathY = -50f;

    [Header("Camera")]
    [SerializeField] private Camera mainCamera;
    [SerializeField] private Transform lookTarget;
    [SerializeField] private bool lockCameraPositionOnDeath = true;
    [SerializeField] private float cameraLookSpeed = 4f;

    [Header("UI References")]
    [SerializeField] private TextMeshProUGUI leftDeathTMP;
    [SerializeField] private TextMeshProUGUI rightDeathTMP;
    [SerializeField] private TextMeshProUGUI deathHintTMP;
    [SerializeField] private Button reviveButton;

    [Header("UI Layout")]
    [SerializeField] private float sideStartOffsetX = 220f;

    [Header("UI Timing")]
    [SerializeField] private float sideSlideDuration = 0.65f;
    [SerializeField] private float hintFadeDuration = 0.35f;
    [SerializeField] private float buttonFadeDuration = 0.35f;

    [Header("UI Sequence")]

    [SerializeField, Range(0f, 0.9f)] private float rightStartAtLeftProgress = 0.4f;

    [Header("Cinematic Letterbox")]
    [SerializeField] private RectTransform topLetterbox;
    [SerializeField] private RectTransform bottomLetterbox;
    [SerializeField, Range(0f, 0.45f)] private float letterboxHeightRatio = 0.13f;
    [SerializeField] private float letterboxAnimDuration = 0.45f;
    [SerializeField] private Color letterboxColor = Color.black;

    [Header("UI Blur Reveal")]
    [SerializeField] private float blurStartScale = 1.08f;
    [SerializeField] private float blurStartFaceDilateBoost = 0.14f;
    [SerializeField] private float blurOutlineToFaceCompensation = 0.45f;
    [SerializeField] private float blurStartOutlineWidth = 0.38f;
    [SerializeField] private float blurStartOutlineSoftness = 0.9f;
    [SerializeField] private float blurStartUnderlayDilate = 0.28f;
    [SerializeField] private float blurStartUnderlaySoftness = 0.85f;
    [SerializeField] private float blurStartUnderlayAlpha = 0.34f;

    [Header("Proxy Drone")]
    public GameObject proxyDronePrefab;
    private GameObject _activeDrone;

    private Transform _playerRoot;
    private Vector3 _spawnPos;
    private Quaternion _spawnRot;
    private Vector3 _frozenCameraPos;
    private bool _isDead;
    private float _reviveProtectionTimer = 0f;
    private Vector3 _cameraOriginalLocalPos;
    private Quaternion _cameraOriginalLocalRot;
    private Transform _cameraOriginalParent;
    private Coroutine _uiCo;

    private enum TrapDeathState { None, Falling, Extracting }
    private TrapDeathState _trapDeathState = TrapDeathState.None;
    private Transform _corpseHeadTarget;
    private Vector3 _trapDeathCameraStartPos;
    private Vector3 _trapDeathCameraTargetPos;
    private float _trapDeathTimer;
    private Camera _deathCamera; // 专用于灵魂抽离的独立摄像机

    private PlayerController _playerController;
    private MouseLook _mouseLook;
    private InteractionSystem _interactionSystem;
    private InventoryCameraController _inventoryCameraController;

    private Canvas _canvas;
    private RectTransform _leftRt;
    private RectTransform _rightRt;
    private RectTransform _hintRt;
    private RectTransform _buttonRt;
    private TextMeshProUGUI _leftLabel;
    private TextMeshProUGUI _rightLabel;
    private TextMeshProUGUI _hintLabel;
    private Button _reviveButton;
    private CanvasGroup _leftCg;
    private CanvasGroup _rightCg;
    private CanvasGroup _hintCg;
    private CanvasGroup _buttonCg;
    private Vector2 _leftFinalAnchoredPos;
    private Vector2 _rightFinalAnchoredPos;
    private bool _capturedUiLayout;
    private RectTransform _topLetterboxRt;
    private RectTransform _bottomLetterboxRt;

    private TextMeshProUGUI _buttonLabel;
    private Material _leftMaterial;
    private Material _rightMaterial;
    private Material _hintMaterial;
    private Material _buttonLabelMaterial;
    private float _leftBaseFaceDilate;
    private float _rightBaseFaceDilate;
    private float _hintBaseFaceDilate;
    private float _buttonLabelBaseFaceDilate;
    private float _leftBaseOutlineSoftness;
    private float _rightBaseOutlineSoftness;
    private float _hintBaseOutlineSoftness;
    private float _buttonLabelBaseOutlineSoftness;
    private float _leftBaseOutlineWidth;
    private float _rightBaseOutlineWidth;
    private float _hintBaseOutlineWidth;
    private float _buttonLabelBaseOutlineWidth;
    private float _leftBaseUnderlayDilate;
    private float _rightBaseUnderlayDilate;
    private float _hintBaseUnderlayDilate;
    private float _buttonLabelBaseUnderlayDilate;
    private float _leftBaseUnderlaySoftness;
    private float _rightBaseUnderlaySoftness;
    private float _hintBaseUnderlaySoftness;
    private float _buttonLabelBaseUnderlaySoftness;
    private Color _leftBaseUnderlayColor;
    private Color _rightBaseUnderlayColor;
    private Color _hintBaseUnderlayColor;
    private Color _buttonLabelBaseUnderlayColor;

    private RenderMode _originalCanvasRenderMode;
    private Camera _originalCanvasWorldCamera;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void AutoBootstrap()
    {
        if (FindObjectOfType<PlayerDeathFlowController>() != null)
            return;

        GameObject go = new GameObject("PlayerDeathFlowController");
        go.AddComponent<PlayerDeathFlowController>();
    }

    private void Awake()
    {
        ResolveRuntimeReferences();
        EnsureUi();
        HideDeathUiImmediate();

        if (_reviveButton != null)
        {
            _reviveButton.onClick.RemoveListener(OnReviveClicked);
            _reviveButton.onClick.AddListener(OnReviveClicked);
        }

        if (_playerRoot != null)
        {
            _spawnPos = _playerRoot.position;
            _spawnRot = _playerRoot.rotation;

            // 尝试通过射线找到真正的地面，防止初始位置在半空中（如果初始掉落距离超过100可能会失败）
            CharacterController cc = _playerRoot.GetComponent<CharacterController>();
            if (cc != null) cc.enabled = false; // 临时关闭碰撞体防止射线扫到自己

            if (Physics.Raycast(_spawnPos + Vector3.up * 0.1f, Vector3.down, out RaycastHit hit, Mathf.Infinity, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
            {
                if (cc != null)
                {
                    // 计算 Pivot 到胶囊体底部的距离
                    float pivotToBottom = (cc.height / 2f) - cc.center.y;
                    _spawnPos = hit.point + Vector3.up * (pivotToBottom + cc.skinWidth + 0.05f);
                }
                else
                {
                    _spawnPos = hit.point + Vector3.up * 0.05f;
                }
            }
            
            if (cc != null) cc.enabled = true;
        }
    }

    private void Update()
    {
        if (_isDead || _playerRoot == null)
            return;

        if (_reviveProtectionTimer > 0f)
        {
            _reviveProtectionTimer -= Time.deltaTime;
            return;
        }

        if (_playerRoot.position.y <= deathY)
        {
            if (_playerController != null) _playerController.hp = 0;
            EnterDeathState();
        }
    }

    private void LateUpdate()
    {
        // 🌟 Keep MainCamera in the player's perspective (attached to the player's head) until they press "Respawn"
        // No custom position freezing or camera detaching during the death state
    }

    private void OnDestroy()
    {
        if (_reviveButton != null)
            _reviveButton.onClick.RemoveListener(OnReviveClicked);
    }

    private void ResolveRuntimeReferences()
    {
        if (_playerController == null)
            _playerController = FindObjectOfType<PlayerController>();

        if (_playerController != null)
            _playerRoot = _playerController.transform;

        if (_mouseLook == null)
            _mouseLook = FindObjectOfType<MouseLook>();

        if (_interactionSystem == null)
            _interactionSystem = FindObjectOfType<InteractionSystem>();

        if (_inventoryCameraController == null)
            _inventoryCameraController = InventoryCameraController.GetPrimaryController();
        if (_inventoryCameraController == null)
            _inventoryCameraController = FindObjectOfType<InventoryCameraController>();

        if (mainCamera == null && _playerRoot != null)
        {
            mainCamera = _playerRoot.GetComponentInChildren<Camera>();
        }

        if (mainCamera == null)
        {
            if (_mouseLook != null)
                mainCamera = _mouseLook.GetComponent<Camera>();
            if (mainCamera == null)
                mainCamera = Camera.main;
        }

        if (lookTarget == null && _playerRoot != null)
        {
            Transform playerBody = FindChildByName(_playerRoot, "PlayerBodyCapsule");
            lookTarget = playerBody != null ? playerBody : _playerRoot;
        }

        if (_playerRb == null && _playerRoot != null)
        {
            _playerRb = _playerRoot.GetComponent<Rigidbody>();
        }
    }
    private Rigidbody _playerRb;

    private void EnterDeathState()
    {
        IsPlayerDead = true;
        _isDead = true;

        EnsureUi(); // Force resolve and bind UI elements upon entering death!

        if (mainCamera != null)
        {
            _frozenCameraPos = mainCamera.transform.position;
            _cameraOriginalParent = mainCamera.transform.parent;
            _cameraOriginalLocalPos = mainCamera.transform.localPosition;
            _cameraOriginalLocalRot = mainCamera.transform.localRotation;
            
            if (_trapDeathState == TrapDeathState.Falling)
            {
                _trapDeathTimer = 0f;
            }
        }

        SetNonMovementSystemsEnabled(false);
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;

        if (_uiCo != null)
            StopCoroutine(_uiCo);
        _uiCo = StartCoroutine(PlayDeathUi());
    }

    public void TriggerTrapDeathPhase1()
    {
        _trapDeathState = TrapDeathState.Falling;
        EnterDeathState();
    }

    private void OnReviveClicked()
    {
        Debug.Log("[PlayerDeathFlow] OnReviveClicked fired.");
        PhysicalGlassShatter physicalShatter = FindObjectOfType<PhysicalGlassShatter>();

        if (physicalShatter != null)
        {
            // 点击复活时，触发真正的物理坠落。将 RevivePlayer 作为截屏定格后的回调执行
            physicalShatter.TriggerFall(() => 
            {
                RevivePlayer();
            });
        }
        else
        {
            RevivePlayer();
        }
    }

    private void RevivePlayer()
    {
        Debug.Log("[PlayerDeathFlow] RevivePlayer started.");
        try
        {
            if (_playerRoot == null)
            {
                Debug.Log("[PlayerDeathFlow] _playerRoot is null. Attempting to resolve...");
                ResolveRuntimeReferences();
                
                if (_playerRoot == null)
                {
                    Debug.Log("[PlayerDeathFlow] Aborting: _playerRoot is still null after resolve.");
                    return;
                }
            }

            Debug.Log("[PlayerDeathFlow] StopAllCoroutines...");
            StopAllCoroutines();

            Debug.Log("[PlayerDeathFlow] Disabling CC...");
            CharacterController cc = _playerRoot.GetComponent<CharacterController>();
            if (cc != null)
                cc.enabled = false;

            // 🌟 Spawn the corpse at the player's current fallen position/rotation before teleporting them back to spawn
            if (_trapDeathState == TrapDeathState.Falling)
            {
                if (_playerController != null)
                {
                    lookTarget = _playerController.SpawnCorpseAndHide();
                }
            }

            Debug.Log("[PlayerDeathFlow] Teleporting player...");
            _playerRoot.SetPositionAndRotation(_spawnPos, _spawnRot);
            if (_playerRb != null)
            {
                _playerRb.position = _spawnPos;
                _playerRb.rotation = _spawnRot;
                if (!_playerRb.isKinematic)
                {
                    _playerRb.velocity = Vector3.zero;
                    _playerRb.angularVelocity = Vector3.zero;
                }
            }

            Debug.Log("[PlayerDeathFlow] Destroying DeathCamera...");
            if (_deathCamera != null)
            {
                Destroy(_deathCamera.gameObject);
                _deathCamera = null;
            }

            Debug.Log("[PlayerDeathFlow] Spawning Proxy Drone...");
            if (proxyDronePrefab != null)
            {
                _activeDrone = Instantiate(proxyDronePrefab, _spawnPos, _spawnRot);
                DroneController dc = _activeDrone.GetComponent<DroneController>();
                if (dc == null) dc = _activeDrone.AddComponent<DroneController>();
                
                // Re-enable mainCamera but pass it to Drone
                if (mainCamera != null)
                {
                    mainCamera.enabled = true;
                    if (mainCamera.GetComponent<AudioListener>() != null)
                        mainCamera.GetComponent<AudioListener>().enabled = true;
                    
                    dc.Initialize(mainCamera, this);
                }
            }
            else
            {
                Debug.LogError("[PlayerDeathFlow] ProxyDronePrefab is missing! Please assign it in the Inspector.");
            }

            // Keep player hidden and CC disabled during Drone phase
            if (_playerController != null)
            {
                _playerController.SetPlayerVisible(false);
            }
            if (cc != null)
                cc.enabled = false;

            IsPlayerDead = false;
            _isDead = false;
            _trapDeathState = TrapDeathState.None;
            _reviveProtectionTimer = 0.2f; 
            
            Debug.Log("[PlayerDeathFlow] Setting NonMovementSystemsEnabled (false for drone)...");
            SetNonMovementSystemsEnabled(false);
            
            Debug.Log("[PlayerDeathFlow] Hiding Death UI Texts...");
            HideDeathUiTextsImmediate();
            _uiCo = null;
            
            Debug.Log("[PlayerDeathFlow] Removing Letterbox...");
            StartCoroutine(RemoveLetterboxSmoothly());

            Debug.Log("[PlayerDeathFlow] Resetting Canvas...");
            if (_canvas != null)
            {
                _canvas.renderMode = _originalCanvasRenderMode;
                _canvas.worldCamera = _originalCanvasWorldCamera;
            }
            
            Debug.Log("[PlayerDeathFlow] Unlocking Cursor...");
            Cursor.lockState = CursorLockMode.Locked;
            Debug.Log("[PlayerDeathFlow] Proxy Revive Phase 1 finished.");
        }
        catch (System.Exception e)
        {
            Debug.LogError($"[PlayerDeathFlow] RevivePlayer CRASHED: {e.Message}\n{e.StackTrace}");
        }
    }

    public void CompleteRevive()
    {
        Debug.Log("[PlayerDeathFlow] CompleteRevive triggered by Drone.");
        StartCoroutine(CompleteReviveSequence());
    }

    private IEnumerator CompleteReviveSequence()
    {
        // Fade to black
        GameObject fadeObj = new GameObject("ReviveFader");
        Canvas fadeCanvas = fadeObj.AddComponent<Canvas>();
        fadeCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
        fadeCanvas.sortingOrder = 999;
        Image fadeImage = fadeObj.AddComponent<Image>();
        fadeImage.color = new Color(0, 0, 0, 0);
        
        float t = 0;
        while (t < 1f)
        {
            t += Time.deltaTime * 2f;
            fadeImage.color = new Color(0, 0, 0, t);
            yield return null;
        }

        Debug.Log($"[CompleteRevive Diagnostics] Starting camera detachment and restoration. mainCamera: {(mainCamera != null ? mainCamera.name : "null")}");

        // 1. 先把摄像头分离下来 (Detach camera from Drone completely to root first!)
        if (mainCamera != null)
        {
            Debug.Log($"[CompleteRevive Diagnostics] Detaching camera. Parent before: {(mainCamera.transform.parent != null ? mainCamera.transform.parent.name : "null")}");
            mainCamera.transform.SetParent(null);
        }

        // 2. 再销毁 Drone (Safely destroy Drone)
        if (_activeDrone != null)
        {
            Debug.Log("[CompleteRevive Diagnostics] Destroying Drone...");
            Destroy(_activeDrone);
            _activeDrone = null;
        }

        // 等待一帧，确保 Drone 被完全销毁并且所有物理周期更新完毕
        yield return null;

        // 3. 寻找并更新场景中当前激活的本地玩家 (Find the newly active/instantiated local player)
        PlayerController localPlayer = null;
        PlayerController[] players = FindObjectsOfType<PlayerController>();
        foreach (PlayerController p in players)
        {
            if (p == null) continue;

            // 🌟 排除场景中的“玩家尸体”克隆体，防止将血量错加在尸体上！
            if (p.gameObject.name.Contains("Corpse") || p.gameObject.name.Contains("corpse"))
                continue;

            bool isNetworkActive = Unity.Netcode.NetworkManager.Singleton != null && Unity.Netcode.NetworkManager.Singleton.IsListening;
            if (isNetworkActive)
            {
                if (p.IsOwner)
                {
                    localPlayer = p;
                    break;
                }
            }
            else
            {
                // 单机模式下，绝对优先将 localPlayer 定位为当前脚本挂载的真实玩家！
                if (p.gameObject == this.gameObject)
                {
                    localPlayer = p;
                    break;
                }
                localPlayer = p; // 兜底
            }
        }

        // 优先使用新寻找到的本地 Player，如果没有则回退到 _playerRoot
        Transform targetPlayerRoot = localPlayer != null ? localPlayer.transform : _playerRoot;
        Debug.Log($"[CompleteRevive Diagnostics] Found Target Player: {(targetPlayerRoot != null ? targetPlayerRoot.name : "null")}");

        if (targetPlayerRoot != null)
        {
            _playerRoot = targetPlayerRoot;
            _playerController = targetPlayerRoot.GetComponent<PlayerController>();
            
            // 🌟 强力重新寻址并绑定新主角上的交互系统和背包相机系统，防止因为对象失效导致复活后白框和交互系统“失灵”！
            _interactionSystem = targetPlayerRoot.GetComponentInChildren<InteractionSystem>(true);
            _inventoryCameraController = targetPlayerRoot.GetComponentInChildren<InventoryCameraController>(true);
            _mouseLook = mainCamera != null ? mainCamera.GetComponent<MouseLook>() : null;

            // Re-enable Player components
            CharacterController cc = targetPlayerRoot.GetComponent<CharacterController>();
            if (cc != null) cc.enabled = true;

            if (_playerController != null)
            {
                _playerController.SetPlayerVisible(true);
                _playerController.ResetVelocity();
                _playerController.hp = 10; // 🌟 重新设为满血 (10)
            }

            // 双重安全保障：确保死亡全局标志完全重置为 false，玩家可再次正常受击！
            IsPlayerDead = false;
            _isDead = false;

            Transform playerBody = FindChildByName(targetPlayerRoot, "PlayerBodyCapsule");
            lookTarget = playerBody != null ? playerBody : targetPlayerRoot;

            // 4. 将摄像头挂载到新/旧 Player 下面 (Attach camera under the Player's holder)
            if (mainCamera != null)
            {
                Transform holder = FindChildByName(targetPlayerRoot, "CameraHolderEmpty");
                if (holder == null) holder = FindChildByName(targetPlayerRoot, "Head");
                if (holder == null) holder = targetPlayerRoot; // final fallback

                Debug.Log($"[CompleteRevive Diagnostics] Parenting camera to: {holder.name}");
                if (_mouseLook != null)
                {
                    _mouseLook.SetupCamera(targetPlayerRoot, holder);
                    _mouseLook.enabled = true;
                }
                else
                {
                    mainCamera.transform.SetParent(holder);
                    mainCamera.transform.localPosition = _cameraOriginalLocalPos;
                    mainCamera.transform.localRotation = _cameraOriginalLocalRot;
                }

                mainCamera.enabled = true;
                AudioListener listener = mainCamera.GetComponent<AudioListener>();
                if (listener != null) listener.enabled = true;

                Debug.Log($"[CompleteRevive Diagnostics] Camera successfully restored to Player! Parent = {mainCamera.transform.parent.name} | Camera.enabled = {mainCamera.enabled} | ActiveInHierarchy = {mainCamera.gameObject.activeInHierarchy}");
            }
        }
        else
        {
            Debug.LogError("[CompleteRevive Diagnostics] No player found to restore the camera to!");
        }

        SetNonMovementSystemsEnabled(true);

        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;

        // Fade back in
        t = 1f;
        while (t > 0f)
        {
            t -= Time.deltaTime * 2f;
            fadeImage.color = new Color(0, 0, 0, t);
            yield return null;
        }

        Destroy(fadeObj);
        Debug.Log("[PlayerDeathFlow] CompleteRevive fully finished.");
    }

    private IEnumerator RemoveLetterboxSmoothly()
    {
        float startHeight = GetLetterboxTargetHeight();
        float duration = 0.4f;
        float t = 0f;
        while (t < duration)
        {
            t += Time.unscaledDeltaTime;
            SetLetterboxHeight(Mathf.Lerp(startHeight, 0f, Ease01(t / duration)));
            yield return null;
        }
        SetLetterboxHeight(0f);
    }

    private void SetNonMovementSystemsEnabled(bool enabled)
    {
        if (_interactionSystem != null)
            _interactionSystem.enabled = enabled;
        if (_inventoryCameraController != null)
            _inventoryCameraController.enabled = enabled;
        if (_mouseLook != null)
            _mouseLook.enabled = enabled;
    }

    private IEnumerator PlayDeathUi()
    {
        if (_canvas != null)
        {
            _originalCanvasRenderMode = _canvas.renderMode;
            _originalCanvasWorldCamera = _canvas.worldCamera;
            _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        }

        if (!_capturedUiLayout)
            CaptureUiLayout();

        if (_leftRt != null) _leftRt.gameObject.SetActive(true);
        if (_rightRt != null) _rightRt.gameObject.SetActive(true);
        if (_hintRt != null)
            _hintRt.gameObject.SetActive(false);
        if (_buttonRt != null)
            _buttonRt.gameObject.SetActive(false);

        Vector2 leftStart = Vector2.zero;
        Vector2 rightStart = Vector2.zero;
        if (_leftRt != null) leftStart = _leftFinalAnchoredPos + Vector2.left * sideStartOffsetX;
        if (_rightRt != null) rightStart = _rightFinalAnchoredPos + Vector2.right * sideStartOffsetX;

        // Reset text positions initially
        if (_leftRt != null && _leftCg != null)
            ApplySideReveal(_leftRt, _leftCg, leftStart, _leftFinalAnchoredPos, _leftMaterial, _leftBaseFaceDilate, _leftBaseOutlineSoftness, _leftBaseOutlineWidth, _leftBaseUnderlayDilate, _leftBaseUnderlaySoftness, _leftBaseUnderlayColor, 0f);
        if (_rightRt != null && _rightCg != null)
            ApplySideReveal(_rightRt, _rightCg, rightStart, _rightFinalAnchoredPos, _rightMaterial, _rightBaseFaceDilate, _rightBaseOutlineSoftness, _rightBaseOutlineWidth, _rightBaseUnderlayDilate, _rightBaseUnderlaySoftness, _rightBaseUnderlayColor, 0f);

        // 1. Capture screen WITHOUT letterbox
        PhysicalGlassShatter physicalShatter = FindObjectOfType<PhysicalGlassShatter>();
        if (physicalShatter == null)
        {
            // Try searching inactive game objects in the scene too!
            PhysicalGlassShatter[] allShatters = Resources.FindObjectsOfTypeAll<PhysicalGlassShatter>();
            foreach (var sh in allShatters)
            {
                if (sh.hideFlags == HideFlags.None && sh.gameObject.scene.name != null)
                {
                    physicalShatter = sh;
                    physicalShatter.gameObject.SetActive(true);
                    Debug.Log("[PlayerDeathFlow] Found and activated inactive PhysicalGlassShatter in scene!");
                    break;
                }
            }
        }

        if (physicalShatter == null)
        {
            Camera cam = mainCamera != null ? mainCamera : Camera.main;
            if (cam != null)
            {
                physicalShatter = cam.gameObject.AddComponent<PhysicalGlassShatter>();
                Debug.Log("[PlayerDeathFlow] PhysicalGlassShatter was missing from scene. Automatically added it to: " + cam.name);
            }
        }

        if (physicalShatter != null)
        {
            // Ensure its camera reference is set correctly before shattering!
            if (physicalShatter.mainCamera == null)
            {
                physicalShatter.mainCamera = mainCamera != null ? mainCamera : Camera.main;
            }

            physicalShatter.TriggerShatter();
            // Wait for it to capture the screen (1 frame)
            yield return new WaitForEndOfFrame();
        }

        // 2. Animate Letterbox First
        float letterboxStartHeight = _topLetterboxRt != null ? _topLetterboxRt.sizeDelta.y : 0f;
        float letterboxTargetHeight = GetLetterboxTargetHeight();
        float letterboxDuration = Mathf.Max(0.01f, letterboxAnimDuration);

        float t = 0f;
        while (t < letterboxDuration)
        {
            t += Time.unscaledDeltaTime;
            float letterboxProgress = Ease01(t / letterboxDuration);
            SetLetterboxHeight(Mathf.Lerp(letterboxStartHeight, letterboxTargetHeight, letterboxProgress));
            yield return null;
        }
        SetLetterboxHeight(letterboxTargetHeight);

        // 3. Wait 0.1s
        yield return new WaitForSecondsRealtime(0.1f);

        // 4. Trigger glass shatter (Crack)
        if (physicalShatter != null)
        {
            physicalShatter.Crack();
        }

        // 5. Animate Death Text (Left/Right)
        float leftDuration = Mathf.Max(0.01f, sideSlideDuration);
        float rightDuration = Mathf.Max(0.01f, sideSlideDuration);

        float rightStartTime = leftDuration * Mathf.Clamp01(rightStartAtLeftProgress);
        float textDuration = Mathf.Max(leftDuration, rightStartTime + rightDuration);

        t = 0f;
        bool hintStarted = false;
        
        while (t < textDuration)
        {
            t += Time.unscaledDeltaTime;

            float leftProgress = Ease01(t / leftDuration);
            float rightProgress = Ease01((t - rightStartTime) / rightDuration);

            if (_leftRt != null && _leftCg != null)
                ApplySideReveal(_leftRt, _leftCg, leftStart, _leftFinalAnchoredPos, _leftMaterial, _leftBaseFaceDilate, _leftBaseOutlineSoftness, _leftBaseOutlineWidth, _leftBaseUnderlayDilate, _leftBaseUnderlaySoftness, _leftBaseUnderlayColor, leftProgress);
            if (_rightRt != null && _rightCg != null)
                ApplySideReveal(_rightRt, _rightCg, rightStart, _rightFinalAnchoredPos, _rightMaterial, _rightBaseFaceDilate, _rightBaseOutlineSoftness, _rightBaseOutlineWidth, _rightBaseUnderlayDilate, _rightBaseUnderlaySoftness, _rightBaseUnderlayColor, rightProgress);

            // Start hint and buttons earlier (when right text is 50% done, or instantly if right text doesn't exist!)
            bool triggerHintAndButton = false;
            if (_rightRt != null)
            {
                if (rightProgress > 0.5f) triggerHintAndButton = true;
            }
            else
            {
                if (t > 0.15f) triggerHintAndButton = true;
            }

            if (!hintStarted && triggerHintAndButton)
            {
                hintStarted = true;
                if (_hintRt != null && _hintCg != null)
                    StartCoroutine(RevealFadeOnly(_hintRt, _hintCg, hintFadeDuration));
                if (_buttonRt != null && _buttonCg != null)
                    StartCoroutine(RevealFadeOnly(_buttonRt, _buttonCg, buttonFadeDuration));
            }

            yield return null;
        }

        if (_leftRt != null && _leftCg != null)
            ApplySideReveal(_leftRt, _leftCg, leftStart, _leftFinalAnchoredPos, _leftMaterial, _leftBaseFaceDilate, _leftBaseOutlineSoftness, _leftBaseOutlineWidth, _leftBaseUnderlayDilate, _leftBaseUnderlaySoftness, _leftBaseUnderlayColor, 1f);
        if (_rightRt != null && _rightCg != null)
            ApplySideReveal(_rightRt, _rightCg, rightStart, _rightFinalAnchoredPos, _rightMaterial, _rightBaseFaceDilate, _rightBaseOutlineSoftness, _rightBaseOutlineWidth, _rightBaseUnderlayDilate, _rightBaseUnderlaySoftness, _rightBaseUnderlayColor, 1f);

        // Ensure hint/buttons start if they somehow didn't
        if (!hintStarted)
        {
            if (_hintRt != null && _hintCg != null)
                StartCoroutine(RevealFadeOnly(_hintRt, _hintCg, hintFadeDuration));
            if (_buttonRt != null && _buttonCg != null)
                StartCoroutine(RevealFadeOnly(_buttonRt, _buttonCg, buttonFadeDuration));
        }
    }

    private static IEnumerator RevealFadeOnly(RectTransform rt, CanvasGroup cg, float duration)
    {
        if (rt == null || cg == null)
            yield break;

        rt.gameObject.SetActive(true);
        rt.localScale = Vector3.one;
        cg.alpha = 0f;

        float t = 0f;
        float safeDuration = Mathf.Max(0.01f, duration);
        while (t < safeDuration)
        {
            t += Time.unscaledDeltaTime;
            cg.alpha = Ease01(t / safeDuration);
            yield return null;
        }

        cg.alpha = 1f;
    }

    private IEnumerator RevealFadeAndBlur(
        RectTransform rt,
        CanvasGroup cg,
        Material mat,
        float baseFace,
        float baseSoft,
        float baseWidth,
        float baseUnderlayDilate,
        float baseUnderlaySoftness,
        Color baseUnderlayColor,
        float duration)
    {
        if (rt == null || cg == null)
            yield break;

        rt.gameObject.SetActive(true);
        rt.localScale = Vector3.one * blurStartScale;
        cg.alpha = 0f;
        ApplyBlurTransition(mat, baseFace, baseSoft, baseWidth, baseUnderlayDilate, baseUnderlaySoftness, baseUnderlayColor, 0f);

        float t = 0f;
        float safeDuration = Mathf.Max(0.01f, duration);

        while (t < safeDuration)
        {
            t += Time.unscaledDeltaTime;
            float n = Mathf.Clamp01(t / safeDuration);
            cg.alpha = n;
            rt.localScale = Vector3.one * Mathf.Lerp(blurStartScale, 1f, n);
            ApplyBlurTransition(mat, baseFace, baseSoft, baseWidth, baseUnderlayDilate, baseUnderlaySoftness, baseUnderlayColor, n);
            yield return null;
        }

        cg.alpha = 1f;
        rt.localScale = Vector3.one;
        ApplyBlurTransition(mat, baseFace, baseSoft, baseWidth, baseUnderlayDilate, baseUnderlaySoftness, baseUnderlayColor, 1f);
    }

    private void HideDeathUiImmediate()
    {
        HideDeathUiTextsImmediate();
        HideLetterboxImmediate();
    }

    private void HideDeathUiTextsImmediate()
    {
        HideOne(_leftRt, _leftCg);
        HideOne(_rightRt, _rightCg);
        HideOne(_hintRt, _hintCg);
        HideOne(_buttonRt, _buttonCg);
        ForceSharp(
            _leftMaterial,
            _leftBaseFaceDilate,
            _leftBaseOutlineSoftness,
            _leftBaseOutlineWidth,
            _leftBaseUnderlayDilate,
            _leftBaseUnderlaySoftness,
            _leftBaseUnderlayColor);
        ForceSharp(
            _rightMaterial,
            _rightBaseFaceDilate,
            _rightBaseOutlineSoftness,
            _rightBaseOutlineWidth,
            _rightBaseUnderlayDilate,
            _rightBaseUnderlaySoftness,
            _rightBaseUnderlayColor);
        ForceSharp(
            _hintMaterial,
            _hintBaseFaceDilate,
            _hintBaseOutlineSoftness,
            _hintBaseOutlineWidth,
            _hintBaseUnderlayDilate,
            _hintBaseUnderlaySoftness,
            _hintBaseUnderlayColor);
        ForceSharp(
            _buttonLabelMaterial,
            _buttonLabelBaseFaceDilate,
            _buttonLabelBaseOutlineSoftness,
            _buttonLabelBaseOutlineWidth,
            _buttonLabelBaseUnderlayDilate,
            _buttonLabelBaseUnderlaySoftness,
            _buttonLabelBaseUnderlayColor);
    }

    private static void HideOne(RectTransform rt, CanvasGroup cg)
    {
        if (cg != null)
            cg.alpha = 0f;

        if (rt != null)
        {
            rt.localScale = Vector3.one;
            rt.gameObject.SetActive(false);
        }
    }

    private void ApplySideReveal(
        RectTransform rt,
        CanvasGroup cg,
        Vector2 startPos,
        Vector2 finalPos,
        Material mat,
        float baseFace,
        float baseSoft,
        float baseWidth,
        float baseUnderlayDilate,
        float baseUnderlaySoftness,
        Color baseUnderlayColor,
        float progress)
    {
        if (rt == null || cg == null)
            return;

        float eased = Ease01(progress);
        rt.anchoredPosition = Vector2.LerpUnclamped(startPos, finalPos, eased);
        cg.alpha = eased;
        rt.localScale = Vector3.one * Mathf.Lerp(blurStartScale, 1f, eased);
        ApplyBlurTransition(
            mat,
            baseFace,
            baseSoft,
            baseWidth,
            baseUnderlayDilate,
            baseUnderlaySoftness,
            baseUnderlayColor,
            eased);
    }

    private static float Ease01(float t)
    {
        float n = Mathf.Clamp01(t);
        return Mathf.SmoothStep(0f, 1f, n);
    }

    private void EnsureLetterbox()
    {
        _topLetterboxRt = topLetterbox;
        _bottomLetterboxRt = bottomLetterbox;

        if (_topLetterboxRt == null)
            _topLetterboxRt = FindRectByAnyName(_canvas.transform, "DeathLetterboxTop", "TopLetterbox");
        if (_bottomLetterboxRt == null)
            _bottomLetterboxRt = FindRectByAnyName(_canvas.transform, "DeathLetterboxBottom", "BottomLetterbox");

        if (_topLetterboxRt == null)
            _topLetterboxRt = CreateLetterbox("DeathLetterboxTop");
        if (_bottomLetterboxRt == null)
            _bottomLetterboxRt = CreateLetterbox("DeathLetterboxBottom");

        topLetterbox = _topLetterboxRt;
        bottomLetterbox = _bottomLetterboxRt;

        ConfigureLetterboxRect(_topLetterboxRt, true);
        ConfigureLetterboxRect(_bottomLetterboxRt, false);
        HideLetterboxImmediate();
    }

    private RectTransform CreateLetterbox(string objectName)
    {
        GameObject go = new GameObject(objectName, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        RectTransform rt = go.GetComponent<RectTransform>();
        rt.SetParent(_canvas.transform, false);
        return rt;
    }

    private void ConfigureLetterboxRect(RectTransform rt, bool isTop)
    {
        if (rt == null || _canvas == null)
            return;

        rt.SetParent(_canvas.transform, false);
        rt.anchorMin = isTop ? new Vector2(0f, 1f) : new Vector2(0f, 0f);
        rt.anchorMax = isTop ? new Vector2(1f, 1f) : new Vector2(1f, 0f);
        rt.pivot = isTop ? new Vector2(0.5f, 1f) : new Vector2(0.5f, 0f);
        rt.anchoredPosition = Vector2.zero;
        rt.SetAsLastSibling();

        Image img = rt.GetComponent<Image>();
        if (img == null)
            img = rt.gameObject.AddComponent<Image>();
        img.color = letterboxColor;
        img.raycastTarget = false;
    }

    private void HideLetterboxImmediate()
    {
        SetLetterboxHeight(0f);
    }

    private float GetLetterboxTargetHeight()
    {
        float ratio = Mathf.Clamp01(letterboxHeightRatio);
        if (ratio <= 0f)
            return 0f;

        RectTransform canvasRt = _canvas != null ? _canvas.GetComponent<RectTransform>() : null;
        float canvasHeight = canvasRt != null ? canvasRt.rect.height : 0f;
        if (canvasHeight <= 0f)
            canvasHeight = Screen.height;

        return canvasHeight * ratio;
    }

    private void SetLetterboxHeight(float height)
    {
        float clamped = Mathf.Max(0f, height);
        ApplyLetterboxHeight(_topLetterboxRt, clamped);
        ApplyLetterboxHeight(_bottomLetterboxRt, clamped);
    }

    private static void ApplyLetterboxHeight(RectTransform rt, float height)
    {
        if (rt == null)
            return;

        Vector2 size = rt.sizeDelta;
        size.y = height;
        rt.sizeDelta = size;
        rt.gameObject.SetActive(height > 0.001f);
    }

    private void EnsureUi()
    {
        // 🌟 Ensure EventSystem exists in the scene so UI click events can be processed!
        if (FindObjectOfType<UnityEngine.EventSystems.EventSystem>() == null)
        {
            GameObject esObj = new GameObject("EventSystem_Dynamic");
            esObj.AddComponent<UnityEngine.EventSystems.EventSystem>();
            esObj.AddComponent<UnityEngine.EventSystems.StandaloneInputModule>();
            Debug.Log("[PlayerDeathFlow] EventSystem was missing from the scene. Dynamic EventSystem successfully created!");
        }

        // 1. Gather all Canvases in the active scene (both active and inactive!)
        List<Canvas> allCanvases = new List<Canvas>();
        Canvas[] canvasesInScene = Resources.FindObjectsOfTypeAll<Canvas>();
        foreach (var c in canvasesInScene)
        {
            if (c.hideFlags == HideFlags.None && c.gameObject.scene.name != null)
            {
                allCanvases.Add(c);
            }
        }

        // 2. Try to find the labels and button by searching all canvases
        _leftLabel = leftDeathTMP;
        _rightLabel = rightDeathTMP;
        _hintLabel = deathHintTMP;
        _reviveButton = reviveButton;

        foreach (Canvas canvas in allCanvases)
        {
            if (_leftLabel == null)
                _leftLabel = FindTextByAnyName(canvas.transform, "DeathLeftTMP", "DeadLeftTMP", "LeftDeathTMP");
            if (_leftLabel == null)
                _leftLabel = FindTextByExactContent(canvas.transform, "死");

            if (_rightLabel == null)
                _rightLabel = FindTextByAnyName(canvas.transform, "DeathRightTMP", "DeadRightTMP", "RightDeathTMP");
            if (_rightLabel == null)
                _rightLabel = FindTextByExactContent(canvas.transform, "亡");

            if (_hintLabel == null)
                _hintLabel = FindTextByAnyName(canvas.transform, "DeathHintTMP", "DeathTipTMP", "DeathPromptTMP");
            if (_hintLabel == null)
                _hintLabel = FindTextContaining(canvas.transform, "死亡");

            // 🌟 1. Prioritize text-based search (Case-insensitive) as it is extremely specific and unique!
            if (_reviveButton == null)
                _reviveButton = FindButtonByChildTextContaining(canvas.transform, "PROXY");
            if (_reviveButton == null)
                _reviveButton = FindButtonByChildTextContaining(canvas.transform, "ACTIVATE");
            if (_reviveButton == null)
                _reviveButton = FindButtonByChildTextContaining(canvas.transform, "Proxy");
            if (_reviveButton == null)
                _reviveButton = FindButtonByChildTextContaining(canvas.transform, "Activate");
            if (_reviveButton == null)
                _reviveButton = FindButtonByChildTextContaining(canvas.transform, "复活");
            if (_reviveButton == null)
                _reviveButton = FindButtonByChildTextContaining(canvas.transform, "重生");

            // 🌟 2. Search by unique/specific names
            if (_reviveButton == null)
                _reviveButton = FindButtonByAnyName(canvas.transform, "ReviveButton", "RespawnButton", "DeathReviveButton", "ProxyButton", "ProxyActivateButton", "ActivateButton", "ActionButton");

            // 🌟 3. Only fall back to default name "Button"
            if (_reviveButton == null)
                _reviveButton = FindButtonByAnyName(canvas.transform, "Button");

            // Absolute Bulletproof Fallback: if we still haven't found the button, grab the first button in this Canvas!
            if (_reviveButton == null)
            {
                Button[] buttons = canvas.GetComponentsInChildren<Button>(true);
                if (buttons.Length > 0)
                {
                    _reviveButton = buttons[0];
                    Debug.Log($"[PlayerDeathFlow] Fallback triggered: bound the first button '{_reviveButton.name}' found on Canvas '{canvas.name}'.");
                }
            }
            
            // If we found the revive button, this is the correct canvas!
            if (_reviveButton != null)
            {
                _canvas = canvas;
            }
        }

        if (_canvas == null && allCanvases.Count > 0)
        {
            _canvas = allCanvases[0];
        }

        if (_reviveButton != null)
        {
            _reviveButton.onClick.RemoveListener(OnReviveClicked);
            _reviveButton.onClick.AddListener(OnReviveClicked);

            // CRITICAL FIX: Ensure the button and all parent canvas groups are fully active, interactable, and block raycasts!
            _reviveButton.interactable = true;
            CanvasGroup[] parentCgs = _reviveButton.GetComponentsInParent<CanvasGroup>(true);
            foreach (var cg in parentCgs)
            {
                cg.interactable = true;
                cg.blocksRaycasts = true;
            }
        }

        if (_leftLabel == null || _rightLabel == null || _hintLabel == null || _reviveButton == null)
        {
            Debug.LogWarning("PlayerDeathFlowController: missing existing death UI references. " +
                             "Expected TMPs (left/right/hint) and revive button in Canvas.");
        }

        _leftRt = _leftLabel != null ? _leftLabel.rectTransform : null;
        _rightRt = _rightLabel != null ? _rightLabel.rectTransform : null;
        _hintRt = _hintLabel != null ? _hintLabel.rectTransform : null;
        _buttonRt = _reviveButton != null ? _reviveButton.GetComponent<RectTransform>() : null;
        _buttonLabel = _reviveButton != null ? _reviveButton.GetComponentInChildren<TextMeshProUGUI>(true) : null;

        _leftCg = _leftRt != null ? GetOrAddCanvasGroup(_leftRt.gameObject) : null;
        _rightCg = _rightRt != null ? GetOrAddCanvasGroup(_rightRt.gameObject) : null;
        _hintCg = _hintRt != null ? GetOrAddCanvasGroup(_hintRt.gameObject) : null;
        _buttonCg = _buttonRt != null ? GetOrAddCanvasGroup(_buttonRt.gameObject) : null;

        CacheBlurState(
            _leftLabel,
            ref _leftMaterial,
            ref _leftBaseFaceDilate,
            ref _leftBaseOutlineSoftness,
            ref _leftBaseOutlineWidth,
            ref _leftBaseUnderlayDilate,
            ref _leftBaseUnderlaySoftness,
            ref _leftBaseUnderlayColor);
        CacheBlurState(
            _rightLabel,
            ref _rightMaterial,
            ref _rightBaseFaceDilate,
            ref _rightBaseOutlineSoftness,
            ref _rightBaseOutlineWidth,
            ref _rightBaseUnderlayDilate,
            ref _rightBaseUnderlaySoftness,
            ref _rightBaseUnderlayColor);

        // Apply Cyberpunk glitch to hint and button
        if (_hintLabel != null)
        {
            if (_hintLabel.GetComponent<CyberpunkUIGlitch>() == null)
                _hintLabel.gameObject.AddComponent<CyberpunkUIGlitch>();
        }
        if (_reviveButton != null)
        {
            if (_reviveButton.GetComponent<CyberpunkUIGlitch>() == null)
                _reviveButton.gameObject.AddComponent<CyberpunkUIGlitch>();
        }

        CacheBlurState(
            _hintLabel,
            ref _hintMaterial,
            ref _hintBaseFaceDilate,
            ref _hintBaseOutlineSoftness,
            ref _hintBaseOutlineWidth,
            ref _hintBaseUnderlayDilate,
            ref _hintBaseUnderlaySoftness,
            ref _hintBaseUnderlayColor);
        CacheBlurState(
            _buttonLabel,
            ref _buttonLabelMaterial,
            ref _buttonLabelBaseFaceDilate,
            ref _buttonLabelBaseOutlineSoftness,
            ref _buttonLabelBaseOutlineWidth,
            ref _buttonLabelBaseUnderlayDilate,
            ref _buttonLabelBaseUnderlaySoftness,
            ref _buttonLabelBaseUnderlayColor);

        EnsureLetterbox();

        CaptureUiLayout();
    }

    private void CaptureUiLayout()
    {
        if (_leftRt == null || _rightRt == null)
            return;

        _leftFinalAnchoredPos = _leftRt.anchoredPosition;
        _rightFinalAnchoredPos = _rightRt.anchoredPosition;
        _capturedUiLayout = true;
    }

    private void CacheBlurState(
        TextMeshProUGUI label,
        ref Material mat,
        ref float baseFace,
        ref float baseSoft,
        ref float baseWidth,
        ref float baseUnderlayDilate,
        ref float baseUnderlaySoftness,
        ref Color baseUnderlayColor)
    {
        mat = null;
        baseFace = 0f;
        baseSoft = 0f;
        baseWidth = 0f;
        baseUnderlayDilate = 0f;
        baseUnderlaySoftness = 0f;
        baseUnderlayColor = Color.clear;
        if (label == null)
            return;

        Material instance = label.fontMaterial;
        if (instance == null)
            return;

        label.fontMaterial = instance;
        mat = label.fontMaterial;
        if (mat == null)
            return;

        if (mat.HasProperty(ShaderUtilities.ID_FaceDilate))
            baseFace = mat.GetFloat(ShaderUtilities.ID_FaceDilate);
        if (mat.HasProperty(ShaderUtilities.ID_OutlineSoftness))
            baseSoft = mat.GetFloat(ShaderUtilities.ID_OutlineSoftness);
        if (mat.HasProperty(ShaderUtilities.ID_OutlineWidth))
            baseWidth = mat.GetFloat(ShaderUtilities.ID_OutlineWidth);
        if (mat.HasProperty(ShaderUtilities.ID_UnderlayDilate))
            baseUnderlayDilate = mat.GetFloat(ShaderUtilities.ID_UnderlayDilate);
        if (mat.HasProperty(ShaderUtilities.ID_UnderlaySoftness))
            baseUnderlaySoftness = mat.GetFloat(ShaderUtilities.ID_UnderlaySoftness);
        if (mat.HasProperty(ShaderUtilities.ID_UnderlayColor))
            baseUnderlayColor = mat.GetColor(ShaderUtilities.ID_UnderlayColor);
    }

    private void ApplyBlurTransition(
        Material mat,
        float baseFace,
        float baseSoft,
        float baseWidth,
        float baseUnderlayDilate,
        float baseUnderlaySoftness,
        Color baseUnderlayColor,
        float t)
    {
        if (mat == null)
            return;

        float n = Mathf.Clamp01(t);
        bool hasFace = mat.HasProperty(ShaderUtilities.ID_FaceDilate);
        bool hasOutlineWidth = mat.HasProperty(ShaderUtilities.ID_OutlineWidth);
        bool hasOutlineSoftness = mat.HasProperty(ShaderUtilities.ID_OutlineSoftness);

        float startWidth = baseWidth;
        if (hasOutlineWidth)
        {
            startWidth = Mathf.Max(baseWidth, blurStartOutlineWidth);
            if (!hasOutlineSoftness)
                startWidth = Mathf.Lerp(startWidth, baseWidth, 0.7f);

            mat.SetFloat(ShaderUtilities.ID_OutlineWidth, Mathf.Lerp(startWidth, baseWidth, n));
            if (startWidth > 0.0001f)
                mat.EnableKeyword("OUTLINE_ON");
        }

        if (hasFace)
        {
            // Compensate face thickness against outline growth so glyph does not look shaved.
            float widthDelta = Mathf.Max(0f, startWidth - baseWidth);
            float startFace = baseFace + blurStartFaceDilateBoost + widthDelta * blurOutlineToFaceCompensation;
            mat.SetFloat(ShaderUtilities.ID_FaceDilate, Mathf.Lerp(startFace, baseFace, n));
        }

        if (hasOutlineSoftness)
        {
            float startSoft = Mathf.Max(baseSoft, blurStartOutlineSoftness);
            mat.SetFloat(ShaderUtilities.ID_OutlineSoftness, Mathf.Lerp(startSoft, baseSoft, n));
        }

        bool hasUnderlay = mat.HasProperty(ShaderUtilities.ID_UnderlayColor) &&
                           mat.HasProperty(ShaderUtilities.ID_UnderlayDilate) &&
                           mat.HasProperty(ShaderUtilities.ID_UnderlaySoftness) &&
                           mat.HasProperty(ShaderUtilities.ID_UnderlayOffsetX) &&
                           mat.HasProperty(ShaderUtilities.ID_UnderlayOffsetY);
        if (hasUnderlay)
        {
            mat.EnableKeyword("UNDERLAY_ON");

            Color startColor = baseUnderlayColor;
            startColor.a = Mathf.Max(startColor.a, blurStartUnderlayAlpha);
            mat.SetColor(ShaderUtilities.ID_UnderlayColor, Color.Lerp(startColor, baseUnderlayColor, n));

            float startDilate = Mathf.Max(baseUnderlayDilate, blurStartUnderlayDilate);
            mat.SetFloat(ShaderUtilities.ID_UnderlayDilate, Mathf.Lerp(startDilate, baseUnderlayDilate, n));

            float startUnderSoft = Mathf.Max(baseUnderlaySoftness, blurStartUnderlaySoftness);
            mat.SetFloat(ShaderUtilities.ID_UnderlaySoftness, Mathf.Lerp(startUnderSoft, baseUnderlaySoftness, n));

            mat.SetFloat(ShaderUtilities.ID_UnderlayOffsetX, 0f);
            mat.SetFloat(ShaderUtilities.ID_UnderlayOffsetY, 0f);
        }
    }

    private static void ForceSharp(
        Material mat,
        float baseFace,
        float baseSoft,
        float baseWidth,
        float baseUnderlayDilate,
        float baseUnderlaySoftness,
        Color baseUnderlayColor)
    {
        if (mat == null)
            return;

        if (mat.HasProperty(ShaderUtilities.ID_FaceDilate))
            mat.SetFloat(ShaderUtilities.ID_FaceDilate, baseFace);
        if (mat.HasProperty(ShaderUtilities.ID_OutlineSoftness))
            mat.SetFloat(ShaderUtilities.ID_OutlineSoftness, baseSoft);
        if (mat.HasProperty(ShaderUtilities.ID_OutlineWidth))
            mat.SetFloat(ShaderUtilities.ID_OutlineWidth, baseWidth);
        if (mat.HasProperty(ShaderUtilities.ID_UnderlayDilate))
            mat.SetFloat(ShaderUtilities.ID_UnderlayDilate, baseUnderlayDilate);
        if (mat.HasProperty(ShaderUtilities.ID_UnderlaySoftness))
            mat.SetFloat(ShaderUtilities.ID_UnderlaySoftness, baseUnderlaySoftness);
        if (mat.HasProperty(ShaderUtilities.ID_UnderlayColor))
            mat.SetColor(ShaderUtilities.ID_UnderlayColor, baseUnderlayColor);
        if (mat.HasProperty(ShaderUtilities.ID_UnderlayOffsetX))
            mat.SetFloat(ShaderUtilities.ID_UnderlayOffsetX, 0f);
        if (mat.HasProperty(ShaderUtilities.ID_UnderlayOffsetY))
            mat.SetFloat(ShaderUtilities.ID_UnderlayOffsetY, 0f);
    }

    private static CanvasGroup GetOrAddCanvasGroup(GameObject go)
    {
        CanvasGroup cg = go.GetComponent<CanvasGroup>();
        if (cg == null)
            cg = go.AddComponent<CanvasGroup>();
        return cg;
    }

    private static TextMeshProUGUI FindTextByExactContent(Transform root, string content)
    {
        if (root == null || string.IsNullOrEmpty(content))
            return null;

        TextMeshProUGUI[] texts = root.GetComponentsInChildren<TextMeshProUGUI>(true);
        for (int i = 0; i < texts.Length; i++)
        {
            TextMeshProUGUI t = texts[i];
            if (t != null && t.text == content)
                return t;
        }

        return null;
    }

    private static TextMeshProUGUI FindTextContaining(Transform root, string textFragment)
    {
        if (root == null || string.IsNullOrEmpty(textFragment))
            return null;

        TextMeshProUGUI[] texts = root.GetComponentsInChildren<TextMeshProUGUI>(true);
        for (int i = 0; i < texts.Length; i++)
        {
            TextMeshProUGUI t = texts[i];
            if (t != null && !string.IsNullOrEmpty(t.text) && t.text.Contains(textFragment))
                return t;
        }

        return null;
    }

    private static TextMeshProUGUI FindTextByAnyName(Transform root, params string[] objectNames)
    {
        if (root == null || objectNames == null || objectNames.Length == 0)
            return null;

        TextMeshProUGUI[] texts = root.GetComponentsInChildren<TextMeshProUGUI>(true);
        for (int i = 0; i < texts.Length; i++)
        {
            TextMeshProUGUI t = texts[i];
            if (t == null)
                continue;

            for (int j = 0; j < objectNames.Length; j++)
            {
                string targetName = objectNames[j];
                if (!string.IsNullOrEmpty(targetName) && t.gameObject.name == targetName)
                    return t;
            }
        }

        return null;
    }

    private static Button FindButtonByAnyName(Transform root, params string[] objectNames)
    {
        if (root == null || objectNames == null || objectNames.Length == 0)
            return null;

        Button[] buttons = root.GetComponentsInChildren<Button>(true);
        for (int i = 0; i < buttons.Length; i++)
        {
            Button b = buttons[i];
            if (b == null)
                continue;

            for (int j = 0; j < objectNames.Length; j++)
            {
                string targetName = objectNames[j];
                if (!string.IsNullOrEmpty(targetName) && b.gameObject.name == targetName)
                    return b;
            }
        }

        return null;
    }

    private static Button FindButtonByChildTextContaining(Transform root, string textFragment)
    {
        if (root == null || string.IsNullOrEmpty(textFragment))
            return null;

        string targetUpper = textFragment.ToUpperInvariant();
        Button[] buttons = root.GetComponentsInChildren<Button>(true);
        for (int i = 0; i < buttons.Length; i++)
        {
            Button b = buttons[i];
            if (b == null)
                continue;

            TextMeshProUGUI label = b.GetComponentInChildren<TextMeshProUGUI>(true);
            if (label != null && !string.IsNullOrEmpty(label.text) && label.text.ToUpperInvariant().Contains(targetUpper))
                return b;

            UnityEngine.UI.Text legacyText = b.GetComponentInChildren<UnityEngine.UI.Text>(true);
            if (legacyText != null && !string.IsNullOrEmpty(legacyText.text) && legacyText.text.ToUpperInvariant().Contains(targetUpper))
                return b;
        }

        return null;
    }

    private static RectTransform FindRectByAnyName(Transform root, params string[] objectNames)
    {
        if (root == null || objectNames == null || objectNames.Length == 0)
            return null;

        RectTransform[] rects = root.GetComponentsInChildren<RectTransform>(true);
        for (int i = 0; i < rects.Length; i++)
        {
            RectTransform rt = rects[i];
            if (rt == null)
                continue;

            for (int j = 0; j < objectNames.Length; j++)
            {
                string targetName = objectNames[j];
                if (!string.IsNullOrEmpty(targetName) && rt.gameObject.name == targetName)
                    return rt;
            }
        }

        return null;
    }

    private static Transform FindChildByName(Transform root, string childName)
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
}