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
    }

    void Update()
    {
        SetCinematicLook();
    }

    /// <summary>
    /// Modifies the VolumeProfile to apply a cinematic Instagram-style look.
    /// Interpolates effect values based on filterIntensity.
    /// </summary>
    public void SetCinematicLook()
    {
        if (_profile == null) return;

        // 1. Film Grain
        if (_profile.TryGet(out FilmGrain grain))
        {
            grain.intensity.overrideState = true;
            // Medium intensity (0.3)
            grain.intensity.value = Mathf.Lerp(0f, 0.3f, filterIntensity);
            
            grain.response.overrideState = true;
            // Lower response means visible mostly in darker areas/midtones
            grain.response.value = Mathf.Lerp(0.8f, 0.5f, filterIntensity); 
        }

        // 2. Teal & Orange Grade
        if (_profile.TryGet(out ShadowsMidtonesHighlights smh))
        {
            smh.shadows.overrideState = true;
            // Shadows to Teal
            Vector4 baseShadows = new Vector4(1f, 1f, 1f, 0f);
            Vector4 targetShadows = new Vector4(_tealShadows.r, _tealShadows.g, _tealShadows.b, 0f);
            smh.shadows.value = Vector4.Lerp(baseShadows, targetShadows, filterIntensity);

            smh.midtones.overrideState = true;
            // Midtones to Warm Orange
            Vector4 baseMidtones = new Vector4(1f, 1f, 1f, 0f);
            Vector4 targetMidtones = new Vector4(_orangeMidtones.r, _orangeMidtones.g, _orangeMidtones.b, 0f);
            smh.midtones.value = Vector4.Lerp(baseMidtones, targetMidtones, filterIntensity);

            smh.highlights.overrideState = true;
            // Highlights to Warm Orange
            Vector4 baseHighlights = new Vector4(1f, 1f, 1f, 0f);
            Vector4 targetHighlights = new Vector4(_orangeMidtones.r, _orangeMidtones.g, _orangeMidtones.b, 0f);
            smh.highlights.value = Vector4.Lerp(baseHighlights, targetHighlights, filterIntensity);
        }

        // 3 & 4. High-light Suppression and Low Contrast Shadows
        if (_profile.TryGet(out LiftGammaGain lgg))
        {
            // Lift shadow floor (Lift) -> Low Contrast Shadows
            lgg.lift.overrideState = true;
            Vector4 baseLift = new Vector4(1f, 1f, 1f, 0f);
            // Positive w component lifts the shadows (greys out blacks)
            Vector4 targetLift = new Vector4(1f, 1f, 1f, 0.05f); 
            lgg.lift.value = Vector4.Lerp(baseLift, targetLift, filterIntensity);

            // Highlight Suppression (Gain) -> Prevent highlights from over-exposing
            lgg.gain.overrideState = true;
            Vector4 baseGain = new Vector4(1f, 1f, 1f, 0f);
            // Negative w component reduces gain (suppresses highlights)
            Vector4 targetGain = new Vector4(1f, 1f, 1f, -0.08f); 
            lgg.gain.value = Vector4.Lerp(baseGain, targetGain, filterIntensity);
        }

        // 5. Soft Lighting (Bloom)
        if (_profile.TryGet(out Bloom bloom))
        {
            bloom.intensity.overrideState = true;
            // Low intensity (0.4)
            bloom.intensity.value = Mathf.Lerp(0f, 0.4f, filterIntensity);

            bloom.scatter.overrideState = true;
            // High scatter (0.7) for a hazy, soft look
            bloom.scatter.value = Mathf.Lerp(0.5f, 0.7f, filterIntensity);
        }
    }
}
