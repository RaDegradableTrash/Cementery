using System.Collections;
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
    [SerializeField, Range(0.5f, 0.98f)] private float leftStartAtLetterboxProgress = 0.82f;
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

    private Transform _playerRoot;
    private Vector3 _spawnPos;
    private Quaternion _spawnRot;
    private Vector3 _frozenCameraPos;
    private bool _isDead;
    private Coroutine _uiCo;

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
        }
    }

    private void Update()
    {
        if (_isDead || _playerRoot == null)
            return;

        if (_playerRoot.position.y <= deathY)
            EnterDeathState();
    }

    private void LateUpdate()
    {
        if (!_isDead || mainCamera == null)
            return;

        if (lockCameraPositionOnDeath)
            mainCamera.transform.position = _frozenCameraPos;

        if (lookTarget == null)
            return;

        Vector3 dir = lookTarget.position - mainCamera.transform.position;
        if (dir.sqrMagnitude > 0.0001f)
        {
            Quaternion targetRot = Quaternion.LookRotation(dir.normalized, Vector3.up);
            // 平滑变速度旋转，Time.deltaTime * speed 会产生非常平滑的先快后慢的阻尼效果
            mainCamera.transform.rotation = Quaternion.Slerp(mainCamera.transform.rotation, targetRot, Time.deltaTime * cameraLookSpeed);
        }
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
    }

    private void EnterDeathState()
    {
        _isDead = true;

        if (mainCamera != null)
            _frozenCameraPos = mainCamera.transform.position;

        SetNonMovementSystemsEnabled(false);
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;

        if (_uiCo != null)
            StopCoroutine(_uiCo);
        _uiCo = StartCoroutine(PlayDeathUi());
    }

    private void OnReviveClicked()
    {
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
        if (_playerRoot == null)
            return;

        CharacterController cc = _playerRoot.GetComponent<CharacterController>();
        if (cc != null)
            cc.enabled = false;

        _playerRoot.SetPositionAndRotation(_spawnPos, _spawnRot);

        if (cc != null)
            cc.enabled = true;

        if (_playerController != null)
            _playerController.ResetVelocity();

        _isDead = false;
        SetNonMovementSystemsEnabled(true);
        HideDeathUiImmediate();
        
        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;
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
        // 1. 触发死亡瞬间的碎屏效果（此时碎片静止悬停，UI通过RenderTexture反射在碎片上）
        PhysicalGlassShatter physicalShatter = FindObjectOfType<PhysicalGlassShatter>();
        if (physicalShatter != null)
        {
            physicalShatter.TriggerShatter();
        }

        if (_leftRt == null || _rightRt == null || _leftCg == null || _rightCg == null)
            yield break;

        if (!_capturedUiLayout)
            CaptureUiLayout();

        _leftRt.gameObject.SetActive(true);
        _rightRt.gameObject.SetActive(true);
        if (_hintRt != null)
            _hintRt.gameObject.SetActive(false);
        if (_buttonRt != null)
            _buttonRt.gameObject.SetActive(false);

        Vector2 leftStart = _leftFinalAnchoredPos + Vector2.left * sideStartOffsetX;
        Vector2 rightStart = _rightFinalAnchoredPos + Vector2.right * sideStartOffsetX;

        float letterboxStartHeight = _topLetterboxRt != null ? _topLetterboxRt.sizeDelta.y : 0f;
        float letterboxTargetHeight = GetLetterboxTargetHeight();

        float letterboxDuration = Mathf.Max(0.01f, letterboxAnimDuration);
        float leftDuration = Mathf.Max(0.01f, sideSlideDuration);
        float rightDuration = Mathf.Max(0.01f, sideSlideDuration);

        float leftStartTime = letterboxDuration * Mathf.Clamp01(leftStartAtLetterboxProgress);
        float rightStartTime = leftStartTime + leftDuration * Mathf.Clamp01(rightStartAtLeftProgress);
        float totalDuration = Mathf.Max(
            letterboxDuration,
            Mathf.Max(leftStartTime + leftDuration, rightStartTime + rightDuration));

        ApplySideReveal(
            _leftRt,
            _leftCg,
            leftStart,
            _leftFinalAnchoredPos,
            _leftMaterial,
            _leftBaseFaceDilate,
            _leftBaseOutlineSoftness,
            _leftBaseOutlineWidth,
            _leftBaseUnderlayDilate,
            _leftBaseUnderlaySoftness,
            _leftBaseUnderlayColor,
            0f);
        ApplySideReveal(
            _rightRt,
            _rightCg,
            rightStart,
            _rightFinalAnchoredPos,
            _rightMaterial,
            _rightBaseFaceDilate,
            _rightBaseOutlineSoftness,
            _rightBaseOutlineWidth,
            _rightBaseUnderlayDilate,
            _rightBaseUnderlaySoftness,
            _rightBaseUnderlayColor,
            0f);

        float t = 0f;
        while (t < totalDuration)
        {
            t += Time.unscaledDeltaTime;

            float letterboxProgress = Ease01(t / letterboxDuration);
            SetLetterboxHeight(Mathf.Lerp(letterboxStartHeight, letterboxTargetHeight, letterboxProgress));

            float leftProgress = Ease01((t - leftStartTime) / leftDuration);
            float rightProgress = Ease01((t - rightStartTime) / rightDuration);

            ApplySideReveal(
                _leftRt,
                _leftCg,
                leftStart,
                _leftFinalAnchoredPos,
                _leftMaterial,
                _leftBaseFaceDilate,
                _leftBaseOutlineSoftness,
                _leftBaseOutlineWidth,
                _leftBaseUnderlayDilate,
                _leftBaseUnderlaySoftness,
                _leftBaseUnderlayColor,
                leftProgress);
            ApplySideReveal(
                _rightRt,
                _rightCg,
                rightStart,
                _rightFinalAnchoredPos,
                _rightMaterial,
                _rightBaseFaceDilate,
                _rightBaseOutlineSoftness,
                _rightBaseOutlineWidth,
                _rightBaseUnderlayDilate,
                _rightBaseUnderlaySoftness,
                _rightBaseUnderlayColor,
                rightProgress);
            yield return null;
        }

        SetLetterboxHeight(letterboxTargetHeight);
        ApplySideReveal(
            _leftRt,
            _leftCg,
            leftStart,
            _leftFinalAnchoredPos,
            _leftMaterial,
            _leftBaseFaceDilate,
            _leftBaseOutlineSoftness,
            _leftBaseOutlineWidth,
            _leftBaseUnderlayDilate,
            _leftBaseUnderlaySoftness,
            _leftBaseUnderlayColor,
            1f);
        ApplySideReveal(
            _rightRt,
            _rightCg,
            rightStart,
            _rightFinalAnchoredPos,
            _rightMaterial,
            _rightBaseFaceDilate,
            _rightBaseOutlineSoftness,
            _rightBaseOutlineWidth,
            _rightBaseUnderlayDilate,
            _rightBaseUnderlaySoftness,
            _rightBaseUnderlayColor,
            1f);

        if (_hintRt != null && _hintCg != null)
        {
            yield return RevealFadeOnly(_hintRt, _hintCg, hintFadeDuration);
        }

        if (_buttonRt != null && _buttonCg != null)
        {
            yield return RevealFadeOnly(_buttonRt, _buttonCg, buttonFadeDuration);
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
        if (_uiCo != null)
        {
            StopCoroutine(_uiCo);
            _uiCo = null;
        }

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
        HideLetterboxImmediate();
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
        if (_canvas == null)
            _canvas = FindObjectOfType<Canvas>();
        if (_canvas == null)
            return;

        _leftLabel = leftDeathTMP;
        _rightLabel = rightDeathTMP;
        _hintLabel = deathHintTMP;
        _reviveButton = reviveButton;

        if (_leftLabel == null)
            _leftLabel = FindTextByAnyName(_canvas.transform, "DeathLeftTMP", "DeadLeftTMP", "LeftDeathTMP");
        if (_leftLabel == null)
            _leftLabel = FindTextByExactContent(_canvas.transform, "死");

        if (_rightLabel == null)
            _rightLabel = FindTextByAnyName(_canvas.transform, "DeathRightTMP", "DeadRightTMP", "RightDeathTMP");
        if (_rightLabel == null)
            _rightLabel = FindTextByExactContent(_canvas.transform, "亡");

        if (_hintLabel == null)
            _hintLabel = FindTextByAnyName(_canvas.transform, "DeathHintTMP", "DeathTipTMP", "DeathPromptTMP");
        if (_hintLabel == null)
            _hintLabel = FindTextContaining(_canvas.transform, "死亡");

        if (_reviveButton == null)
            _reviveButton = FindButtonByAnyName(_canvas.transform, "ReviveButton", "RespawnButton", "DeathReviveButton");
        if (_reviveButton == null)
            _reviveButton = FindButtonByChildTextContaining(_canvas.transform, "复活");

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

        Button[] buttons = root.GetComponentsInChildren<Button>(true);
        for (int i = 0; i < buttons.Length; i++)
        {
            Button b = buttons[i];
            if (b == null)
                continue;

            TextMeshProUGUI label = b.GetComponentInChildren<TextMeshProUGUI>(true);
            if (label != null && !string.IsNullOrEmpty(label.text) && label.text.Contains(textFragment))
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