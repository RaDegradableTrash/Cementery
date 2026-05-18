using UnityEngine;

public abstract class GearButtonBase : MonoBehaviour, ICockpitInteractable
{
    [SerializeField] protected CarControl carControl;
    [SerializeField] private Renderer targetRenderer;
    [SerializeField] private Color activeEmissionColor = new Color(0.2f, 0.6f, 1f, 1f);
    [SerializeField] private bool glowWhenActive = true;

    private Color inactiveEmissionColor = Color.black;
    private bool hasEmission;
    private static readonly int EmissionColorId = Shader.PropertyToID("_EmissionColor");

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
        UpdateVisual(carControl != null && carControl.CurrentGear == Gear);
    }

    private void OnEnable()
    {
        if (carControl != null)
        {
            carControl.OnGearChanged += HandleGearChanged;
        }
    }

    private void OnDisable()
    {
        if (carControl != null)
        {
            carControl.OnGearChanged -= HandleGearChanged;
        }
    }

    private void HandleGearChanged(CarControl.GearMode gear)
    {
        UpdateVisual(carControl != null && carControl.CurrentGear == Gear);
    }

    public void Interact()
    {
        if (carControl != null)
        {
            carControl.SetGear(Gear);
        }
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

    private void UpdateVisual(bool isActive)
    {
        if (!glowWhenActive || targetRenderer == null || !hasEmission)
        {
            return;
        }

        Material mat = targetRenderer.material;
        if (isActive)
        {
            mat.EnableKeyword("_EMISSION");
            mat.SetColor(EmissionColorId, activeEmissionColor);
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
