using System.Collections;
using UnityEngine;
using UnityEngine.Events;

/// <summary>
/// Add this component to any scene GameObject to make it interactive.
/// Flags control which interactions are available; UnityEvents let designers
/// wire up behaviour directly in the Inspector without extra code.
///
///  interactable → [F]   trigger a behaviour; plays squash → restore → stretch → restore
///  carryable    → [LMB] hold to carry, release to drop
///  collectable  → [RMB] plays pop-and-vanish animation, then destroys
/// </summary>
public class WorldObject : MonoBehaviour
{
    // ── Flags ────────────────────────────────────────────────────────────────
    [Header("Interaction Flags")]
    public bool interactable = false;
    public bool carryable    = false;
    public bool collectable  = false;

    // ── Messages ─────────────────────────────────────────────────────────────
    [Header("Messages")]
    [Tooltip("Shown in the shared info label when this object is interacted with.")]
    public string interactMessage = "";
    [Tooltip("Shown in the shared info label when this object is collected.")]
    public string collectMessage  = "";

    // ── Animation tuning ─────────────────────────────────────────────────────
    [Header("Squash & Stretch")]
    [Tooltip("Duration of each animation phase (squash / restore / stretch / restore).")]
    [SerializeField] private float animPhaseTime    = 0.12f;
    [Tooltip("Half-duration of the grow + squash/stretch phase during collect.")]
    [SerializeField] private float collectGrowTime  = 0.15f;
    [Tooltip("Duration of the shrink-to-zero phase during collect.")]
    [SerializeField] private float collectShrinkTime = 0.22f;

    // ── Events ───────────────────────────────────────────────────────────────
    [Header("Events")]
    public UnityEvent<GameObject> onInteract;
    public UnityEvent<GameObject> onPickUp;
    public UnityEvent<GameObject> onDrop;
    public UnityEvent<GameObject> onCollect;

    // ── Internal API (called by InteractionSystem) ───────────────────────────
    internal void TriggerInteract(GameObject actor) => onInteract?.Invoke(actor);
    internal void TriggerPickUp(GameObject actor)   => onPickUp?.Invoke(actor);
    internal void TriggerDrop(GameObject actor)     => onDrop?.Invoke(actor);
    internal void TriggerCollect(GameObject actor)  => onCollect?.Invoke(actor);

    // ── Animations ───────────────────────────────────────────────────────────
    private Coroutine _interactAnim;

    /// <summary>Plays squash → restore → stretch → restore once. Re-entrant calls are ignored.</summary>
    public void PlayInteractAnim()
    {
        if (_interactAnim != null) return;   // already animating — don't stack
        _interactAnim = StartCoroutine(InteractAnimCo());
    }

    /// <summary>Plays the collect pop animation (grow + squash/stretch → shrink to zero), then calls onComplete.</summary>
    public void PlayCollectAnim(System.Action onComplete)
        => StartCoroutine(CollectAnimCo(onComplete));

    // ── Interact animation ────────────────────────────────────────────────────
    IEnumerator InteractAnimCo()
    {
        Vector3 orig    = transform.localScale;
        Vector3 squash  = new Vector3(orig.x * 1.35f, orig.y * 0.55f, orig.z * 1.35f);
        Vector3 stretch = new Vector3(orig.x * 0.75f, orig.y * 1.45f, orig.z * 0.75f);
        float   t       = animPhaseTime;
        float   e;

        // Squash (fast out)
        e = 0f;
        while (e < t) { e += Time.deltaTime; transform.localScale = Vector3.LerpUnclamped(orig,    squash,  EaseOut  (Mathf.Clamp01(e / t))); yield return null; }
        transform.localScale = squash;

        // Restore (smooth)
        e = 0f;
        while (e < t) { e += Time.deltaTime; transform.localScale = Vector3.LerpUnclamped(squash,  orig,    EaseInOut(Mathf.Clamp01(e / t))); yield return null; }
        transform.localScale = orig;

        // Stretch (fast out)
        e = 0f;
        while (e < t) { e += Time.deltaTime; transform.localScale = Vector3.LerpUnclamped(orig,    stretch, EaseOut  (Mathf.Clamp01(e / t))); yield return null; }
        transform.localScale = stretch;

        // Restore (smooth)
        e = 0f;
        while (e < t) { e += Time.deltaTime; transform.localScale = Vector3.LerpUnclamped(stretch, orig,    EaseInOut(Mathf.Clamp01(e / t))); yield return null; }
        transform.localScale = orig;

        _interactAnim = null;
    }

    // ── Collect animation ─────────────────────────────────────────────────────
    IEnumerator CollectAnimCo(System.Action onComplete)
    {
        Vector3 orig         = transform.localScale;
        float   totalGrow    = collectGrowTime * 2f;   // squash phase + stretch phase
        float   maxOverall   = 1.3f;
        Vector3 squashShape  = new Vector3(1.35f, 0.55f, 1.35f);
        Vector3 stretchShape = new Vector3(0.72f, 1.55f, 0.72f);

        // Grow + squash → peak stretch (EaseOut on overall size, EaseInOut on shape)
        float elapsed = 0f;
        while (elapsed < totalGrow)
        {
            elapsed += Time.deltaTime;
            float   p       = Mathf.Clamp01(elapsed / totalGrow);
            float   overall = Mathf.Lerp(1f, maxOverall, EaseOut(p));
            Vector3 shape   = p <= 0.5f
                ? Vector3.Lerp(Vector3.one,   squashShape,  EaseInOut(p * 2f))
                : Vector3.Lerp(squashShape,   stretchShape, EaseInOut((p - 0.5f) * 2f));
            transform.localScale = Vector3.Scale(orig * overall, shape);
            yield return null;
        }

        // Shrink to zero — accelerates (EaseIn)
        Vector3 peak    = transform.localScale;
        elapsed = 0f;
        while (elapsed < collectShrinkTime)
        {
            elapsed += Time.deltaTime;
            transform.localScale = Vector3.Lerp(peak, Vector3.zero,
                EaseIn(Mathf.Clamp01(elapsed / collectShrinkTime)));
            yield return null;
        }

        transform.localScale = Vector3.zero;
        onComplete?.Invoke();
    }

    // ── Easing ───────────────────────────────────────────────────────────────
    static float EaseOut  (float t) => 1f - (1f - t) * (1f - t);
    static float EaseIn   (float t) => t * t;
    static float EaseInOut(float t) => t < 0.5f ? 2f * t * t : 1f - 2f * (1f - t) * (1f - t);
}
