using UnityEngine;

public class SimpleLight : MonoBehaviour
{
    [Header("Power")]
    [SerializeField] private StartProcedure startProcedure;
    [SerializeField] private bool requiresPower = true;
    [SerializeField] private bool defaultOn = false;

    [Header("Targets")]
    [SerializeField] private Renderer targetRenderer;
    [SerializeField] private Light targetLight;
    [SerializeField] private Renderer[] targetRenderers;
    [SerializeField] private Light[] targetLights;

    [Header("Emission")]
    [SerializeField] private Color emissionColor = Color.white;
    [SerializeField] private float offEmissionHdr = -10f;
    [SerializeField] private float onEmissionHdr = 0f;

    [Header("Light Intensity")]
    [SerializeField] private float offIntensity = 0f;
    [SerializeField] private float onIntensity = 35f;

    private bool desiredOn;
    private bool lastEffectiveOn;
    private Color[] baseEmissionColors;
    private bool[] hasEmission;
    private static readonly int EmissionColorId = Shader.PropertyToID("_EmissionColor");

    private void Awake()
    {
        if (startProcedure == null)
        {
            startProcedure = FindObjectOfType<StartProcedure>();
        }

        if (targetRenderers == null || targetRenderers.Length == 0)
        {
            if (targetRenderer == null)
            {
                targetRenderer = GetComponentInChildren<Renderer>();
            }
            targetRenderers = targetRenderer != null ? new[] { targetRenderer } : new Renderer[0];
        }

        if (targetLights == null || targetLights.Length == 0)
        {
            if (targetLight == null)
            {
                targetLight = GetComponentInChildren<Light>();
            }
            targetLights = targetLight != null ? new[] { targetLight } : new Light[0];
        }

        desiredOn = defaultOn;
        CacheEmissionColor();
        bool effectiveOn = GetEffectiveOn();
        ApplyState(effectiveOn);
        lastEffectiveOn = effectiveOn;
    }

    private void Update()
    {
        bool effectiveOn = GetEffectiveOn();
        if (effectiveOn != lastEffectiveOn)
        {
            lastEffectiveOn = effectiveOn;
            ApplyState(effectiveOn);
        }
    }

    public void Toggle()
    {
        SetOn(!desiredOn);
    }

    public void SetOn(bool value)
    {
        if (desiredOn == value)
        {
            return;
        }
        desiredOn = value;
    }

    public bool IsOn()
    {
        return GetEffectiveOn();
    }

    public bool IsDesiredOn()
    {
        return desiredOn;
    }

    private void CacheEmissionColor()
    {
        if (targetRenderers == null)
        {
            return;
        }

        baseEmissionColors = new Color[targetRenderers.Length];
        hasEmission = new bool[targetRenderers.Length];

        for (int i = 0; i < targetRenderers.Length; i++)
        {
            Renderer renderer = targetRenderers[i];
            if (renderer == null)
            {
                baseEmissionColors[i] = Color.white;
                continue;
            }

            Material mat = renderer.material;
            if (mat != null && mat.HasProperty(EmissionColorId))
            {
                baseEmissionColors[i] = NormalizeColor(mat.GetColor(EmissionColorId));
                hasEmission[i] = true;
            }
            else
            {
                baseEmissionColors[i] = Color.white;
            }
        }
    }

    private void ApplyState(bool on)
    {
        float hdr = on ? onEmissionHdr : offEmissionHdr;
        float intensity = Mathf.Pow(2f, hdr);

        if (targetRenderers != null && hasEmission != null)
        {
            for (int i = 0; i < targetRenderers.Length; i++)
            {
                if (!hasEmission[i] || targetRenderers[i] == null)
                {
                    continue;
                }

                Material mat = targetRenderers[i].material;
                mat.EnableKeyword("_EMISSION");
                mat.SetColor(EmissionColorId, baseEmissionColors[i] * emissionColor * intensity);
            }
        }

        if (targetLights != null)
        {
            float targetIntensity = on ? onIntensity : offIntensity;
            for (int i = 0; i < targetLights.Length; i++)
            {
                if (targetLights[i] != null)
                {
                    targetLights[i].intensity = targetIntensity;
                }
            }
        }
    }

    private bool GetEffectiveOn()
    {
        bool hasPower = !requiresPower || startProcedure == null || startProcedure.HasAnyBatteryOn();
        return desiredOn && hasPower;
    }

    private static Color NormalizeColor(Color color)
    {
        float max = Mathf.Max(color.r, color.g, color.b);
        if (max > 1f)
        {
            return color / max;
        }
        return max > 0f ? color : Color.white;
    }
}
