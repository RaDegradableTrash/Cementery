using UnityEngine;
using TMPro;
using UnityEngine.UI;

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
    
    private float _timer;
    private int _burstFramesRemaining;

    private void Awake()
    {
        _rt = GetComponent<RectTransform>();


        _cg = GetComponent<CanvasGroup>();
        _tmp = GetComponent<TextMeshProUGUI>();
        
        if (_tmp != null)
            _originalColor = _tmp.color;

        _timer = Random.Range(0f, baseInterval);
    }

    private void OnEnable()
    {
        if (_cg != null) 
        {
            _cg.alpha = 1f;
            _originalAlpha = 1f;
        }
        if (_tmp != null) _tmp.color = _originalColor;
    }

    private void OnDisable()
    {
        if (_tmp != null) _tmp.color = _originalColor;
    }

    private void Update()
    {
        if (_burstFramesRemaining > 0)
        {
            ApplyGlitchFrame();
            _burstFramesRemaining--;

            if (_burstFramesRemaining <= 0)
            {
                ResetGlitch();
            }
            return;
        }

        _timer -= Time.unscaledDeltaTime;
        if (_timer <= 0f)
        {
            if (_cg == null) _cg = GetComponent<CanvasGroup>();
            
            // Start a glitch burst
            _burstFramesRemaining = Random.Range(2, 6);
            _timer = baseInterval * Random.Range(0.2f, 1.5f);
            
            // Save state just before glitching
            if (_rt != null) _originalPos = _rt.anchoredPosition;
            if (_cg != null) _originalAlpha = _cg.alpha;
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
