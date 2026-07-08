using System.Collections;
using UnityEngine;
using UnityEngine.Events;

public enum InteractionFeedbackState
{
    None,
    Hover,
    Focus,
    Valid,
    Invalid,
    Active,
    Completed,
    Cooldown
}

/// <summary>
/// Shared feedback hook for usable world objects. It is intentionally optional:
/// systems can add it at runtime, while designers can preconfigure clips,
/// particles, tint colors, and pulse strength in the Inspector.
/// </summary>
public class InteractionFeedback : MonoBehaviour
{
    [System.Serializable]
    public class FeedbackStateEvent : UnityEvent<InteractionFeedbackState, string> { }

    [Header("Visual")]
    [SerializeField] private bool autoCollectRenderers = true;
    [SerializeField] private Renderer[] renderers;
    [SerializeField] private Color hoverTint = new Color(1f, 0.95f, 0.45f, 1f);
    [SerializeField] private Color validTint = new Color(0.35f, 1f, 0.45f, 1f);
    [SerializeField] private Color invalidTint = new Color(1f, 0.25f, 0.18f, 1f);
    [SerializeField] private Color activeTint = new Color(0.3f, 0.75f, 1f, 1f);
    [SerializeField] private Color completedTint = new Color(1f, 1f, 1f, 1f);
    [SerializeField] private float tintStrength = 0.22f;
    [SerializeField] private float pulseScale = 0.035f;
    [SerializeField] private float pulseSpeed = 9f;
    [SerializeField] private float completedHoldSeconds = 0.35f;

    [Header("Effects")]
    [SerializeField] private ParticleSystem validParticles;
    [SerializeField] private ParticleSystem invalidParticles;
    [SerializeField] private ParticleSystem completedParticles;

    [Header("Audio")]
    [SerializeField] private AudioSource audioSource;
    [SerializeField] private AudioClip startClip;
    [SerializeField] private AudioClip successClip;
    [SerializeField] private AudioClip failureClip;
    [SerializeField] private AudioClip unavailableClip;

    [Header("Events")]
    public FeedbackStateEvent onStateChanged;

    private readonly MaterialPropertyBlock _propertyBlock = new MaterialPropertyBlock();
    private Vector3 _baseScale;
    private InteractionFeedbackState _state = InteractionFeedbackState.None;
    private Coroutine _completedRoutine;

    public InteractionFeedbackState State => _state;

    private void Awake()
    {
        _baseScale = transform.localScale;
        if (autoCollectRenderers && (renderers == null || renderers.Length == 0))
            renderers = GetComponentsInChildren<Renderer>(true);
        if (audioSource == null)
            audioSource = GetComponent<AudioSource>();
    }

    private void OnDisable()
    {
        if (_completedRoutine != null)
        {
            StopCoroutine(_completedRoutine);
            _completedRoutine = null;
        }
        transform.localScale = _baseScale;
        ClearTint();
        _state = InteractionFeedbackState.None;
    }

    private void Update()
    {
        if (_state != InteractionFeedbackState.Hover &&
            _state != InteractionFeedbackState.Focus &&
            _state != InteractionFeedbackState.Valid &&
            _state != InteractionFeedbackState.Invalid &&
            _state != InteractionFeedbackState.Active)
        {
            return;
        }

        float pulse = Mathf.Sin(Time.unscaledTime * pulseSpeed) * pulseScale;
        if (_state == InteractionFeedbackState.Active)
            pulse *= 1.5f;
        transform.localScale = _baseScale * (1f + pulse);
    }

    public void SetState(InteractionFeedbackState state, string message = null)
    {
        if (_state == state && state != InteractionFeedbackState.Completed)
            return;

        if (_completedRoutine != null)
        {
            StopCoroutine(_completedRoutine);
            _completedRoutine = null;
        }

        _state = state;
        ApplyStateVisual(state);
        PlayStateEffects(state);
        onStateChanged?.Invoke(state, message ?? string.Empty);

        if (state == InteractionFeedbackState.Completed)
            _completedRoutine = StartCoroutine(ClearCompletedAfterDelay());
    }

    public void Clear()
    {
        SetState(InteractionFeedbackState.None);
    }

    private IEnumerator ClearCompletedAfterDelay()
    {
        yield return new WaitForSeconds(completedHoldSeconds);
        _completedRoutine = null;
        SetState(InteractionFeedbackState.None);
    }

    private void ApplyStateVisual(InteractionFeedbackState state)
    {
        transform.localScale = _baseScale;

        switch (state)
        {
            case InteractionFeedbackState.Hover:
            case InteractionFeedbackState.Focus:
                ApplyTint(hoverTint);
                break;
            case InteractionFeedbackState.Valid:
                ApplyTint(validTint);
                break;
            case InteractionFeedbackState.Invalid:
            case InteractionFeedbackState.Cooldown:
                ApplyTint(invalidTint);
                break;
            case InteractionFeedbackState.Active:
                ApplyTint(activeTint);
                break;
            case InteractionFeedbackState.Completed:
                ApplyTint(completedTint);
                break;
            default:
                ClearTint();
                break;
        }
    }

    private void PlayStateEffects(InteractionFeedbackState state)
    {
        switch (state)
        {
            case InteractionFeedbackState.Active:
                PlayClip(startClip);
                break;
            case InteractionFeedbackState.Valid:
                PlayParticles(validParticles);
                break;
            case InteractionFeedbackState.Invalid:
                PlayParticles(invalidParticles);
                PlayClip(failureClip);
                break;
            case InteractionFeedbackState.Completed:
                PlayParticles(completedParticles);
                PlayClip(successClip);
                break;
            case InteractionFeedbackState.Cooldown:
                PlayClip(unavailableClip);
                break;
        }
    }

    private void ApplyTint(Color tint)
    {
        if (renderers == null)
            return;

        Color mixed = Color.Lerp(Color.white, tint, Mathf.Clamp01(tintStrength));
        for (int i = 0; i < renderers.Length; i++)
        {
            Renderer r = renderers[i];
            if (r == null) continue;
            r.GetPropertyBlock(_propertyBlock);
            if (r.sharedMaterial != null && r.sharedMaterial.HasProperty("_BaseColor"))
                _propertyBlock.SetColor("_BaseColor", mixed);
            if (r.sharedMaterial != null && r.sharedMaterial.HasProperty("_Color"))
                _propertyBlock.SetColor("_Color", mixed);
            r.SetPropertyBlock(_propertyBlock);
        }
    }

    private void ClearTint()
    {
        if (renderers == null)
            return;

        for (int i = 0; i < renderers.Length; i++)
        {
            Renderer r = renderers[i];
            if (r == null) continue;
            r.SetPropertyBlock(null);
        }
    }

    private void PlayParticles(ParticleSystem particles)
    {
        if (particles != null)
            particles.Play(true);
    }

    private void PlayClip(AudioClip clip)
    {
        if (clip == null || audioSource == null)
            return;

        audioSource.PlayOneShot(clip);
    }

    public static InteractionFeedback GetOrCreate(Component component)
    {
        if (component == null)
            return null;
        return GetOrCreate(component.gameObject);
    }

    public static InteractionFeedback GetOrCreate(GameObject target)
    {
        if (target == null)
            return null;

        InteractionFeedback feedback = target.GetComponentInParent<InteractionFeedback>();
        if (feedback == null)
            feedback = target.AddComponent<InteractionFeedback>();
        return feedback;
    }
}
