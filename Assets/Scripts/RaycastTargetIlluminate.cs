using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

[RequireComponent(typeof(Camera))]
public class RaycastTargetIlluminate : MonoBehaviour
{
    [Header("Outline Settings")]
    [Tooltip("The color of the outline border.")]
    public Color outlineColor = new Color(1f, 1f, 0.5f, 1f); // Soft yellow
    
    [Tooltip("The thickness of the outline border (in pixels).")]
    [Range(1f, 10f)]
    public float outlineWidth = 2.0f;

    private Material _maskMaterial;
    private Material _outlineMaterial;
    private CommandBuffer _cb;
    private RenderTexture _maskRT;
    private Camera _camera;
    private Mesh _fullscreenQuad;

    void OnEnable()
    {
        _camera = GetComponent<Camera>();
        
        Shader maskShader = Shader.Find("Hidden/URPSilhouetteMask");
        Shader outlineShader = Shader.Find("Hidden/URPSilhouetteOutline");

        if (maskShader != null) _maskMaterial = new Material(maskShader);
        if (outlineShader != null) _outlineMaterial = new Material(outlineShader);

        _cb = new CommandBuffer() { name = "Draw Silhouette Outline" };

        // Create a simple fullscreen quad with clip space coordinates
        _fullscreenQuad = new Mesh();
        _fullscreenQuad.vertices = new Vector3[] {
            new Vector3(-1, -1, 0), new Vector3(-1, 1, 0),
            new Vector3(1, 1, 0), new Vector3(1, -1, 0)
        };
        _fullscreenQuad.uv = new Vector2[] {
            new Vector2(0, 0), new Vector2(0, 1),
            new Vector2(1, 1), new Vector2(1, 0)
        };
        _fullscreenQuad.triangles = new int[] { 0, 1, 2, 0, 2, 3 };

        RenderPipelineManager.endCameraRendering += OnEndCameraRendering;
    }

    void OnDisable()
    {
        RenderPipelineManager.endCameraRendering -= OnEndCameraRendering;
        
        if (_cb != null) _cb.Release();
        if (_maskRT != null)
        {
            _maskRT.Release();
            Destroy(_maskRT);
        }
    }

    void OnEndCameraRendering(ScriptableRenderContext context, Camera cam)
    {
        if (cam != _camera || _maskMaterial == null || _outlineMaterial == null) return;
        if (InteractionSystem.Instance == null) return;

        GameObject lookedAt = InteractionSystem.Instance.GetLookedAtTarget();
        GameObject carryTarget = InteractionSystem.Instance.GetCarryTarget();

        if (lookedAt == null && carryTarget == null) return;

        // Ensure RenderTarget size matches screen
        if (_maskRT == null || _maskRT.width != cam.pixelWidth || _maskRT.height != cam.pixelHeight)
        {
            if (_maskRT != null) _maskRT.Release();
            // Use ARGB32 for maximum compatibility across platforms instead of R8
            _maskRT = new RenderTexture(cam.pixelWidth, cam.pixelHeight, 0, RenderTextureFormat.ARGB32);
            _maskRT.filterMode = FilterMode.Bilinear;
        }

        _cb.Clear();

        // 1. Clear Mask RT
        _cb.SetRenderTarget(_maskRT);
        _cb.ClearRenderTarget(false, true, Color.black);

        // URP drops matrices in endCameraRendering, so we compute and supply our own VP matrix.
        Matrix4x4 projMatrix = GL.GetGPUProjectionMatrix(cam.projectionMatrix, true);
        _cb.SetGlobalMatrix("_CustomVP", projMatrix * cam.worldToCameraMatrix);

        // 2. Draw hierarchical mask
        // If we have a carryTarget (like the Cabinet parent), draw it at 50% intensity
        if (carryTarget != null && carryTarget != lookedAt)
        {
            _cb.SetGlobalColor("_MaskColor", new Color(0.5f, 0.5f, 0.5f, 1f));
            DrawMeshes(_cb, carryTarget);
        }

        // Draw the specifically looked-at target (like the Door child, or the Cabinet if looked at directly) at 100% intensity.
        // ZTest Always ensures the child overwrites the parent's 50% pixels with 100% pixels.
        GameObject primaryTarget = lookedAt != null ? lookedAt : carryTarget;
        if (primaryTarget != null)
        {
            _cb.SetGlobalColor("_MaskColor", new Color(1f, 1f, 1f, 1f));
            DrawMeshes(_cb, primaryTarget);
        }

        // 3. Set mask texture for the outline post-process shader
        _cb.SetGlobalTexture("_SilhouetteMask", _maskRT);

        // 4. Draw outline overlay onto the camera target
        RenderTargetIdentifier camTarget = cam.targetTexture != null ? new RenderTargetIdentifier(cam.targetTexture) : new RenderTargetIdentifier(BuiltinRenderTextureType.CameraTarget);
        _cb.SetRenderTarget(camTarget);
        
        _outlineMaterial.SetColor("_OutlineColor", outlineColor);
        _outlineMaterial.SetFloat("_OutlineWidth", outlineWidth);

        // Draw fullscreen quad
        _cb.DrawMesh(_fullscreenQuad, Matrix4x4.identity, _outlineMaterial, 0, 0);

        context.ExecuteCommandBuffer(_cb);
        context.Submit(); // Ensure the command buffer is submitted immediately
    }

    private void DrawMeshes(CommandBuffer cb, GameObject target)
    {
        Renderer[] renderers = target.GetComponentsInChildren<Renderer>();
        foreach (Renderer r in renderers)
        {
            if (!r.enabled) continue;
            
            MeshFilter mf = r.GetComponent<MeshFilter>();
            if (mf != null && mf.sharedMesh != null)
            {
                for (int i = 0; i < mf.sharedMesh.subMeshCount; i++)
                    cb.DrawRenderer(r, _maskMaterial, i, 0);
            }
            
            SkinnedMeshRenderer smr = r as SkinnedMeshRenderer;
            if (smr != null && smr.sharedMesh != null)
            {
                for (int i = 0; i < smr.sharedMesh.subMeshCount; i++)
                    cb.DrawRenderer(smr, _maskMaterial, i, 0);
            }
        }
    }
}
