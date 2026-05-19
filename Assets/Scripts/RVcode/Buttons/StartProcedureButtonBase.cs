using UnityEngine;

public abstract class StartProcedureButtonBase : MonoBehaviour, ICockpitInteractable, ICockpitHighlightable
{
    [SerializeField] protected StartProcedure startProcedure;
    [SerializeField] private Renderer targetRenderer;
    [SerializeField] private Color activeEmissionColor = new Color(0.2f, 0.6f, 1f, 1f);
    [SerializeField] private bool glowWhenActive = true;
    [SerializeField] private bool showIdleGlow = false;
    [SerializeField] private Color idleEmissionColor = new Color(0.9f, 0.9f, 0.9f, 1f);
    [SerializeField] private float idleEmissionHdr = -5.5f;
    [SerializeField] private Color highlightEmissionColor = new Color(0.95f, 0.95f, 0.95f, 1f);
    [SerializeField] private float highlightEmissionHdr = -4.5f;

    private Color inactiveEmissionColor = Color.black;
    private bool hasEmission;
    private static readonly int EmissionColorId = Shader.PropertyToID("_EmissionColor");
    private bool isHighlighted;

    protected abstract bool IsOn(StartProcedure procedure);
    protected abstract void Toggle(StartProcedure procedure);

    protected virtual void Awake()
    {
        if (startProcedure == null)
        {
            startProcedure = FindObjectOfType<StartProcedure>();
        }

        if (targetRenderer == null)
        {
            targetRenderer = GetComponentInChildren<Renderer>();
        }

        CacheEmissionColor();
        UpdateVisual();
    }

    protected void SetIdleGlow(bool enabled, Color color, float hdr)
    {
        showIdleGlow = enabled;
        idleEmissionColor = color;
        idleEmissionHdr = hdr;
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

    private void OnEnable()
    {
        if (startProcedure != null)
        {
            startProcedure.OnStateChanged += HandleStateChanged;
        }
    }

    private void OnDisable()
    {
        if (startProcedure != null)
        {
            startProcedure.OnStateChanged -= HandleStateChanged;
        }
    }

    public void Interact()
    {
        if (startProcedure != null)
        {
            Toggle(startProcedure);
        }
    }

    private void HandleStateChanged()
    {
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

        Material mat = targetRenderer.material;
        bool hasPower = startProcedure == null || startProcedure.HasAnyBatteryOn();
        bool isActive = glowWhenActive && IsOn(startProcedure) && hasPower;
        if (isActive)
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
        else if (showIdleGlow)
        {
            mat.EnableKeyword("_EMISSION");
            float intensity = Mathf.Pow(2f, idleEmissionHdr);
            mat.SetColor(EmissionColorId, idleEmissionColor * intensity);
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
