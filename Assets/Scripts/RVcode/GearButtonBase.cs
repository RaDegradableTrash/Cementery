using UnityEngine;

public abstract class GearButtonBase : MonoBehaviour, ICockpitInteractable, ICockpitHighlightable
{
    [SerializeField] protected CarControl carControl;
    [SerializeField] private Renderer targetRenderer;
    [SerializeField] private Color activeEmissionColor = new Color(0.2f, 0.6f, 1f, 1f);
    [SerializeField] private bool glowWhenActive = true;
    [SerializeField] private Color highlightEmissionColor = new Color(0.95f, 0.95f, 0.95f, 1f);
    [SerializeField] private float highlightEmissionHdr = -4.5f;

    // ── 🌟 核心新增：音效播放接口的静态引用 ─────────────────────────────────
    // 使用静态（static）引用，这样全局只需要有一个音效管理者注册进来，所有按钮都能直接调用
    public static IGearAudioPlayer AudioPlayer { get; set; }
    // ────────────────────────────────────────────────────────────────────────

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

    // ── 🌟 核心修改：在换挡交互时，调用音效接口 ─────────────────────────────
    public void Interact()
    {
        if (carControl != null)
        {
            if (carControl.EngineOn)
            {
                carControl.SetGear(Gear);
                
                // 成功时：通知音效器播放成功音效，并把当前的档位传过去（方便你不同档位播不同声音）
                AudioPlayer?.PlayShiftSuccess(Gear);
            }
            else
            {
                // 失败时：通知音效器播放失败/警报音效
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