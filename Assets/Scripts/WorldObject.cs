using UnityEngine;
using UnityEngine.Events;

/// <summary>
/// Add this component to any scene GameObject to make it interactive.
/// Flags control which interactions are available; UnityEvents let designers
/// wire up behaviour directly in the Inspector without extra code.
///
///  interactable → player can press [E] to trigger a behaviour  (door, vending machine…)
///  carryable    → player can press [F] to carry the object and [F] again to drop it
///  collectable  → player can press [E] to collect the object (disappears; inventory later)
///
/// Priority: if BOTH interactable and collectable are true, [E] triggers interact only.
/// Carry ([F]) is always independent of the other two.
/// </summary>
public class WorldObject : MonoBehaviour
{
    // ── Flags ────────────────────────────────────────────────────────────────
    [Header("Interaction Flags")]
    public bool interactable = false;
    public bool carryable    = false;
    public bool collectable  = false;

    // ── Prompt text (shown in HUD) ───────────────────────────────────────────
    [Header("Prompt Text")]
    public string interactPrompt = "Interact";
    public string carryPrompt    = "Carry";
    public string dropPrompt     = "Put down";
    public string collectPrompt  = "Collect";

    // ── Events ───────────────────────────────────────────────────────────────
    [Header("Events")]
    [Tooltip("Fired when the player presses [E] on an interactable object.")]
    public UnityEvent<GameObject> onInteract;

    [Tooltip("Fired when the player picks this object up with [F].")]
    public UnityEvent<GameObject> onPickUp;

    [Tooltip("Fired when the player drops this object with [F].")]
    public UnityEvent<GameObject> onDrop;

    [Tooltip("Fired just before a collectable object is destroyed.")]
    public UnityEvent<GameObject> onCollect;

    // ── Internal API (called by InteractionSystem) ───────────────────────────
    internal void TriggerInteract(GameObject actor) => onInteract?.Invoke(actor);
    internal void TriggerPickUp(GameObject actor)   => onPickUp?.Invoke(actor);
    internal void TriggerDrop(GameObject actor)     => onDrop?.Invoke(actor);
    internal void TriggerCollect(GameObject actor)  => onCollect?.Invoke(actor);
}
