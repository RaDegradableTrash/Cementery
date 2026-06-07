using UnityEngine;
using TMPro;

public class FuelTank : MonoBehaviour
{
    [Header("3D Visual Mesh References")]
    [Tooltip("The 3D Object or Quad representing the fuel meter display container.")]
    public GameObject displayObject;
    
    [Tooltip("The MeshRenderer/Renderer of the progress bar Quad.")]
    public Renderer fuelBarFillRenderer;
    
    [Tooltip("3D Text Mesh (TextMeshPro) for displaying percentage directly in the 3D space.")]
    public TextMeshPro fuelText;

    [Header("Fuel Config")]
    public float maxCapacity = 100f;
    public float currentFuel = 20f; // Initial fuel amount
    public string fuelItemNameFilter = "fuel"; // If matching held item name

    [Header("UI Visuals (Neon Holographic Preset)")]
    public Color lowFuelColor = new Color(0.9f, 0.2f, 0.2f, 0.85f);
    public Color mediumFuelColor = new Color(0.9f, 0.6f, 0.1f, 0.85f);
    public Color fullFuelColor = new Color(0.0f, 0.85f, 0.95f, 0.85f);

    [Header("Procedural Wave Settings")]
    public int waveSegments = 35;
    public float waveSpeed = 7f;
    public float waveAmplitude = 0.05f;
    public float waveFrequency = 11f;

    [Tooltip("For rotated quads: LocalX makes the wave animate vertically along the local X axis.")]
    public enum WaveAxis { LocalX, LocalY, LocalZ }
    public WaveAxis fillHeightAxis = WaveAxis.LocalY; // Overridden to default to LocalY since user fixed rotation

    private MeshFilter _fillMeshFilter;
    private Mesh _proceduralMesh;
    private Vector3[] _baseVertices;
    private int[] _baseTriangles;
    private Vector2[] _baseUVs;
    private float _currentRatio = 0f;

    private bool _isLookingAt = false;
    private float _currentAlpha = 0f;
    private MaterialPropertyBlock _propBlock;

    private void Awake()
    {
        // Enforce clean Neon hologram colors on awake
        lowFuelColor = new Color(0.9f, 0.2f, 0.2f, 0.85f);
        mediumFuelColor = new Color(0.9f, 0.6f, 0.1f, 0.85f);
        fullFuelColor = new Color(0.0f, 0.85f, 0.95f, 0.85f);

        _propBlock = new MaterialPropertyBlock();
        if (fuelBarFillRenderer != null)
        {
            _fillMeshFilter = fuelBarFillRenderer.GetComponent<MeshFilter>();
            if (_fillMeshFilter != null)
            {
                // Create custom mesh instance so we deform vertices properly
                _proceduralMesh = new Mesh();
                _proceduralMesh.name = "ProceduralFuelWaveMesh";
                _fillMeshFilter.mesh = _proceduralMesh;
                BuildBaseWaveMesh();
            }
        }
    }

    private void Start()
    {
        if (displayObject != null)
        {
            displayObject.SetActive(false);
        }
        UpdateUI();
    }

    private void Update()
    {
        // Debug fuel control via keyboard: '-' decreases by 5%, '=' increases by 5%
        if (Input.GetKeyDown(KeyCode.Minus) || Input.GetKeyDown(KeyCode.KeypadMinus))
        {
            float amountToReduce = maxCapacity * 0.05f;
            currentFuel = Mathf.Clamp(currentFuel - amountToReduce, 0f, maxCapacity);
            UpdateUI();
        }
        if (Input.GetKeyDown(KeyCode.Equals) || Input.GetKeyDown(KeyCode.KeypadEquals))
        {
            float amountToAdd = maxCapacity * 0.05f;
            currentFuel = Mathf.Clamp(currentFuel + amountToAdd, 0f, maxCapacity);
            UpdateUI();
        }

        // Animate wave vertices over time
        if (displayObject != null && displayObject.activeSelf && _proceduralMesh != null)
        {
            AnimateWaveMesh();
        }

        float targetAlpha = _isLookingAt ? 1f : 0f;
        
        // Handle visual activation and fading
        if (displayObject != null)
        {
            if (_isLookingAt && !displayObject.activeSelf)
            {
                displayObject.SetActive(true);
                _currentAlpha = 0f;
                displayObject.transform.localScale = Vector3.one * 0.8f;
            }

            _currentAlpha = Mathf.MoveTowards(_currentAlpha, targetAlpha, Time.deltaTime * 8f);
            displayObject.transform.localScale = Vector3.one * Mathf.SmoothStep(0.8f, 1f, _currentAlpha);

            // Apply fading to display alpha
            SetDisplayAlpha(_currentAlpha);

            if (!_isLookingAt && Mathf.Approximately(_currentAlpha, 0f))
            {
                displayObject.SetActive(false);
            }
        }
    }

    private void BuildBaseWaveMesh()
    {
        int numVerts = (waveSegments + 1) * 2;
        Vector3[] vertices = new Vector3[numVerts];
        Vector2[] uvs = new Vector2[numVerts];
        int[] triangles = new int[waveSegments * 6];

        for (int i = 0; i <= waveSegments; i++)
        {
            float t = (float)i / waveSegments;
            float primary = t - 0.5f;

            if (fillHeightAxis == WaveAxis.LocalX)
            {
                // X is height, Y is horizontal segment (Rotated Quad case)
                vertices[i * 2] = new Vector3(-0.5f, primary, 0f);
                uvs[i * 2] = new Vector2(0f, t);

                vertices[i * 2 + 1] = new Vector3(-0.5f, primary, 0f);
                uvs[i * 2 + 1] = new Vector2(1f, t);
            }
            else if (fillHeightAxis == WaveAxis.LocalZ)
            {
                // Z is height
                vertices[i * 2] = new Vector3(primary, 0f, -0.5f);
                uvs[i * 2] = new Vector2(t, 0f);

                vertices[i * 2 + 1] = new Vector3(primary, 0f, -0.5f);
                uvs[i * 2 + 1] = new Vector2(t, 1f);
            }
            else
            {
                // Standard Y-up
                vertices[i * 2] = new Vector3(primary, -0.5f, 0f);
                uvs[i * 2] = new Vector2(t, 0f);

                vertices[i * 2 + 1] = new Vector3(primary, -0.5f, 0f);
                uvs[i * 2 + 1] = new Vector2(t, 1f);
            }

            if (i < waveSegments)
            {
                triangles[i * 6] = i * 2;
                triangles[i * 6 + 1] = i * 2 + 1;
                triangles[i * 6 + 2] = (i + 1) * 2;

                triangles[i * 6 + 3] = (i + 1) * 2;
                triangles[i * 6 + 4] = i * 2 + 1;
                triangles[i * 6 + 5] = (i + 1) * 2 + 1;
            }
        }

        _baseVertices = vertices;
        _baseUVs = uvs;
        _baseTriangles = triangles;

        _proceduralMesh.vertices = _baseVertices;
        _proceduralMesh.uv = _baseUVs;
        _proceduralMesh.triangles = _baseTriangles;
        _proceduralMesh.RecalculateBounds();
        _proceduralMesh.RecalculateNormals();
    }

    private void AnimateWaveMesh()
    {
        Vector3[] verts = new Vector3[_baseVertices.Length];
        System.Array.Copy(_baseVertices, verts, _baseVertices.Length);

        float timeVal = Time.time * waveSpeed;

        for (int i = 0; i <= waveSegments; i++)
        {
            float t = (float)i / waveSegments;
            float fillOffset = -0.5f + _currentRatio;

            float wave = 0f;
            if (_currentRatio > 0.01f && _currentRatio < 0.99f)
            {
                wave = Mathf.Sin(t * waveFrequency + timeVal) * waveAmplitude;
            }

            if (fillHeightAxis == WaveAxis.LocalX)
            {
                verts[i * 2 + 1].x = fillOffset + wave;
            }
            else if (fillHeightAxis == WaveAxis.LocalZ)
            {
                verts[i * 2 + 1].z = fillOffset + wave;
            }
            else
            {
                verts[i * 2 + 1].y = fillOffset + wave;
            }
        }

        _proceduralMesh.vertices = verts;
        _proceduralMesh.RecalculateBounds();
    }

    private void SetDisplayAlpha(float alpha)
    {
        if (displayObject == null) return;

        // Automatically scale color transparency for all components in displayObject
        Renderer[] renderers = displayObject.GetComponentsInChildren<Renderer>(true);
        foreach (var r in renderers)
        {
            if (r == fuelBarFillRenderer) continue;

            r.GetPropertyBlock(_propBlock);
            
            // Neon Hologram background/outline glows with fading 25% alpha
            Color baseCol = Color.white;
            if (r.sharedMaterial != null)
            {
                if (r.sharedMaterial.HasProperty("_BaseColor"))
                    baseCol = r.sharedMaterial.GetColor("_BaseColor");
                else if (r.sharedMaterial.HasProperty("_Color"))
                    baseCol = r.sharedMaterial.GetColor("_Color");
                else
                    baseCol = Color.cyan;
            }
            baseCol.a = alpha * 0.25f;
            
            // Set both URP and Standard color variables
            _propBlock.SetColor("_Color", baseCol);
            _propBlock.SetColor("_BaseColor", baseCol);
            _propBlock.SetFloat("_Alpha", alpha);
            r.SetPropertyBlock(_propBlock);
        }

        // Handle percentage text transparency
        if (fuelText != null)
        {
            float ratio = maxCapacity > 0f ? Mathf.Clamp01(currentFuel / maxCapacity) : 0f;
            Color targetColor = fullFuelColor;
            if (ratio < 0.3f) targetColor = Color.Lerp(lowFuelColor, mediumFuelColor, ratio / 0.3f);
            else targetColor = Color.Lerp(mediumFuelColor, fullFuelColor, (ratio - 0.3f) / 0.7f);
            
            fuelText.color = new Color(targetColor.r, targetColor.g, targetColor.b, alpha);
        }
    }

    public void ShowUI(bool isLooking)
    {
        _isLookingAt = isLooking;
    }

    public bool AddFuel(float amount)
    {
        if (currentFuel >= maxCapacity)
            return false;

        currentFuel = Mathf.Clamp(currentFuel + amount, 0f, maxCapacity);
        UpdateUI();
        return true;
    }

    public void UpdateUI()
    {
        float ratio = maxCapacity > 0f ? Mathf.Clamp01(currentFuel / maxCapacity) : 0f;
        _currentRatio = ratio;

        Color targetColor = fullFuelColor;
        if (ratio < 0.3f)
        {
            targetColor = Color.Lerp(lowFuelColor, mediumFuelColor, ratio / 0.3f);
        }
        else
        {
            targetColor = Color.Lerp(mediumFuelColor, fullFuelColor, (ratio - 0.3f) / 0.7f);
        }

        // Float fluid glows with 40% neon transparency
        Color holoFillColor = targetColor;
        holoFillColor.a = 0.4f * _currentAlpha;

        if (fuelBarFillRenderer != null)
        {
            fuelBarFillRenderer.GetPropertyBlock(_propBlock);
            
            // Set both URP and Standard variables in property block
            _propBlock.SetColor("_Color", holoFillColor);
            _propBlock.SetColor("_BaseColor", holoFillColor);
            _propBlock.SetFloat("_Alpha", _currentAlpha);
            fuelBarFillRenderer.SetPropertyBlock(_propBlock);

            if (fuelBarFillRenderer.sharedMaterial != null)
            {
                if (fuelBarFillRenderer.material.HasProperty("_Color"))
                    fuelBarFillRenderer.material.SetColor("_Color", holoFillColor);
                if (fuelBarFillRenderer.material.HasProperty("_BaseColor"))
                    fuelBarFillRenderer.material.SetColor("_BaseColor", holoFillColor);
            }
            AnimateWaveMesh();
        }

        if (fuelText != null)
        {
            fuelText.text = $"{(ratio * 100f):F0}%";
            fuelText.color = new Color(targetColor.r, targetColor.g, targetColor.b, _currentAlpha);
        }
    }
}
