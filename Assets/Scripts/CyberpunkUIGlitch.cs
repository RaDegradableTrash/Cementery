using System.Collections;
using UnityEngine;
using TMPro;

public class CyberpunkUIGlitch : MonoBehaviour
{
    public float glitchIntensity = 1f;
    public float baseInterval = 1.5f;

    private RectTransform _rt;
    private CanvasGroup _cg;
    private TextMeshProUGUI _tmp;
    private Vector2 _originalPos;
    private Color _originalColor;
    private float _originalAlpha;
    
    private Coroutine _glitchRoutine;

    private void Awake()
    {
        _rt = GetComponent<RectTransform>();


        _cg = GetComponent<CanvasGroup>();
        _tmp = GetComponent<TextMeshProUGUI>();
        
        if (_tmp != null)
            _originalColor = _tmp.color;
    }

    private void OnEnable()
    {
        if (_rt != null)
            _originalPos = _rt.anchoredPosition;

        if (_cg != null) 
        {
            _cg.alpha = 1f;
            _originalAlpha = 1f;
        }
        if (_tmp != null) _tmp.color = _originalColor;

        _glitchRoutine = StartCoroutine(GlitchRoutine());
    }

    private void OnDisable()
    {
        if (_glitchRoutine != null)
        {
            StopCoroutine(_glitchRoutine);
            _glitchRoutine = null;
        }

        ResetGlitch();
        if (_tmp != null) _tmp.color = _originalColor;
    }

    private IEnumerator GlitchRoutine()
    {
        float initialDelay = Random.Range(0f, Mathf.Max(0.05f, baseInterval));
        if (initialDelay > 0f)
            yield return new WaitForSecondsRealtime(initialDelay);

        while (enabled)
        {
            if (_cg == null) _cg = GetComponent<CanvasGroup>();

            if (_rt != null) _originalPos = _rt.anchoredPosition;
            if (_cg != null) _originalAlpha = _cg.alpha;

            int burstFrames = Random.Range(2, 6);
            for (int i = 0; i < burstFrames; i++)
            {
                ApplyGlitchFrame();
                yield return null;
            }

            ResetGlitch();
            float waitTime = Mathf.Max(0.05f, baseInterval * Random.Range(0.2f, 1.5f));
            yield return new WaitForSecondsRealtime(waitTime);
        }
    }

    private void ApplyGlitchFrame()
    {
        if (_rt != null)
        {
            float offsetX = Random.Range(-10f, 10f) * glitchIntensity;
            float offsetY = Random.Range(-3f, 3f) * glitchIntensity;
            _rt.anchoredPosition = _originalPos + new Vector2(offsetX, offsetY);
        }

        if (_cg != null && _cg.alpha > 0f)
        {
            // Occasionally drop alpha drastically, but only if it's currently visible
            if (Random.value < 0.3f)
                _cg.alpha = Random.Range(0.1f, 0.5f);
        }

        if (_tmp != null)
        {
            if (Random.value < 0.2f)
            {
                // Cyan/Magenta chromatic split flash
                _tmp.color = Random.value > 0.5f ? Color.cyan : Color.magenta;
            }
            else
            {
                _tmp.color = _originalColor;
            }
        }
    }

    private void ResetGlitch()
    {
        if (_rt != null) _rt.anchoredPosition = _originalPos;
        if (_cg != null) _cg.alpha = _originalAlpha;
        if (_tmp != null) _tmp.color = _originalColor;
    }
}
