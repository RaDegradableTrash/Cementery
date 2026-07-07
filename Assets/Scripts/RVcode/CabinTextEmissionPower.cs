using UnityEngine;

public class CabinTextEmissionPower : MonoBehaviour
{
    [SerializeField] private StartProcedure startProcedure;
    [SerializeField] private Material targetMaterial;
    [SerializeField] private float offEmissionHdr = -5f;
    [SerializeField] private bool useMaterialOnEmissionHdr = true;
    [SerializeField] private float onEmissionHdr = 0f;
    [SerializeField] private float rampUpSeconds = 1.5f;
    [SerializeField] private float rampDownSeconds = 0.5f;
    [SerializeField, Min(0.02f)] private float stablePowerCheckInterval = 0.15f;

    private Color baseEmissionColor = Color.white;
    private Color originalEmissionColor = Color.white;
    private float baseEmissionHdr;
    private float currentHdr;
    private bool hasEmission;
    private bool hasOriginalEmission;
    private bool hasAppliedEmission;
    private float lastAppliedHdr = float.NaN;
    private float lastTargetHdr;
    private float cachedOnHdr;
    private bool cachedUseMaterialOnEmissionHdr;
    private float cachedInspectorOnEmissionHdr = float.NaN;
    private float cachedBaseEmissionHdr = float.NaN;
    private float nextStablePowerCheckTime;
    private static readonly int EmissionColorId = Shader.PropertyToID("_EmissionColor");

    private void Awake()
    {
        if (startProcedure == null)
        {
            startProcedure = FindObjectOfType<StartProcedure>();
        }

        if (targetMaterial == null)
        {
            return;
        }

        CacheBaseEmission();
        currentHdr = offEmissionHdr;
        lastTargetHdr = offEmissionHdr;
        ApplyEmission(currentHdr, true);
    }

    private void OnDisable()
    {
        RestoreOriginalEmission();
    }

    private void OnEnable()
    {
        hasAppliedEmission = false;
    }

    private void OnDestroy()
    {
        RestoreOriginalEmission();
    }

    private void Update()
    {
        if (targetMaterial == null || !hasEmission)
        {
            return;
        }

        float now = Time.unscaledTime;
        bool transitionSettled = Mathf.Abs(currentHdr - lastTargetHdr) < 0.001f;
        if (transitionSettled && now < nextStablePowerCheckTime)
        {
            return;
        }

        bool hasPower = startProcedure == null || startProcedure.HasAnyBatteryOn();
        float onHdr = GetOnEmissionHdr();
        float targetHdr = hasPower ? onHdr : offEmissionHdr;
        lastTargetHdr = targetHdr;
        float duration = hasPower ? rampUpSeconds : rampDownSeconds;

        if (duration <= 0f)
        {
            currentHdr = targetHdr;
        }
        else
        {
            float fullDelta = Mathf.Abs(onHdr - offEmissionHdr);
            float speed = fullDelta > 0f ? fullDelta / duration : 0f;
            currentHdr = Mathf.MoveTowards(currentHdr, targetHdr, speed * Time.deltaTime);
        }

        ApplyEmission(currentHdr);

        if (Mathf.Abs(currentHdr - targetHdr) < 0.001f)
        {
            nextStablePowerCheckTime = now + Mathf.Max(0.02f, stablePowerCheckInterval);
        }
    }

    private float GetOnEmissionHdr()
    {
        if (cachedUseMaterialOnEmissionHdr != useMaterialOnEmissionHdr ||
            !Mathf.Approximately(cachedInspectorOnEmissionHdr, onEmissionHdr) ||
            !Mathf.Approximately(cachedBaseEmissionHdr, baseEmissionHdr))
        {
            cachedUseMaterialOnEmissionHdr = useMaterialOnEmissionHdr;
            cachedInspectorOnEmissionHdr = onEmissionHdr;
            cachedBaseEmissionHdr = baseEmissionHdr;
            cachedOnHdr = useMaterialOnEmissionHdr ? baseEmissionHdr : onEmissionHdr;
        }

        return cachedOnHdr;
    }

    private void CacheBaseEmission()
    {
        if (!targetMaterial.HasProperty(EmissionColorId))
        {
            return;
        }

        Color emission = targetMaterial.GetColor(EmissionColorId);
        originalEmissionColor = emission;
        hasOriginalEmission = true;
        float max = GetMaxComponent(emission);
        baseEmissionHdr = max > 0f ? Mathf.Log(max, 2f) : 0f;
        baseEmissionColor = NormalizeColor(emission);
        hasEmission = true;
    }

    private void RestoreOriginalEmission()
    {
        if (!hasOriginalEmission || targetMaterial == null || !targetMaterial.HasProperty(EmissionColorId))
        {
            return;
        }

        targetMaterial.EnableKeyword("_EMISSION");
        targetMaterial.SetColor(EmissionColorId, originalEmissionColor);
    }

    private void ApplyEmission(float hdr, bool force = false)
    {
        if (!hasEmission)
        {
            return;
        }

        if (!force && hasAppliedEmission && Mathf.Abs(lastAppliedHdr - hdr) < 0.001f)
        {
            return;
        }

        hasAppliedEmission = true;
        lastAppliedHdr = hdr;

        float intensity = Mathf.Pow(2f, hdr);
        targetMaterial.EnableKeyword("_EMISSION");
        targetMaterial.SetColor(EmissionColorId, baseEmissionColor * intensity);
    }

    private static float GetMaxComponent(Color color)
    {
        float max = Mathf.Max(color.r, color.g, color.b);
        return max > 0f ? max : 1f;
    }

    private static Color NormalizeColor(Color color)
    {
        float max = GetMaxComponent(color);
        if (max > 1f)
        {
            return color / max;
        }
        return max > 0f ? color : Color.white;
    }
}
