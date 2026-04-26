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

    // ── Physics ───────────────────────────────────────────────────────────────
    [Header("Physics")]
    [Tooltip("When true, player/carry scripts can actively push this object. " +
             "When false, script-driven push is disabled; Rigidbody gravity/physics still follow its own inspector settings.")]
    public bool canBePushed = false;

    [Header("Placement Options")]
    [Tooltip("If true, the object can be placed on vertical surfaces ignoring gravity.")]
    public bool canBePlacedOnWall = false;
    [Tooltip("If true, the object can be placed on ceilings ignoring gravity.")]
    public bool canBePlacedOnCeiling = false;
    
    internal bool isPlacedAndAttached = false;

    // ── Messages ─────────────────────────────────────────────────────────────
    [Header("Messages")]
    [Tooltip("Shown in the shared info label when this object is interacted with.")]
    public string interactMessage = "";
    [Tooltip("Shown in the shared info label when this object is collected.")]
    public string collectMessage  = "";

    [Header("Inventory")]
    [Tooltip("Optional item data passed to InventoryCameraController when this object is collected.")]
    public ItemData collectItemData;

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
    private Coroutine _collectAnim;
    private Vector3   _baseScale;        // original scale, cached before any anim
    private Rigidbody _rb;
    private bool      _isCarried;
    private Rigidbody _animRb;           // rb being locked by current anim
    private bool      _animPrevKinematic;
    private Vector3   _posBeforeAnim;    // world position before animation started; restored on CancelAnims

    /// <summary>True while this object is currently carried by the player.</summary>
    public bool IsCarried => _isCarried;

    void Awake()
    {
        _baseScale = transform.localScale;
        _rb = GetComponent<Rigidbody>();
        ApplyPushabilityState();
    }

    void OnValidate() => ApplyPushabilityState();

    void FixedUpdate()
    {
        // Re-assert each physics step so external forces/scripts cannot keep a
        // non-pushable object dynamic by accident.
        ApplyPushabilityState();
    }

    internal void SetCarriedState(bool carried)
    {
        _isCarried = carried;
        ApplyPushabilityState();
    }

    void ApplyPushabilityState()
    {
        if (_rb == null) _rb = GetComponent<Rigidbody>();
        if (_rb == null) return;

        // While carried, placed on wall/ceiling, or during scale animations, logic owns RB mode.
        if (_isCarried || _animRb != null || isPlacedAndAttached) return;

        if (canBePushed)
        {
            _rb.isKinematic = false;
            return;
        }

        // Non-pushable objects now preserve their own Rigidbody settings.
        // If gravity is enabled and the body was kinematic, release it so falling looks natural.
        if (_rb.useGravity && _rb.isKinematic)
            _rb.isKinematic = false;
    }

    /// <summary>
    /// Immediately stops any running animation, restores scale and Rigidbody state.
    /// Call this before externally moving the object (e.g. PickUp).
    /// </summary>
    public void CancelAnims()
    {
        bool hadInteract = _interactAnim != null;
        bool had = hadInteract || _collectAnim != null;
        if (_interactAnim != null) { StopCoroutine(_interactAnim); _interactAnim = null; }
        if (_collectAnim  != null) { StopCoroutine(_collectAnim);  _collectAnim  = null; }
        transform.localScale = _baseScale;
        // Restore position: interact anchor adjusts position during scaling to keep the active bottom support fixed.
        // Revert to pre-animation pose so object remains where physics left it.
        if (hadInteract) transform.position = _posBeforeAnim;
        if (had && _animRb != null)
        {
            _animRb.isKinematic    = _animPrevKinematic;
            _animRb.velocity       = Vector3.zero;
            _animRb.angularVelocity = Vector3.zero;
            _animRb = null;
        }
    }

    /// <summary>Plays squash → restore → stretch → restore once. Re-entrant calls are ignored.</summary>
    public void PlayInteractAnim()
    {
        if (_interactAnim != null) return;   // already animating — don't stack
        _interactAnim = StartCoroutine(InteractAnimCo());
    }

    /// <summary>Plays the collect pop animation (grow + squash/stretch → shrink to zero), then calls onComplete.</summary>
    public void PlayCollectAnim(System.Action onComplete)
        => _collectAnim = StartCoroutine(CollectAnimCo(onComplete));

    // ── Interact animation ────────────────────────────────────────────────────
    IEnumerator InteractAnimCo()
    {
        Vector3 origScale  = transform.localScale;
        Vector3 origPos    = transform.position;
        _posBeforeAnim     = origPos;   // snapshot BEFORE any scale/position change
        Vector3 downDir      = Physics.gravity.sqrMagnitude > 0.0001f ? Physics.gravity.normalized : Vector3.down;
        Collider[] colliders = GetComponentsInChildren<Collider>(true);
        Vector3 squash    = new Vector3(origScale.x * 1.17f, origScale.y * 0.77f, origScale.z * 1.17f);
        Vector3 stretch   = new Vector3(origScale.x * 0.87f, origScale.y * 1.22f, origScale.z * 0.87f);
        float   t         = animPhaseTime;
        float   e;

        float BottomSupportDot()
        {
            bool found = false;
            float support = float.NegativeInfinity;
            for (int i = 0; i < colliders.Length; i++)
            {
                Collider c = colliders[i];
                if (c == null || !c.enabled)
                    continue;

                Bounds b = c.bounds;
                float d = Vector3.Dot(b.center, downDir)
                        + Mathf.Abs(downDir.x) * b.extents.x
                        + Mathf.Abs(downDir.y) * b.extents.y
                        + Mathf.Abs(downDir.z) * b.extents.z;
                if (!found || d > support)
                {
                    support = d;
                    found = true;
                }
            }

            return found ? support : Vector3.Dot(transform.position, downDir);
        }

        float origBottomSupport = BottomSupportDot();

        // Make kinematic so growing/shrinking colliders can't be pushed by the ground
        _animRb = GetComponent<Rigidbody>();
        if (_animRb != null)
        {
            _animPrevKinematic      = _animRb.isKinematic;
            _animRb.velocity        = Vector3.zero;   // clear before kinematic or stored velocity replays
            _animRb.angularVelocity = Vector3.zero;
            _animRb.isKinematic     = true;
        }

        // Keep the currently downward-facing bottom support point fixed while scaling.
        void AnchorBottom()
        {
            float currentBottomSupport = BottomSupportDot();
            transform.position += downDir * (origBottomSupport - currentBottomSupport);
        }

        // Squash (fast out)
        e = 0f;
        while (e < t) { e += Time.deltaTime; transform.localScale = Vector3.LerpUnclamped(origScale, squash,    EaseOut  (Mathf.Clamp01(e / t))); AnchorBottom(); yield return null; }
        transform.localScale = squash;   AnchorBottom();

        // Restore (smooth)
        e = 0f;
        while (e < t) { e += Time.deltaTime; transform.localScale = Vector3.LerpUnclamped(squash,    origScale, EaseInOut(Mathf.Clamp01(e / t))); AnchorBottom(); yield return null; }
        transform.localScale = origScale; AnchorBottom();

        // Stretch (fast out)
        e = 0f;
        while (e < t) { e += Time.deltaTime; transform.localScale = Vector3.LerpUnclamped(origScale, stretch,   EaseOut  (Mathf.Clamp01(e / t))); AnchorBottom(); yield return null; }
        transform.localScale = stretch;  AnchorBottom();

        // Restore (smooth)
        e = 0f;
        while (e < t) { e += Time.deltaTime; transform.localScale = Vector3.LerpUnclamped(stretch,   origScale, EaseInOut(Mathf.Clamp01(e / t))); AnchorBottom(); yield return null; }
        transform.localScale = origScale;
        AnchorBottom();

        if (_animRb != null)
        {
            _animRb.isKinematic    = _animPrevKinematic;
            _animRb.velocity       = Vector3.zero;   // don't launch the object after animation ends
            _animRb.angularVelocity = Vector3.zero;
            _animRb = null;
        }
        _interactAnim = null;
    }

    // ── Collect animation ─────────────────────────────────────────────────────
    IEnumerator CollectAnimCo(System.Action onComplete)
    {
        Vector3 orig         = transform.localScale;
        Vector3 lockedPos    = transform.position;   // keep base anchored
        float   totalGrow    = collectGrowTime * 2f;
        float   maxOverall   = 1.3f;
        Vector3 squashShape  = new Vector3(1.35f, 0.55f, 1.35f);
        Vector3 stretchShape = new Vector3(0.72f, 1.55f, 0.72f);

        // Kinematic so scaled colliders don't interact with ground during anim
        _animRb = GetComponent<Rigidbody>();
        if (_animRb != null)
        {
            _animPrevKinematic      = _animRb.isKinematic;
            _animRb.velocity        = Vector3.zero;
            _animRb.angularVelocity = Vector3.zero;
            _animRb.isKinematic     = true;
        }

        // Grow + squash → peak stretch (EaseOut on overall size, EaseInOut on shape)
        float elapsed = 0f;
        while (elapsed < totalGrow)
        {
            elapsed += Time.deltaTime;
            float   p       = Mathf.Clamp01(elapsed / totalGrow);
            float   overall = Mathf.Lerp(1f, maxOverall, EaseOut(p));
            Vector3 shape   = p <= 0.5f
                ? Vector3.Lerp(Vector3.one,  squashShape,  EaseInOut(p * 2f))
                : Vector3.Lerp(squashShape,  stretchShape, EaseInOut((p - 0.5f) * 2f));
            transform.localScale = Vector3.Scale(orig * overall, shape);
            transform.position   = lockedPos;
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
            transform.position = lockedPos;
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
