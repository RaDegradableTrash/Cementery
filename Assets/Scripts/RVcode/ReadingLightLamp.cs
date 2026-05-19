using UnityEngine;

public class ReadingLightLamp : MonoBehaviour
{
    [SerializeField] private ReadingLightSystem system;
    [SerializeField] private int lightIndex = 0;
    [SerializeField] private Renderer targetRenderer;
    [SerializeField] private Light targetLight;
    [SerializeField] private Light targetLightUp;
    [SerializeField] private Color emissionColor = Color.white;
    [SerializeField] private float onEmissionHdr = 0f;
    [SerializeField] private float offEmissionHdr = -10f;
    [SerializeField] private float onIntensity = 35f;
    [SerializeField] private float offIntensity = 0f;

    private Color baseEmissionColor = Color.white;
    private bool hasEmission;
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

        if (targetLight == null)
        {
            targetLight = GetComponentInChildren<Light>();
        }

        if (targetLightUp == null)
        {
            Light[] lights = GetComponentsInChildren<Light>();
            if (lights != null && lights.Length > 1)
            {
                targetLightUp = lights[1];
            }
        }

        CacheEmissionColor();
        UpdateLamp();
    }

    private void OnEnable()
    {
        if (system != null)
        {
            system.OnStateChanged += UpdateLamp;
        }
    }

    private void OnDisable()
    {
        if (system != null)
        {
            system.OnStateChanged -= UpdateLamp;
        }
    }

    private void Update()
    {
        UpdateLamp();
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
            baseEmissionColor = NormalizeColor(mat.GetColor(EmissionColorId));
            hasEmission = true;
        }
    }

    private void UpdateLamp()
    {
        if (targetRenderer == null || !hasEmission)
        {
            return;
        }

        bool hasPower = system == null || system.HasPower;
        bool isOn = system != null && system.IsLightOn(lightIndex);
        float hdr = hasPower && isOn ? onEmissionHdr : offEmissionHdr;
        float intensity = Mathf.Pow(2f, hdr);

        Material mat = targetRenderer.material;
        mat.EnableKeyword("_EMISSION");
        mat.SetColor(EmissionColorId, baseEmissionColor * emissionColor * intensity);

        float targetIntensity = hasPower && isOn ? onIntensity : offIntensity;
        if (targetLight != null)
        {
            targetLight.intensity = targetIntensity;
        }
        if (targetLightUp != null)
        {
            targetLightUp.intensity = targetIntensity;
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
