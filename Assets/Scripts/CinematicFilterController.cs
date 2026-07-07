using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

[RequireComponent(typeof(Volume))]
public class CinematicFilterController : MonoBehaviour
{
    private Volume _volume;
    private VolumeProfile _profile;

    [Range(0f, 1f)]
    [Tooltip("Lerp the strength of the cinematic effects.")]
    public float filterIntensity = 1f;

    private Color _tealShadows;
    private Color _orangeMidtones;
    private float _lastAppliedIntensity = -1f;
    private FilmGrain _filmGrain;
    private ShadowsMidtonesHighlights _shadowsMidtonesHighlights;
    private LiftGammaGain _liftGammaGain;
    private Bloom _bloom;
    private ColorAdjustments _colorAdjustments;
    private Vignette _vignette;
    private bool _hasFilmGrain;
    private bool _hasShadowsMidtonesHighlights;
    private bool _hasLiftGammaGain;
    private bool _hasBloom;
    private bool _hasColorAdjustments;
    private bool _hasVignette;

    void Start()
    {
        _volume = GetComponent<Volume>();
        
        // Use profile to modify a local instance instead of the shared asset
        // If you want to modify the global asset, you would use sharedProfile
        _profile = _volume.profile;

        if (_profile == null)
        {
            Debug.LogWarning("CinematicFilterController: No Volume Profile found on this object.");
            return;
        }

        // Parse hex colors for Teal & Orange grading
        ColorUtility.TryParseHtmlString("#1be2adff", out _tealShadows);
        ColorUtility.TryParseHtmlString("#f78c29ff", out _orangeMidtones);
        CacheProfileComponents();
        SetCinematicLook(true);
        enabled = false;
    }

    /// <summary>
    /// Modifies the VolumeProfile to apply a cinematic Instagram-style look.
    /// Interpolates effect values based on filterIntensity.
    /// </summary>
    public void SetCinematicLook()
    {
        SetCinematicLook(true);
        enabled = false;
    }

    private void CacheProfileComponents()
    {
        _hasFilmGrain = _profile.TryGet(out _filmGrain);
        _hasShadowsMidtonesHighlights = _profile.TryGet(out _shadowsMidtonesHighlights);
        _hasLiftGammaGain = _profile.TryGet(out _liftGammaGain);
        _hasBloom = _profile.TryGet(out _bloom);
        _hasColorAdjustments = _profile.TryGet(out _colorAdjustments);
        _hasVignette = _profile.TryGet(out _vignette);
    }

    private void SetCinematicLook(bool force)
    {
        if (_profile == null) return;
        float clampedIntensity = Mathf.Clamp01(filterIntensity);
        if (!force && Mathf.Abs(clampedIntensity - _lastAppliedIntensity) < 0.001f)
            return;

        _lastAppliedIntensity = clampedIntensity;

        // 1. Film Grain
        if (_hasFilmGrain)
        {
            _filmGrain.intensity.overrideState = true;
            // Medium intensity (0.3)
            _filmGrain.intensity.value = Mathf.Lerp(0f, 0.3f, clampedIntensity);
            
            _filmGrain.response.overrideState = true;
            // Lower response means visible mostly in darker areas/midtones
            _filmGrain.response.value = Mathf.Lerp(0.8f, 0.5f, clampedIntensity);
        }

        // 2. Teal & Orange Grade
        if (_hasShadowsMidtonesHighlights)
        {
            _shadowsMidtonesHighlights.shadows.overrideState = true;
            // Shadows to Teal
            Vector4 baseShadows = new Vector4(1f, 1f, 1f, 0f);
            Vector4 targetShadows = new Vector4(_tealShadows.r, _tealShadows.g, _tealShadows.b, 0f);
            _shadowsMidtonesHighlights.shadows.value = Vector4.Lerp(baseShadows, targetShadows, clampedIntensity);

            _shadowsMidtonesHighlights.midtones.overrideState = true;
            // Midtones to Warm Orange
            Vector4 baseMidtones = new Vector4(1f, 1f, 1f, 0f);
            Vector4 targetMidtones = new Vector4(_orangeMidtones.r, _orangeMidtones.g, _orangeMidtones.b, 0f);
            _shadowsMidtonesHighlights.midtones.value = Vector4.Lerp(baseMidtones, targetMidtones, clampedIntensity);

            _shadowsMidtonesHighlights.highlights.overrideState = true;
            // Highlights to Warm Orange
            Vector4 baseHighlights = new Vector4(1f, 1f, 1f, 0f);
            Vector4 targetHighlights = new Vector4(_orangeMidtones.r, _orangeMidtones.g, _orangeMidtones.b, 0f);
            _shadowsMidtonesHighlights.highlights.value = Vector4.Lerp(baseHighlights, targetHighlights, clampedIntensity);
        }

        // 3 & 4. High-light Suppression and Low Contrast Shadows
        if (_hasLiftGammaGain)
        {
            // Lift shadow floor (Lift) -> Low Contrast Shadows
            _liftGammaGain.lift.overrideState = true;
            Vector4 baseLift = new Vector4(1f, 1f, 1f, 0f);
            // Positive w component lifts the shadows (greys out blacks)
            Vector4 targetLift = new Vector4(1f, 1f, 1f, 0.05f); 
            _liftGammaGain.lift.value = Vector4.Lerp(baseLift, targetLift, clampedIntensity);

            // Highlight Suppression (Gain) -> Prevent highlights from over-exposing
            _liftGammaGain.gain.overrideState = true;
            Vector4 baseGain = new Vector4(1f, 1f, 1f, 0f);
            // Negative w component reduces gain (suppresses highlights)
            Vector4 targetGain = new Vector4(1f, 1f, 1f, -0.08f); 
            _liftGammaGain.gain.value = Vector4.Lerp(baseGain, targetGain, clampedIntensity);
        }

        // 5. Soft Lighting (Bloom)
        if (_hasBloom)
        {
            _bloom.intensity.overrideState = true;
            _bloom.intensity.value = Mathf.Lerp(0f, 0.85f, clampedIntensity);

            _bloom.scatter.overrideState = true;
            _bloom.scatter.value = Mathf.Lerp(0.45f, 0.55f, clampedIntensity);
        }

        if (_hasColorAdjustments)
        {
            _colorAdjustments.postExposure.overrideState = true;
            _colorAdjustments.postExposure.value = Mathf.Lerp(0f, 0.12f, clampedIntensity);

            _colorAdjustments.contrast.overrideState = true;
            _colorAdjustments.contrast.value = Mathf.Lerp(0f, 14f, clampedIntensity);

            _colorAdjustments.saturation.overrideState = true;
            _colorAdjustments.saturation.value = Mathf.Lerp(0f, 18f, clampedIntensity);

            _colorAdjustments.colorFilter.overrideState = true;
            _colorAdjustments.colorFilter.value = Color.Lerp(Color.white, new Color(1f, 0.965f, 0.9f, 1f), clampedIntensity);
        }

        if (_hasVignette)
        {
            _vignette.intensity.overrideState = true;
            _vignette.intensity.value = Mathf.Lerp(0f, 0.18f, clampedIntensity);

            _vignette.smoothness.overrideState = true;
            _vignette.smoothness.value = Mathf.Lerp(0.2f, 0.42f, clampedIntensity);
        }
    }
}
