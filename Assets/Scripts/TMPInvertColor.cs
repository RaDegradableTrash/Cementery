using TMPro;
using UnityEngine;

/// <summary>
/// Applies white face colour + black outline to TMP labels via TMP's built-in
/// Distance Field material properties. No custom shader needed.
/// Font, font size, bold, and text content are untouched.
/// </summary>
public class TMPInvertColor : MonoBehaviour
{
    [SerializeField] private TextMeshProUGUI[] labels;

    [Range(0f, 1f)]
    [SerializeField] private float outlineWidth     = 0.2f;
    [Range(0f, 1f)]
    [SerializeField] private float outlineSoftness  = 0.0f;

    void Awake()
    {
        foreach (var label in labels)
        {
            if (label == null) continue;

            // fontMaterial returns a per-instance copy — safe to modify.
            Material mat = label.fontMaterial;
            mat.SetColor("_FaceColor",    Color.white);
            mat.SetColor("_OutlineColor", Color.black);
            mat.SetFloat("_OutlineWidth", outlineWidth);
            mat.SetFloat("_OutlineSoftness", outlineSoftness);
            label.fontMaterial = mat;
        }
    }
}
