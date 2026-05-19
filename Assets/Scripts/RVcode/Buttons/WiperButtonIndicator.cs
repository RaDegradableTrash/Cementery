using UnityEngine;

public class WiperButtonIndicator : MonoBehaviour
{
    [SerializeField] private WiperControl[] wiperControls;
    [SerializeField] private Renderer targetRenderer;
    [SerializeField] private Color activeEmissionColor = new Color(0.2f, 0.6f, 1f, 1f);

    private Color inactiveEmissionColor = Color.black;
    private bool hasEmission;
    private static readonly int EmissionColorId = Shader.PropertyToID("_EmissionColor");

    private void Awake()
    {
        if (wiperControls == null || wiperControls.Length == 0)
        {
            wiperControls = FindObjectsOfType<WiperControl>();
        }

        if (targetRenderer == null)
        {
            targetRenderer = GetComponentInChildren<Renderer>();
        }

        CacheEmissionColor();
        UpdateVisual();
    }

    private void Update()
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
        if (targetRenderer == null || !hasEmission || wiperControls == null || wiperControls.Length == 0)
        {
            return;
        }

        bool isActive = false;
        for (int i = 0; i < wiperControls.Length; i++)
        {
            WiperControl control = wiperControls[i];
            if (control != null && control.CurrentMode != WiperControl.WiperMode.Off)
            {
                isActive = true;
                break;
            }
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
