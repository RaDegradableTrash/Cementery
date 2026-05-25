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

    private Color inactiveEmissionColor = Color.black;
    private bool hasEmission;
    private bool isHighlighted;
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
    }

    private void Update()
    {
        UpdateVisual();
    }

    public void Interact()
    {
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

        Material mat = targetRenderer.material;
        if (IsAnyLightOn())
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
