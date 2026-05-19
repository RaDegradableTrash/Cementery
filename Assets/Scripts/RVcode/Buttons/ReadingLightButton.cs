using UnityEngine;

public class ReadingLightButton : MonoBehaviour, ICockpitInteractable, ICockpitHighlightable
{
    [SerializeField] private ReadingLightSystem system;
    [SerializeField] private int lightIndex = 0;
    [SerializeField] private Renderer targetRenderer;
    [SerializeField] private Color activeEmissionColor = new Color(0.2f, 0.6f, 1f, 1f);
    [SerializeField] private Color highlightEmissionColor = new Color(0.95f, 0.95f, 0.95f, 1f);
    [SerializeField] private float highlightEmissionHdr = -4.5f;

    private Color inactiveEmissionColor = Color.black;
    private bool hasEmission;
    private bool isHighlighted;
    private static readonly int EmissionColorId = Shader.PropertyToID("_EmissionColor");

    private void Awake()
    {
        if (system == null)
        {
            system = FindObjectOfType<ReadingLightSystem>();
        }

        if (targetRenderer == null)
        {
            targetRenderer = GetComponentInChildren<Renderer>();
        }

        CacheEmissionColor();
        UpdateVisual();
    }

    private void OnEnable()
    {
        if (system != null)
        {
            system.OnStateChanged += UpdateVisual;
        }
    }

    private void OnDisable()
    {
        if (system != null)
        {
            system.OnStateChanged -= UpdateVisual;
        }
    }

    public void Interact()
    {
        if (system == null)
        {
            return;
        }

        system.ToggleLight(lightIndex);
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

        bool hasPower = system == null || system.HasPower;
        bool isOn = system != null && system.IsLightOn(lightIndex) && hasPower;

        Material mat = targetRenderer.material;
        if (isOn)
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
