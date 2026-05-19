using UnityEngine;

public class LightControl : MonoBehaviour
{
    [SerializeField] private StartProcedure startProcedure;

    [Header("Brake Light Renderers")]
    [SerializeField] private Renderer brakeRenderer1;
    [SerializeField] private Renderer brakeRenderer2;
    [SerializeField] private Renderer brakeRenderer3;
    [SerializeField] private Renderer brakeRenderer4;

    [Header("Brake Light Spots")]
    [SerializeField] private Light brakeLight1;
    [SerializeField] private Light brakeLight2;
    [SerializeField] private Light brakeLight3;
    [SerializeField] private Light brakeLight4;

    [Header("Emission HDR")]
    [SerializeField] private float normalEmissionHdr = -5f;
    [SerializeField] private float brakeEmissionHdr = 1f;

    [Header("Light Intensity")]
    [SerializeField] private float normalIntensity = 25f;
    [SerializeField] private float brakeIntensity = 35f;

    [Header("Headlight Renderers")]
    [SerializeField] private Renderer headRenderer1;
    [SerializeField] private Renderer headRenderer2;

    [Header("Headlight Spots")]
    [SerializeField] private Light headLight1;
    [SerializeField] private Light headLight2;

    [Header("Headlight Settings")]
    [SerializeField] private float headlightEmissionHdr = 0f;
    [SerializeField] private float headlightIntensity = 35f;

    private Renderer[] brakeRenderers;
    private Light[] brakeLights;
    private Color[] baseEmissionColors;
    private Renderer[] headRenderers;
    private Light[] headLights;
    private Color[] baseHeadEmissionColors;

    private void Awake()
    {
        if (startProcedure == null)
        {
            startProcedure = FindObjectOfType<StartProcedure>();
        }

        brakeRenderers = new[] { brakeRenderer1, brakeRenderer2, brakeRenderer3, brakeRenderer4 };
        brakeLights = new[] { brakeLight1, brakeLight2, brakeLight3, brakeLight4 };
        baseEmissionColors = new Color[brakeRenderers.Length];

        headRenderers = new[] { headRenderer1, headRenderer2 };
        headLights = new[] { headLight1, headLight2 };
        baseHeadEmissionColors = new Color[headRenderers.Length];

        CacheBaseEmissionColors(brakeRenderers, baseEmissionColors);
        CacheBaseEmissionColors(headRenderers, baseHeadEmissionColors);
        ApplyPowerState(false);
        ApplyBrakeState(false, false);
    }

    private void Update()
    {
        bool hasPower = startProcedure == null || startProcedure.HasAnyBatteryOn();
        bool braking = hasPower && Input.GetKey(KeyCode.S);

        ApplyPowerState(hasPower);
        ApplyBrakeState(braking, hasPower);
    }

    private void CacheBaseEmissionColors(Renderer[] renderers, Color[] cache)
    {
        for (int i = 0; i < renderers.Length; i++)
        {
            Renderer renderer = renderers[i];
            if (renderer == null)
            {
                cache[i] = Color.white;
                continue;
            }

            Material mat = renderer.material;
            mat.EnableKeyword("_EMISSION");
            Color emission = mat.HasProperty("_EmissionColor") ? mat.GetColor("_EmissionColor") : Color.white;
            cache[i] = NormalizeColor(emission);
        }
    }

    private void ApplyBrakeState(bool braking, bool hasPower)
    {
        float hdr = hasPower ? (braking ? brakeEmissionHdr : normalEmissionHdr) : -10f;
        float intensity = Mathf.Pow(2f, hdr);

        for (int i = 0; i < brakeRenderers.Length; i++)
        {
            Renderer renderer = brakeRenderers[i];
            if (renderer == null)
            {
                continue;
            }

            Material mat = renderer.material;
            mat.EnableKeyword("_EMISSION");
            mat.SetColor("_EmissionColor", baseEmissionColors[i] * intensity);
        }

        float targetIntensity = hasPower ? (braking ? brakeIntensity : normalIntensity) : 0f;
        for (int i = 0; i < brakeLights.Length; i++)
        {
            Light light = brakeLights[i];
            if (light != null)
            {
                light.intensity = targetIntensity;
            }
        }
    }

    private void ApplyPowerState(bool hasPower)
    {
        float hdr = hasPower ? headlightEmissionHdr : -10f;
        float intensity = Mathf.Pow(2f, hdr);

        for (int i = 0; i < headRenderers.Length; i++)
        {
            Renderer renderer = headRenderers[i];
            if (renderer == null)
            {
                continue;
            }

            Material mat = renderer.material;
            mat.EnableKeyword("_EMISSION");
            mat.SetColor("_EmissionColor", baseHeadEmissionColors[i] * intensity);
        }

        float targetIntensity = hasPower ? headlightIntensity : 0f;
        for (int i = 0; i < headLights.Length; i++)
        {
            Light light = headLights[i];
            if (light != null)
            {
                light.intensity = targetIntensity;
            }
        }
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
