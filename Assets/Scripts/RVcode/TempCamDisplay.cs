using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class TempCamDisplay : MonoBehaviour
{
    [Header("Assign a Camera (optional)")]
    [SerializeField]
    private Camera cameraToUse;

    [Header("Display Settings")]
    [Tooltip("Display number starting from 1 (Display1 = 1). Will be mapped to Camera.targetDisplay (0-based).")]
    [SerializeField]
    [Range(1, 8)]
    private int displayNumber = 1;

    [Tooltip("If true, will call Display.displays[displayIndex].Activate() for that display at runtime.")]
    [SerializeField]
    private bool activateDisplay = true;

    [Tooltip("If true and no Camera is assigned, add a Camera component on this GameObject.")]
    [SerializeField]
    private bool createIfMissing = false;

    private void Awake()
    {
        if (cameraToUse == null)
        {
            cameraToUse = GetComponent<Camera>();
            if (cameraToUse == null && createIfMissing)
            {
                cameraToUse = gameObject.AddComponent<Camera>();
            }
        }

        int target = Mathf.Clamp(displayNumber, 1, 8) - 1; // convert to 0-based
        if (cameraToUse != null)
        {
            cameraToUse.targetDisplay = target;
        }

        // Activate additional displays (only relevant in standalone builds)
        if (activateDisplay && target > 0)
        {
            if (Display.displays != null && Display.displays.Length > target)
            {
                try
                {
                    Display.displays[target].Activate();
                }
                catch
                {
                    // ignore activation failures (platform/editor may not support)
                }
            }
        }
    }

#if UNITY_EDITOR
    private void OnValidate()
    {
        if (displayNumber < 1) displayNumber = 1;
        if (displayNumber > 8) displayNumber = 8;
        if (cameraToUse == null) cameraToUse = GetComponent<Camera>();
        if (cameraToUse != null)
        {
            cameraToUse.targetDisplay = displayNumber - 1;
        }
    }
#endif
}
