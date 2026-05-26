using UnityEngine;

public class ShaderDiagnostic : MonoBehaviour
{
    private float _timer = 0f;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void AutoInitialize()
    {
        GameObject go = new GameObject("[ShaderDiagnostic_AutoBootstrapper]");
        go.AddComponent<ShaderDiagnostic>();
        DontDestroyOnLoad(go);
        Debug.LogWarning("[ShaderDiagnostic] Auto-Bootstrapper successfully initialized. Scanning will run shortly...");
    }

    void Start()
    {
        RunDiagnostic();
    }

    void Update()
    {
        // Poll every 3 seconds to print streamed-in chunk material details
        _timer += Time.deltaTime;
        if (_timer >= 3.0f)
        {
            _timer = 0f;
            RunDiagnostic();
        }
    }

    public void RunDiagnostic()
    {
        Shader triplanarShader = Shader.Find("Environment/URPTriplanarEnvironment");
        Debug.LogWarning($"[ShaderDiagnostic] Shader.Find('Environment/URPTriplanarEnvironment') found: {triplanarShader != null}");

        MeshRenderer[] renderers = FindObjectsOfType<MeshRenderer>();
        Debug.LogWarning($"====== [ShaderDiagnostic] Scanning {renderers.Length} MeshRenderers in the Scene ======");
        
        int printedCount = 0;
        foreach (var mr in renderers)
        {
            if (mr == null || !mr.gameObject.activeInHierarchy) continue;
            
            // Skip the player and truck to focus on environmental/terrain objects
            if (mr.gameObject.CompareTag("Player") || mr.gameObject.name.Contains("Player") || mr.gameObject.name.Contains("RV"))
            {
                continue;
            }

            if (mr.sharedMaterial != null)
            {
                string shaderName = mr.sharedMaterial.shader != null ? mr.sharedMaterial.shader.name : "NULL";
                int queue = mr.sharedMaterial.renderQueue;
                string objName = mr.gameObject.name.ToLower();

                // Target ONLY chunk/terrain/desert related objects or triplanar shaders
                bool isTerrainOrChunk = objName.Contains("chunk") || 
                                        objName.Contains("desert") || 
                                        objName.Contains("terrain") || 
                                        shaderName.Contains("Triplanar");

                if (isTerrainOrChunk)
                {
                    Debug.LogWarning($"[ShaderDiagnostic] OBJ: '{mr.gameObject.name}' | Layer: {LayerMask.LayerToName(mr.gameObject.layer)} | Shader: '{shaderName}' | Queue: {queue} | ZWrite: {(mr.sharedMaterial.HasProperty("_ZWrite") ? mr.sharedMaterial.GetFloat("_ZWrite").ToString() : "N/A")}");
                    printedCount++;
                    
                    if (printedCount >= 40) // Limit print count to prevent log flooding
                    {
                        Debug.LogWarning("[ShaderDiagnostic] Limit reached. Skipping remaining filtered objects...");
                        break;
                    }
                }
            }
        }
        
        if (printedCount == 0)
        {
            Debug.LogWarning("[ShaderDiagnostic] Warning: No objects containing 'chunk', 'desert', 'terrain', or using 'Triplanar' shader were found in the active scene renderers!");
        }
        
        Debug.LogWarning("==========================================================================");
    }
}
