using UnityEngine;

public class SimpleLightButton : MonoBehaviour, ICockpitInteractable, ICockpitHighlightable
{
    [SerializeField] private SimpleLight lightTarget;
    [SerializeField] private SimpleLight[] lightTargets;
    [SerializeField] private Renderer targetRenderer;
    [SerializeField] private Color activeEmissionColor = new Color(0.2f, 0.6f, 1f, 1f);
    [SerializeField] private float activeEmissionHdr = 0f;
    [SerializeField] private Color highlightEmissionColor = new Color(0.95f, 0.95f, 0.95f, 1f);
    [SerializeField] private float highlightEmissionHdr = -4.5f;

    // ── 🌟 核心新增：在 Inspector 中公开音效槽位 ──────────────────────────────
    [Header("Audio Settings")]
    [Tooltip("在这里拖入你想要播放的开关灯点击音效 MP3 文件")]
    [SerializeField] private AudioClip clickSound; 
    [Range(0f, 1f)] [SerializeField] private float volume = 0.8f;
    
    private AudioSource _audioSource;
    // ────────────────────────────────────────────────────────────────────────

    private Color inactiveEmissionColor = Color.black;
    private bool hasEmission;
    private bool isHighlighted;
    private bool lastAnyLightOn;
    private bool lastHighlighted;
    private bool hasAppliedVisual;
    private static readonly int EmissionColorId = Shader.PropertyToID("_EmissionColor");

    private void Awake()
    {
        if (targetRenderer == null)
        {
            targetRenderer = GetComponentInChildren<Renderer>();
        }

        if (lightTargets == null || lightTargets.Length == 0)
        {
            if (lightTarget != null)
            {
                lightTargets = new[] { lightTarget };
            }
        }

        CacheEmissionColor();
        UpdateVisual();

        // ── 🌟 核心新增：全自动挂载/获取播放器组件，省去手动操作 ─────────────────
        _audioSource = GetComponent<AudioSource>();
        if (_audioSource == null)
        {
            _audioSource = gameObject.AddComponent<AudioSource>();
        }
        _audioSource.playOnAwake = false;
        _audioSource.spatialBlend = 0.0f; // 2D音效，保证在座舱内戴耳机听得最清晰
        // ────────────────────────────────────────────────────────────────────────
    }

    private void Update()
    {
        UpdateVisual();
    }

    public void Interact()
    {
        // ── 🌟 核心新增：只要触发了交互，无条件播放点击音效 ──────────────────────
        if (clickSound != null && _audioSource != null)
        {
            _audioSource.PlayOneShot(clickSound, volume);
        }
        // ────────────────────────────────────────────────────────────────────────

        if (lightTargets == null || lightTargets.Length == 0)
        {
            return;
        }

        bool anyOff = false;
        for (int i = 0; i < lightTargets.Length; i++)
        {
            if (lightTargets[i] != null && !lightTargets[i].IsDesiredOn())
            {
                anyOff = true;
                break;
            }
        }

        bool nextState = anyOff;
        for (int i = 0; i < lightTargets.Length; i++)
        {
            if (lightTargets[i] != null)
            {
                lightTargets[i].SetOn(nextState);
            }
        }
    }

    public void SetHighlighted(bool highlighted)
    {
        if (isHighlighted == highlighted)
        {
            return;
        }
        isHighlighted = highlighted;
        UpdateVisual();
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

    private void UpdateVisual()
    {
        if (targetRenderer == null || !hasEmission)
        {
            return;
        }

        bool anyLightOn = IsAnyLightOn();
        if (hasAppliedVisual && anyLightOn == lastAnyLightOn && isHighlighted == lastHighlighted)
        {
            return;
        }

        hasAppliedVisual = true;
        lastAnyLightOn = anyLightOn;
        lastHighlighted = isHighlighted;

        Material mat = targetRenderer.material;
        if (anyLightOn)
        {
            mat.EnableKeyword("_EMISSION");
            float intensity = Mathf.Pow(2f, activeEmissionHdr);
            mat.SetColor(EmissionColorId, activeEmissionColor * intensity);
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

    private bool IsAnyLightOn()
    {
        if (lightTargets == null || lightTargets.Length == 0)
        {
            return false;
        }

        for (int i = 0; i < lightTargets.Length; i++)
        {
            if (lightTargets[i] != null && lightTargets[i].IsOn())
            {
                return true;
            }
        }
        return false;
    }
}
