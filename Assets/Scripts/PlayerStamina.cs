using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Tracks sprint stamina. Attach to the Player GameObject alongside PlayerController.
/// </summary>
public class PlayerStamina : MonoBehaviour
{
    [Header("Stamina Values")]
    public float maxStamina = 100f;
    [Tooltip("Stamina drained per second while sprinting.")]
    public float drainRate = 20f;
    [Tooltip("Stamina recovered per second while not sprinting.")]
    public float recoverRate = 10f;
    [Tooltip("Seconds of idle time before recovery begins.")]
    public float recoverDelay = 1.5f;

    [Header("UI")]
    [Tooltip("Assign a UI Image (Image Type = Filled, Fill Method = Horizontal) to show the stamina bar.")]
    public Image staminaBarFill;

    public bool HasStamina => _stamina > 1f;

    private float _stamina;
    private float _recoverCooldown;

    void Awake()
    {
        _stamina = maxStamina;
    }

    void Update()
    {
        UpdateUI();
    }

    public void Drain()
    {
        _stamina = Mathf.Max(0f, _stamina - drainRate * Time.deltaTime);
        _recoverCooldown = recoverDelay;
    }

    public void Recover()
    {
        if (_recoverCooldown > 0f)
        {
            _recoverCooldown -= Time.deltaTime;
            return;
        }
        _stamina = Mathf.Min(maxStamina, _stamina + recoverRate * Time.deltaTime);
    }

    float NormalizedStamina => _stamina / maxStamina;

    void UpdateUI()
    {
        if (staminaBarFill == null) return;

        staminaBarFill.fillAmount = NormalizedStamina;

        // Hide the whole bar container when stamina is full
        Transform barRoot = staminaBarFill.transform.parent;
        if (barRoot != null)
            barRoot.gameObject.SetActive(NormalizedStamina < 0.99f);
    }
}
