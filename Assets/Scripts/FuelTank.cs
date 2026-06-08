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
    public string fuelItemNameFilter = "fuel"; // If matching held item name

    // Static variables to ensure the fuel level is globally unique and shared across both tanks
    private static float _sharedFuel = 100f; // Initial default fuel
    private static float _sharedMaxCapacity = 100f;
    private static System.Collections.Generic.List<FuelTank> _activeTanks = new System.Collections.Generic.List<FuelTank>();

    public static float SharedFuel
    {
        get => _sharedFuel;
        set
        {
            _sharedFuel = Mathf.Clamp(value, 0f, _sharedMaxCapacity);
            NotifyAllTanksToUpdate();
        }
    }

    public float currentFuel
    {
        get => _sharedFuel;
        set
        {
            _sharedFuel = Mathf.Clamp(value, 0f, maxCapacity);
            NotifyAllTanksToUpdate();
        }
    }

    private static void NotifyAllTanksToUpdate()
    {
        for (int i = _activeTanks.Count - 1; i >= 0; i--)
        {
            if (_activeTanks[i] != null)
            {
                _activeTanks[i].UpdateUI();
            }
            else
            {
                _activeTanks.RemoveAt(i);
            }
        }
    }

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
    private float _targetRatio = 0f;

    [Tooltip("How fast the fuel level transitions visually (ratio per second).")]
    public float fillTransitionSpeed = 0.5f;

    private bool _isLookingAt = false;
    private float _currentAlpha = 0f;
    private float _lookAwayTimer = 0f; // 1-second delay buffer when player looks away
    private MaterialPropertyBlock _propBlock;

    private void SetupMaterialTransparent(Material mat)
    {
        if (mat == null) return;

        // Support for URP Lit shader Surface Type
        if (mat.HasProperty("_Surface"))
        {
            mat.SetFloat("_Surface", 1f); // 1 = Transparent
        }

        // Support for URP Blend Mode
        if (mat.HasProperty("_Blend"))
        {
            mat.SetFloat("_Blend", 0f); // 0 = Alpha Blending
        }

        // Standard Shader mode
        if (mat.HasProperty("_Mode"))
        {
            mat.SetFloat("_Mode", 3f); // 3 = Transparent
        }

        // Configure blending factor settings
        mat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
        mat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
        mat.SetInt("_ZWrite", 0);

        mat.DisableKeyword("_ALPHATEST_ON");
        mat.EnableKeyword("_ALPHABLEND_ON");
        mat.DisableKeyword("_ALPHAPREMULTIPLY_ON");

        // Force transparent render queue (3000)
        mat.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
    }

    private void Awake()
    {
        // Register this instance to the shared active list
        if (!_activeTanks.Contains(this))
        {
            _activeTanks.Add(this);
        }
        
        _sharedMaxCapacity = maxCapacity;

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

            // Programmatically configure material for transparency
            if (fuelBarFillRenderer.material != null)
            {
                SetupMaterialTransparent(fuelBarFillRenderer.material);
            }

            // Disable shadow casting and receiving on the wave fill mesh to strip shadows
            fuelBarFillRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            fuelBarFillRenderer.receiveShadows = false;
        }

        // Apply shadow disabling on all displayObject outlines/backgrounds
        if (displayObject != null)
        {
            foreach (var r in displayObject.GetComponentsInChildren<Renderer>(true))
            {
                // Programmatically configure material for transparency
                if (r.material != null)
                {
                    SetupMaterialTransparent(r.material);
                }

                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                r.receiveShadows = false;
            }
        }
    }

    private void OnDestroy()
    {
        _activeTanks.Remove(this);
    }

    private void Start()
    {
        if (displayObject != null)
        {
            displayObject.SetActive(false);
        }
        UpdateUI();
        _currentRatio = _targetRatio; // Initialize instantly to avoid startup lag
    }

    private void Update()
    {
        // Smoothly interpolate the visual ratio towards the target fuel ratio
        _currentRatio = Mathf.MoveTowards(_currentRatio, _targetRatio, Time.deltaTime * fillTransitionSpeed);

        // Debug fuel control via keyboard: '-' decreases by 5%, '=' increases by 5%
        if (Input.GetKeyDown(KeyCode.Minus) || Input.GetKeyDown(KeyCode.KeypadMinus))
        {
            float amountToReduce = maxCapacity * 0.05f;
            currentFuel -= amountToReduce;
        }
        if (Input.GetKeyDown(KeyCode.Equals) || Input.GetKeyDown(KeyCode.KeypadEquals))
        {
            float amountToAdd = maxCapacity * 0.05f;
            currentFuel += amountToAdd;
        }

        // Animate wave vertices over time
        if (displayObject != null && displayObject.activeSelf && _proceduralMesh != null)
        {
            AnimateWaveMesh();
        }

        // 1-second delay buffer when looking away
        bool shouldShow = _isLookingAt;
        if (!_isLookingAt && _lookAwayTimer > 0f)
        {
            _lookAwayTimer -= Time.deltaTime;
            shouldShow = true; // Stay visible during the 1-second grace period
        }

        float targetAlpha = shouldShow ? 1f : 0f;
        
        // Handle visual activation and fading
        if (displayObject != null)
        {
            if (shouldShow && !displayObject.activeSelf)
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
            
            // Neon Hologram background/outline glows with fading 30% alpha (alpha * 0.3f)
            Color baseCol = Color.white;
            if (r.material != null)
            {
                if (r.material.HasProperty("_BaseColor"))
                    baseCol = r.material.GetColor("_BaseColor");
                else if (r.material.HasProperty("_Color"))
                    baseCol = r.material.GetColor("_Color");
                else
                    baseCol = Color.cyan;
            }
            baseCol.a = alpha * 0.3f;
            
            // Set both URP and Standard color variables
            _propBlock.SetColor("_Color", baseCol);
            _propBlock.SetColor("_BaseColor", baseCol);
            _propBlock.SetFloat("_Alpha", alpha);
            r.SetPropertyBlock(_propBlock);

            if (r.material != null)
            {
                if (r.material.HasProperty("_Color"))
                    r.material.SetColor("_Color", baseCol);
                if (r.material.HasProperty("_BaseColor"))
                    r.material.SetColor("_BaseColor", baseCol);
            }
        }

        // Float fluid glows with 60% neon transparency (alpha * 0.6f)
        if (fuelBarFillRenderer != null)
        {
            Color targetColor = fullFuelColor;
            if (_currentRatio < 0.3f) targetColor = Color.Lerp(lowFuelColor, mediumFuelColor, _currentRatio / 0.3f);
            else targetColor = Color.Lerp(mediumFuelColor, fullFuelColor, (_currentRatio - 0.3f) / 0.7f);

            Color holoFillColor = targetColor;
            holoFillColor.a = 0.6f * alpha;

            fuelBarFillRenderer.GetPropertyBlock(_propBlock);
            _propBlock.SetColor("_Color", holoFillColor);
            _propBlock.SetColor("_BaseColor", holoFillColor);
            _propBlock.SetFloat("_Alpha", alpha);
            fuelBarFillRenderer.SetPropertyBlock(_propBlock);

            if (fuelBarFillRenderer.material != null)
            {
                if (fuelBarFillRenderer.material.HasProperty("_Color"))
                    fuelBarFillRenderer.material.SetColor("_Color", holoFillColor);
                if (fuelBarFillRenderer.material.HasProperty("_BaseColor"))
                    fuelBarFillRenderer.material.SetColor("_BaseColor", holoFillColor);
            }
        }

        // Handle percentage text transparency - 50% opacity (alpha * 0.5f)
        if (fuelText != null)
        {
            Color targetColor = fullFuelColor;
            if (_currentRatio < 0.3f) targetColor = Color.Lerp(lowFuelColor, mediumFuelColor, _currentRatio / 0.3f);
            else targetColor = Color.Lerp(mediumFuelColor, fullFuelColor, (_currentRatio - 0.3f) / 0.7f);
            
            fuelText.text = $"{(_currentRatio * 100f):F0}%";
            fuelText.color = new Color(targetColor.r, targetColor.g, targetColor.b, alpha * 0.5f);
        }
    }

    public void ShowUI(bool isLooking)
    {
        if (isLooking)
        {
            _lookAwayTimer = 0f; // Reset buffer timer if focused again
        }
        else if (_isLookingAt) // Transition from focused to unfocused
        {
            _lookAwayTimer = 1.0f; // Initialize 1-second delay buffer
        }
        _isLookingAt = isLooking;
    }

    public bool AddFuel(float amount)
    {
        if (currentFuel >= maxCapacity)
            return false;

        currentFuel += amount;
        return true;
    }

    public void UpdateUI()
    {
        float ratio = maxCapacity > 0f ? Mathf.Clamp01(currentFuel / maxCapacity) : 0f;
        _targetRatio = ratio;
    }
}
