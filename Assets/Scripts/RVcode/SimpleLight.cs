using System.Collections.Generic;
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
    [SerializeField] private bool autoFindTargets = false;
    [SerializeField] private bool excludeLightControlTargets = true;

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
    public event System.Action OnStateChanged;

    private void Awake()
    {
        if (startProcedure == null)
        {
            startProcedure = FindObjectOfType<StartProcedure>();
        }

        if (targetRenderers == null || targetRenderers.Length == 0)
        {
            if (targetRenderer != null)
            {
                targetRenderers = new[] { targetRenderer };
            }
            else if (autoFindTargets)
            {
                Renderer found = FindAutoRenderer();
                targetRenderers = found != null ? new[] { found } : new Renderer[0];
            }
            else
            {
                targetRenderers = new Renderer[0];
            }
        }

        if (targetLights == null || targetLights.Length == 0)
        {
            if (targetLight != null)
            {
                targetLights = new[] { targetLight };
            }
            else if (autoFindTargets)
            {
                Light found = FindAutoLight();
                targetLights = found != null ? new[] { found } : new Light[0];
            }
            else
            {
                targetLights = new Light[0];
            }
        }

        PruneLightControlTargets();

        desiredOn = defaultOn;
        CacheEmissionColor();
        bool effectiveOn = GetEffectiveOn();
        ApplyState(effectiveOn);
        lastEffectiveOn = effectiveOn;
        OnStateChanged?.Invoke();
    }

    private void PruneLightControlTargets()
    {
        if (!excludeLightControlTargets)
        {
            return;
        }

        LightControl[] lightControls = FindObjectsOfType<LightControl>();
        if (lightControls == null || lightControls.Length == 0)
        {
            return;
        }

        if (targetLights != null && targetLights.Length > 0)
        {
            HashSet<Light> seenLights = new HashSet<Light>();
            List<Light> filteredLights = new List<Light>(targetLights.Length);
            int removed = 0;

            for (int i = 0; i < targetLights.Length; i++)
            {
                Light light = targetLights[i];
                if (light == null)
                {
                    continue;
                }

                if (!seenLights.Add(light))
                {
                    continue;
                }

                if (IsControlledByAny(lightControls, light))
                {
                    removed++;
                    continue;
                }

                filteredLights.Add(light);
            }

            if (removed > 0)
            {
#if UNITY_EDITOR
                Debug.LogWarning($"{name}: SimpleLight ignored {removed} Light target(s) owned by LightControl.", this);
#endif
            }

            targetLights = filteredLights.ToArray();
        }

        if (targetRenderers != null && targetRenderers.Length > 0)
        {
            HashSet<Renderer> seenRenderers = new HashSet<Renderer>();
            List<Renderer> filteredRenderers = new List<Renderer>(targetRenderers.Length);
            int removed = 0;

            for (int i = 0; i < targetRenderers.Length; i++)
            {
                Renderer renderer = targetRenderers[i];
                if (renderer == null)
                {
                    continue;
                }

                if (!seenRenderers.Add(renderer))
                {
                    continue;
                }

                if (IsControlledByAny(lightControls, renderer))
                {
                    removed++;
                    continue;
                }

                filteredRenderers.Add(renderer);
            }

            if (removed > 0)
            {
#if UNITY_EDITOR
                Debug.LogWarning($"{name}: SimpleLight ignored {removed} Renderer target(s) owned by LightControl.", this);
#endif
            }

            targetRenderers = filteredRenderers.ToArray();
        }
    }

    private Light FindAutoLight()
    {
        if (!excludeLightControlTargets)
        {
            return GetComponentInChildren<Light>();
        }

        LightControl[] lightControls = FindObjectsOfType<LightControl>();
        if (lightControls == null || lightControls.Length == 0)
        {
            return GetComponentInChildren<Light>();
        }

        Light[] lights = GetComponentsInChildren<Light>(true);
        for (int i = 0; i < lights.Length; i++)
        {
            Light light = lights[i];
            if (light != null && !IsControlledByAny(lightControls, light))
            {
                return light;
            }
        }

        return null;
    }

    private Renderer FindAutoRenderer()
    {
        if (!excludeLightControlTargets)
        {
            return GetComponentInChildren<Renderer>();
        }

        LightControl[] lightControls = FindObjectsOfType<LightControl>();
        if (lightControls == null || lightControls.Length == 0)
        {
            return GetComponentInChildren<Renderer>();
        }

        Renderer[] renderers = GetComponentsInChildren<Renderer>(true);
        for (int i = 0; i < renderers.Length; i++)
        {
            Renderer renderer = renderers[i];
            if (renderer != null && !IsControlledByAny(lightControls, renderer))
            {
                return renderer;
            }
        }

        return null;
    }

    private static bool IsControlledByAny(LightControl[] lightControls, Light light)
    {
        if (light == null)
        {
            return false;
        }

        for (int i = 0; i < lightControls.Length; i++)
        {
            LightControl lc = lightControls[i];
            if (lc != null && lc.ControlsLight(light))
            {
                return true;
            }
        }
        return false;
    }

    private static bool IsControlledByAny(LightControl[] lightControls, Renderer renderer)
    {
        if (renderer == null)
        {
            return false;
        }

        for (int i = 0; i < lightControls.Length; i++)
        {
            LightControl lc = lightControls[i];
            if (lc != null && lc.ControlsRenderer(renderer))
            {
                return true;
            }
        }
        return false;
    }

    private void Update()
    {
        bool effectiveOn = GetEffectiveOn();
        if (effectiveOn != lastEffectiveOn)
        {
            lastEffectiveOn = effectiveOn;
            ApplyState(effectiveOn);
            OnStateChanged?.Invoke();
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
        OnStateChanged?.Invoke();
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
