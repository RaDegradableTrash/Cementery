using UnityEngine;

public class WiperButton : MonoBehaviour, ICockpitInteractable, ICockpitHighlightable
{
    [SerializeField] private WiperControl[] wiperControls;
    [SerializeField] private StartProcedure startProcedure;
    [SerializeField] private bool requirePower = true;
    [SerializeField] private Renderer targetRenderer;
    [SerializeField] private Color highlightEmissionColor = new Color(0.95f, 0.95f, 0.95f, 1f);
    [SerializeField] private float highlightEmissionHdr = -4.5f;

    private Color inactiveEmissionColor = Color.black;
    private bool hasEmission;
    private bool isHighlighted;
    private static readonly int EmissionColorId = Shader.PropertyToID("_EmissionColor");

    private void Awake()
    {
        if (wiperControls == null || wiperControls.Length == 0)
        {
            wiperControls = FindObjectsOfType<WiperControl>();
        }

        if (startProcedure == null)
        {
            startProcedure = FindObjectOfType<StartProcedure>();
        }

        if (targetRenderer == null)
        {
            targetRenderer = GetComponentInChildren<Renderer>();
        }

        CacheEmissionColor();
        UpdateHighlight();
    }

    public void Interact()
    {
        if (wiperControls == null || wiperControls.Length == 0)
        {
            return;
        }

        if (requirePower && startProcedure != null && !startProcedure.HasAnyBatteryOn())
        {
            return;
        }

        for (int i = 0; i < wiperControls.Length; i++)
        {
            if (wiperControls[i] != null)
            {
                wiperControls[i].CycleMode();
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
        UpdateHighlight();
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

    private void UpdateHighlight()
    {
        if (targetRenderer == null || !hasEmission)
        {
            return;
        }

        Material mat = targetRenderer.material;
        if (isHighlighted)
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
