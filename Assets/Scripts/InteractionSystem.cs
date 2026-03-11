using TMPro;
using UnityEngine;

/// <summary>
/// Raycasts from the player camera each frame to detect IInteractable objects.
/// Press E (or configured key) to interact.
/// </summary>
public class InteractionSystem : MonoBehaviour
{
    [Header("Settings")]
    public float interactRange = 3f;
    public LayerMask interactMask = ~0;
    public KeyCode interactKey = KeyCode.E;

    [Header("UI")]
    [Tooltip("Optional: assign a TextMeshProUGUI element to display '[ E ]  Pick up Wood' style prompts.")]
    public TextMeshProUGUI promptLabel;

    private Camera _cam;
    private IInteractable _currentTarget;

    void Awake()
    {
        // Prefer a camera on a child (the camera holder), fall back to Camera.main
        _cam = GetComponentInChildren<Camera>();
        if (_cam == null) _cam = Camera.main;
    }

    void Update()
    {
        ScanForInteractable();

        if (_currentTarget != null && Input.GetKeyDown(interactKey))
            _currentTarget.Interact(gameObject);
    }

    void ScanForInteractable()
    {
        Ray ray = new Ray(_cam.transform.position, _cam.transform.forward);

        if (Physics.Raycast(ray, out RaycastHit hit, interactRange, interactMask, QueryTriggerInteraction.Ignore))
        {
            IInteractable found = hit.collider.GetComponentInParent<IInteractable>();
            if (found != null)
            {
                _currentTarget = found;
                SetPrompt($"[ E ]  {found.InteractPrompt}");
                return;
            }
        }

        _currentTarget = null;
        SetPrompt(null);
    }

    void SetPrompt(string text)
    {
        if (promptLabel == null) return;
        bool hasText = !string.IsNullOrEmpty(text);
        promptLabel.gameObject.SetActive(hasText);
        if (hasText) promptLabel.text = text;
    }
}
