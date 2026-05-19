using UnityEngine;

public class WiperControl : MonoBehaviour
{
    public enum WiperMode
    {
        Off,
        Intermittent,
        Fast
    }

    [SerializeField] private Transform wiperTransform;
    [SerializeField] private Transform[] wiperTransforms;
    [SerializeField] private float sweepAngle = 90f;
    [SerializeField] private float sweepDuration = 0.7f;
    [SerializeField] private float intermittentPause = 1f;
    [SerializeField] private bool invert = false;

    private WiperMode currentMode = WiperMode.Off;
    private bool forward = true;
    private float progress;
    private float pauseTimer;
    private bool pendingOff;

    public WiperMode CurrentMode => currentMode;

    private void Awake()
    {
        if (wiperTransforms == null || wiperTransforms.Length == 0)
        {
            if (wiperTransform == null)
            {
                wiperTransform = transform;
            }
            wiperTransforms = new[] { wiperTransform };
        }

        ApplyAngle(0f);
    }

    private void Update()
    {
        if (currentMode == WiperMode.Off && !pendingOff)
        {
            return;
        }

        if (pauseTimer > 0f)
        {
            pauseTimer -= Time.deltaTime;
            return;
        }

        float speed = sweepDuration > 0.001f ? 1f / sweepDuration : 1f;
        progress += (forward ? 1f : -1f) * speed * Time.deltaTime;

        if (progress >= 1f)
        {
            progress = 1f;
            forward = false;
        }
        else if (progress <= 0f)
        {
            progress = 0f;
            forward = true;
            if (pendingOff)
            {
                pendingOff = false;
                currentMode = WiperMode.Off;
                pauseTimer = 0f;
                ApplyAngle(0f);
                return;
            }

            pauseTimer = currentMode == WiperMode.Intermittent ? intermittentPause : 0f;
        }

        ApplyAngle(progress * sweepAngle);
    }

    public void ToggleMode()
    {
        if (currentMode == WiperMode.Off)
        {
            SetMode(WiperMode.Intermittent);
        }
        else if (currentMode == WiperMode.Intermittent)
        {
            SetMode(WiperMode.Fast);
        }
        else
        {
            SetMode(WiperMode.Off);
        }
    }

    public void SetMode(WiperMode mode)
    {
        if (currentMode == mode)
        {
            return;
        }

        if (mode == WiperMode.Off)
        {
            pendingOff = true;
            pauseTimer = 0f;
            return;
        }

        currentMode = mode;
        pendingOff = false;
        pauseTimer = 0f;
    }

    public void CycleMode()
    {
        if (currentMode == WiperMode.Off)
        {
            SetMode(WiperMode.Intermittent);
        }
        else if (currentMode == WiperMode.Intermittent)
        {
            SetMode(WiperMode.Fast);
        }
        else
        {
            SetMode(WiperMode.Off);
        }
    }

    private void ApplyAngle(float angle)
    {
        float dir = invert ? -1f : 1f;
        if (wiperTransforms == null)
        {
            return;
        }

        for (int i = 0; i < wiperTransforms.Length; i++)
        {
            Transform target = wiperTransforms[i];
            if (target != null)
            {
                target.localRotation = Quaternion.Euler(0f, angle * dir, 0f);
            }
        }
    }
}
