using UnityEngine;

public abstract class GearButtonBase : MonoBehaviour, ICockpitInteractable, ICockpitHighlightable
{
    [SerializeField] protected CarControl carControl;
    [SerializeField] private Renderer targetRenderer;
    [SerializeField] private Color activeEmissionColor = new Color(0.2f, 0.6f, 1f, 1f);
    [SerializeField] private bool glowWhenActive = true;
    [SerializeField] private Color highlightEmissionColor = new Color(0.95f, 0.95f, 0.95f, 1f);
    [SerializeField] private float highlightEmissionHdr = -4.5f;

    // ── 🌟 核心修改：在 Inspector 中公开音效槽位 ──────────────────────────────
    [Header("Audio Settings")]
    [Tooltip("在这里拖入你想要播放的点击音效 MP3 文件")]
    [SerializeField] private AudioClip clickSound; 
    private AudioSource _audioSource;
    // ────────────────────────────────────────────────────────────────────────

    public static IGearAudioPlayer AudioPlayer { get; set; }

    private Color inactiveEmissionColor = Color.black;
    private bool hasEmission;
    private static readonly int EmissionColorId = Shader.PropertyToID("_EmissionColor");
    private bool isHighlighted;

    protected abstract CarControl.GearMode Gear { get; }

    private void Awake()
    {
        if (carControl == null)
        {
            carControl = FindObjectOfType<CarControl>();
        }

        if (targetRenderer == null)
        {
            targetRenderer = GetComponentInChildren<Renderer>();
        }

        CacheEmissionColor();
        UpdateVisual(ShouldGlow(carControl));

        // ── 🌟 核心修改：全自动挂载/获取播放器组件（不需要手动去加 AudioSource） ──
        _audioSource = GetComponent<AudioSource>();
        if (_audioSource == null)
        {
            _audioSource = gameObject.AddComponent<AudioSource>();
        }
        _audioSource.playOnAwake = false;
        _audioSource.spatialBlend = 0.0f; // 2D音效，保证无论离多远都听得最清楚
    }

    private void OnEnable()
    {
        if (carControl != null)
        {
            carControl.OnGearChanged += HandleGearChanged;
            carControl.OnEngineStateChanged += HandleEngineStateChanged;
        }
    }

    private void OnDisable()
    {
        if (carControl != null)
        {
            carControl.OnGearChanged -= HandleGearChanged;
            carControl.OnEngineStateChanged -= HandleEngineStateChanged;
        }
    }

    private void HandleGearChanged(CarControl.GearMode gear)
    {
        UpdateVisual(ShouldGlow(carControl));
    }

    private void HandleEngineStateChanged(bool isOn)
    {
        UpdateVisual(ShouldGlow(carControl));
    }

    // ── 🌟 核心修改：点击时，播放你在 Inspector 里拖进去的音效 ────────────────
    public void Interact()
    {
        // 1. 检查有没有在 Inspector 里拖入音效，如果有，立刻播放
        if (clickSound != null && _audioSource != null)
        {
            _audioSource.PlayOneShot(clickSound, 0.8f); // 0.8f 是音量大小
        }

        // 2. 原本的换挡逻辑
        if (carControl != null)
        {
            if (carControl.EngineOn)
            {
                carControl.SetGear(Gear);
                AudioPlayer?.PlayShiftSuccess(Gear);
            }
            else
            {
                AudioPlayer?.PlayShiftFail(Gear);
            }
        }
    }
    // ────────────────────────────────────────────────────────────────────────

    private bool ShouldGlow(CarControl control)
    {
        return control != null
            && control.EngineOn
            && control.ElectricalPowerOn
            && control.CurrentGear == Gear;
    }

    private void CacheEmissionColor()
    {
        if (targetRenderer == null)
        {
            return;
        }

        Material mat = targetRenderer.material;
        if (mat != null && mat.HasProperty(EmissionColorId))
        {
            inactiveEmissionColor = mat.GetColor(EmissionColorId);
            hasEmission = true;
        }
    }

    public void SetHighlighted(bool highlighted)
    {
        if (isHighlighted == highlighted)
        {
            return;
        }
        isHighlighted = highlighted;
        UpdateVisual(ShouldGlow(carControl));
    }

    private void UpdateVisual(bool isActive)
    {
        if (targetRenderer == null || !hasEmission)
        {
            return;
        }

        Material mat = targetRenderer.material;
        if (glowWhenActive && isActive)
        {
            mat.EnableKeyword("_EMISSION");
            mat.SetColor(EmissionColorId, activeEmissionColor);
        }
        else if (isHighlighted)
        {
            mat.EnableKeyword("_EMISSION");
            float intensity = Mathf.Pow(2f, highlightEmissionHdr);
            mat.SetColor(EmissionColorId, highlightEmissionColor * intensity);
        }
        else
        {
            mat.SetColor(EmissionColorId, inactiveEmissionColor);
            if (inactiveEmissionColor.maxColorComponent <= 0.001f)
            {
                mat.DisableKeyword("_EMISSION");
            }
        }
    }
}